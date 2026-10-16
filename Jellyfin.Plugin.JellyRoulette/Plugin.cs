using System.Globalization;
using Jellyfin.Plugin.JellyRoulette.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyRoulette;

/// <summary>
/// Clase principal del plugin. Jellyfin la descubre automáticamente al arrancar:
/// busca en la carpeta <c>plugins/</c> cualquier ensamblado que contenga una clase
/// pública que herede de <see cref="BasePlugin{TConfigurationType}"/>.
/// </summary>
/// <remarks>
/// Se implementan dos interfaces:
/// <list type="bullet">
///   <item><description><see cref="BasePlugin{T}"/> → le da al plugin su nombre, GUID, versión y una configuración persistente en XML.</description></item>
///   <item><description><see cref="IHasWebPages"/> → registra páginas HTML embebidas. Es la ÚNICA vía oficial que queda en Jellyfin 10.10+ para
///   servir HTML de un plugin (el viejo atributo <c>[WebEntryPoint]</c> ya no existe).</description></item>
/// </list>
/// </remarks>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Nombre del recurso embebido con la página de ajustes del panel de administración.
    /// Ruta lógica = RootNamespace + carpeta + archivo.
    /// </summary>
    internal const string ConfigPageResource = "Jellyfin.Plugin.JellyRoulette.Configuration.configPage.html";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Rutas de datos/config del servidor (inyectado por el contenedor DI de Jellyfin).</param>
    /// <param name="xmlSerializer">Serializador que usa Jellyfin para guardar la configuración en <c>configurations/*.xml</c>.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the instance singleton. Se usa desde el middleware y los controladores
    /// cuando no resulta cómodo inyectar el plugin por constructor.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "JellyRoulette";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("f299f82b-8485-4547-be7c-14e0e10e3b2c");

    /// <inheritdoc />
    public override string Description =>
        "Ruleta aleatoria de películas y series integrada en la interfaz web de Jellyfin.";

    /// <summary>
    /// Gets the versión del ensamblado, expuesta en la página de ajustes para depurar.
    /// </summary>
    public static string VersionString =>
        typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <inheritdoc />
    /// <remarks>
    /// Cada <see cref="PluginPageInfo"/> se convierte en:
    /// <list type="bullet">
    ///   <item><description>Una entrada en <c>GET /web/ConfigurationPages</c> (el listado que consume la web).</description></item>
    ///   <item><description>Una página servida en <c>GET /web/ConfigurationPage?name=Nombre</c>.</description></item>
    ///   <item><description>Si <c>EnableInMainMenu = true</c>, un enlace con <c>MenuIcon</c> en el cajón "Plugins" del panel de administración.</description></item>
    /// </list>
    /// </remarks>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            // Página de configuración (Panel → Plugins → JellyRoulette → Ajustes).
            // Es solo para administradores: Jellyfin protege el listado de páginas
            // con la política "RequiresElevation".
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = "JellyRoulette",
                EmbeddedResourcePath = ConfigPageResource,
                EnableInMainMenu = true,
                MenuSection = "Plugins",
                // Nombre de un icono de Material Icons (la web usa esa fuente).
                MenuIcon = "casino"
            }
        ];
    }

    /// <summary>
    /// Helper para construir nombres de recursos embebidos sin repetir literales.
    /// </summary>
    /// <param name="fileName">Nombre del archivo dentro de la carpeta Web.</param>
    /// <returns>La ruta lógica del recurso embebido.</returns>
    internal static string WebResource(string fileName)
        => string.Format(CultureInfo.InvariantCulture, "{0}.Web.{1}", typeof(Plugin).Namespace, fileName);
}
