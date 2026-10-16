using System.Text;
using Jellyfin.Plugin.JellyRoulette.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyRoulette.Tests;

/// <summary>
/// Pruebas del middleware que inyecta el &lt;script&gt; en /web/index.html.
/// Es la pieza más delicada del plugin: si se equivoca, la web de Jellyfin deja
/// de cargar. Aquí comprobamos que inyecta, que es idempotente, que ignora lo que
/// no debe tocar, que respeta un PathBase y que nunca se traga un error ajeno.
/// </summary>
public class UiInjectionStartupFilterTests
{
    /// <summary>
    /// Construye un pipeline real de ASP.NET Core con nuestro filtro por delante
    /// de un manejador terminal configurable, y ejecuta una petición contra él.
    /// </summary>
    private static async Task<HttpContext> RunAsync(
        string path,
        string method = "GET",
        string? responseBody = SampleHtml,
        string contentType = "text/html",
        int statusCode = 200,
        string? pathBase = null,
        bool downstreamThrows = false)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);

        var filter = new UiInjectionStartupFilter(NullLogger<UiInjectionStartupFilter>.Instance);
        var configure = filter.Configure(app =>
        {
            app.Run(context =>
            {
                if (downstreamThrows)
                {
                    throw new InvalidOperationException("fallo simulado aguas abajo");
                }

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = contentType;
                var bytes = Encoding.UTF8.GetBytes(responseBody ?? string.Empty);
                context.Response.ContentLength = bytes.Length;
                return context.Response.Body.WriteAsync(bytes).AsTask();
            });
        });

        configure(builder);
        var pipeline = builder.Build();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.PathBase = pathBase ?? string.Empty;
        context.Response.Body = new MemoryStream();

        await pipeline(context);

        context.Response.Body.Position = 0;
        return context;
    }

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private const string SampleHtml =
        "<!DOCTYPE html><html><head><title>Jellyfin</title></head>" +
        "<body><div id=\"root\"></div><script src=\"main.js\"></script></body></html>";

    [Fact]
    public async Task InjectsTheLoaderScriptBeforeClosingBody()
    {
        var context = await RunAsync("/web/index.html");
        var body = ReadBody(context);

        Assert.Contains("/JellyRoulette/loader.js", body, StringComparison.Ordinal);
        Assert.Contains("<!--JellyRoulette:injected-->", body, StringComparison.Ordinal);

        // Debe ir JUSTO antes de </body>, no en cualquier sitio.
        var scriptAt = body.IndexOf("/JellyRoulette/loader.js", StringComparison.Ordinal);
        var bodyCloseAt = body.LastIndexOf("</body>", StringComparison.Ordinal);
        Assert.True(scriptAt < bodyCloseAt, "El script debe ir antes de </body>.");

        // El contenido original no se pierde.
        Assert.Contains("<div id=\"root\"></div>", body, StringComparison.Ordinal);
        Assert.Contains("main.js", body, StringComparison.Ordinal);

        // Cabeceras coherentes con el nuevo cuerpo.
        Assert.Equal(Encoding.UTF8.GetByteCount(body), context.Response.ContentLength);
        Assert.False(context.Response.Headers.ContainsKey("ETag"));
        Assert.StartsWith("text/html", context.Response.ContentType, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IsIdempotent_AlreadyInjectedHtmlIsNotTouchedTwice()
    {
        var alreadyInjected = SampleHtml.Replace(
            "</body>",
            "<!--JellyRoulette:injected--><script defer src=\"/JellyRoulette/loader.js\"></script>\n</body>");

        var context = await RunAsync("/web/index.html", responseBody: alreadyInjected);
        var body = ReadBody(context);

        var occurrences = body.Split("/JellyRoulette/loader.js").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Theory]
    [InlineData("/web/index.html")]
    [InlineData("/web/")]
    [InlineData("/web")]
    [InlineData("/jellyfin/web/index.html")]
    public async Task MatchesTheWebAppShellPaths(string path)
    {
        var context = await RunAsync(path);
        Assert.Contains("/JellyRoulette/loader.js", ReadBody(context), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Items/123/Images/Primary")]
    [InlineData("/JellyRoulette/Spin")]
    [InlineData("/system/info/public")]
    [InlineData("/web/manifest.json")]
    public async Task IgnoresEveryOtherRoute(string path)
    {
        var context = await RunAsync(path);
        var body = ReadBody(context);

        Assert.DoesNotContain("JellyRoulette", body, StringComparison.Ordinal);
        Assert.Equal(SampleHtml, body);
    }

    [Fact]
    public async Task IgnoresNonGetRequests()
    {
        var context = await RunAsync("/web/index.html", method: "POST");
        Assert.DoesNotContain("JellyRoulette", ReadBody(context), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PassesThroughNonHtmlResponses()
    {
        var context = await RunAsync("/web/index.html", responseBody: "{\"a\":1}", contentType: "application/json");
        var body = ReadBody(context);

        Assert.Equal("{\"a\":1}", body);
        Assert.DoesNotContain("JellyRoulette", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PassesThroughErrorResponses()
    {
        var context = await RunAsync("/web/index.html", responseBody: "nope", statusCode: 404);

        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("nope", ReadBody(context));
    }

    [Fact]
    public async Task RespectsPathBase()
    {
        var context = await RunAsync("/web/index.html", pathBase: "/jellyfin");
        var body = ReadBody(context);

        Assert.Contains("/jellyfin/JellyRoulette/loader.js", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StripsRangeHeadersSoTheRewriteIsComplete()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        string? seenRange = "not-read";

        var filter = new UiInjectionStartupFilter(NullLogger<UiInjectionStartupFilter>.Instance);
        filter.Configure(app =>
        {
            app.Run(context =>
            {
                seenRange = context.Request.Headers["Range"].ToString();
                context.Response.ContentType = "text/html";
                var bytes = Encoding.UTF8.GetBytes(SampleHtml);
                return context.Response.Body.WriteAsync(bytes).AsTask();
            });
        })(builder);

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.Path = "/web/index.html";
        context.Request.Headers["Range"] = "bytes=0-10";
        context.Request.Headers["Accept-Encoding"] = "gzip";
        context.Response.Body = new MemoryStream();

        await builder.Build()(context);

        Assert.True(string.IsNullOrEmpty(seenRange), "Range debe eliminarse para poder reescribir la respuesta.");
        Assert.Contains("/JellyRoulette/loader.js", ReadBody(context), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RethrowsDownstreamFailuresInsteadOfSwallowingThem()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunAsync("/web/index.html", downstreamThrows: true));
    }
}
