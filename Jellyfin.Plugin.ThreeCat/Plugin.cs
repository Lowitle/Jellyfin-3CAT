using System.Globalization;
using Jellyfin.Plugin.ThreeCat.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ThreeCat;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The plugin identifier. Must match the <see cref="System.Runtime.InteropServices.GuidAttribute"/>
    /// applied to the assembly and the <c>guid</c> field in <c>build.yaml</c>.
    /// </summary>
    public const string PluginId = "d8b0f3a2-6c4e-4f0a-9e1b-2c3d4e5f6a7b";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        MigrateConfiguration(Configuration);
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "3Cat";

    /// <inheritdoc />
    public override string Description =>
        "Genera una biblioteca de series a partir del catálogo a la carta de 3Cat (.strm + metadatos + subtítulos).";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(PluginId);

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        ];
    }

    /// <summary>
    /// Brings the stored settings forward to the current schema.
    /// </summary>
    /// <remarks>
    /// The plugin ships a single schema so far. The migration only repairs the collections that the
    /// XML serializer leaves as <c>null</c> when it reads a configuration written by an older build,
    /// which would otherwise make the refresh task throw a <see cref="NullReferenceException"/>.
    /// </remarks>
    /// <param name="configuration">Stored configuration.</param>
    private void MigrateConfiguration(PluginConfiguration configuration)
    {
        var changed = false;

        if (configuration.Programs is null)
        {
            configuration.Programs = [];
            changed = true;
        }

        if (configuration.SubtitleLanguages is null)
        {
            configuration.SubtitleLanguages = ["ca"];
            changed = true;
        }

        if (configuration.ConfigurationVersion < PluginConfiguration.CurrentVersion)
        {
            configuration.ConfigurationVersion = PluginConfiguration.CurrentVersion;
            changed = true;
        }

        if (changed)
        {
            UpdateConfiguration(configuration);
        }
    }
}
