using Jellyfin.Plugin.ThreeCat.Library;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the film folders the plugin writes: the layout the movie resolver of Jellyfin reads.
/// </summary>
public sealed class MovieLibraryTests
{
    private const string Url = "https://mp4.3cat.cat/video/3251892.mp4";

    [Fact]
    public void Write_CreatesTheFilmFolderWithTheStreamAndTheNfo()
    {
        using var folder = new TempFolder();

        var result = MovieLibrary.Write(folder.Root, Movie(), [Video()], new LibraryWriteOptions());

        var movieFolder = folder.Combine("Ermessenda (2011)");
        var baseName = "Ermessenda (2011)";

        Assert.Equal(movieFolder, result.SeriesPath);
        Assert.Equal(1, result.EpisodesWritten);
        Assert.Equal(2, result.FilesWritten);
        Assert.True(File.Exists(Path.Combine(movieFolder, "movie.nfo")));
        Assert.Equal(Url + "\n", File.ReadAllText(Path.Combine(movieFolder, baseName + ".strm")));
    }

    [Fact]
    public void Write_WritesThePartsOfAFilmAsVersionsOfIt()
    {
        // Jellyfin has no notion of the parts of a film: the second video of the same title is written as
        // another version of the same folder, which is what keeps a film split by 3Cat in one entry.
        using var folder = new TempFolder();

        var result = MovieLibrary.Write(
            folder.Root,
            Movie(),
            [Video(part: 1), Video(part: 2)],
            new LibraryWriteOptions());

        var movieFolder = folder.Combine("Ermessenda (2011)");

        Assert.Equal(2, result.EpisodesWritten);
        Assert.Equal(
            ["Ermessenda (2011) - part1.strm", "Ermessenda (2011) - part2.strm"],
            Directory.GetFiles(movieFolder, "*.strm").Select(file => Path.GetFileName(file)!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Write_SecondRunWithTheSameContentWritesNothing()
    {
        using var folder = new TempFolder();
        MovieLibrary.Write(folder.Root, Movie(), [Video()], new LibraryWriteOptions());

        var result = MovieLibrary.Write(folder.Root, Movie(), [Video()], new LibraryWriteOptions());

        Assert.Equal(0, result.FilesWritten);
    }

    [Fact]
    public void Write_KeepsOneSubtitlePerLanguage()
    {
        using var folder = new TempFolder();
        var file = Video();
        file.Subtitles =
        [
            new LibrarySubtitle("ca", "primer"),
            new LibrarySubtitle("CA", "segon"),
            new LibrarySubtitle("es", "tercer")
        ];

        var result = MovieLibrary.Write(folder.Root, Movie(), [file], new LibraryWriteOptions());

        var movieFolder = folder.Combine("Ermessenda (2011)");
        var subtitles = Directory
            .GetFiles(movieFolder, "*.srt")
            .Select(file => Path.GetFileName(file)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(4, result.FilesWritten);
        Assert.Equal(["Ermessenda (2011).ca.srt", "Ermessenda (2011).es.srt"], subtitles);
        Assert.StartsWith("primer", File.ReadAllText(Path.Combine(movieFolder, subtitles[0]!)), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_RespectsTheWriteFlags()
    {
        using var folder = new TempFolder();

        var result = MovieLibrary.Write(
            folder.Root,
            Movie(),
            [Video()],
            new LibraryWriteOptions { WriteNfo = false, WriteSubtitles = false });

        var movieFolder = folder.Combine("Ermessenda (2011)");

        Assert.Equal(1, result.FilesWritten);
        Assert.False(File.Exists(Path.Combine(movieFolder, "movie.nfo")));
        Assert.Equal(
            ["Ermessenda (2011).strm"],
            Directory.GetFiles(movieFolder).Select(file => Path.GetFileName(file)!));
    }

    [Fact]
    public void Write_WithoutAYear_NamesTheFolderWithTheTitleAlone()
    {
        using var folder = new TempFolder();
        var movie = Movie();
        movie.Year = null;

        var result = MovieLibrary.Write(folder.Root, movie, [Video()], new LibraryWriteOptions());

        Assert.Equal(folder.Combine("Ermessenda"), result.SeriesPath);
    }

    [Fact]
    public void TryDeleteMovieFolder_KeepsTheFolderOfAnotherProgram()
    {
        using var folder = new TempFolder();
        var result = MovieLibrary.Write(folder.Root, Movie(), [Video()], new LibraryWriteOptions());

        Assert.False(MovieLibrary.TryDeleteMovieFolder(result.SeriesPath, "999"));
        Assert.True(Directory.Exists(result.SeriesPath));

        Assert.True(MovieLibrary.TryDeleteMovieFolder(result.SeriesPath, "119987484"));
        Assert.False(Directory.Exists(result.SeriesPath));
    }

    [Fact]
    public void TryDeleteMovieFolder_KeepsAFolderTheUserFilledWithSomethingElse()
    {
        using var folder = new TempFolder();
        var result = MovieLibrary.Write(folder.Root, Movie(), [Video()], new LibraryWriteOptions());
        File.WriteAllText(Path.Combine(result.SeriesPath, "notas.txt"), "mÃƒÂ­as");

        Assert.False(MovieLibrary.TryDeleteMovieFolder(result.SeriesPath, "119987484"));
        Assert.True(File.Exists(Path.Combine(result.SeriesPath, "notas.txt")));
    }

    [Fact]
    public void DeleteUnwrittenMovies_RemovesTheFilmsTheRunDidNotWrite()
    {
        using var folder = new TempFolder();
        var first = MovieLibrary.Write(folder.Root, Movie(), [Video()], new LibraryWriteOptions());

        var other = Movie();
        other.Title = "Pa negre";
        var second = MovieLibrary.Write(folder.Root, other, [Video()], new LibraryWriteOptions());

        // A run that writes only the second film: the first one is not in the catalog anymore.
        var removed = MovieLibrary.DeleteUnwrittenMovies(folder.Root, "119987484", [second.SeriesPath]);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(first.SeriesPath));
        Assert.True(Directory.Exists(second.SeriesPath));
    }

    [Fact]
    public void DeleteUnwrittenMovies_KeepsTheFilmsOfAnotherProgram()
    {
        using var folder = new TempFolder();
        var result = MovieLibrary.Write(folder.Root, Movie(), [Video()], new LibraryWriteOptions());

        var removed = MovieLibrary.DeleteUnwrittenMovies(folder.Root, "999", []);

        Assert.Equal(0, removed);
        Assert.True(Directory.Exists(result.SeriesPath));
    }

    private static LibraryMovie Movie() => new()
    {
        Title = "Ermessenda",
        ProgramId = "119987484",
        Year = 2011,
        Plot = "La comtessa Ermessenda de Carcassona.",
        Genres = ["Cinema", "FicciÃƒÂ³ histÃƒÂ²rica"],
        Country = "ES"
    };

    private static LibraryMovieFile Video(int part = 0) => new()
    {
        Url = Url,
        Part = part,
        VideoId = "3251892"
    };
}
