using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the name of the series: 3Cat publishes the full emission of a program as a program of its own
/// («Crackòvia - Programa sencer»), and the series of the library is the show, not the shape of the video.
/// </summary>
public sealed class SeriesNamingTests
{
    private const string CatalogTitle = "Crackòvia - Programa sencer";

    [Fact]
    public void SeriesName_KeepsTheTitleOfTheCatalogWhenTheUserDidNotAsk()
    {
        Assert.Equal(
            CatalogTitle,
            ThreeCatSyncService.SeriesName([], null, CatalogTitle, trimSuffix: false));
    }

    [Fact]
    public void SeriesName_RemovesTheWholeProgramFormatWhenTheUserAsked()
    {
        Assert.Equal(
            "Crackòvia",
            ThreeCatSyncService.SeriesName([], null, CatalogTitle, trimSuffix: true));

        // A configuration that lost its programs still names the series: the guard only reads the cache.
        Assert.Equal(
            "Crackòvia",
            ThreeCatSyncService.SeriesName(null, null, CatalogTitle, trimSuffix: true));
    }

    [Fact]
    public void SeriesName_KeepsTheFormatWhenAnotherProgramAlreadyAnswersToTheName()
    {
        // 3Cat publishes the clips and the whole program as two programs: one folder per series means that the
        // second one would delete the files of the first on every run.
        var whole = new ThreeCatProgramReference { Id = "119987484", Title = CatalogTitle };
        var clips = new ThreeCatProgramReference { Id = "119987485", Title = "Crackòvia" };

        Assert.Equal(
            CatalogTitle,
            ThreeCatSyncService.SeriesName([whole, clips], whole, CatalogTitle, trimSuffix: true));
    }

    [Fact]
    public void SeriesName_IgnoresTheCachedTitleOfTheProgramItself()
    {
        // The program is not a rival of itself: its own cached name never blocks the new one.
        var whole = new ThreeCatProgramReference { Id = "119987484", Title = "Crackòvia" };

        Assert.Equal(
            "Crackòvia",
            ThreeCatSyncService.SeriesName([whole], whole, CatalogTitle, trimSuffix: true));
    }

    [Fact]
    public void SeriesName_LeavesATitleWithoutTheFormatAlone()
    {
        Assert.Equal("Crackòvia", ThreeCatSyncService.SeriesName([], null, "Crackòvia", trimSuffix: true));
        Assert.Equal(
            "Programa sencer",
            ThreeCatSyncService.SeriesName([], null, "Programa sencer", trimSuffix: true));
    }

    [Fact]
    public void RenamedSeriesFolder_IsWhereTheRunsBeforeTheRenameWroteTheSeries()
    {
        var root = Path.Combine("mnt", "media", "strm", "3CAT", "Programes TV");

        // The folder is the one of the old name, next to the folder of this run: it is cleaned like any other
        // folder the program leaves behind.
        Assert.Equal(
            StrmLibrary.SeriesFolder(root, CatalogTitle),
            ThreeCatSyncService.RenamedSeriesFolder(root, CatalogTitle, "Crackòvia"));
    }

    [Fact]
    public void RenamedSeriesFolder_IsNothingWhenTheNameDidNotChange()
    {
        Assert.Null(ThreeCatSyncService.RenamedSeriesFolder("/mnt/media/strm/3CAT", "Crackòvia", "Crackòvia"));
        Assert.Null(ThreeCatSyncService.RenamedSeriesFolder(null, CatalogTitle, "Crackòvia"));
        Assert.Null(ThreeCatSyncService.RenamedSeriesFolder(" ", CatalogTitle, "Crackòvia"));
    }

    [Fact]
    public void RenamedSeriesFolder_IsTheFolderThePluginWroteWithTheTitleOfTheCatalog()
    {
        // The folder the new name leaves behind is the one the runs before wrote, and it is removed like any
        // other folder of this plugin for the program: the library does not keep the show twice.
        using var folder = new TempFolder();
        var written = StrmLibrary.Write(
            folder.Root,
            new LibrarySeries
            {
                Title = CatalogTitle,
                ProgramId = "119987484",
                Plot = "Sinopsi",
                Genres = ["Humor"]
            },
            [
                new LibraryEpisode
                {
                    Season = 22,
                    Episode = 8,
                    Title = "La dimissió del ministre Puente",
                    Plot = "Sinopsi de l'episodi",
                    Url = "https://mp4.3cat.cat/video/3251892.mp4",
                    VideoId = "3251892",
                    Subtitles = []
                }
            ],
            new LibraryWriteOptions()).SeriesPath;

        var name = ThreeCatSyncService.SeriesName([], null, CatalogTitle, trimSuffix: true);
        var renamed = ThreeCatSyncService.RenamedSeriesFolder(folder.Root, CatalogTitle, name);

        Assert.Equal(written, renamed);
        Assert.Equal(1, LibraryCleanup.TryDeleteProgram(renamed, "119987484"));
        Assert.False(Directory.Exists(written));
        Assert.True(Directory.Exists(folder.Root));
    }
}
