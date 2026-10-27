using Jellyfin.Plugin.ThreeCat.Library;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the <c>.strm</c> + <c>.nfo</c> + <c>.srt</c> library written to the destination folder.
/// </summary>
public class StrmLibraryTests
{
    private const string Url = "https://mp4.3cat.cat/video/3251892.mp4";

    [Fact]
    public void Write_CreatesTheSeriesFolderWithTheEpisodeFiles()
    {
        using var folder = new TempFolder();

        var result = StrmLibrary.Write(folder.Root, Series(), [Episode()], new LibraryWriteOptions());

        var seriesFolder = folder.Combine("Doraemon");
        var seasonFolder = Path.Combine(seriesFolder, "Season 1");
        var baseName = "Doraemon - S01E03 - El gat còsmic";

        Assert.Equal(seriesFolder, result.SeriesPath);
        Assert.Equal(1, result.EpisodesWritten);
        Assert.Equal(4, result.FilesWritten);
        Assert.Equal(0, result.FilesRemoved);
        Assert.True(File.Exists(Path.Combine(seriesFolder, "tvshow.nfo")));
        Assert.Equal(Url + "\n", File.ReadAllText(Path.Combine(seasonFolder, baseName + ".strm")));
        Assert.True(File.Exists(Path.Combine(seasonFolder, baseName + ".nfo")));
        Assert.Equal(
            "1\r\n00:00:01,000 --> 00:00:02,000\r\nHola\r\n\r\n",
            File.ReadAllText(Path.Combine(seasonFolder, baseName + ".ca.srt")));
    }

    [Fact]
    public void Write_SecondRunWithTheSameContentWritesNothing()
    {
        using var folder = new TempFolder();
        StrmLibrary.Write(folder.Root, Series(), [Episode()], new LibraryWriteOptions());

        var result = StrmLibrary.Write(folder.Root, Series(), [Episode()], new LibraryWriteOptions());

        Assert.Equal(1, result.EpisodesWritten);
        Assert.Equal(0, result.FilesWritten);
    }

    [Fact]
    public void Write_RespectsTheWriteFlags()
    {
        using var folder = new TempFolder();

        var result = StrmLibrary.Write(
            folder.Root,
            Series(),
            [Episode()],
            new LibraryWriteOptions { WriteNfo = false, WriteSubtitles = false });

        var seasonFolder = Path.Combine(folder.Combine("Doraemon"), "Season 1");

        Assert.Equal(1, result.FilesWritten);
        Assert.Equal(
            ["Doraemon - S01E03 - El gat còsmic.strm"],
            Directory.GetFiles(seasonFolder).Select(Path.GetFileName));
        Assert.False(File.Exists(Path.Combine(folder.Combine("Doraemon"), "tvshow.nfo")));
    }

