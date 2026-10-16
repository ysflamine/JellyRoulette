using System.Security.Claims;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellyRoulette.Models;
using Jellyfin.Plugin.JellyRoulette.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace JellyRoulette.Tests;

/// <summary>
/// Pruebas del motor de la ruleta. Verifican el contrato que el frontend asume:
/// filtros correctos, aleatoriedad uniforme y mapeo estable de los DTO.
/// </summary>
public class RandomMediaServiceTests
{
    private readonly ILibraryManager _libraryManager = Substitute.For<ILibraryManager>();
    private readonly IUserManager _userManager = Substitute.For<IUserManager>();
    private readonly IDtoService _dtoService = Substitute.For<IDtoService>();

    private RandomMediaService CreateService()
        => new(_libraryManager, _userManager, _dtoService, NullLogger<RandomMediaService>.Instance);

    private static Movie NewMovie(string name = "Movie")
        => new() { Id = Guid.NewGuid(), Name = name };

    private static Series NewSeries(string name = "Series")
        => new() { Id = Guid.NewGuid(), Name = name };

    /// <summary>Registra qué consultas recibe GetItemsResult, en orden.</summary>
    private List<InternalItemsQuery> CaptureQueries(params QueryResult<BaseItem>[] results)
    {
        var captured = new List<InternalItemsQuery>();
        var call = 0;

        _libraryManager
            .When(x => x.GetItemsResult(Arg.Any<InternalItemsQuery>()))
            .Do(ci => captured.Add(ci.Arg<InternalItemsQuery>()));

        _libraryManager
            .GetItemsResult(Arg.Any<InternalItemsQuery>())
            .Returns(_ => results[Math.Min(call++, results.Length - 1)]);

        return captured;
    }

