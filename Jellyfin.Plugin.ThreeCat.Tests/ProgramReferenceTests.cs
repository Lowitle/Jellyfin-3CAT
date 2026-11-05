using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the references the configuration page accepts for a program and for a series of TMDB.
/// </summary>
public sealed class ProgramReferenceTests
{
    [Theory]
    [InlineData("https://www.3cat.cat/3cat/detectiu-conan/", "detectiu-conan")]
    [InlineData("https://www.3cat.cat/3cat/detectiu-conan", "detectiu-conan")]
    [InlineData("www.3cat.cat/3cat/detectiu-conan/?v=1", "detectiu-conan")]
    [InlineData("  https://www.3cat.cat/3cat/detectiu-conan/#top  ", "detectiu-conan")]
    [InlineData("https://www.3cat.cat/3cat/detectiu-conan/video/3251892/", "detectiu-conan")]
    [InlineData("Detectiu-Conan", "detectiu-conan")]
    [InlineData("https://www.3cat.cat/3cat/l%27ultim-estiu/", "l'ultim-estiu")]
    public void ProgramSlug_ReadsTheSlugOfTheProgramPage(string reference, string expected)
        => Assert.Equal(expected, ProgramReference.ProgramSlug(reference));

    [Theory]
    [InlineData("conan")]
    [InlineData("El detectiu Conan")]
    [InlineData("https://www.themoviedb.org/tv/30983-case-closed")]
    [InlineData("120086583")]
    [InlineData(null)]
    [InlineData("   ")]
    public void ProgramSlug_ReturnsNullWhenTheReferenceNamesNoPage(string? reference)
        => Assert.Null(ProgramReference.ProgramSlug(reference));

    [Theory]
    [InlineData("30983", "30983")]
    [InlineData("  30983  ", "30983")]
    [InlineData("https://www.themoviedb.org/tv/30983-case-closed?language=ca", "30983")]
    [InlineData("https://www.themoviedb.org/tv/30983-case-closed?language=es", "30983")]
    [InlineData("https://www.themoviedb.org/movie/12345", "12345")]
    public void TmdbId_ReadsTheIdOutOfTheUrl(string reference, string expected)
        => Assert.Equal(expected, ProgramReference.TmdbId(reference));

    [Theory]
    [InlineData("https://www.3cat.cat/3cat/detectiu-conan/")]
    [InlineData("El detectiu Conan")]
    [InlineData(null)]
    [InlineData("  ")]
    public void TmdbId_ReturnsNullWhenThereIsNoId(string? reference)
        => Assert.Null(ProgramReference.TmdbId(reference));

    [Theory]
    [InlineData("120086583", true)]
    [InlineData("  3251892  ", true)]
    [InlineData("detectiu-conan", false)]
    [InlineData("120086583/video", false)]
    [InlineData(null, false)]
    public void IsId_TellsTheIdsApartFromTheRest(string? reference, bool expected)
        => Assert.Equal(expected, ProgramReference.IsId(reference));

    [Fact]
    public void Match_PrefersTheSlugOfTheProgramPage()
    {
        var matches = ProgramReference.Match(Catalog(), "https://www.3cat.cat/3cat/detectiu-conan/");

        var best = Assert.Single(matches);
        Assert.Equal("120086583", best.Id);
        Assert.Equal(100, best.Score);
    }

    [Fact]
    public void Match_FindsTheTitleWithoutItsArticle()
    {
        var matches = ProgramReference.Match(Catalog(), "detectiu conan");

        Assert.Equal("120086583", matches[0].Id);
        Assert.True(matches[0].Score >= 60);
    }

    [Fact]
    public void Match_OffersEveryProgramWhenTheReferenceIsAmbiguous()
    {
        IReadOnlyList<ThreeCatCatalogProgram> catalog =
        [
            Program("120086583", "El detectiu Conan", "detectiu-conan"),
            Program("111", "Les aventures de Conan", "les-aventures-de-conan")
        ];

        var matches = ProgramReference.Match(catalog, "conan");

        Assert.Equal(2, matches.Count);
        Assert.Equal(matches[0].Score, matches[1].Score);
        Assert.Contains(matches, match => match.Id == "120086583");
        Assert.Contains(matches, match => match.Id == "111");
    }

    [Fact]
    public void Match_IgnoresTheAccentsAndThePunctuation()
    {
        var matches = ProgramReference.Match(Catalog(), "L'últim estiu");

        Assert.Equal("999", matches[0].Id);
        Assert.Equal(90, matches[0].Score);
    }

    [Fact]
    public void Match_DoesNotScoreAShortTitleAgainstALongerReference()
    {
        IReadOnlyList<ThreeCatCatalogProgram> catalog =
        [
            Program("120086583", "El detectiu Conan", "detectiu-conan"),
            Program("231148590", "Detectiu", "detectiu"),
            Program("52296", ".CAT", "cat")
        ];

        Assert.Equal("120086583", ProgramReference.Match(catalog, "detectiu conan")[0].Id);

        var fromUrl = ProgramReference.Match(catalog, "https://www.3cat.cat/3cat/detectiu-conan/");
        Assert.Equal("120086583", fromUrl[0].Id);
        Assert.DoesNotContain(fromUrl, match => match.Id == "52296");
        Assert.DoesNotContain(fromUrl, match => match.Id == "231148590");
    }

    [Fact]
    public void Match_ReturnsNothingWhenTheReferenceNamesNothing()
    {
        Assert.Empty(ProgramReference.Match(Catalog(), "doctor who"));
        Assert.Empty(ProgramReference.Match(Catalog(), "   "));
        Assert.Empty(ProgramReference.Match([], "detectiu-conan"));
        Assert.Empty(ProgramReference.Match(null, "detectiu-conan"));
    }

    private static IReadOnlyList<ThreeCatCatalogProgram> Catalog() =>
    [
        Program("120086583", "El detectiu Conan", "detectiu-conan"),
        Program("75756", "Conan, el nen del futur", "conan-el-nen-del-futur"),
        Program("999", "L'últim estiu", "l%27ultim-estiu")
    ];

    private static ThreeCatCatalogProgram Program(string id, string title, string slug)
        => new(
            id,
            title,
            $"https://www.3cat.cat/3cat/{slug}/",
            "TV",
            "TV3",
            ["ca"],
            null,
            "2020-01-01",
            true);
}