    [Fact]
    public void Write_KeepsOneSubtitlePerLanguage()
    {
        using var folder = new TempFolder();
        var episode = Episode();
        episode.Subtitles =
        [
            new LibrarySubtitle("ca", "primer"),
            new LibrarySubtitle("CA", "segon"),
            new LibrarySubtitle("es", "tercer")
        ];

        var result = StrmLibrary.Write(folder.Root, Series(), [episode], new LibraryWriteOptions());

        var seasonFolder = Path.Combine(folder.Combine("Doraemon"), "Season 1");
        var subtitles = Directory.GetFiles(seasonFolder, "*.srt").Select(file => Path.GetFileName(file)!).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(5, result.FilesWritten);
        Assert.Equal(["Doraemon - S01E03 - El gat còsmic.ca.srt", "Doraemon - S01E03 - El gat còsmic.es.srt"], subtitles);
        Assert.StartsWith("primer", File.ReadAllText(Path.Combine(seasonFolder, subtitles[0])), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_SanitizesTheLanguageCode()
    {
        using var folder = new TempFolder();
        var episode = Episode();
        episode.Subtitles = [new LibrarySubtitle("ca-ES", "Hola"), new LibrarySubtitle("  ", "Adéu")];

        StrmLibrary.Write(folder.Root, Series(), [episode], new LibraryWriteOptions());

        var seasonFolder = Path.Combine(folder.Combine("Doraemon"), "Season 1");
        var subtitles = Directory.GetFiles(seasonFolder, "*.srt").Select(file => Path.GetFileName(file)!).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            ["Doraemon - S01E03 - El gat còsmic.ca.srt", "Doraemon - S01E03 - El gat còsmic.caes.srt"],
            subtitles);
    }

    [Fact]
    public void Write_UsesASuffixForRepeatedEpisodes()
    {
        using var folder = new TempFolder();

        var result = StrmLibrary.Write(folder.Root, Series(), [Episode(), Episode()], new LibraryWriteOptions());

        var seasonFolder = Path.Combine(folder.Combine("Doraemon"), "Season 1");
        var streams = Directory.GetFiles(seasonFolder, "*.strm").Select(file => Path.GetFileName(file)!).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(2, result.EpisodesWritten);
        Assert.Equal(
            ["Doraemon - S01E03 - El gat còsmic [2].strm", "Doraemon - S01E03 - El gat còsmic.strm"],
            streams);
    }

    [Fact]
    public void Write_DeleteObsolete_RemovesStaleFilesAndKeepsTheSeriesNfo()
    {
        using var folder = new TempFolder();
        var seriesFolder = folder.Combine("Doraemon");
        var staleFolder = Path.Combine(seriesFolder, "Season 2");
        Directory.CreateDirectory(staleFolder);
        File.WriteAllText(Path.Combine(seriesFolder, "tvshow.nfo"), "<tvshow />");
        File.WriteAllText(Path.Combine(staleFolder, "Doraemon - S02E01 - Vell.strm"), "https://old");
        File.WriteAllText(Path.Combine(staleFolder, "notas.txt"), "no se toca");

        var result = StrmLibrary.Write(
            folder.Root,
            Series(),
            [Episode()],
            new LibraryWriteOptions { DeleteObsolete = true });

        Assert.Equal(1, result.FilesRemoved);
        Assert.True(File.Exists(Path.Combine(seriesFolder, "tvshow.nfo")));
        Assert.False(File.Exists(Path.Combine(staleFolder, "Doraemon - S02E01 - Vell.strm")));
        Assert.True(File.Exists(Path.Combine(staleFolder, "notas.txt")));
    }

    [Fact]
    public void Write_DoesNotDeleteAnythingByDefault()
    {
        using var folder = new TempFolder();
        var staleFolder = Path.Combine(folder.Combine("Doraemon"), "Season 2");
        Directory.CreateDirectory(staleFolder);
        var stale = Path.Combine(staleFolder, "Doraemon - S02E01 - Vell.strm");
        File.WriteAllText(stale, "https://old");

        var result = StrmLibrary.Write(folder.Root, Series(), [Episode()], new LibraryWriteOptions());

        Assert.Equal(0, result.FilesRemoved);
        Assert.True(File.Exists(stale));
    }

    [Fact]
    public void Write_WritesUtf8WithoutBom()
    {
        using var folder = new TempFolder();

        StrmLibrary.Write(folder.Root, Series(), [Episode()], new LibraryWriteOptions());

        var seasonFolder = Path.Combine(folder.Combine("Doraemon"), "Season 1");
        foreach (var file in Directory.GetFiles(seasonFolder))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                file);
        }
    }

    [Fact]
    public void SeriesFolder_SanitizesTheTitle()
    {
        var root = Path.Combine(Path.GetTempPath(), "threecat-naming");

        Assert.Equal(
            Path.Combine(root, "Doraemon ¿i el gat còsmic"),
            StrmLibrary.SeriesFolder(root, "Doraemon? ¿i el gat còsmic/"));
    }

    [Theory]
    [InlineData("", "Doraemon")]
    [InlineData("  ", "Doraemon")]
    [InlineData("C:\\media", " ")]
    public void Write_RejectsAnEmptyRootOrTitle(string root, string title)
    {
        var series = new LibrarySeries { Title = title, ProgramId = "1" };

        Assert.Throws<ArgumentException>(() => StrmLibrary.Write(root, series, [Episode()], new LibraryWriteOptions()));
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
