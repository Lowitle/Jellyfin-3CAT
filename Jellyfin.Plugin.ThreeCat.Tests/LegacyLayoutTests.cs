using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the folder a series had before the layout of the kinds: turning the layout on has to move the
/// program to the folder of its kind and leave nothing behind, even when no run recorded the old folder.
/// </summary>
public class LegacyLayoutTests
{
    private const string Url = "https://mp4.3cat.cat/video/3251892.mp4";

    [Fact]
    public void FlatSeriesFolder_WithTheLayoutOn_IsTheFolderOfTheDestination()
    {
        var root = Path.Combine("mnt", "media", "strm", "3CAT");

        Assert.Equal(
            StrmLibrary.SeriesFolder(root, "Doraemon"),
            ThreeCatSyncService.FlatSeriesFolder(root, organized: true, "Doraemon"));
    }

    [Fact]
    public void FlatSeriesFolder_WithTheLayoutOff_IsNothing()
    {
        // With the layout off there is one folder and the program keeps being written to it: there is no
        // old folder to clean.
        Assert.Null(ThreeCatSyncService.FlatSeriesFolder("/mnt/media/strm/3CAT", organized: false, "Doraemon"));
        Assert.Null(ThreeCatSyncService.FlatSeriesFolder(null, organized: true, "Doraemon"));
        Assert.Null(ThreeCatSyncService.FlatSeriesFolder(" ", organized: true, "Doraemon"));
    }

    [Fact]
    public void RecordedFolder_OfARunThatKeptThePath_IsTheOneItRecorded()
    {
        Assert.Equal(
            "/mnt/media/strm/3CAT/Programes TV/Polònia",
            ThreeCatSyncService.RecordedFolder(
                "/mnt/media/strm/3CAT/Programes TV/Polònia",
                "/mnt/media/strm/3CAT/Polònia",
                "/mnt/media/strm/3CAT/Series/Polònia"));
    }

    [Fact]
    public void RecordedFolder_WithoutARecordedPath_IsTheFolderOfTheFlatLayout()
    {
        Assert.Equal(
            "/mnt/media/strm/3CAT/Polònia",
            ThreeCatSyncService.RecordedFolder(
                null,
                "/mnt/media/strm/3CAT/Polònia",
                "/mnt/media/strm/3CAT/Series/Polònia"));
    }

    [Fact]
    public void RecordedFolder_OfTheFolderOfThisRun_IsNothing()
    {
        // The layout off: the flat folder is the folder of the run, so it is not cleaned after writing it.
        Assert.Null(ThreeCatSyncService.RecordedFolder(null, "/mnt/media/strm/3CAT/Polònia", "/mnt/media/strm/3CAT/Polònia"));
        Assert.Null(ThreeCatSyncService.RecordedFolder(null, null, "/mnt/media/strm/3CAT/Series/Polònia"));
        Assert.Null(ThreeCatSyncService.RecordedFolder(string.Empty, null, null));
    }

    [Fact]
    public void Cleanup_RemovesTheSeriesFolderOfTheFlatLayout()
    {
        using var folder = new TempFolder();
        var flatFolder = StrmLibrary.Write(folder.Root, Series(), [Episode()], new LibraryWriteOptions()).SeriesPath;

        // The folder the layout of the kinds leaves behind is the one of this program, so it is deleted like
        // any other folder the plugin wrote when the series moves to the folder of its kind.
        Assert.Equal(1, LibraryCleanup.TryDeleteProgram(flatFolder, "119987484"));
        Assert.False(Directory.Exists(flatFolder));
        Assert.True(Directory.Exists(folder.Root));
    }

    private static LibrarySeries Series() => new()
    {
        Title = "Polònia",
        ProgramId = "119987484",
        Plot = "Sinopsi",
        Genres = ["Humor"]
    };

    private static LibraryEpisode Episode() => new()
    {
        Season = 22,
        Episode = 8,
        Title = "La dimissió del ministre Puente",
        Plot = "Sinopsi de l'episodi",
        Url = Url,
        VideoId = "3251892",
        Subtitles = []
    };
}
