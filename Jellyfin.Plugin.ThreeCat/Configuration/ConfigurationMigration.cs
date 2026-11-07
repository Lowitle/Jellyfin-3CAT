using Jellyfin.Plugin.ThreeCat.Sync;

namespace Jellyfin.Plugin.ThreeCat.Configuration;

/// <summary>
/// Brings the stored settings forward to the current schema.
/// </summary>
/// <remarks>
/// The migration repairs the collections that the XML serializer leaves as <c>null</c> when it reads a
/// configuration written by an older build, which would otherwise make the refresh task throw a
/// <see cref="NullReferenceException"/>, and stamps the version of the schema: version 2 no longer has
/// subtitle languages (every published track is downloaded), so the elements written by version 1 are
/// dropped when the configuration is saved again.
/// </remarks>
internal static class ConfigurationMigration
{
    /// <summary>
    /// Brings the configuration forward to the schema of this build.
    /// </summary>
    /// <param name="configuration">Stored configuration.</param>
    /// <returns><c>true</c> when the configuration has to be saved again.</returns>
    /// <remarks>
    /// <para>
    /// The folder layout is only turned off for a configuration written before it existed
    /// (<see cref="PluginConfiguration.LayoutVersion"/>). Raising
    /// <see cref="PluginConfiguration.CurrentVersion"/> for an unrelated change must never undo the
    /// layout the user asked for: the programs would be written back into the flat destination folder
    /// while the libraries index the folders of the kinds, so the library of the plugin would show
    /// nothing and the series of the old layout would stay where they are.
    /// </para>
    /// <para>
    /// A fresh installation never goes through here with an older version: the constructor of
    /// <see cref="PluginConfiguration"/> already carries the current version and the organised layout.
    /// </para>
    /// </remarks>
    internal static bool Migrate(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var changed = false;

        if (configuration.Programs is null)
        {
            configuration.Programs = [];
            changed = true;
        }

        // The sub-libraries of 1.10.0.0 are a list the XML serializer leaves as null when it reads a
        // configuration written by an older build.
        if (configuration.KindLibraries is null)
        {
            configuration.KindLibraries = [];
            changed = true;
        }

        // A configuration written before the dataset existed (or with the field emptied by hand) points
        // at the dataset the plugin publishes.
        if (string.IsNullOrWhiteSpace(configuration.OpenTvIdsUrl))
        {
            configuration.OpenTvIdsUrl = OpenTvIdsClient.DefaultUrl;
            changed = true;
        }

        // The folder layout of 1.9.0.0 moves every program to a folder of its kind, and Jellyfin reads the
        // moved series again: a configuration written by an older build keeps the flat destination folder
        // until the user asks for the change from the page. A fresh installation starts organised.
        if (configuration.ConfigurationVersion < PluginConfiguration.LayoutVersion)
        {
            configuration.OrganizeByKind = false;
            changed = true;
        }

        if (configuration.ConfigurationVersion < PluginConfiguration.CurrentVersion)
        {
            configuration.ConfigurationVersion = PluginConfiguration.CurrentVersion;
            changed = true;
        }

        return changed;
    }
}
