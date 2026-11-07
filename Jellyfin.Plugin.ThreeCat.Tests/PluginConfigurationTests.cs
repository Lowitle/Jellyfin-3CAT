using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the defaults of the configuration, which keep the behaviour of 1.5.0.0.
/// </summary>
public sealed class PluginConfigurationTests
{
    [Fact]
    public void Defaults_KeepTheNumberingAndTheMetadataOf3Cat()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal(MetadataSource.ThreeCat, configuration.MetadataSource);
        Assert.Equal(OpenTvIdsClient.DefaultUrl, configuration.OpenTvIdsUrl);
        Assert.Equal(5, PluginConfiguration.CurrentVersion);

        // A fresh installation writes the programs one folder per kind: the layout of 1.9.0.0 is the one
        // the page shows and the one «Crear biblioteca» prepares out of the box.
        Assert.True(configuration.OrganizeByKind);

        // No kind of program is attached to a second library until the user chooses one.
        Assert.Empty(configuration.KindLibraries);

        // Without a key and with a language, the plugin is in the state of a fresh installation: TMDB is
        // never read until the user asks for it.
        Assert.Equal(string.Empty, configuration.TmdbApiKey);
        Assert.Equal(Providers.TmdbClient.DefaultLanguage, configuration.TmdbLanguage);
        Assert.False(Providers.TmdbClient.IsConfigured(configuration.TmdbApiKey));
    }

    [Fact]
    public void Reference_UsesTheGlobalSourceUntilItIsOverridden()
    {
        var reference = new ThreeCatProgramReference();

        Assert.Null(reference.Override);
        Assert.Null(reference.TmdbId);
        Assert.Null(reference.TvdbId);

        // The kind of program is automatic until it is pinned, and nothing is cached until a run resolves it.
        Assert.Null(reference.CategoryOverride);
        Assert.Null(reference.Category);

        // The source the last run wrote is empty until a run reaches the program: a fresh installation
        // must never delete a folder.
        Assert.Null(reference.WrittenSource);
    }
}
