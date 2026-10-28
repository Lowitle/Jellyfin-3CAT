using System.Xml.Linq;
using Jellyfin.Plugin.ThreeCat.Library;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the classification of a 3Cat program with the facets the search endpoint files it under.
/// </summary>
public sealed class ProgramClassifierTests
{
    [Fact]
    public void Classify_PutsTheKindBeforeTheThemes()
    {
        var classification = ProgramClassifier.Classify(["PUF_SER"], null, ["Ficció", "Juvenil"]);

        Assert.Equal(ProgramClassifier.Series, classification.Kind);
        Assert.Equal(["Sèrie", "Ficció", "Juvenil"], classification.Genres);
    }

    [Fact]
    public void Classify_TheSpecificFormatNamesTheKind()
    {
        // «Naruto» is filed under the animation and the series formats: the animation is the specific one.
        var classification = ProgramClassifier.Classify(["PUF_SER", "PUFF_SDIBU"], null, []);

        Assert.Equal(ProgramClassifier.Animation, classification.Kind);
    }

    [Fact]
    public void Classify_TheFormatWinsOverTheTheme()
    {
        // A documentary about sports is a «Documental», not «Esports».
        var classification = ProgramClassifier.Classify(["PUFA_DOC"], ["PUE3"], ["Esports"]);

        Assert.Equal(ProgramClassifier.Documentary, classification.Kind);
        Assert.Equal(["Documental", "Esports"], classification.Genres);
    }

    [Fact]
    public void Classify_TheCoarseFormatDoesNotNameTheProgram()
    {
        // «PUFACTUALITA» holds almost everything that is not fiction, so it only names the programs that
        // appear in no other known format.
        var classification = ProgramClassifier.Classify(["PUFACTUALITA", "PUFA_MAGZ"], null, []);

        Assert.Equal(ProgramClassifier.Magazine, classification.Kind);
    }

    [Theory]
    [InlineData("PUFICCIO", ProgramClassifier.Series)]
    [InlineData("PUDIVULGACIO", ProgramClassifier.Documentary)]
    [InlineData("PUENTRETENIM", ProgramClassifier.Entertainment)]
    [InlineData("PUMUSICA", ProgramClassifier.Music)]
    [InlineData("PUE3", ProgramClassifier.Sports)]
    [InlineData("PUACTUALITAT", ProgramClassifier.News)]
    public void Classify_FallsBackToTheThemeOfTheWebAndRadioPrograms(string theme, string expected)
    {
        // The web and radio programs of the catalog publish no «format_contingut».
        var classification = ProgramClassifier.Classify(null, [theme], []);

        Assert.Equal(expected, classification.Kind);
    }

    [Fact]
    public void Classify_WithoutKnownFacets_KeepsTheThemesAndWritesNoKind()
    {
        var classification = ProgramClassifier.Classify(["UNKNOWN"], ["UNKNOWN"], ["Cultura"]);

        Assert.Null(classification.Kind);
        Assert.Equal(["Cultura"], classification.Genres);
    }

    [Fact]
    public void Classify_DoesNotRepeatAThemeAlreadyUsedAsTheKind()
    {
        var classification = ProgramClassifier.Classify(["PUF_SER"], null, [" Sèrie ", "Ficció"]);

        Assert.Equal(["Sèrie", "Ficció"], classification.Genres);
    }

    [Fact]
    public void Classify_IgnoresTheEmptyFacets()
    {
        var classification = ProgramClassifier.Classify(["", "  ", "PUFF_FILM"], [""], ["Ficció", "", "  "]);

        Assert.Equal(ProgramClassifier.Cinema, classification.Kind);
        Assert.Equal(["Cinema", "Ficció"], classification.Genres);
    }

    [Fact]
    public void Classify_WithoutFacetsNorThemes_WritesNothing()
    {
        var classification = ProgramClassifier.Classify(null, null, null);

        Assert.Null(classification.Kind);
        Assert.Empty(classification.Genres);
    }

    [Fact]
    public void Classify_MatchesTheFacetCodeIgnoringCaseAndSpaces()
    {
        var classification = ProgramClassifier.Classify([" puff_film "], null, []);

        Assert.Equal(ProgramClassifier.Cinema, classification.Kind);
    }

    [Fact]
    public void TheKindIsTheFirstGenreOfTheSeriesNfo()
    {
        // What the sync writes to every «tvshow.nfo»: the kind of program first, then its themes.
        var classification = ProgramClassifier.Classify(["PUF_SER"], null, ["Ficció"]);

        var document = XDocument.Parse(NfoBuilder.BuildSeries(new LibrarySeries
        {
            Title = "Merlí",
            ProgramId = "119987484",
            Genres = classification.Genres
        }));

        Assert.Equal(
            ["Sèrie", "Ficció"],
            document.Root!.Elements("genre").Select(element => element.Value));
    }

    [Fact]
    public void FacetsOfTheSweep_AreListedOnce()
    {
        Assert.NotEmpty(ProgramClassifier.FormatFacets);
        Assert.NotEmpty(ProgramClassifier.ThemeFacets);
        Assert.Equal(
            ProgramClassifier.FormatFacets.Count,
            ProgramClassifier.FormatFacets.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            ProgramClassifier.ThemeFacets.Count,
            ProgramClassifier.ThemeFacets.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
