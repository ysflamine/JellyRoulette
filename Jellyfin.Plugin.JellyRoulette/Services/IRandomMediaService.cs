using Jellyfin.Plugin.JellyRoulette.Models;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.JellyRoulette.Services;

/// <summary>
/// Contrato del "motor" del plugin: todo lo que tiene que ver con consultar la
/// biblioteca y elegir un elemento al azar. Separarlo del controlador es buena
/// práctica (patrón Servicio): el controlador solo habla HTTP, la lógica vive aquí
/// y se puede testear de forma aislada.
/// </summary>
public interface IRandomMediaService
{
    /// <summary>
    /// Devuelve el <see cref="User"/> correspondiente al id indicado o, si es nulo,
    /// el usuario autenticado de la petición HTTP actual.
    /// </summary>
    /// <param name="userId">Id opcional del usuario.</param>
    /// <param name="httpContext">Contexto HTTP de la petición en curso.</param>
    /// <returns>El usuario, o <c>null</c> si no se pudo resolver.</returns>
    User? ResolveUser(Guid? userId, Microsoft.AspNetCore.Http.HttpContext httpContext);

    /// <summary>
    /// Lista los géneros presentes en la biblioteca del usuario, con recuentos.
    /// </summary>
    /// <param name="user">Usuario (aplica sus permisos y bibliotecas visibles).</param>
    /// <param name="mediaType">Filtro de tipo: "movie", "series" o "both".</param>
    /// <param name="libraryId">Biblioteca opcional.</param>
    /// <param name="limit">Máximo de géneros a devolver.</param>
    /// <returns>Lista de géneros ordenada por número de elementos (desc).</returns>
    IReadOnlyList<GenreOptionDto> GetGenres(User? user, string? mediaType, Guid? libraryId, int limit);

    /// <summary>
    /// Lista las bibliotecas configuradas en el servidor (para el desplegable de biblioteca).
    /// </summary>
    /// <returns>Bibliotecas con su id, nombre y tipo de colección.</returns>
    IReadOnlyList<LibraryOptionDto> GetLibraries();

    /// <summary>
    /// Ejecuta la "tirada": cuenta cuántos elementos cumplen los filtros, elige uno
    /// al azar de forma uniforme y devuelve además un puñado de pósters para la animación.
    /// </summary>
    /// <param name="user">Usuario actual.</param>
    /// <param name="options">Filtros solicitados por el frontend.</param>
    /// <returns>El resultado de la tirada.</returns>
    SpinResultDto Spin(User? user, RouletteQueryOptions options);
}
