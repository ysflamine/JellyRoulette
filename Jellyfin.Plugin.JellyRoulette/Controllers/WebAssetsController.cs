using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyRoulette.Controllers;

/// <summary>
/// Sirve los archivos web del plugin (JS/CSS/HTML) que van incrustados en la DLL.
/// </summary>
/// <remarks>
/// ¿Por qué un controlador propio y no <c>IHasWebPages</c>?
/// Porque <c>IHasWebPages</c> alimenta <c>/web/ConfigurationPage</c>, que está pensado
/// para páginas del panel de administración (la lista exige permisos de administrador).
/// La ruleta la usan TODOS los usuarios, así que servimos sus recursos desde aquí:
/// <list type="bullet">
///   <item><description><c>/JellyRoulette/loader.js</c> → el trocito que se inyecta en <c>index.html</c>.</description></item>
///   <item><description><c>/JellyRoulette/roulette.js</c> → la interfaz completa de la ruleta.</description></item>
///   <item><description><c>/JellyRoulette/roulette.css</c> → los estilos.</description></item>
/// </list>
/// Estos endpoints son anónimos a propósito: solo devuelven código/estilos públicos
/// (igual que cualquier JS de la web de Jellyfin, que se descarga antes de iniciar sesión).
/// Los datos de la biblioteca siguen protegidos por <c>[Authorize]</c> en
/// <see cref="RandomMediaController"/>.
/// </remarks>
[ApiController]
[Route("JellyRoulette")]
public class WebAssetsController : ControllerBase
{
    /// <summary>
    /// Lista blanca de recursos: la clave es lo que aparece en la URL y el valor el nombre
    /// lógico del recurso embebido. Con una lista blanca es imposible usar la ruta para leer
    /// cualquier otro recurso del ensamblado.
    /// </summary>
    private static readonly Dictionary<string, string> Assets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["loader"] = "Jellyfin.Plugin.JellyRoulette.Web.roulette.loader.js",
        ["overlay"] = "Jellyfin.Plugin.JellyRoulette.Web.roulette.js",
        ["styles"] = "Jellyfin.Plugin.JellyRoulette.Web.roulette.css"
    };

    /// <summary>
    /// Punto de entrada JS que el middleware inserta en <c>/web/index.html</c>.
    /// </summary>
    /// <param name="v">Parámetro de versión para invalidar la caché del navegador.</param>
    /// <returns>El script como <c>application/javascript</c>.</returns>
    [HttpGet("loader.js")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetLoader([FromQuery] string? v = null)
        => ServeAsset("loader", "application/javascript; charset=utf-8", v);

    /// <summary>
    /// Interfaz completa de la ruleta (se carga de forma diferida al pulsar el botón).
    /// </summary>
    /// <param name="v">Parámetro de versión para invalidar la caché.</param>
    /// <returns>El script como <c>application/javascript</c>.</returns>
    [HttpGet("roulette.js")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetOverlayScript([FromQuery] string? v = null)
        => ServeAsset("overlay", "application/javascript; charset=utf-8", v);

    /// <summary>
    /// Hoja de estilos de la ruleta.
    /// </summary>
    /// <param name="v">Parámetro de versión para invalidar la caché.</param>
    /// <returns>El CSS como <c>text/css</c>.</returns>
    [HttpGet("roulette.css")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetStyles([FromQuery] string? v = null)
        => ServeAsset("styles", "text/css; charset=utf-8", v);

    /// <summary>
    /// Lee un recurso embebido y lo devuelve con su tipo MIME.
    /// </summary>
    private ActionResult ServeAsset(string key, string contentType, string? cacheBuster)
    {
        if (!Assets.TryGetValue(key, out var resourceName))
        {
            return NotFound();
        }

        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            // No debería ocurrir nunca: significa que el archivo no se incrustó al compilar.
            return NotFound($"Recurso no encontrado: {resourceName}");
        }

        // La versión del plugin como clave de caché: al actualizar el plugin cambia la URL
        // y el navegador descarga la copia nueva sin que el usuario borre nada.
        var version = Plugin.VersionString;
        var cacheValue = string.IsNullOrEmpty(cacheBuster) ? version : $"{version}-{cacheBuster}";

        Response.Headers.CacheControl = "public, max-age=300";
        Response.Headers["X-JellyRoulette-Version"] = cacheValue;

        return File(stream, contentType);
    }
}
