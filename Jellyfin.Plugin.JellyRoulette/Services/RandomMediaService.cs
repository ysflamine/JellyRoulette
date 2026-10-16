using System.Security.Claims;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyRoulette.Models;
using MediaBrowser.Controller.Dto;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRoulette.Services;

/// <summary>
/// Implementación del motor de la ruleta sobre la API interna de Jellyfin.
/// </summary>
/// <remarks>
/// Decisiones de diseño importantes (y por qué):
/// <list type="bullet">
/// <item><description>
/// <b>Aleatoriedad real y barata.</b> En vez de cargar toda la biblioteca en memoria,
/// hacemos una consulta de recuento (<c>Limit = 0</c>, <c>EnableTotalRecordCount = true</c>),
/// sorteamos un índice <c>startIndex</c> y pedimos una "ventana" pequeña de elementos.
/// Con eso el ganador es uniforme sobre TODO el conjunto filtrado y la consulta es O(1)
/// en memoria, aunque tengas 50.000 películas.
/// </description></item>
/// <item><description>
/// <b>Una sola ventana sirve para dos cosas:</b> de ahí sale el ganador Y los pósters
/// de relleno para la animación. Cero consultas extra.
/// </description></item>
/// <item><description>
/// <b>Filtros de usuario.</b> Al pasar el <see cref="User"/> a <see cref="InternalItemsQuery"/>
/// Jellyfin ya aplica permisos, bibliotecas ocultas y control parental por nosotros.
/// </description></item>
/// <item><description>
/// <b>Extras fuera.</b> Tráilers, clips y demás material extra también son
/// <c>BaseItemKind.Movie</c>, así que se excluyen explícitamente con <c>ExcludeItemTypes</c>.
/// </description></item>
/// </list>
/// </remarks>
public sealed class RandomMediaService : IRandomMediaService
{
    /// <summary>Valor de filtro: solo películas.</summary>
    public const string MediaMovies = "movie";

    /// <summary>Valor de filtro: solo series.</summary>
    public const string MediaSeries = "series";

    /// <summary>Valor de filtro: películas y series.</summary>
    public const string MediaBoth = "both";

    /// <summary>
    /// Tipo de claim con el que Jellyfin marca al usuario autenticado.
    /// Es el mismo valor que usa <c>Jellyfin.Api.Constants.InternalClaimTypes.UserId</c>,
    /// pero esa clase vive en el ensamblado del servidor (no se publica en NuGet),
    /// por eso la redeclaramos aquí.
    /// </summary>
    private const string UserIdClaimType = "Jellyfin-UserId";

    private static readonly BaseItemKind[] MovieOnly = [BaseItemKind.Movie];
    private static readonly BaseItemKind[] SeriesOnly = [BaseItemKind.Series];
    private static readonly BaseItemKind[] MoviesAndSeries = [BaseItemKind.Movie, BaseItemKind.Series];

    /// <summary>Material extra que jamás debe salir en la ruleta.</summary>
    private static readonly BaseItemKind[] Extras =
    [
        BaseItemKind.Trailer,
        BaseItemKind.Video,
        BaseItemKind.Audio,
        BaseItemKind.BoxSet,
        BaseItemKind.Playlist,
        BaseItemKind.Episode,
        BaseItemKind.Season,
        BaseItemKind.Book,
        BaseItemKind.Recording,
        BaseItemKind.Program
    ];

