#!/usr/bin/env python3
"""
Verificador de integridad de JellyRoulette.

Dos modos:

  verify.py repo
      Inspecciona el árbol de fuentes. Caza la clase de errores que no se ven
      hasta que alguien intenta instalar el plugin: BOM en los archivos, GUID
      distinto en los cuatro sitios donde debe coincidir, recursos embebidos que
      apuntan a un archivo que no existe, manifests con sintaxis Markdown en la
      URL o con claves que el servidor no reconoce.

  verify.py release --tag v1.0.1 --repo owner/name
      Descarga lo que la CI acaba de publicar y comprueba que UN SERVIDOR
      JELLYFIN PODRÁ INSTALARLO: que cada sourceUrl exista, que su MD5 coincida
      con el checksum declarado, que el zip lleve la DLL y el meta.json en la
      raíz, y que meta.json y manifest digan lo mismo.

Sale con código 1 en cuanto algo falla, así que la CI se pone roja.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import sys
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

# Claves que el servidor acepta al deserializar el manifest de un repositorio.
# Fuente: MediaBrowser.Model/Updates/PackageInfo.cs y VersionInfo.cs
PACKAGE_KEYS = {"name", "description", "overview", "owner", "category", "guid", "versions", "imageUrl"}
VERSION_KEYS = {"version", "changelog", "targetAbi", "sourceUrl", "checksum", "timestamp",
                "repositoryName", "repositoryUrl"}

TEXT_SUFFIXES = {".cs", ".csproj", ".js", ".css", ".html", ".json", ".md", ".yml", ".yaml",
                 ".props", ".slnx", ".ps1", ".winget", ".gitignore", ".gitattributes", ""}

ok_count = 0
fail_count = 0


def ok(msg: str) -> None:
    global ok_count
    ok_count += 1
    print(f"  \033[32mOK\033[0m   {msg}")


def fail(msg: str) -> None:
    global fail_count
    fail_count += 1
    print(f"  \033[31mFALLO\033[0m {msg}")


def check(cond: bool, msg: str, detail: str = "") -> None:
    ok(msg) if cond else fail(f"{msg}" + (f"  → {detail}" if detail else ""))


def tracked_files() -> list[Path]:
    """Todos los archivos del árbol, salvo artefactos de compilación y dependencias.

    No se excluyen .vscode/, .config/, tools/ ni la documentación interna: si están
    aquí, check_no_private() debe verlos y avisar. Esa es justo su función.
    """
    skip_dirs = {".git", "bin", "obj", "publish", "publish-jf10", "node_modules"}
    out = []
    for p in sorted(ROOT.rglob("*")):
        if not p.is_file():
            continue
        rel = p.relative_to(ROOT)
        if any(part in skip_dirs or part.startswith("publish") for part in rel.parts):
            continue
        out.append(p)
    return out


# ---------------------------------------------------------------------------
# Modo repo
# ---------------------------------------------------------------------------

def expected_guid() -> str | None:
    plugin = ROOT / "Jellyfin.Plugin.JellyRoulette" / "Plugin.cs"
    if not plugin.exists():
        return None
    m = re.search(r'Id\s*=>\s*Guid\.Parse\("([0-9a-fA-F-]{36})"\)', plugin.read_text(encoding="utf-8"))
    return m.group(1).lower() if m else None


def check_bom() -> None:
    print("\n[1] BOM en los archivos de texto")
    con_bom = []
    for p in tracked_files():
        if p.suffix.lower() not in TEXT_SUFFIXES and p.name != "LICENSE":
            continue
        if p.read_bytes()[:3] == b"\xef\xbb\xbf":
            con_bom.append(str(p.relative_to(ROOT)))
    check(not con_bom, "ningún archivo lleva BOM", ", ".join(con_bom))


def check_fences() -> None:
    print("\n[2] Bloques de código cerrados en los .md")
    for p in tracked_files():
        if p.suffix.lower() != ".md":
            continue
        stack = None
        line_no = 0
        for i, line in enumerate(p.read_text(encoding="utf-8").splitlines(), 1):
            m = re.match(r"^(`{3,})", line.strip())
            if not m:
                continue
            if stack is None:
                stack = (m.group(1), i)
            elif m.group(1) == stack[0]:
                stack = None
            line_no = i
        check(stack is None, f"{p.name}: vallas balanceadas",
              f"bloque abierto en la línea {stack[1] if stack else '?'} de {line_no}")


def check_guid() -> None:
    print("\n[3] GUID coherente en todos los sitios")
    guid = expected_guid()
    if not guid:
        fail("no se encontró Plugin.Id en Plugin.cs")
        return
    ok(f"Plugin.cs → {guid}")

    sitios = {
        "Properties/AssemblyInfo.cs": r'Guid\("([0-9a-fA-F-]{36})"\)',
        "Jellyfin.Plugin.JellyRoulette.csproj": r"<PluginGuid>([0-9a-fA-F-]{36})</PluginGuid>",
        "Configuration/configPage.html": r"pluginUniqueId:\s*'([0-9a-fA-F-]{36})'",
    }
    base = ROOT / "Jellyfin.Plugin.JellyRoulette"
    for rel, rx in sitios.items():
        f = base / rel
        if not f.exists():
            fail(f"{rel} no existe")
            continue
        m = re.search(rx, f.read_text(encoding="utf-8"))
        check(bool(m) and m.group(1).lower() == guid, f"{rel} coincide",
              m.group(1) if m else "no se encontró")

    for mf in ("manifest.json", "manifest-10.11.json"):
        p = ROOT / mf
        if not p.exists():
            continue
        g = json.loads(p.read_text(encoding="utf-8"))[0].get("guid", "").lower()
        check(g == guid, f"{mf} coincide", g)


def check_embedded_resources() -> None:
    print("\n[4] Recursos embebidos declarados en el .csproj")
    csproj = ROOT / "Jellyfin.Plugin.JellyRoulette" / "Jellyfin.Plugin.JellyRoulette.csproj"
    texto = csproj.read_text(encoding="utf-8")
    base = csproj.parent

    # Rutas literales
    for m in re.finditer(r'<EmbeddedResource Include="([^"*]+)"\s*/?>', texto):
        rel = m.group(1).replace("\\", "/")
        check((base / rel).exists(), f"existe {rel}")

    # Globs: Web/** debe dar al menos los tres archivos del frontend
    if re.search(r'<EmbeddedResource Include="Web\\\*\*\\\*"', texto) or \
       re.search(r'<EmbeddedResource Include="Web/\*\*/\*"', texto):
        web = sorted(p.name for p in (base / "Web").glob("*") if p.is_file())
        check(set(web) >= {"roulette.css", "roulette.js", "roulette.loader.js"},
              f"Web/ contiene el frontend: {web}")

    # Los nombres lógicos que usa el C# deben corresponder a archivos reales
    print("\n[5] Nombres de recurso referenciados desde el C#")
    fuentes = list((base / "Controllers").glob("*.cs")) + [base / "Plugin.cs"]
    root_ns = "Jellyfin.Plugin.JellyRoulette"
    for f in fuentes:
        for m in re.finditer(r'"(Jellyfin\.Plugin\.JellyRoulette\.[A-Za-z0-9_.\-]+)"', f.read_text(encoding="utf-8")):
            name = m.group(1)
            if name == root_ns:
                continue
            # nombre lógico → ruta relativa: quita el namespace raíz y pasa puntos a barras
            tail = name[len(root_ns) + 1:]
            candidatos = []
            partes = tail.split(".")
            for corte in range(1, len(partes) + 1):
                candidatos.append("/".join(partes[:corte]) + ("/" + ".".join(partes[corte:]) if corte < len(partes) else ""))
            existe = any((base / c).exists() for c in candidatos)
            check(existe, f"{f.name}: {name}", f"no corresponde a ningún archivo (probado: {candidatos[:3]})")


def check_manifests() -> None:
    print("\n[6] Esquema y URLs de los manifests")
    guid = expected_guid()
    for mf in ("manifest.json", "manifest-10.11.json"):
        p = ROOT / mf
        if not p.exists():
            fail(f"{mf} no existe")
            continue
        raw = p.read_bytes()
        check(not raw.startswith(b"\xef\xbb\xbf"), f"{mf}: sin BOM")
        try:
            data = json.loads(raw.decode("utf-8"))
        except Exception as e:
            fail(f"{mf}: JSON inválido → {e}")
            continue

        check(isinstance(data, list) and len(data) == 1, f"{mf}: array con una entrada")
        pkg = data[0]
        desconocidas = set(pkg) - PACKAGE_KEYS
        check(not desconocidas, f"{mf}: claves reconocidas por el servidor", str(desconocidas))
        check(pkg.get("guid", "").lower() == guid, f"{mf}: guid = Plugin.Id")

        versiones = pkg.get("versions") or []
        check(len(versiones) == 1, f"{mf}: exactamente una versión (un ABI por manifest)",
              f"hay {len(versiones)}")

        for v in versiones:
            desco = set(v) - VERSION_KEYS
            check(not desco, f"{mf}: claves de versión reconocidas", str(desco))

            url = v.get("sourceUrl") or ""
            check(url.startswith("https://"), f"{mf}: sourceUrl es https", url[:60])
            check(url.endswith(".zip"), f"{mf}: sourceUrl termina en .zip", url[-24:])
            check(not re.search(r"[][(]", url), f"{mf}: sourceUrl sin sintaxis Markdown")
            check(bool(re.fullmatch(r"\d+\.\d+(\.\d+){0,2}", v.get("version") or "")),
                  f"{mf}: version con formato válido", v.get("version", ""))
            check(bool(re.fullmatch(r"\d+\.\d+\.\d+\.\d+", v.get("targetAbi") or "")),
                  f"{mf}: targetAbi con 4 partes", v.get("targetAbi", ""))
            check(bool(re.fullmatch(r"[0-9a-fA-F]{32}", v.get("checksum") or "")),
                  f"{mf}: checksum de 32 hex", v.get("checksum", "")[:40])
            ts = v.get("timestamp")
            check(not ts or re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", ts),
                  f"{mf}: timestamp ISO-8601 o vacío", str(ts))

    # Las dos ramas no pueden compartir targetAbi
    abis = []
    for mf in ("manifest.json", "manifest-10.11.json"):
        p = ROOT / mf
        if p.exists():
            abis.append(json.loads(p.read_text(encoding="utf-8"))[0]["versions"][0]["targetAbi"])
    check(len(abis) == len(set(abis)), "cada manifest declara un targetAbi distinto", str(abis))


def check_version_consistency() -> None:
    print("\n[7] Versión coherente entre props y manifests")
    props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    m = re.search(r"<AssemblyVersion>([^<]+)</AssemblyVersion>", props)
    if not m:
        fail("no se encontró AssemblyVersion en Directory.Build.props")
        return
    ver = m.group(1)
    ok(f"Directory.Build.props → {ver}")
    for mf in ("manifest.json", "manifest-10.11.json"):
        p = ROOT / mf
        if not p.exists():
            continue
        mv = json.loads(p.read_text(encoding="utf-8"))[0]["versions"][0]["version"]
        # El manifest lo reescribe la CI al publicar; antes del primer tag puede
        # llevar un marcador. Solo avisamos si difieren y ninguno es de ceros.
        if mv != ver and "00000000" not in json.dumps(json.loads(p.read_text(encoding="utf-8"))):
            fail(f"{mf}: version {mv} ≠ AssemblyVersion {ver}")
        else:
            ok(f"{mf}: version {mv}")


def check_readme_links() -> None:
    print("\n[8] Enlaces internos del README")
    readme = ROOT / "README.md"
    if not readme.exists():
        fail("README.md no existe")
        return
    existentes = {str(p.relative_to(ROOT)).replace("\\", "/") for p in tracked_files()}
    code = False
    for line in readme.read_text(encoding="utf-8").splitlines():
        if re.match(r"^`{3,}", line.strip()):
            code = not code
            continue
        if code:
            continue
        for m in re.finditer(r"\]\((?!https?:|#|mailto:)([^)\s]+)\)", line):
            target = m.group(1).split("#")[0]
            if not target or target.startswith("../"):
                continue  # ../../releases es un enlace relativo de GitHub, no un archivo
            check(target in existentes, f"README → {target}")


def check_no_private() -> None:
    print("\n[9] Nada privado en el árbol público")
    prohibidos = ("MEGA-PROMPT", "PLAN-", "GUIA-", "AUDITORIA", "ACTUALIZAR", "deploy.ps1",
                  "configuration.winget", "build.yaml")
    colados = []
    for p in tracked_files():
        rel = str(p.relative_to(ROOT)).replace("\\", "/")
        if any(x in rel for x in prohibidos):
            colados.append(rel)
    check(not colados, "sin documentación ni tooling personal", ", ".join(colados))


def mode_repo() -> int:
    print("=" * 68)
    print(" VERIFICACIÓN DEL ÁRBOL DE FUENTES")
    print("=" * 68)
    check_bom()
    check_fences()
    check_guid()
    check_embedded_resources()
    check_manifests()
    check_version_consistency()
    check_readme_links()
    check_no_private()
    return resumen()


# ---------------------------------------------------------------------------
# Modo release
# ---------------------------------------------------------------------------

def download(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": "JellyRoulette-verify/1.0"})
    with urllib.request.urlopen(req, timeout=90) as r:
        return r.read()


def head_status(url: str) -> int:
    req = urllib.request.Request(url, headers={"User-Agent": "JellyRoulette-verify/1.0"}, method="HEAD")
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.status
    except urllib.error.HTTPError as e:
        return e.code
    except Exception:
        return 0


def mode_release(tag: str, repo: str) -> int:
    print("=" * 68)
    print(f" VERIFICACIÓN DE LO PUBLICADO  ({repo} @ {tag})")
    print("=" * 68)

    base = f"https://github.com/{repo}/releases/download/{tag}"
    raw = f"https://raw.githubusercontent.com/{repo}/main"

    for mf in ("manifest.json", "manifest-10.11.json"):
        print(f"\n--- {mf} ---")

        # 1) El manifest que sirve raw.githubusercontent (lo que añade el usuario)
        url_raw = f"{raw}/{mf}"
        st = head_status(url_raw)
        check(st == 200, f"raw.githubusercontent sirve {mf}", f"HTTP {st}")
        if st != 200:
            continue

        # Se añade un parámetro de cache-busting: la CDN de GitHub puede servir
        # una copia antigua durante unos minutos tras el push de la CI.
        data = json.loads(download(url_raw + f"?cb={tag}").decode("utf-8"))
        pkg = data[0]
        v = pkg["versions"][0]
        ok(f"versión {v['version']} · targetAbi {v['targetAbi']}")

        # 2) La URL de descarga existe
        src = v["sourceUrl"]
        st = head_status(src)
        check(st == 200, f"sourceUrl responde 200", f"HTTP {st} → {src}")
        if st != 200:
            continue
        check(src.startswith(base), "sourceUrl apunta a esta release", src)

        # 3) El MD5 del archivo real coincide con el declarado
        blob = download(src)
        real = hashlib.md5(blob).hexdigest()
        check(real.lower() == (v["checksum"] or "").lower(),
              "checksum MD5 coincide", f"manifest={v['checksum']} real={real}")

        # 4) El zip lleva la DLL y meta.json EN LA RAÍZ
        try:
            zf = zipfile.ZipFile(io.BytesIO(blob))
        except Exception as e:
            fail(f"no es un zip válido → {e}")
            continue
        nombres = zf.namelist()
        check("Jellyfin.Plugin.JellyRoulette.dll" in nombres,
              "el zip contiene la DLL en la raíz", str(nombres))
        check("meta.json" in nombres, "el zip contiene meta.json en la raíz", str(nombres))
        check(all("/" not in n for n in nombres),
              "ningún archivo va dentro de una subcarpeta", str(nombres))
        ok(f"zip: {len(blob):,} bytes · {len(nombres)} archivos")

        # 5) meta.json y manifest dicen lo mismo
        meta = json.loads(zf.read("meta.json").decode("utf-8-sig"))
        check(meta.get("id", "").lower() == pkg.get("guid", "").lower(),
              "meta.json id == manifest guid", f"{meta.get('id')} vs {pkg.get('guid')}")
        check(meta.get("version") == v["version"],
              "meta.json version == manifest version", f"{meta.get('version')} vs {v['version']}")
        check(meta.get("targetAbi") == v["targetAbi"],
              "meta.json targetAbi == manifest targetAbi",
              f"{meta.get('targetAbi')} vs {v['targetAbi']}")
        check(meta.get("name") == pkg.get("name"),
              "meta.json name == manifest name", f"{meta.get('name')} vs {pkg.get('name')}")

        # 6) La DLL declara el target framework que corresponde a ese targetAbi
        dll = zf.read("Jellyfin.Plugin.JellyRoulette.dll")
        tfms = sorted(set(m.decode() for m in re.findall(rb"\.NETCoreApp,Version=v(\d+\.\d+)", dll)))
        esperada = "10.0" if v["targetAbi"].startswith("12.") else "9.0"
        check(tfms == [esperada], f"la DLL es net{esperada}", f"TFM detectado: {tfms}")

    # 7) El manifest publicado en la release y el de main deben ser iguales
    print("\n--- coherencia release ↔ main ---")
    for mf in ("manifest.json", "manifest-10.11.json"):
        try:
            a = json.loads(download(f"{raw}/{mf}?cb={tag}").decode("utf-8"))
            b = json.loads(download(f"{base}/{mf}").decode("utf-8"))
            check(a[0]["versions"][0]["checksum"] == b[0]["versions"][0]["checksum"],
                  f"{mf}: mismo checksum en main y en la release")
        except Exception as e:
            fail(f"{mf}: no se pudo comparar → {e}")

    return resumen()


def resumen() -> int:
    print("\n" + "=" * 68)
    total = ok_count + fail_count
    if fail_count:
        print(f" \033[31m{fail_count} FALLOS\033[0m de {total} comprobaciones")
        print("=" * 68)
        return 1
    print(f" \033[32m{total} COMPROBACIONES CORRECTAS\033[0m")
    print("=" * 68)
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="Verificador de integridad de JellyRoulette")
    ap.add_argument("mode", choices=["repo", "release"])
    ap.add_argument("--tag", default="")
    ap.add_argument("--repo", default="")
    args = ap.parse_args()

    if args.mode == "repo":
        return mode_repo()

    if not args.tag or not args.repo:
        print("El modo release necesita --tag y --repo", file=sys.stderr)
        return 2
    return mode_release(args.tag, args.repo)


if __name__ == "__main__":
    sys.exit(main())
