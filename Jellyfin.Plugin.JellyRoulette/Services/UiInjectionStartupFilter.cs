using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRoulette.Services;

/// <summary>
/// Middleware que inyecta el script de JellyRoulette en la interfaz web de Jellyfin.
/// </summary>
/// <remarks>
/// <para><b>Contexto histórico (importante para entender por qué se hace así):</b></para>
/// <para>
/// En Jellyfin 10.8/10.9 los plugins añadían JavaScript a la web con el atributo
/// <c>[WebEntryPoint]</c> y con <c>IPluginPage.InsertAfter</c>. Ese mecanismo
/// <b>ya no existe</b> en 10.10+ ni en 12.x: lo único que queda en la API pública es
/// <c>IHasWebPages</c>, que sirve páginas HTML sueltas para el panel de administración
/// (<c>/web/ConfigurationPage?name=...</c>), no para la interfaz de usuario.
/// </para>
/// <para>
/// La solución moderna (y la que usan los plugins de inyección de la comunidad) es
/// interceptar la respuesta de <c>/web/index.html</c> y añadir una etiqueta
/// <c>&lt;script&gt;</c> antes de <c>&lt;/body&gt;</c>. Se hace con un
/// <see cref="IStartupFilter"/>, que es hosting estándar de ASP.NET Core: funciona igual
/// en Jellyfin 10.11 (.NET 9) y en Jellyfin 12 (.NET 10) y <b>no escribe nada en disco</b>
/// (nada de permisos rotos en Docker).
/// </para>
/// <para>Reglas de seguridad que respeta este filtro:</para>
/// <list type="bullet">
///   <item><description>Solo toca GET de <c>/web</c>, <c>/web/</c> o <c>/web/index.html</c>.</description></item>
///   <item><description>Es idempotente: si la marca ya está, no la vuelve a poner.</description></item>
///   <item><description>Si algo falla, sirve el HTML original intacto. Nunca rompe la web.</description></item>
///   <item><description>Se puede desactivar desde los ajustes del plugin.</description></item>
/// </list>
/// </remarks>
public sealed class UiInjectionStartupFilter : IStartupFilter
{
    /// <summary>Marca que deja el HTML ya inyectado (para no duplicar el script).</summary>
    private const string Marker = "<!--JellyRoulette:injected-->";

    private readonly ILogger<UiInjectionStartupFilter> _logger;
    private int _loggedOnce;

    /// <summary>
    /// Initializes a new instance of the <see cref="UiInjectionStartupFilter"/> class.
    /// </summary>
    /// <param name="logger">Registro del servidor.</param>
    public UiInjectionStartupFilter(ILogger<UiInjectionStartupFilter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Se registra ANTES del resto del pipeline (next(app) al final) para que nuestro
            // middleware sea el más externo: así podemos quitar Accept-Encoding y estar
            // seguros de que lo que llega es texto plano, no gzip.
            app.Use(InvokeAsync);
            next(app);
        };
    }

    /// <summary>Cuerpo del middleware.</summary>
    private async Task InvokeAsync(HttpContext context, Func<Task> nextMiddleware)
    {
        if (!IsWebIndexRequest(context.Request.Path.Value)
            || !HttpMethods.IsGet(context.Request.Method))
        {
            await nextMiddleware().ConfigureAwait(false);
            return;
        }

        // Solo desactivamos la inyección si el administrador lo pide explícitamente.
        // Si el plugin aún no ha terminado de cargarse (Instance == null), inyectamos:
        // es lo que el usuario espera al instalarlo.
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is not null && !configuration.EnableUiInjection)
        {
            await nextMiddleware().ConfigureAwait(false);
            return;
        }

        // Quitamos compresión y peticiones parciales para poder leer y reescribir el HTML.
        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("Range");
        context.Request.Headers.Remove("If-Range");

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await nextMiddleware().ConfigureAwait(false);
        }
        catch
        {
            // Si falla algo "aguas abajo" no es nuestro problema: restauramos el stream y
            // relanzamos para que el manejador de errores de Jellyfin responda como siempre.
            context.Response.Body = originalBody;
            throw;
        }

        context.Response.Body = originalBody;
        buffer.Seek(0, SeekOrigin.Begin);

        var isHtml = context.Response.StatusCode == StatusCodes.Status200OK
            && (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false);

        if (!isHtml)
        {
            // 304 (no modificado), redirecciones, respuestas que no son HTML → tal cual.
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            return;
        }

        string html;
        using (var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024, leaveOpen: true))
        {
            html = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        try
        {
            var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);

            if (bodyClose >= 0 && !html.Contains(Marker, StringComparison.OrdinalIgnoreCase))
            {
                // PathBase respeta instalaciones bajo una subcarpeta (p. ej. /jellyfin/web/).
                var baseUrl = context.Request.PathBase.Value ?? string.Empty;
                var version = Plugin.VersionString;

                var snippet = new StringBuilder(256)
                    .Append(Marker)
                    .Append("<script defer src=\"")
                    .Append(baseUrl)
                    .Append("/JellyRoulette/loader.js?v=")
                    .Append(version)
                    .Append("\"></script>")
                    .ToString();

                html = string.Concat(html.AsSpan(0, bodyClose), snippet, "\n", html.AsSpan(bodyClose));

                if (Interlocked.Exchange(ref _loggedOnce, 1) == 0)
                {
                    _logger.LogInformation("JellyRoulette: script inyectado en la interfaz web (v{Version}).", version);
                }
            }
        }
        catch (Exception ex)
        {
            // Nunca rompemos index.html: ante cualquier error servimos lo que haya.
            _logger.LogWarning(ex, "JellyRoulette: fallo al inyectar el script (se sirve el HTML original).");
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = bytes.Length;

        // El contenido cambió, así que los validadores del archivo estático ya no valen.
        context.Response.Headers.Remove("ETag");
        context.Response.Headers.Remove("Last-Modified");
        context.Response.Headers.Remove("Accept-Ranges");

        await originalBody.WriteAsync(bytes).ConfigureAwait(false);
    }

    /// <summary>
    /// Detecta la petición del "caparazón" de la web, también cuando Jellyfin está
    /// publicado bajo un prefijo de ruta.
    /// </summary>
    private static bool IsWebIndexRequest(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web", StringComparison.OrdinalIgnoreCase);
    }
}
