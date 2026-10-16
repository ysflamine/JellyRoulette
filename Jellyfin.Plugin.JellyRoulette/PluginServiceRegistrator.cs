using System.Text;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyRoulette;

/// <summary>
/// Punto de enganche con el contenedor de dependencias de Jellyfin.
/// </summary>
/// <remarks>
/// Jellyfin busca en el ensamblado del plugin una clase que implemente
/// <see cref="IPluginServiceRegistrator"/> y la invoca al arrancar (debe tener
/// constructor sin parámetros). Aquí registramos:
/// <list type="bullet">
///   <item><description><see cref="Services.IRandomMediaService"/> → para que el controlador lo reciba inyectado.</description></item>
///   <item><description><see cref="Services.UiInjectionStartupFilter"/> → el middleware que añade nuestro botón a la web.</description></item>
/// </list>
/// </remarks>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<Services.IRandomMediaService, Services.RandomMediaService>();

        // IStartupFilter es el mecanismo estándar de ASP.NET Core para añadir middleware
        // al principio del pipeline sin tocar el código del servidor.
        serviceCollection.AddSingleton<IStartupFilter, Services.UiInjectionStartupFilter>();
    }
}
