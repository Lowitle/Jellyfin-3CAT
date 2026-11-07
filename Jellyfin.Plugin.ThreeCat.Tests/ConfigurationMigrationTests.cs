using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the migration of the stored settings: a plugin update brings the schema forward without
/// undoing the choices of the user.
/// </summary>
public sealed class ConfigurationMigrationTests
{
    [Fact]
    public void Migrate_KeepsTheLayoutOfAConfigurationThatAlreadyHasIt()
    {
        // The configuration of 1.10.0.0: the user asked for one folder per kind, so raising the schema
        // for the TVDB numbering must not write the programs back to the flat destination folder —
        // the libraries would index the empty folders of the kinds and show nothing.
        var configuration = new PluginConfiguration
        {
            ConfigurationVersion = PluginConfiguration.CurrentVersion - 1,
            OrganizeByKind = true
        };

        var changed = ConfigurationMigration.Migrate(configuration);

        Assert.True(changed);
        Assert.True(configuration.OrganizeByKind);
        Assert.Equal(PluginConfiguration.CurrentVersion, configuration.ConfigurationVersion);
    }

    [Fact]
    public void Migrate_TurnsTheLayoutOffForAConfigurationWrittenBeforeItExisted()
    {
        // A configuration of 1.8.x: it never had the layout, so the flat destination folder is the one the
        // library of the user indexes and moving it would be a silent change of the library.
        var configuration = new PluginConfiguration
        {
            ConfigurationVersion = PluginConfiguration.LayoutVersion - 1,
            OrganizeByKind = true
        };

        ConfigurationMigration.Migrate(configuration);

        Assert.False(configuration.OrganizeByKind);
        Assert.Equal(PluginConfiguration.CurrentVersion, configuration.ConfigurationVersion);
    }

    [Fact]
    public void Migrate_RepairsTheCollectionsAndTheDatasetUrl()
    {
        var configuration = new PluginConfiguration
        {
            ConfigurationVersion = PluginConfiguration.CurrentVersion,
            Programs = null!,
            KindLibraries = null!,
            OpenTvIdsUrl = " "
        };

        var changed = ConfigurationMigration.Migrate(configuration);

        Assert.True(changed);
        Assert.Empty(configuration.Programs);
        Assert.Empty(configuration.KindLibraries);
        Assert.Equal(OpenTvIdsClient.DefaultUrl, configuration.OpenTvIdsUrl);
        Assert.True(configuration.OrganizeByKind);
    }

    [Fact]
    public void Migrate_AnUpToDateConfiguration_ChangesNothing()
    {
        var configuration = new PluginConfiguration();

        Assert.False(ConfigurationMigration.Migrate(configuration));
        Assert.True(configuration.OrganizeByKind);
    }

    [Fact]
    public void LayoutVersion_IsTheVersionThatIntroducedTheLayout()
        => Assert.Equal(4, PluginConfiguration.LayoutVersion);
}
