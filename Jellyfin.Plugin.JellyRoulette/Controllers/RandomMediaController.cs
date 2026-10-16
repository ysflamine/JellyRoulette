using Jellyfin.Plugin.JellyRoulette.Models;
using Jellyfin.Plugin.JellyRoulette.Services;
using Jellyfin.Database.Implementations.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRoulette.Controllers;

/// <summary>
/// API REST del plugin. Jellyfin descubre automáticamente cualquier clase pública que
/// herede de <see cref="ControllerBase"/> dentro del ensamblado del plugin
/// (el servidor llama a <c>mvcBuilder.AddApplicationPart(pluginAssembly)</c>), así que
/// no hay que registrar nada a mano: al reiniciar, estas rutas ya existen.
/// </summary>
/// <remarks>
/// Rutas expuestas:
/// <list type="bullet">
///   <item><description><c>GET /JellyRoulette/Genres</c> → géneros disponibles con recuentos.</description></item>
///   <item><description><c>GET /JellyRoulette/Libraries</c> → bibliotecas para el desplegable.</description></item>
///   <item><description><c>GET /JellyRoulette/Spin</c> → elige un elemento al azar y devuelve pósters para la animación.</description></item>
/// </list>
/// Todas exigen sesión iniciada (<c>[Authorize]</c>): el token viaja en la cabecera
/// <c>X-Emby-Token</c> o <c>Authorization: MediaBrowser Token="..."</c>, que es justo lo
/// que hace el <c>ApiClient</c> de la web de Jellyfin.
/// </remarks>
[ApiController]
[Route("JellyRoulette")]
[Authorize]
[Produces("application/json")]
public class RandomMediaController : ControllerBase
{
    private readonly IRandomMediaService _randomMedia;
    private readonly ILogger<RandomMediaController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RandomMediaController"/> class.
    /// </summary>
    /// <param name="randomMedia">Motor de selección aleatoria.</param>
    /// <param name="logger">Registro del servidor.</param>
    public RandomMediaController(IRandomMediaService randomMedia, ILogger<RandomMediaController> logger)
    {
        _randomMedia = randomMedia;
        _logger = logger;
    }

