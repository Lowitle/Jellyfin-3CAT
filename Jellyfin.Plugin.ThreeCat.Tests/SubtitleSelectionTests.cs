using Jellyfin.Plugin.ThreeCat.Sync;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the pairing between the subtitle tracks published by 3Cat and the SRT files that are
/// written next to each episode.
/// </summary>
public sealed class SubtitleSelectionTests
{
    [Fact]
    public void SelectSubtitleTracks_KeepsOneTrackPerPublishedLanguage()
    {
        var tracks = new[]
        {
            Track("ca-ES", "https://subs/ca.vtt"),
            Track("CA", "https://subs/ca-hd.vtt"),
            Track("es", "https://subs/es.vtt"),
            Track("en", "https://subs/en.vtt")
        };

        var selection = ThreeCatSyncService.SelectSubtitleTracks(tracks);

        Assert.Equal(
            [
                new SubtitleSource("ca", "https://subs/ca.vtt"),
                new SubtitleSource("es", "https://subs/es.vtt"),
                new SubtitleSource("en", "https://subs/en.vtt")
            ],
            selection);
    }

    [Fact]
    public void SelectSubtitleTracks_UsesCatalanWhenTheIsoIsMissing()
    {
        var selection = ThreeCatSyncService.SelectSubtitleTracks([Track(null, "https://subs/x.vtt")]);

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

        var selection = ThreeCatSyncService.SelectSubtitleTracks(tracks);

        Assert.Equal([new SubtitleSource("en", "https://subs/en.vtt")], selection);
    }

    [Fact]
    public void SelectSubtitleTracks_WithoutTracks_ReturnsNothing()
    {
        Assert.Empty(ThreeCatSyncService.SelectSubtitleTracks([]));
    }

    private static ThreeCatSubtitle Track(string? iso, string? url)
        => new() { Iso = iso, Url = url, Format = "vtt" };
}