    private void StubDtos()
    {
        _dtoService
            .GetBaseItemDtos(Arg.Any<IReadOnlyList<BaseItem>>(), Arg.Any<DtoOptions>(), Arg.Any<User?>(), Arg.Any<BaseItem?>())
            .Returns(ci => ci.Arg<IReadOnlyList<BaseItem>>()
                .Select(i => new BaseItemDto
                {
                    Id = i.Id,
                    Name = i.Name,
                    Type = i is Series ? BaseItemKind.Series : BaseItemKind.Movie,
                    ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = "tag" }
                })
                .ToArray());
    }

    [Fact]
    public void Spin_WithoutMatches_ReturnsNullWinnerAndZeroTotal()
    {
        CaptureQueries(new QueryResult<BaseItem>(0, 0, []));
        StubDtos();

        var result = CreateService().Spin(null, new RouletteQueryOptions("movie", null, null, null, null));

        Assert.Null(result.Winner);
        Assert.Equal(0, result.TotalMatches);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Spin_UsesTwoQueries_CountThenWindow()
    {
        // La ventana debe venir completa: si el servidor devuelve menos elementos de
        // los pedidos, el servicio lanza una tercera consulta de relleno (probado en
        // Spin_ShortWindow_IsCompletedFromTheBeginning).
        var movies = Enumerable.Range(0, 20).Select(i => (BaseItem)NewMovie("M" + i)).ToArray();

        var captured = CaptureQueries(
            new QueryResult<BaseItem>(0, 500, []),           // 1ª llamada: solo recuento
            new QueryResult<BaseItem>(10, 500, movies));     // 2ª llamada: la ventana
        StubDtos();

        var result = CreateService().Spin(null, new RouletteQueryOptions("movie", null, null, null, 20));

        Assert.Equal(2, captured.Count);

        // La primera consulta no debe traer filas: solo contar.
        Assert.Equal(0, captured[0].Limit);
        Assert.True(captured[0].EnableTotalRecordCount);

        // La segunda trae una ventana aleatoria dentro del total.
        Assert.NotNull(captured[1].Limit);
        Assert.NotNull(captured[1].StartIndex);
        Assert.InRange(captured[1].StartIndex!.Value, 0, 500 - movies.Length);
        Assert.True(captured[1].OrderBy.Count > 0, "La ventana necesita un orden estable para que StartIndex tenga sentido.");

        Assert.Equal(500, result.TotalMatches);
    }

    [Fact]
    public void Spin_WinnerComesFromTheWindow_AndIsNotDuplicatedInCandidates()
    {
        var items = Enumerable.Range(0, 6).Select(i => (BaseItem)NewMovie("M" + i)).ToArray();

        CaptureQueries(
            new QueryResult<BaseItem>(0, 6, []),
            new QueryResult<BaseItem>(0, 6, items));
        StubDtos();

        var result = CreateService().Spin(null, new RouletteQueryOptions(null, null, null, null, 10));

        Assert.NotNull(result.Winner);
        Assert.Contains(result.Winner!.Id, items.Select(i => i.Id));
        Assert.DoesNotContain(result.Winner.Id, result.Candidates.Select(c => c.Id));
        Assert.Equal(items.Length - 1, result.Candidates.Count);
    }

    [Fact]
    public void Spin_IsUniformAcrossTheWholeFilteredSet()
    {
        // Con 4 elementos y 4000 tiradas, cada uno debe salir ~1000 veces.
        var items = Enumerable.Range(0, 4).Select(i => (BaseItem)NewMovie("M" + i)).ToArray();

        CaptureQueries(
            new QueryResult<BaseItem>(0, 4, []),
            new QueryResult<BaseItem>(0, 4, items));
        StubDtos();

        var service = CreateService();
        var hits = new Dictionary<Guid, int>();

        for (var i = 0; i < 4000; i++)
        {
            var winner = service.Spin(null, new RouletteQueryOptions(null, null, null, null, 10)).Winner;
            Assert.NotNull(winner);
            hits[winner!.Id] = hits.GetValueOrDefault(winner.Id) + 1;
        }

        Assert.Equal(items.Length, hits.Count);
        foreach (var item in items)
        {
            Assert.InRange(hits[item.Id], 850, 1150);
        }
    }

    [Fact]
    public void Spin_UnwatchedOnly_SetsIsPlayedFalse()
    {
        var captured = CaptureQueries(new QueryResult<BaseItem>(0, 0, []));
        StubDtos();
        var user = new User("tester", "provider", "reset");

        CreateService().Spin(user, new RouletteQueryOptions(null, true, null, null, null));

        Assert.False(captured[0].IsPlayed);
        Assert.Same(user, captured[0].User);
    }

    [Fact]
    public void Spin_WithoutUnwatchedFilter_DoesNotFilterByPlayed()
    {
        var captured = CaptureQueries(new QueryResult<BaseItem>(0, 0, []));
        StubDtos();

        CreateService().Spin(null, new RouletteQueryOptions(null, false, null, null, null));

        Assert.Null(captured[0].IsPlayed);
    }

    [Theory]
    [InlineData("movie", BaseItemKind.Movie)]
    [InlineData("series", BaseItemKind.Series)]
    [InlineData("MOVIES", BaseItemKind.Movie)]
    [InlineData("tv", BaseItemKind.Series)]
    [InlineData("nonsense", BaseItemKind.Movie)]
    [InlineData("", BaseItemKind.Movie)]
    public void Spin_MediaTypeIsNormalized(string input, BaseItemKind expectedFirstType)
    {
        var captured = CaptureQueries(new QueryResult<BaseItem>(0, 0, []));
        StubDtos();

        var result = CreateService().Spin(null, new RouletteQueryOptions(input, null, null, null, null));

        Assert.Contains(expectedFirstType, captured[0].IncludeItemTypes);
        Assert.Contains(result.Filters.IncludeItemTypes, t => t == expectedFirstType.ToString());
    }

    [Fact]
    public void Spin_BothMediaTypes_IncludesMovieAndSeries()
    {
        var captured = CaptureQueries(new QueryResult<BaseItem>(0, 0, []));
        StubDtos();

        CreateService().Spin(null, new RouletteQueryOptions("both", null, null, null, null));

        Assert.Contains(BaseItemKind.Movie, captured[0].IncludeItemTypes);
        Assert.Contains(BaseItemKind.Series, captured[0].IncludeItemTypes);
    }

    [Fact]
    public void Spin_AlwaysExcludesExtrasAndRequestsRecursion()
    {
        var captured = CaptureQueries(new QueryResult<BaseItem>(0, 0, []));
        StubDtos();

        CreateService().Spin(null, new RouletteQueryOptions(null, null, null, null, null));

        var query = captured[0];
        Assert.True(query.Recursive, "Sin Recursive la consulta solo miraría la raíz.");
        Assert.Contains(BaseItemKind.Trailer, query.ExcludeItemTypes);
        Assert.Contains(BaseItemKind.BoxSet, query.ExcludeItemTypes);
        Assert.False(query.IsVirtualItem!.Value, "Los elementos virtuales (no emitidos) no deben salir.");
        Assert.True(query.DtoOptions.EnableImages, "Sin imágenes la ruleta no tiene pósters.");
        Assert.True(query.DtoOptions.EnableUserData, "Sin UserData no sabemos si está vista.");
    }

    [Fact]
    public void Spin_GenresAreTrimmedAndDeduplicated()
    {
        var captured = CaptureQueries(new QueryResult<BaseItem>(0, 0, []));
        StubDtos();

        CreateService().Spin(null, new RouletteQueryOptions(
            null, null, null, ["  Horror ", "horror", "Comedy", ""], null));

        Assert.Equal(2, captured[0].Genres.Count);
        Assert.Contains("Horror", captured[0].Genres);
        Assert.Contains("Comedy", captured[0].Genres);
    }

    [Fact]
    public void Spin_UnknownLibrary_ThrowsArgumentException()
    {
        _libraryManager.GetItemById(Arg.Any<Guid>()).Returns((BaseItem?)null);

        Assert.Throws<ArgumentException>(() =>
            CreateService().Spin(null, new RouletteQueryOptions(null, null, Guid.NewGuid(), null, null)));
    }

    [Fact]
    public void Spin_KnownLibrary_ScopesTheQueryToThatFolder()
    {
        var folder = new MediaBrowser.Controller.Entities.Folder { Id = Guid.NewGuid(), Name = "Películas" };
        _libraryManager.GetItemById(folder.Id).Returns(folder);

        var captured = CaptureQueries(new QueryResult<BaseItem>(0, 0, []));
        StubDtos();

        CreateService().Spin(null, new RouletteQueryOptions(null, null, folder.Id, null, null));

        Assert.Equal(folder.Id, captured[0].ParentId);
    }

    [Fact]
    public void Spin_ShortWindow_IsCompletedFromTheBeginning()
    {
        var first = Enumerable.Range(0, 2).Select(i => (BaseItem)NewMovie("A" + i)).ToArray();
        var fill = Enumerable.Range(0, 3).Select(i => (BaseItem)NewMovie("B" + i)).ToArray();

        CaptureQueries(
            new QueryResult<BaseItem>(0, 30, []),            // recuento
            new QueryResult<BaseItem>(28, 30, first),        // ventana corta (solo 2 de 5)
            new QueryResult<BaseItem>(0, 30, fill));         // relleno desde el inicio
        StubDtos();

        var result = CreateService().Spin(null, new RouletteQueryOptions(null, null, null, null, 5));

        Assert.NotNull(result.Winner);
        // 2 de la ventana + 3 del relleno = 5, sin duplicados.
        Assert.Equal(5, result.Candidates.Count + 1);
        Assert.Equal(5, result.Candidates.Select(c => c.Id).Append(result.Winner!.Id).Distinct().Count());
    }

    [Fact]
    public void GetGenres_SortsByTotalCountAndMapsCounts()
    {
        var horror = new Genre { Id = Guid.NewGuid(), Name = "Horror" };
        var comedy = new Genre { Id = Guid.NewGuid(), Name = "Comedy" };
        var empty = new Genre { Id = Guid.NewGuid(), Name = " " };

        _libraryManager.GetGenres(Arg.Any<InternalItemsQuery>()).Returns(new QueryResult<(BaseItem, ItemCounts)>(
        [
            (horror, new ItemCounts { MovieCount = 10, SeriesCount = 2 }),
            (comedy, new ItemCounts { MovieCount = 90, SeriesCount = 5 }),
            (empty, new ItemCounts { MovieCount = 0, SeriesCount = 0 })
        ]));

        var genres = CreateService().GetGenres(null, null, null, 40);

        Assert.Equal(2, genres.Count);                        // el género vacío se descarta
        Assert.Equal("Comedy", genres[0].Name);               // el más numeroso primero
        Assert.Equal(95, genres[0].TotalCount);
        Assert.Equal(90, genres[0].MovieCount);
        Assert.Equal("Horror", genres[1].Name);
    }

    [Fact]
    public void GetGenres_RespectsTheLimit()
    {
        var items = Enumerable.Range(0, 50)
            .Select(i => ((BaseItem)new Genre { Id = Guid.NewGuid(), Name = "G" + i }, new ItemCounts { MovieCount = 100 - i }))
            .ToArray();

        _libraryManager.GetGenres(Arg.Any<InternalItemsQuery>())
            .Returns(new QueryResult<(BaseItem, ItemCounts)>(items));

        Assert.Equal(12, CreateService().GetGenres(null, null, null, 12).Count);
    }

    [Fact]
    public void GetLibraries_SkipsEntriesWithoutValidId()
    {
        _libraryManager.GetVirtualFolders().Returns(
        [
            new MediaBrowser.Model.Entities.VirtualFolderInfo { ItemId = Guid.NewGuid().ToString("N"), Name = "Películas", CollectionType = CollectionTypeOptions.movies },
            new MediaBrowser.Model.Entities.VirtualFolderInfo { ItemId = "not-a-guid", Name = "Rota" }
        ]);

        var libraries = CreateService().GetLibraries();

        Assert.Single(libraries);
        Assert.Equal("Películas", libraries[0].Name);
        Assert.Equal("movies", libraries[0].CollectionType);
    }

    [Fact]
    public void ResolveUser_PrefersTheExplicitUserId()
    {
        var user = new User("explicit", "provider", "reset");
        _userManager.GetUserById(user.Id).Returns(user);

        var resolved = CreateService().ResolveUser(user.Id, new DefaultHttpContext());

        Assert.NotNull(resolved);
        Assert.Equal(user.Id, resolved!.Id);
    }

    [Fact]
    public void ResolveUser_FallsBackToTheJellyfinUserIdClaim()
    {
        var user = new User("claims", "provider", "reset");
        _userManager.GetUserById(user.Id).Returns(user);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("Jellyfin-UserId", user.Id.ToString("N"))],
            authenticationType: "CustomAuthentication"));

        var resolved = CreateService().ResolveUser(null, new DefaultHttpContext { User = principal });

        Assert.NotNull(resolved);
        Assert.Equal(user.Id, resolved!.Id);
    }

    [Fact]
    public void ResolveUser_WithoutUserIdOrClaims_ReturnsNull()
    {
        Assert.Null(CreateService().ResolveUser(null, new DefaultHttpContext()));
    }

    [Fact]
    public void Dto_MappingFillsWhatTheUiNeeds()
    {
        var movie = NewMovie("Blade Runner");
        movie.ProductionYear = 1982;
        movie.Overview = new string('x', 900);
        movie.RunTimeTicks = TimeSpan.FromMinutes(117).Ticks;
        movie.Genres = ["Science Fiction", "Thriller"];
        movie.CommunityRating = 8.1f;
        movie.OfficialRating = "R";

        CaptureQueries(
            new QueryResult<BaseItem>(0, 1, []),
            new QueryResult<BaseItem>(0, 1, [movie]));

        _dtoService
            .GetBaseItemDtos(Arg.Any<IReadOnlyList<BaseItem>>(), Arg.Any<DtoOptions>(), Arg.Any<User?>(), Arg.Any<BaseItem?>())
            .Returns(ci => ci.Arg<IReadOnlyList<BaseItem>>().Select(i => new BaseItemDto
            {
                Id = i.Id,
                Name = i.Name,
                Type = BaseItemKind.Movie,
                ProductionYear = i.ProductionYear,
                Overview = i.Overview,
                RunTimeTicks = i.RunTimeTicks,
                Genres = i.Genres,
                CommunityRating = i.CommunityRating,
                OfficialRating = i.OfficialRating,
                ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = "abc" },
                UserData = new UserItemDataDto { Key = "k", Played = true, IsFavorite = true }
            }).ToArray());

        var winner = CreateService().Spin(null, new RouletteQueryOptions("movie", null, null, null, 10)).Winner;

        Assert.NotNull(winner);
        Assert.Equal("Blade Runner", winner!.Name);
        Assert.Equal("Movie", winner.Type);
        Assert.Equal(1982, winner.Year);
        Assert.Equal(117, winner.RunTimeMinutes);
        Assert.Equal(8.1f, winner.CommunityRating);
        Assert.Equal("R", winner.OfficialRating);
        Assert.True(winner.HasPrimaryImage);
        Assert.True(winner.Played);
        Assert.True(winner.IsFavorite);
        Assert.Equal(["Science Fiction", "Thriller"], winner.Genres);
        Assert.True(winner.Overview!.Length <= 600, "La sinopsis debe llegar recortada al móvil.");
    }
}
