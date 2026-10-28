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

    [Fact]
    public void Classify_AnimeGoesBeforeTheAnimation()
    {
        // «Bola de Drac» is filed under the animation and the series formats, like «Mironins», and the
        // only thing that tells them apart is the page of the program.
        var classification = ProgramClassifier.Classify(
            ["PUF_SER", "PUFF_SDIBU"],
            null,
            ["Infantil"],
            anime: true);

        Assert.Equal(ProgramClassifier.Anime, classification.Kind);
        Assert.Equal(["Anime", "Animació", "Infantil"], classification.Genres);
    }

    [Fact]
    public void Classify_WithoutAnime_KeepsTheAnimation()
    {
        var classification = ProgramClassifier.Classify(["PUFF_SDIBU"], null, [], anime: false);

        Assert.Equal(ProgramClassifier.Animation, classification.Kind);
        Assert.Equal(["Animació"], classification.Genres);
    }

    [Fact]
    public void Classify_AnimeDoesNotRepeatTheAnimationOfTheThemes()
    {
        var classification = ProgramClassifier.Classify(["PUFF_SDIBU"], null, ["Animació", "Infantil"], anime: true);

        Assert.Equal(["Anime", "Animació", "Infantil"], classification.Genres);
    }

    [Fact]
    public void TheAnimeIsWrittenBeforeTheAnimationInTheSeriesNfo()
    {
        var classification = ProgramClassifier.Classify(["PUFF_SDIBU"], null, ["Infantil"], anime: true);

        var document = XDocument.Parse(NfoBuilder.BuildSeries(new LibrarySeries
        {
            Title = "Bola de Drac",
            ProgramId = "119987484",
            Genres = classification.Genres
        }));

        Assert.Equal(
            ["Anime", "Animació", "Infantil"],
            document.Root!.Elements("genre").Select(element => element.Value));
    }

    [Theory]
    [InlineData("Japó")]
    [InlineData("japó")]
    [InlineData("Japón")]
    [InlineData(" Japó ")]
    public void IsAnime_ReadsTheJapaneseCountryOfOrigin(string country)
    {
        Assert.True(ProgramClassifier.IsAnime(null, [country]));
    }

    [Fact]
    public void IsAnime_ReadsAnyCountryOfACoproduction()
    {
        Assert.True(ProgramClassifier.IsAnime(null, ["França", "Japó"]));
        Assert.True(ProgramClassifier.IsAnime(null, ["Estats Units", "Japó"]));
    }

    [Fact]
    public void IsAnime_IgnoresTheAnimationOfOtherCountries()
    {
        Assert.False(ProgramClassifier.IsAnime(null, ["Catalunya"]));
        Assert.False(ProgramClassifier.IsAnime(null, ["França", "Bèlgica"]));
        Assert.False(ProgramClassifier.IsAnime(null, []));
        Assert.False(ProgramClassifier.IsAnime(null, null));
    }

    [Theory]
    [InlineData("PUA_ANIMEX3")]
    [InlineData("PUAG_3PPANIM")]
    [InlineData("PUAG_ANI3CAT")]
    [InlineData("PUAGR_CIANIM")]
    public void IsAnime_ReadsTheCollectionsOfTheProgramPage(string group)
    {
        // Programs 3Cat publishes no country for, such as «Ranma», are anime through their collection.
        Assert.True(ProgramClassifier.IsAnime([group], []));
        Assert.True(ProgramClassifier.IsAnime(["PUSX3_S3", group], []));
    }

    [Fact]
    public void IsAnime_IgnoresTheCollectionThatMixesAnimationAndLiveShows()
    {
        // «3Cat Nova» also holds «Arròs covat» and «Món 3XL», so it tells nothing about the program.
        Assert.False(ProgramClassifier.IsAnime(["PUAG_3CANEVA"], []));
        Assert.False(ProgramClassifier.IsAnime(["PUAG_3CANEVA"], ["Catalunya"]));
    }

    [Fact]
    public void IsAnime_WithoutThePageOfTheProgram_SaysNo()
    {
        Assert.False(ProgramClassifier.IsAnime(null, null));
    }

    [Theory]
    [InlineData("PUFF_SDIBU")]
    [InlineData("PUF_PAN")]
    public void IsAnimation_ReadsTheAnimatedFormats(string format)
    {
        Assert.True(ProgramClassifier.IsAnimation([format], null));
    }

    [Theory]
    [InlineData("PUF_SER")]
    [InlineData("PUFF_FILM")]
    [InlineData("PUFA_DOC")]
    public void IsAnimation_LeavesTheOtherFormatsOut(string format)
    {
        Assert.False(ProgramClassifier.IsAnimation([format], null));
    }

    [Fact]
    public void IsAnimation_WithoutFacets_SaysNo()
    {
        Assert.False(ProgramClassifier.IsAnimation(null, null));
    }

    [Fact]
    public void Classify_WritesTheChildrenGenreAfterTheKind()
    {
        // «Mironins»: an animated programme for children, where the audience is a sign of its own.
        var classification = ProgramClassifier.Classify(["PUF_SER"], null, ["Ficció"], children: true);

        Assert.Equal(["Sèrie", "Infantil", "Ficció"], classification.Genres);
    }

    [Fact]
    public void Classify_AnAnimeForChildren_KeepsTheAnimationBeforeTheAudience()
    {
        var classification = ProgramClassifier.Classify(
            ["PUFF_SDIBU"],
            null,
            null,
            anime: true,
            children: true);

        Assert.Equal(["Anime", "Animació", "Infantil"], classification.Genres);
    }

    [Fact]
    public void Classify_DoesNotRepeatTheChildrenGenreOfTheThemes()
    {
        var classification = ProgramClassifier.Classify(["PUFF_SDIBU"], null, ["Infantil"], children: true);

        Assert.Equal(["Animació", "Infantil"], classification.Genres);
    }

    [Theory]
    [InlineData("PUT_INF")]
    [InlineData("put_inf")]
    [InlineData(" PUT_INF ")]
    public void IsChildren_ReadsTheAudienceOfTheVideos(string target)
    {
        Assert.True(ProgramClassifier.IsChildren([target], null));
    }

    [Theory]
    [InlineData("PUT_JUV")]
    [InlineData("PUT_TOTS")]
    public void IsChildren_LeavesTheOtherAudiencesOut(string target)
    {
        // «Bola de Drac» is filed under «Juvenil», so the audience is what tells it from «Doraemon».
        Assert.False(ProgramClassifier.IsChildren([target], null));
    }

    [Theory]
    [InlineData("PUC_S3")]
    [InlineData("PUC_S3_S3")]
    [InlineData("PUC_S3_X3")]
    public void IsChildren_ReadsTheSx3Channels(string channel)
    {
        // The fallback for a video that publishes no audience.
        Assert.True(ProgramClassifier.IsChildren(null, [channel]));
        Assert.True(ProgramClassifier.IsChildren(["PUT_TOTS"], [channel]));
    }

    [Theory]
    [InlineData("PUC_TV3")]
    [InlineData("PUC_TV3EI")]
    [InlineData("PUC_33")]
    [InlineData("PUC_E3")]
    public void IsChildren_LeavesTheOtherChannelsOut(string channel)
    {
        Assert.False(ProgramClassifier.IsChildren(null, [channel]));
    }

    [Fact]
    public void IsChildren_WithoutData_SaysNo()
    {
        Assert.False(ProgramClassifier.IsChildren(null, null));
        Assert.False(ProgramClassifier.IsChildren([], []));
        Assert.False(ProgramClassifier.IsChildren(["", "  "], ["", "  "]));
    }
}
