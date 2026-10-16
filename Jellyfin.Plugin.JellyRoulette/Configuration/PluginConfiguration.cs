using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyRoulette.Configuration;

/// <summary>
/// Cuándo debe mostrarse el botón flotante de la ruleta.
/// </summary>
public enum FloatingButtonMode
{
    /// <summary>
    /// Automático: en pantallas pequeñas o cuando falla la inyección en la cabecera.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Siempre visible.
    /// </summary>
    Always = 1,

    /// <summary>
    /// Nunca.
    /// </summary>
    Never = 2
}

/// <summary>
/// Opciones del plugin. Todo lo que pongas aquí se guarda automáticamente en
/// <c>&lt;datos de Jellyfin&gt;/configurations/JellyRoulette_&lt;guid&gt;.xml</c>
/// y se edita desde la página de ajustes (Panel → Plugins → JellyRoulette).
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        EnableUiInjection = true;
        FloatingButtonMode = FloatingButtonMode.Auto;
        DefaultMediaType = "both";
        DefaultUnwatchedOnly = false;
        SpinDurationMs = 4200;
        WheelCandidates = 28;
        MaxGenreChips = 40;
    }

    /// <summary>
    /// Gets or sets a value indicating whether el plugin inyecta su botón/interfaz
    /// en la web de Jellyfin. Apágalo si quieres usar solo la API o la página suelta.
    /// </summary>
    public bool EnableUiInjection { get; set; }

    /// <summary>
    /// Gets or sets cuándo se muestra el botón flotante (FAB).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description><c>Auto</c> (por defecto): en pantallas pequeñas —donde el pulgar llega
    ///   mejor al FAB que a la cabecera— o cuando no se ha podido colocar el botón en la
    ///   cabecera (temas muy personalizados). En escritorio con cabecera correcta, no aparece.</description></item>
    ///   <item><description><c>Always</c>: siempre visible, también en escritorio.</description></item>
    ///   <item><description><c>Never</c>: nunca; solo el botón de la cabecera/menú.</description></item>
    /// </list>
    /// </remarks>
    public FloatingButtonMode FloatingButtonMode { get; set; }

    /// <summary>
    /// Gets or sets el tipo de medio por defecto: <c>movie</c>, <c>series</c> o <c>both</c>.
    /// </summary>
    public string DefaultMediaType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether el filtro "solo no vistos" viene activado por defecto.
    /// </summary>
    public bool DefaultUnwatchedOnly { get; set; }

    /// <summary>
    /// Gets or sets la duración (ms) del giro de la ruleta. La animación del frontal
    /// la usa como referencia para no durar ni muy poco ni demasiado.
    /// </summary>
    public int SpinDurationMs { get; set; }

    /// <summary>
    /// Gets or sets cuántos pósters "candidatos" se descargan para llenar la ruleta.
    /// Más candidatos = giro más vistoso, pero más datos transferidos.
    /// </summary>
    public int WheelCandidates { get; set; }

    /// <summary>
    /// Gets or sets el máximo de géneros que se muestran como botones (chips).
    /// </summary>
    public int MaxGenreChips { get; set; }
}