    /// <summary>
    /// Obtiene los géneros presentes en la biblioteca, ordenados por número de elementos.
    /// </summary>
    /// <param name="mediaType">Opcional: "movie", "series" o "both".</param>
    /// <param name="libraryId">Opcional: GUID de la biblioteca.</param>
    /// <param name="limit">Máximo de géneros (por defecto los que diga la configuración del plugin).</param>
    /// <returns>Lista de <see cref="GenreOptionDto"/>.</returns>
    /// <response code="200">Géneros devueltos.</response>
    /// <response code="401">Sin sesión válida.</response>
    [HttpGet("Genres")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<GenreOptionDto>>> GetGenres(
        [FromQuery] string? mediaType = null,
        [FromQuery] Guid? libraryId = null,
        [FromQuery] int? limit = null)
    {
        var user = ResolveCurrentUser();
        if (user is null)
        {
            return Unauthorized("No se pudo determinar el usuario de Jellyfin para esta petición.");
        }

        var max = limit ?? Plugin.Instance?.Configuration.MaxGenreChips ?? 40;

        // GetGenres toca la base de datos de forma síncrona: lo sacamos del hilo de la
        // petición para no bloquear el servidor con bibliotecas grandes.
        var genres = await Task.Run(() => _randomMedia.GetGenres(user, mediaType, libraryId, max), HttpContext.RequestAborted)
            .ConfigureAwait(false);

        return Ok(genres);
    }

    /// <summary>
    /// Obtiene las bibliotecas del servidor (para limitar la ruleta a una de ellas).
    /// </summary>
    /// <returns>Lista de <see cref="LibraryOptionDto"/>.</returns>
    /// <response code="200">Bibliotecas devueltas.</response>
    [HttpGet("Libraries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LibraryOptionDto>>> GetLibraries()
    {
        var libraries = await Task.Run(() => _randomMedia.GetLibraries(), HttpContext.RequestAborted)
            .ConfigureAwait(false);

        return Ok(libraries);
    }

    /// <summary>
    /// "Gira la ruleta": devuelve un elemento elegido al azar entre los que cumplen los
    /// filtros, junto con un conjunto de pósters para alimentar la animación.
    /// </summary>
    /// <param name="mediaType">"movie" (solo películas), "series" (solo series) o "both" (ambos).</param>
    /// <param name="unwatchedOnly"><c>true</c> para descartar lo ya visto por el usuario actual.</param>
    /// <param name="libraryId">GUID opcional de la biblioteca a consultar.</param>
    /// <param name="genres">
    /// Géneros separados por coma. Deben enviarse con el nombre interno de Jellyfin
    /// (el que devuelve <c>/JellyRoulette/Genres</c>); varios géneros se combinan con OR.
    /// </param>
    /// <param name="candidates">Cuántos pósters devolver para la animación (1-60).</param>
    /// <returns>Un <see cref="SpinResultDto"/>.</returns>
    /// <response code="200">Tirada realizada (el ganador puede ser <c>null</c> si no hay coincidencias).</response>
    /// <response code="400">Parámetros inválidos (por ejemplo, biblioteca inexistente).</response>
    /// <response code="401">Sin sesión válida.</response>
    [HttpGet("Spin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SpinResultDto>> Spin(
        [FromQuery] string? mediaType = null,
        [FromQuery] bool? unwatchedOnly = null,
        [FromQuery] Guid? libraryId = null,
        [FromQuery] string? genres = null,
        [FromQuery] int? candidates = null)
    {
        var user = ResolveCurrentUser();
        if (user is null)
        {
            return Unauthorized("No se pudo determinar el usuario de Jellyfin para esta petición.");
        }

        // "Horror,Comedy" → ["Horror", "Comedy"]. Se limpia y se descarta lo vacío.
        var genreList = string.IsNullOrWhiteSpace(genres)
            ? []
            : genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var options = new RouletteQueryOptions(mediaType, unwatchedOnly, libraryId, genreList, candidates);

        try
        {
            var result = await Task.Run(() => _randomMedia.Spin(user, options), HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (result.Winner is null)
            {
                // No es un error del servidor: simplemente no hay nada que cumpla los filtros.
                // Devolvemos 200 con winner = null y totalMatches = 0 para que el frontend
                // pueda explicar "prueba con otros filtros" en lugar de mostrar un fallo.
                return Ok(result);
            }

            _logger.LogDebug(
                "JellyRoulette: ganador {Name} ({Type}) entre {Total} coincidencias para {User}",
                result.Winner.Name,
                result.Winner.Type,
                result.TotalMatches,
                user.Username);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Estado del plugin: versión y ajustes que afectan a la interfaz inyectada.
    /// </summary>
    /// <remarks>
    /// El <c>loader.js</c> consulta este endpoint al arrancar para saber si debe
    /// mostrar el botón flotante y con qué valores por defecto debe abrir la ruleta.
    /// Así, lo que configures en el panel se refleja en la web sin recompilar nada.
    /// </remarks>
    /// <returns>Versión, ajustes de interfaz y valores por defecto de los filtros.</returns>
    [HttpGet("Info")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetInfo()
    {
        var config = Plugin.Instance?.Configuration;

        return Ok(new
        {
            name = Plugin.Instance?.Name ?? "JellyRoulette",
            version = Plugin.VersionString,
            id = Plugin.Instance?.Id.ToString(),
            ui = new
            {
                enableInjection = config?.EnableUiInjection ?? true,
                floatingButton = (config?.FloatingButtonMode ?? Configuration.FloatingButtonMode.Auto).ToString(),
                spinDurationMs = config?.SpinDurationMs ?? 4200,
                candidates = config?.WheelCandidates ?? 28
            },
            defaults = new
            {
                mediaType = config?.DefaultMediaType ?? "both",
                unwatchedOnly = config?.DefaultUnwatchedOnly ?? false,
                maxGenreChips = config?.MaxGenreChips ?? 40
            }
        });
    }

    /// <summary>
    /// Resuelve el usuario de la petición: primero mira el parámetro <c>userId</c>
    /// (por compatibilidad con clientes que lo pasan explícitamente) y, si no viene,
    /// lo deduce del token.
    /// </summary>
    private User? ResolveCurrentUser()
        => _randomMedia.ResolveUser(null, HttpContext);
}
