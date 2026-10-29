using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the second copy of a series: the folder of the plugin is always written, and the folder of
/// another library gets the same files plus a copy of the artwork.
/// </summary>
public class SeriesMirrorTests
{
    private const string Url = "https://mp4.3cat.cat/video/3251892.mp4";

    [Fact]
    public void Write_OnBothRoots_LeavesTheSeriesInTheTwoLibraries()
    {
        using var folder = new TempFolder();
        var primaryRoot = folder.Combine("3Cat");
        var mirrorRoot = folder.Combine("Anime");
        Directory.CreateDirectory(primaryRoot);
        Directory.CreateDirectory(mirrorRoot);

        // The service writes the series twice, once per root, and copies the images of the first write.
        var primary = StrmLibrary.Write(primaryRoot, Series(), [Episode()], new LibraryWriteOptions());
        var mirror = StrmLibrary.Write(mirrorRoot, Series(), [Episode()], new LibraryWriteOptions());
        File.WriteAllBytes(Path.Combine(primary.SeriesPath, "poster.jpg"), [0xFF, 0xD8, 0xFF]);
        var copied = ThreeCatSyncService.CopyArtworkFiles(primary.SeriesPath, mirror.SeriesPath);

        Assert.NotEqual(primary.SeriesPath, mirror.SeriesPath);
        Assert.Equal(1, copied);
        Assert.Equal(4, primary.FilesWritten);
        Assert.Equal(4, mirror.FilesWritten);
        Assert.True(File.Exists(Path.Combine(mirror.SeriesPath, "poster.jpg")));

        var season = Path.Combine("Season 1", "Doraemon - S01E03 - El gat còsmic.strm");
        Assert.True(File.Exists(Path.Combine(primary.SeriesPath, season)));
        Assert.True(File.Exists(Path.Combine(mirror.SeriesPath, season)));
    }

    [Fact]
    public void CopyArtworkFiles_CopiesOnlyTheImages()
    {
        using var folder = new TempFolder();
        var source = folder.Combine("3Cat", "Doraemon");
        var target = folder.Combine("Anime", "Doraemon");
        Directory.CreateDirectory(Path.Combine(source, "Season 1"));

        foreach (var name in new[] { "poster.jpg", "fanart.JPEG", "logo.png", "banner.webp" })
        {
            File.WriteAllText(Path.Combine(source, name), name);
        }

        File.WriteAllText(Path.Combine(source, "tvshow.nfo"), "<tvshow />");
        File.WriteAllText(Path.Combine(Path.Combine(source, "Season 1"), "capitulo.strm"), Url);

        var copied = ThreeCatSyncService.CopyArtworkFiles(source, target);

        Assert.Equal(4, copied);
        Assert.Equal(
            ["banner.webp", "fanart.JPEG", "logo.png", "poster.jpg"],
            Directory.GetFiles(target).Select(file => Path.GetFileName(file)!).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(Path.Combine(target, "tvshow.nfo")));
        Assert.False(Directory.Exists(Path.Combine(target, "Season 1")));
    }

    [Fact]
    public void CopyArtworkFiles_OverwritesThePictureOfAPreviousRun()
    {
        using var folder = new TempFolder();
        var source = folder.Combine("3Cat", "Doraemon");
        var target = folder.Combine("Anime", "Doraemon");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "poster.jpg"), "nueva");
        File.WriteAllText(Path.Combine(target, "poster.jpg"), "vieja");

        ThreeCatSyncService.CopyArtworkFiles(source, target);

        Assert.Equal("nueva", File.ReadAllText(Path.Combine(target, "poster.jpg")));
    }

    [Fact]
    public void CopyArtworkFiles_WithoutImages_DoesNotCreateTheMirrorFolder()
    {
        using var folder = new TempFolder();
        var source = folder.Combine("3Cat", "Doraemon");
        var target = folder.Combine("Anime", "Doraemon");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "tvshow.nfo"), "<tvshow />");

        var copied = ThreeCatSyncService.CopyArtworkFiles(source, target);

        Assert.Equal(0, copied);
        Assert.False(Directory.Exists(target));
    }

    private static LibrarySeries Series() => new()
    {
        Title = "Doraemon",
        ProgramId = "119987484",
        Plot = "Sinopsi",
        Genres = ["Infantil"]
    };

    private static LibraryEpisode Episode() => new()
    {
        Season = 1,
        Episode = 3,
        Title = "El gat còsmic",
        Plot = "Sinopsi de l'episodi",
        Url = Url,
        VideoId = "3251892",
        Subtitles = [new LibrarySubtitle("ca", "1\n00:00:01,000 --> 00:00:02,000\nHola\n\n")]
    };
}
