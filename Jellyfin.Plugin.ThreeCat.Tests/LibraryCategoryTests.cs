using Jellyfin.Plugin.ThreeCat.Library;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the folder each kind of program is written to, and of the folders the libraries index.
/// </summary>
public sealed class LibraryCategoryTests
{
    [Theory]
    [InlineData("PUF_SER", "Series")]
    [InlineData("PUFF_SDRAM", "Series")]
    [InlineData("PUFF_SDIBU", "Series")]
    [InlineData("PUFF_FILM", "Movies")]
    [InlineData("PUF_PDO", "Documentals")]
    [InlineData("PUFA_DOC", "Documentals")]
    [InlineData("PUFA_CONCURS", "Programes TV")]
    [InlineData("PUFACTUALITA", "Programes TV")]
    [InlineData("PUFM_CONC", "Programes TV")]
    [InlineData("PUE3", "Programes TV")]
    public void Resolve_FilesTheKindOfProgramUnderItsFolder(string format, string expected)
    {
        var classification = ProgramClassifier.Classify([format], [], []);

        Assert.Equal(expected, LibraryCategory.Resolve(classification));
    }

    [Fact]
    public void Resolve_SendsTheAnimeToItsFolderEvenWhenItIsForChildren()
    {
        // «Bola de Drac» is animation, Japanese and for children: the three rules match, and the anime
        // wins because it is the narrowest one. «Infantil» stays as a genre of the series.
        var classification = ProgramClassifier.Classify(["PUFF_SDIBU"], [], [], anime: true, children: true);

        Assert.Equal("Anime", LibraryCategory.Resolve(classification));
        Assert.Equal(["Anime", "Animació", "Infantil"], classification.Genres);
    }

    [Fact]
    public void Resolve_GivesTheChildrenFolderPrecedenceOverTheKind()
    {
        // The audience is what groups the programmes of the SX3, whatever 3Cat files them as.
        var classification = ProgramClassifier.Classify(["PUF_PDO"], [], [], children: true);

        Assert.Equal("Infantil", LibraryCategory.Resolve(classification));
    }

    [Fact]
    public void Resolve_PutsAFilmInTheMoviesFolderWhateverTheAudience()
    {
        var classification = ProgramClassifier.Classify(["PUFF_FILM"], [], [], children: true);

        Assert.Equal("Movies", LibraryCategory.Resolve(classification));
    }

    [Fact]
    public void Resolve_PutsAProgramWithoutKindInTheShowsFolder()
    {
        var classification = ProgramClassifier.Classify([], [], ["Ficció"]);

        Assert.Equal("Programes TV", LibraryCategory.Resolve(classification));
    }

    [Fact]
    public void Resolve_PutsEverythingInTheShowsFolderWithoutClassification()
    {
        Assert.Equal("Programes TV", LibraryCategory.Resolve(null));
        Assert.Equal("Programes TV", LibraryCategory.Resolve(new ProgramClassification(null, [])));
    }

    [Fact]
    public void Folders_SeparateTheSeriesFromTheFilms()
    {
        // The series library indexes one folder per kind; «Movies» is the only one of the film library, or
        // Jellyfin would import a film as a one-episode series.
        Assert.Equal(
            ["Programes TV", "Series", "Documentals", "Anime", "Infantil"],
            LibraryCategory.SeriesFolders);
        Assert.Equal(["Movies"], LibraryCategory.MovieFolders);
    }

    [Fact]
    public void Resolve_WithoutTheLayout_IndexesTheDestinationFolderItself()
    {
        var layout = LibraryLayout.Resolve("/media/3cat", organizeByKind: false);

        Assert.Equal(["/media/3cat"], layout.Series);
        Assert.Empty(layout.Movies);
    }

    [Fact]
    public void Resolve_WithTheLayout_SeparatesTheSeriesFromTheFilms()
    {
        var layout = LibraryLayout.Resolve("/media/3cat", organizeByKind: true);

        Assert.All(
            layout.Series,
            folder => Assert.StartsWith(
                "/media/3cat" + Path.DirectorySeparatorChar,
                folder,
                StringComparison.Ordinal));
        Assert.Contains(layout.Series, folder => folder.EndsWith(Path.DirectorySeparatorChar + "Anime", StringComparison.Ordinal));
        Assert.Equal([Path.Combine("/media/3cat", "Movies")], layout.Movies);
    }

    [Fact]
    public void ProgramFolder_BuildsTheFolderOfTheKind()
    {
        Assert.Equal(
            Path.Combine("/media/3cat", "Documentals"),
            LibraryLayout.ProgramFolder("/media/3cat", organizeByKind: true, LibraryCategory.Documentaries));

        Assert.Equal(
            Path.GetFullPath("/media/3cat"),
            LibraryLayout.ProgramFolder("/media/3cat", organizeByKind: false, LibraryCategory.Documentaries));
    }

    [Fact]
    public void Resolve_KeepsTheKindTheUserPinned()
    {
        var classification = ProgramClassifier.Classify(["PUFF_SDIBU"], [], [], anime: true);

        Assert.Equal("Documentals", LibraryCategory.Resolve(classification, "Documentals"));
        Assert.Equal("Documentals", LibraryCategory.Resolve(classification, "  documentals "));

        // The empty value is «automatic»: the classification is the one that decides.
        Assert.Equal("Anime", LibraryCategory.Resolve(classification, string.Empty));
        Assert.Equal("Anime", LibraryCategory.Resolve(classification, null));
    }

    [Fact]
    public void Resolve_IgnoresAPinnedKindThatIsNotAFolderOfSeries()
    {
        var classification = ProgramClassifier.Classify(["PUF_PDO"], [], []);

        // «Movies» is not a folder decision: it changes the shape of the files, and a program the catalog
        // files as a documentary stays a documentary.
        Assert.Equal("Documentals", LibraryCategory.Resolve(classification, "Movies"));

        // A value the page never offers —a folder that was renamed— is not invented either.
        Assert.Equal("Documentals", LibraryCategory.Resolve(classification, "Peliculas"));
    }

    [Fact]
    public void Normalize_ReturnsTheFolderOfTheLayout()
    {
        Assert.Equal("Programes TV", LibraryCategory.Normalize("programes tv"));
        Assert.Equal("Infantil", LibraryCategory.Normalize("  INFANTIL "));
        Assert.Null(LibraryCategory.Normalize(string.Empty));
        Assert.Null(LibraryCategory.Normalize("Peliculas"));

        Assert.True(LibraryCategory.IsSeriesCategory("Anime"));
        Assert.True(LibraryCategory.IsSeriesCategory("programes tv"));
        Assert.False(LibraryCategory.IsSeriesCategory("Movies"));
        Assert.False(LibraryCategory.IsSeriesCategory(null));
    }

    [Fact]
    public void IsAncestor_TellsTheDestinationFolderFromTheFoldersOfTheLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "threecat-layout");
        var anime = Path.Combine(root, "Anime");

        Assert.True(LibraryProvisioner.IsAncestor(root, anime));
        Assert.False(LibraryProvisioner.IsAncestor(anime, root));
        Assert.False(LibraryProvisioner.IsAncestor(root, root));

        // A folder whose name only starts with the name of another one is not inside it.
        Assert.False(LibraryProvisioner.IsAncestor(root, root + "2"));
    }
}
