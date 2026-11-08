using Jellyfin.Plugin.ThreeCat.Library;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the file and folder names written to the library folder.
/// </summary>
public class LibraryNamingTests
{
    [Theory]
    [InlineData("Telenotícies: edició nit", "Telenotícies edició nit")]
    [InlineData("A/B\\C|D?E*F<G>H\"I", "A B C D E F G H I")]
    [InlineData("  Nombre   con    espacios  ", "Nombre con espacios")]
    [InlineData("Nombre...", "Nombre")]
    [InlineData("Nombre - ", "Nombre")]
    [InlineData("a\tb\nc", "a b c")]
    public void Sanitize_RemovesForbiddenCharacters(string value, string expected)
    {
        Assert.Equal(expected, LibraryNaming.Sanitize(value, 100, "Serie"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("***")]
    [InlineData("...")]
    public void Sanitize_ReturnsTheFallbackWhenNothingUsableRemains(string? value)
    {
        Assert.Equal("Serie", LibraryNaming.Sanitize(value, 100, "Serie"));
    }

    [Fact]
    public void Sanitize_TruncatesAndRemovesDanglingSeparators()
    {
        Assert.Equal("aaaaaaaaaa", LibraryNaming.Sanitize(new string('a', 50), 10, "Serie"));
        Assert.Equal("aaaaaaaaa", LibraryNaming.Sanitize(new string('a', 9) + "-colita", 10, "Serie"));
    }

    [Fact]
    public void EpisodeFileName_SaysThePartSoJellyfinGroupsTheTwoFilesAsOneEpisode()
    {
        // Jellyfin reads two files of the same episode whose names end in «- part1» and «- part2» as the parts of
        // one episode, and plays them in order: 3Cat publishes the episodes it splits that way.
        Assert.Equal(
            "Detectiu Conan - S01E11 - Sonata a la luz de la luna - part1",
            LibraryNaming.EpisodeFileName("Detectiu Conan", 1, 11, "Sonata a la luz de la luna", 1));

        Assert.Equal(
            "Detectiu Conan - S01E11 - Sonata a la luz de la luna - part2",
            LibraryNaming.EpisodeFileName("Detectiu Conan", 1, 11, "Sonata a la luz de la luna", 2));

        Assert.Equal(
            "Detectiu Conan - S01E11 - Sonata a la luz de la luna",
            LibraryNaming.EpisodeFileName("Detectiu Conan", 1, 11, "Sonata a la luz de la luna"));
    }

    [Fact]
    public void Sanitize_KeepsTheCatalanCharactersOf3Cat()
    {
        Assert.Equal("Doraemon, el gat còsmic", LibraryNaming.Sanitize("Doraemon, el gat còsmic", 100, "Serie"));
    }

    [Fact]
    public void SeriesFolderName_UsesTheFallbackForEmptyTitles()
    {
        Assert.Equal("Doraemon, el gat còsmic", LibraryNaming.SeriesFolderName("Doraemon, el gat còsmic"));
        Assert.Equal("Serie", LibraryNaming.SeriesFolderName("???"));
    }

    [Theory]
    [InlineData("Crackòvia - Programa sencer", "Crackòvia")]
    [InlineData("Crackòvia – Programa sencer", "Crackòvia")]
    [InlineData("Crackòvia — Programa sencer", "Crackòvia")]
    [InlineData("Crackòvia | Programa sencer", "Crackòvia")]
    [InlineData("Crackòvia: Programa sencer", "Crackòvia")]
    [InlineData("Crackòvia (Programa sencer)", "Crackòvia")]
    [InlineData("Crackòvia - programa sencer", "Crackòvia")]
    [InlineData("Crackòvia - Programa Sencer", "Crackòvia")]
    [InlineData("Crackòvia  -  Programa sencer", "Crackòvia")]
    [InlineData("Crackòvia - Programa sencer ", "Crackòvia")]
    [InlineData("Doraemon, el gat còsmic - Programa sencer", "Doraemon, el gat còsmic")]
    public void StripWholeProgramSuffix_RemovesTheFormat3CatAppendsToAWholeProgram(string title, string expected)
    {
        // The suffix names the shape of the video, not the program: the series of the library is the show.
        Assert.Equal(expected, LibraryNaming.StripWholeProgramSuffix(title));
        Assert.Equal(expected, LibraryNaming.SeriesFolderName(LibraryNaming.StripWholeProgramSuffix(title)));
    }

    [Theory]
    [InlineData("Crackòvia")]
    [InlineData("Programa sencer")]
    [InlineData("El programa sencer")]
    [InlineData("Crackòvia - Programa sencer - Temporada 2")]
    [InlineData("El programa sencer de Crackòvia")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StripWholeProgramSuffix_KeepsATitleThatAnnouncesNoWholeProgramFormat(string? title)
    {
        // A program called «Programa sencer» carries no separator before the suffix and is a show of its own.
        Assert.Equal(title ?? string.Empty, LibraryNaming.StripWholeProgramSuffix(title));
    }

    [Theory]
    [InlineData(1, 3, "15/06/26", "Doraemon - S01E03 - 15 06 26")]
    [InlineData(12, 7, "Títol", "Doraemon - S12E07 - Títol")]
    [InlineData(1, 1, "", "Doraemon - S01E01")]
    public void EpisodeFileName_BuildsTheJellyfinNamingScheme(int season, int episode, string title, string expected)
    {
        Assert.Equal(expected, LibraryNaming.EpisodeFileName("Doraemon", season, episode, title));
    }

    [Fact]
    public void EpisodeFileName_KeepsTheNameEvenWithoutTitle()
    {
        Assert.Equal(
            "Doraemon - S01E02",
            LibraryNaming.EpisodeFileName("Doraemon", 1, 2, null));
    }

    [Fact]
    public void EpisodeFileName_TruncatesLongTitles()
    {
        var name = LibraryNaming.EpisodeFileName("Doraemon", 1, 2, new string('x', 300));

        Assert.True(name.Length <= 160);
        Assert.StartsWith("Doraemon - S01E02 - ", name, StringComparison.Ordinal);
    }
}
