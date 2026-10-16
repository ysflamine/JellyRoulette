namespace Jellyfin.Plugin.JellyRoulette.Models;

/// <summary>
/// Filtros que el frontend envía a la API. Se declaran como <c>record</c> porque son
/// datos inmutables de transporte (DTO): nadie debe poder modificarlos a mitad de vuelo.
/// </summary>
/// <param name="MediaType">Tipo de medio: <c>movie</c>, <c>series</c> o <c>both</c>.</param>
/// <param name="UnwatchedOnly">Si es <c>true</c>, solo se devuelven elementos no vistos por el usuario actual.</param>
/// <param name="LibraryId">GUID opcional de una biblioteca/carpeta para limitar la búsqueda.</param>
/// <param name="Genres">Nombres de género tal y como los almacena Jellyfin (en inglés por defecto: "Horror", "Comedy"...).</param>
/// <param name="Candidates">Cuántos pósters se devuelven para animar la ruleta (además del ganador).</param>
public sealed record RouletteQueryOptions(
    string? MediaType,
    bool? UnwatchedOnly,
    Guid? LibraryId,
    string[]? Genres,
    int? Candidates);

/// <summary>
/// Versión ligera y "aplanada" de <c>BaseItemDto</c>.
/// Devolvemos SOLO lo que necesita la ruleta: menos bytes por la red y un contrato
/// estable que no se rompe cuando Jellyfin cambia sus DTOs internos.
/// </summary>
/// <param name="Id">Identificador del elemento.</param>
/// <param name="Name">Título.</param>
/// <param name="Type">"Movie" o "Series".</param>
/// <param name="Year">Año de estreno.</param>
/// <param name="Overview">Sinopsis.</param>
/// <param name="Genres">Géneros.</param>
/// <param name="CommunityRating">Puntuación de la comunidad (0-10).</param>
/// <param name="OfficialRating">Clasificación por edades (PG-13, 18...).</param>
/// <param name="RunTimeMinutes">Duración en minutos.</param>
/// <param name="HasPrimaryImage">Indica si tiene póster (para no pintar huecos).</param>
/// <param name="Played">Si el usuario ya lo vio.</param>
/// <param name="IsFavorite">Si el usuario lo marcó como favorito.</param>
public sealed record RouletteItemDto(
    Guid Id,
    string Name,
    string Type,
    int? Year,
    string? Overview,
    string[] Genres,
    float? CommunityRating,
    string? OfficialRating,
    int? RunTimeMinutes,
    bool HasPrimaryImage,
    bool Played,
    bool IsFavorite);

/// <summary>
/// Un género disponible en la biblioteca, con recuento para poder ordenar/limitar la lista.
/// </summary>
/// <param name="Name">Nombre interno (el que hay que enviar de vuelta en el filtro).</param>
/// <param name="MovieCount">Películas con ese género.</param>
/// <param name="SeriesCount">Series con ese género.</param>
/// <param name="TotalCount">Total de elementos.</param>
public sealed record GenreOptionDto(
    string Name,
    int MovieCount,
    int SeriesCount,
    int TotalCount);

/// <summary>
/// Una biblioteca visible para el usuario (para el desplegable "Biblioteca").
/// </summary>
/// <param name="Id">GUID de la vista/biblioteca.</param>
/// <param name="Name">Nombre visible.</param>
/// <param name="CollectionType">Tipo: movies, tvshows, mixed...</param>
public sealed record LibraryOptionDto(
    Guid Id,
    string Name,
    string? CollectionType);

/// <summary>
/// Respuesta completa de <c>GET /JellyRoulette/Spin</c>.
/// </summary>
/// <param name="Winner">El elemento elegido al azar. <c>null</c> si no hay coincidencias.</param>
/// <param name="Candidates">Pósters de relleno para la animación (puede incluir al ganador).</param>
/// <param name="TotalMatches">Cuántos elementos cumplen los filtros (se muestra en la interfaz).</param>
/// <param name="Filters">Eco de los filtros aplicados, útil para depurar desde la consola del navegador.</param>
public sealed record SpinResultDto(
    RouletteItemDto? Winner,
    IReadOnlyList<RouletteItemDto> Candidates,
    int TotalMatches,
    RouletteFiltersDto Filters);

/// <summary>
/// Eco normalizado de los filtros realmente aplicados por el servidor.
/// </summary>
/// <param name="MediaType">Tipo de medio resuelto.</param>
/// <param name="UnwatchedOnly">Si se filtraron los ya vistos.</param>
/// <param name="Genres">Géneros aplicados.</param>
/// <param name="LibraryId">Biblioteca aplicada.</param>
/// <param name="IncludeItemTypes">Tipos de elemento consultados.</param>
public sealed record RouletteFiltersDto(
    string MediaType,
    bool UnwatchedOnly,
    string[] Genres,
    Guid? LibraryId,
    string[] IncludeItemTypes);
