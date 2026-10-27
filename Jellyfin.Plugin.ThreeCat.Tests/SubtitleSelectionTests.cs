using Jellyfin.Plugin.ThreeCat.Sync;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the pairing between the configured languages and the tracks published by 3Cat.
/// </summary>
public sealed class SubtitleSelectionTests
{
    [Fact]
    public void SelectSubtitleTracks_PicksTheConfiguredLanguages()
    {
        var tracks = new[]
        {
            Track("ca-ES", "https://subs/ca.vtt"),
            Track("es", "https://subs/es.vtt"),
            Track("en", "https://subs/en.vtt")
        };

        var selection = ThreeCatSyncService.SelectSubtitleTracks(tracks, ["ES", "ca"], false);

        Assert.Equal(
            [
                new SubtitleSource("ES", "https://subs/es.vtt"),
                new SubtitleSource("ca", "https://subs/ca.vtt")
            ],
            selection);
    }

    [Fact]
    public void SelectSubtitleTracks_AllLanguages_KeepsOneTrackPerLanguage()
    {
        var tracks = new[]
        {
            Track("ca", "https://subs/ca.vtt"),
            Track("CA-ES", "https://subs/ca-es.vtt"),
            Track(null, "https://subs/unknown.vtt"),
            Track("es", "https://subs/es.vtt")
        };

        var selection = ThreeCatSyncService.SelectSubtitleTracks(tracks, [], true);

        Assert.Equal(
            [
                new SubtitleSource("ca", "https://subs/ca.vtt"),
                new SubtitleSource("es", "https://subs/es.vtt")
            ],
            selection);
    }

    [Fact]
    public void SelectSubtitleTracks_AllLanguages_UsesCatalanWhenTheIsoIsMissing()
    {
        var selection = ThreeCatSyncService.SelectSubtitleTracks([Track(null, "https://subs/x.vtt")], [], true);

        Assert.Equal([new SubtitleSource("ca", "https://subs/x.vtt")], selection);
    }

    [Fact]
    public void SelectSubtitleTracks_IgnoresTracksWithoutUrl()
    {
        var tracks = new[]
        {
            Track("ca", " "),
            Track("es", null),
            Track("en", "https://subs/en.vtt")
        };

        var selection = ThreeCatSyncService.SelectSubtitleTracks(tracks, ["es", "en"], false);

        Assert.Equal([new SubtitleSource("en", "https://subs/en.vtt")], selection);
    }

    [Fact]
    public void SelectSubtitleTracks_WithoutMatchingLanguage_ReturnsNothing()
    {
        var tracks = new[]
        {
            Track("ca", "https://subs/ca.vtt"),
            Track("es", "https://subs/es.vtt")
        };

        Assert.Empty(ThreeCatSyncService.SelectSubtitleTracks(tracks, ["fr"], false));
    }

    [Fact]
    public void SelectSubtitleTracks_SingleTrackWithoutIso_KeepsTheRequestedLanguage()
    {
        var selection = ThreeCatSyncService.SelectSubtitleTracks([Track(null, "https://subs/x.vtt")], ["ca"], false);

        Assert.Equal([new SubtitleSource("ca", "https://subs/x.vtt")], selection);
    }

    [Fact]
    public void SelectSubtitleTracks_WithoutTracks_ReturnsNothing()
    {
        Assert.Empty(ThreeCatSyncService.SelectSubtitleTracks([], ["ca"], false));
        Assert.Empty(ThreeCatSyncService.SelectSubtitleTracks([], [], true));
    }

    private static ThreeCatSubtitle Track(string? iso, string? url)
        => new() { Iso = iso, Url = url, Format = "vtt" };
}
