using Jellyfin.Plugin.ThreeCat.Sync;
using Jellyfin.Plugin.ThreeCat.Tasks;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ThreeCat;

/// <summary>
/// Registers the plugin services in the Jellyfin container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <summary>
    /// User agent sent to 3Cat.
    /// </summary>
    public const string UserAgent = "JellyfinThreeCat/1.0 (+https://github.com/jellyfin)";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient<ThreeCatClient>(client => ConfigureClient(client, "https://api.3cat.cat/"));

        serviceCollection.AddSingleton<ThreeCatSyncService>();
        serviceCollection.AddSingleton<IScheduledTask, ThreeCatRefreshTask>();
    }

    private static void ConfigureClient(HttpClient client, string baseAddress)
    {
        client.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(100);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ca-ES,ca;q=0.9,es;q=0.8");
    }
}
