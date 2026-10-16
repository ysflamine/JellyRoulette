# 🎲 JellyRoulette

Plugin para **Jellyfin** que añade un dado en la cabecera de la web. Lo pulsas, eliges
género y tipo de medio, y una ruleta 3D de pósters gira hasta detenerse en un título
elegido **al azar** entre todos los que cumplen tus filtros.

Para cuando la pregunta no es *«qué veo»* sino *«no sé qué ver»*.

<!-- SUSTITUYE ESTO POR UNA CAPTURA O UN GIF DE LA RULETA GIRANDO.
     Es lo primero que mira la gente y lo que más instalaciones trae.
     Guárdala en docs/img/ruleta.png (o enlaza un asset de la release si pesa mucho).
<p align="center"><img src="docs/img/ruleta.png" width="72%" alt="Ruleta girando"></p>
-->

---

## Requisitos

| | |
|---|---|
| **Jellyfin** | **12.x** (recomendado) o **10.11.x** |
| **Navegador** | Cualquiera moderno (PC o móvil) |
| **Biblioteca** | Alguna con películas y/o series ya escaneada |

Consulta tu versión en **Panel de control → General**, o:

```bash
curl -s http://localhost:8096/System/Info/Public | grep Version
```

---

## Instalación desde el catálogo (recomendada)

Así el plugin se actualizará solo desde el panel.

1. Entra en Jellyfin como **administrador**.
2. Ve a **Panel de control → Plugins → Repositorios** y pulsa **`+`**.
3. Rellena:
   - **Nombre:** `JellyRoulette`
   - **URL:** la que corresponda a tu versión de Jellyfin 👇

   **Jellyfin 12.x**
   ```text
   https://raw.githubusercontent.com/ysflamine/JellyRoulette/main/manifest.json
   ```

   **Jellyfin 10.11.x**
   ```text
   https://raw.githubusercontent.com/ysflamine/JellyRoulette/main/manifest-10.11.json
   ```

4. Guarda, abre la pestaña **Catálogo**, busca **JellyRoulette** (categoría *General*) y pulsa **Instalar**.
5. **Reinicia el servidor.**
6. Recarga la web con **`Ctrl + Shift + R`** (recarga dura).

> **¿Por qué dos URLs?** Cada rama de Jellyfin necesita una compilación distinta
> (.NET 10 para la 12.x, .NET 9 para la 10.11.x) y no son intercambiables.

---

## Instalación manual

1. Descarga de **[Releases](../../releases)** el `.zip` de tu rama:
   - Jellyfin **12.x** → `jellyroulette_12.zip`
   - Jellyfin **10.11.x** → `jellyroulette_10.11.zip`