    /// <summary>Campos que pedimos al generador de DTOs: los justos para pintar la ficha.</summary>
    private static readonly ItemFields[] DtoFields =
    [
        ItemFields.Genres,
        ItemFields.Overview,
        ItemFields.Tags,
        ItemFields.PrimaryImageAspectRatio
    ];

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IDtoService _dtoService;
    private readonly ILogger<RandomMediaService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RandomMediaService"/> class.
    /// </summary>
    /// <param name="libraryManager">Acceso a la biblioteca (consultas, vistas, elementos).</param>
    /// <param name="userManager">Resolución de usuarios por id.</param>
    /// <param name="dtoService">Convierte <see cref="BaseItem"/> (entidad interna) en <see cref="BaseItemDto"/> (objeto de red).</param>
    /// <param name="logger">Trazas que acaban en el log de Jellyfin.</param>
    public RandomMediaService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IDtoService dtoService,
        ILogger<RandomMediaService> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _dtoService = dtoService;
        _logger = logger;
    }

    /// <inheritdoc />
    public User? ResolveUser(Guid? userId, HttpContext httpContext)
    {
        if (userId is not null && userId != Guid.Empty)
        {
            return _userManager.GetUserById(userId.Value);
        }

        var fromClaims = GetUserIdFromClaims(httpContext.User);
        return fromClaims is null ? null : _userManager.GetUserById(fromClaims.Value);
    }

    /// <inheritdoc />
    public IReadOnlyList<GenreOptionDto> GetGenres(User? user, string? mediaType, Guid? libraryId, int limit)
    {
        var query = BuildQuery(user, mediaType, null, libraryId, unwatchedOnly: false);
        query.Limit = null;
        query.StartIndex = null;
        query.EnableTotalRecordCount = false;

        // ItemCounts hace que Jellyfin rellene cuántas películas/series hay por género.
        query.DtoOptions = new DtoOptions
        {
            Fields = [ItemFields.ItemCounts],
            EnableImages = false,
            EnableUserData = false,
            ImageTypeLimit = 0
        };

        var effectiveLimit = Math.Clamp(limit, 1, 500);

        // GetGenres devuelve tuplas (elemento, recuentos): de ahí sacamos los números.
        return _libraryManager.GetGenres(query)
            .Items
            .Select(tuple => new GenreOptionDto(
                tuple.Item.Name,
                tuple.ItemCounts.MovieCount,
                tuple.ItemCounts.SeriesCount,
                Math.Max(tuple.ItemCounts.TotalItemCount(), tuple.ItemCounts.MovieCount + tuple.ItemCounts.SeriesCount)))
            .Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.TotalCount > 0)
            .OrderByDescending(g => g.TotalCount)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(effectiveLimit)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<LibraryOptionDto> GetLibraries()
    {
        return _libraryManager.GetVirtualFolders()
            .Where(v => Guid.TryParse(v.ItemId, out _))
            .Select(v => new LibraryOptionDto(
                Guid.Parse(v.ItemId),
                v.Name,
                v.CollectionType?.ToString()))
            .ToArray();
    }

    /// <inheritdoc />
    public SpinResultDto Spin(User? user, RouletteQueryOptions options)
    {
        var baseQuery = BuildQuery(
            user,
            options.MediaType,
            options.Genres,
            options.LibraryId,
            options.UnwatchedOnly ?? false);

        // ---- 1) ¿Cuántos elementos cumplen los filtros? (consulta barata, sin cargar filas)
        var countQuery = CloneForCount(baseQuery);
        var total = _libraryManager.GetItemsResult(countQuery).TotalRecordCount;

        var appliedFilters = new RouletteFiltersDto(
            NormalizeMediaType(options.MediaType),
            options.UnwatchedOnly ?? false,
            options.Genres ?? [],
            options.LibraryId,
            ResolveItemTypes(options.MediaType).Select(k => k.ToString()).ToArray());

        if (total <= 0)
        {
            _logger.LogInformation(
                "JellyRoulette: sin coincidencias para {MediaType} genres={Genres} unwatched={Unwatched}",
                appliedFilters.MediaType,
                string.Join('|', appliedFilters.Genres),
                appliedFilters.UnwatchedOnly);

            return new SpinResultDto(null, [], 0, appliedFilters);
        }

        // ---- 2) Sorteamos el punto de partida y pedimos una ventana de candidatos
        var candidateLimit = Math.Clamp(options.Candidates ?? 24, 1, 60);
        var windowSize = Math.Min(candidateLimit, total);
        var startIndex = total <= windowSize ? 0 : Random.Shared.Next(0, total - windowSize + 1);

        var windowQuery = CloneForFetch(baseQuery, startIndex, windowSize);
        var windowItems = _libraryManager.GetItemsResult(windowQuery).Items;

        var items = new List<BaseItem>(windowItems);

        // Si la ventana quedó corta (puede pasar con colecciones/boxsets colapsados),
        // la completamos desde el principio de la lista.
        if (items.Count < windowSize && total > items.Count)
        {
            var fillSize = Math.Min(windowSize - items.Count, total);
            var fillQuery = CloneForFetch(baseQuery, 0, fillSize);
            var seen = items.Select(i => i.Id).ToHashSet();

            foreach (var extra in _libraryManager.GetItemsResult(fillQuery).Items)
            {
                if (seen.Add(extra.Id))
                {
                    items.Add(extra);
                }
            }
        }

        if (items.Count == 0)
        {
            return new SpinResultDto(null, [], total, appliedFilters);
        }

        // ---- 3) El ganador sale de la ventana sorteada: distribución uniforme.
        var winnerItem = items[Random.Shared.Next(items.Count)];

        // Convertimos las entidades internas en DTOs de red (añaden UserData,
        // ImageTags, recuentos, etc. según lo que pedimos en DtoOptions).
        var mapped = _dtoService.GetBaseItemDtos(items, baseQuery.DtoOptions, user);
        var winnerDto = mapped.FirstOrDefault(dto => dto.Id == winnerItem.Id);

        if (winnerDto is null)
        {
            return new SpinResultDto(null, [], total, appliedFilters);
        }

        var winner = MapToDto(winnerDto);
        var candidates = mapped
            .Where(dto => dto.Id != winnerDto.Id)
            .Select(MapToDto)
            .ToArray();

        return new SpinResultDto(winner, candidates, total, appliedFilters);
    }

    /// <summary>
    /// Construye la <see cref="InternalItemsQuery"/> con todos los filtros del usuario.
    /// </summary>
    private InternalItemsQuery BuildQuery(
        User? user,
        string? mediaType,
        string[]? genres,
        Guid? libraryId,
        bool unwatchedOnly)
    {
        var query = new InternalItemsQuery(user)
        {
            Recursive = true,
            IncludeItemTypes = ResolveItemTypes(mediaType),
            ExcludeItemTypes = Extras,
            IsVirtualItem = false,
            CollapseBoxSetItems = false
        };

        if (genres is { Length: > 0 })
        {
            query.Genres = genres
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (unwatchedOnly)
        {
            // IsPlayed solo tiene sentido con un usuario; sin él lo dejamos en null.
            query.IsPlayed = user is null ? null : false;
        }

        if (libraryId is not null && libraryId != Guid.Empty)
        {
            var folder = _libraryManager.GetItemById(libraryId.Value);
            if (folder is null)
            {
                throw new ArgumentException($"La biblioteca {libraryId} no existe en este servidor.", nameof(libraryId));
            }

            query.Parent = folder;
        }

        query.DtoOptions = new DtoOptions
        {
            Fields = DtoFields,
            EnableImages = true,
            EnableUserData = true,
            ImageTypeLimit = 1,
            ImageTypes = [ImageType.Primary]
        };

        return query;
    }

    /// <summary>Copia "barata" de la consulta solo para contar (sin paginación ni DTOs).</summary>
    private static InternalItemsQuery CloneForCount(InternalItemsQuery source)
    {
        return new InternalItemsQuery(source.User)
        {
            Recursive = source.Recursive,
            IncludeItemTypes = source.IncludeItemTypes,
            ExcludeItemTypes = source.ExcludeItemTypes,
            Genres = source.Genres,
            IsPlayed = source.IsPlayed,
            IsVirtualItem = source.IsVirtualItem,
            CollapseBoxSetItems = source.CollapseBoxSetItems,
            ParentId = source.ParentId,
            ParentType = source.ParentType,
            Limit = 0,
            StartIndex = null,
            EnableTotalRecordCount = true,
            DtoOptions = new DtoOptions(false)
        };
    }

    /// <summary>Copia de la consulta para traer una ventana concreta de elementos.</summary>
    private static InternalItemsQuery CloneForFetch(InternalItemsQuery source, int startIndex, int limit)
    {
        var query = CloneForCount(source);
        query.Limit = limit;
        query.StartIndex = startIndex;
        query.EnableTotalRecordCount = true;
        query.DtoOptions = source.DtoOptions;

        // Orden estable: imprescindible para que StartIndex tenga sentido.
        query.OrderBy = [(ItemSortBy.Name, SortOrder.Ascending)];

        return query;
    }

    /// <summary>Traduce el string del frontend a los tipos de elemento de Jellyfin.</summary>
    private static BaseItemKind[] ResolveItemTypes(string? mediaType)
        => NormalizeMediaType(mediaType) switch
        {
            MediaMovies => MovieOnly,
            MediaSeries => SeriesOnly,
            _ => MoviesAndSeries
        };

    /// <summary>Normaliza/valida el parámetro mediaType (tolerante a mayúsculas y errores).</summary>
    private static string NormalizeMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return MediaBoth;
        }

        return mediaType.Trim().ToLowerInvariant() switch
        {
            "movie" or "movies" or "pelicula" or "película" or "peliculas" or "películas" => MediaMovies,
            "series" or "serie" or "tv" or "tvshow" or "show" => MediaSeries,
            _ => MediaBoth
        };
    }

    /// <summary>Convierte la entidad interna en nuestro DTO plano.</summary>
    private static RouletteItemDto MapToDto(BaseItemDto dto)
    {
        return new RouletteItemDto(
            dto.Id,
            dto.Name ?? string.Empty,
            dto.Type.ToString(),
            dto.ProductionYear,
            Truncate(dto.Overview, 600),
            dto.Genres ?? [],
            dto.CommunityRating,
            dto.OfficialRating,
            dto.RunTimeTicks.HasValue
                ? (int)Math.Round(dto.RunTimeTicks.Value / (double)TimeSpan.TicksPerMinute)
                : null,
            dto.ImageTags?.ContainsKey(ImageType.Primary) ?? false,
            dto.UserData?.Played ?? false,
            dto.UserData?.IsFavorite ?? false);
    }

    /// <summary>Ata el texto para no mandar sinopsis kilométricas al móvil.</summary>
    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength - 1), "…");
    }

    /// <summary>
    /// Extrae el id de usuario del token de la petición.
    /// Equivalente a <c>HttpContext.User.GetUserId()</c> del servidor, reimplementado
    /// porque esa extensión no forma parte de los paquetes NuGet públicos.
    /// </summary>
    private static Guid? GetUserIdFromClaims(ClaimsPrincipal principal)
    {
        var value = principal
            .Claims
            .FirstOrDefault(c =>
                c.Type.Equals(UserIdClaimType, StringComparison.OrdinalIgnoreCase)
                || c.Type.Equals(ClaimTypes.NameIdentifier, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}