2. Extrae los dos archivos (`Jellyfin.Plugin.JellyRoulette.dll` y `meta.json`) en una
   **subcarpeta llamada `JellyRoulette`** dentro de la carpeta de plugins:

   | Instalación | Ruta |
   |---|---|
   | Windows (instalador o servicio) | `C:\ProgramData\Jellyfin\Server\plugins\JellyRoulette\` |
   | Linux (paquete) | `/var/lib/jellyfin/plugins/JellyRoulette/` |
   | Docker | `/config/plugins/JellyRoulette/` |
   | macOS | `~/.local/share/jellyfin/plugins/JellyRoulette/` |

3. Reinicia el servidor y recarga la web con `Ctrl + Shift + R`.

> ⚠️ La DLL va en una **subcarpeta propia**, no suelta en `plugins/`.
> ⚠️ En Linux/Docker comprueba los permisos: si el proceso no puede leer la DLL, la ignora en silencio.

---

## Cómo se usa

1. Pulsa el **dado 🎲** de la cabecera (o `Ctrl` / `⌘` + `Shift` + `J`).
2. Ajusta los filtros:

   | Filtro | Opciones |
   |---|---|
   | **Género** | Chips multi-selección con el nº de títulos de cada uno. Varios géneros se combinan con *O*. |
   | **Tipo** | Películas · Series · Ambos |
   | **Estado** | Solo no vistos · Todos |
   | **Biblioteca** | Todas, o una concreta |

3. Pulsa **GIRAR**. La ruleta gira y frena sobre el elegido.
4. En la ficha puedes **Reproducir**, **Ver ficha** o **Girar otra vez**.
5. `Esc` cierra.

En móvil aparece además un **botón flotante** en la esquina inferior derecha.

### Ajustes del plugin

**Panel de control → Plugins → JellyRoulette → Ajustes**:

| Ajuste | Para qué |
|---|---|
| Mostrar JellyRoulette en la interfaz web | Desactiva el botón inyectado (la API sigue funcionando) |
| Botón flotante | `Automático` (móvil o si la cabecera falla) · `Siempre` · `Nunca` |
| Tipo de medio por defecto | Con qué filtro abre la ruleta |
| Filtrar «solo no vistos» por defecto | Idem |
| Duración del giro (ms) | 1600 – 9000. Más alto = más dramático |
| Pósters en la ruleta | Cuántos candidatos se descargan por tirada (8 – 60) |
| Máximo de géneros | Se muestran primero los que tienen más títulos |

Los cambios se aplican al recargar la web; no hace falta reiniciar el servidor.

---

## Solución de problemas

| Síntoma | Solución |
|---|---|
| No aparece en **Panel → Plugins** | La DLL debe estar en `plugins/JellyRoulette/`, no suelta. Revisa permisos de lectura. |
| Aparece pero no hay dado en la cabecera | `Ctrl + Shift + R`. Si sigue sin salir, activa *Botón flotante → Siempre* en los ajustes. |
| Estado *Malfunctioned* | Estás usando la DLL de la otra rama de Jellyfin. Descarga el `.zip` correcto. |
| La ruleta sale vacía («Nada por aquí…») | Ningún título cumple esos filtros: quita géneros o desactiva *solo no vistos*. |
| La ruleta sale vacía y no tienes géneros | Biblioteca sin escanear o sin metadatos. **Panel → Bibliotecas → Escanear todas**. |
| Se ven los pósters como cuadros grises | El título no tiene imagen *Primary*. Ponle póster en Jellyfin. |
| Instalación desde catálogo falla | Mira el log del servidor y busca `JellyRoulette`, `checksum` u `Only zip`. |
| Tras actualizar, sigue la versión vieja | Recarga dura (`Ctrl + Shift + R`). El navegador cachea `index.html`. |

**Dónde está el log:**

| | Ruta |
|---|---|
| Windows | `C:\ProgramData\Jellyfin\Server\log\log_AAAAMMDD.log` |
| Linux | `/var/log/jellyfin/log_AAAAMMDD.log` |
| Docker | `docker logs jellyfin --tail 100` |

---

## Desinstalar

**Panel → Plugins → JellyRoulette → Desinstalar**, y reinicia.

A mano: borra la carpeta `plugins/JellyRoulette/` y reinicia. Los ajustes viven aparte, en
`configurations/JellyRoulette_f299f82b-….xml`; bórralo también si quieres empezar de cero.

---

## Compilar desde el código fuente

| Jellyfin | .NET SDK | Comando |
|---|---|---|
| 12.x | 10.0.x | `dotnet publish Jellyfin.Plugin.JellyRoulette -c Release -o publish` |
| 10.11.x | 9.0.x | `dotnet publish Jellyfin.Plugin.JellyRoulette -c Release -o publish-jf10 -p:JellyfinTarget=jf10` |

Copia `Jellyfin.Plugin.JellyRoulette.dll` y `meta.json` a la carpeta de plugins y reinicia.
El `.pdb`, el `.xml` y el `.deps.json` no hacen falta.

Tests:

```bash
dotnet test tests/JellyRoulette.Tests        # backend (41 tests)
cd tests/frontend && npm install && npm test # interfaz (jsdom)
```

---

## API

Todas las rutas de datos exigen sesión iniciada (cabecera
`Authorization: MediaBrowser Token="…"`).

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/JellyRoulette/Spin` | Elige un título al azar + pósters para la animación |
| `GET` | `/JellyRoulette/Genres` | Géneros de la biblioteca con recuentos |
| `GET` | `/JellyRoulette/Libraries` | Bibliotecas del servidor |
| `GET` | `/JellyRoulette/Info` | Versión y ajustes activos |

Parámetros de `/Spin`: `mediaType` (`movie`·`series`·`both`), `unwatchedOnly` (`true`),
`libraryId` (GUID), `genres` (separados por coma, con el nombre interno: `Horror,Comedy`),
`candidates` (1-60).

```bash
curl -H "Authorization: MediaBrowser Token=\"TU_TOKEN\"" \
  "http://localhost:8096/JellyRoulette/Spin?mediaType=movie&unwatchedOnly=true&genres=Horror"
```

Los archivos estáticos (`/loader.js`, `/roulette.js`, `/roulette.css`) son anónimos, como
el resto de recursos de la web de Jellyfin. No exponen datos de la biblioteca.

---

## Licencia

[GPL-3.0](LICENSE). Al enlazar contra los paquetes NuGet de Jellyfin (GPLv3), el binario
resultante es GPLv3 por derivación. Es la licencia que el proyecto Jellyfin exige a los
plugins distribuidos públicamente.
