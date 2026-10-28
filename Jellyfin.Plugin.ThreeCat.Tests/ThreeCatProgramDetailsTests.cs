using System.Text.Json;
using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the reading of the page of a program (<c>api.3cat.cat/programestv/{id}</c>), which is where
/// 3Cat publishes the collections of a program and its country of origin.
/// </summary>
public sealed class ThreeCatProgramDetailsTests
{
    /// <summary>
    /// The page of «Doraemon», cut down to the fields that are read: the synopsis carries the card of the
    /// program in its last paragraphs.
    /// </summary>
    private const string DoraemonJson = """
        {
          "resposta": {
            "status": "OK",
            "item": {
              "id": "119987484",
              "titol": "Doraemon",
              "formats": { "id": "PUFF_SDIBU", "idint": "50729", "desc": "Sèrie animació", "main": true },
              "agrupacions": [
                { "id": "PUSX3_S3", "idint": "54660", "desc": "S3", "main": false },
                { "id": "PUA_NOVS3", "idint": "55074", "desc": "Novetats S3", "main": true }
              ],
              "info": {
                "id": "111846",
                "desc": "De què va?",
                "text": "<div><p>\"Doraemon\" és una sèrie d'animació japonesa basada en el manga homònim.</p><p><strong>Títol original:</strong> Doraemon&nbsp;</p><p><strong>País d'origen:</strong> Japó&nbsp;</p><p><strong>Any de producció:</strong> 2007-2013</p></div>"
              }
            }
          }
        }
        """;

    /// <summary>
    /// The page of a program the catalog writes the country of with the short label («Origen:»), which is
    /// the one the older pages use.
    /// </summary>
    private const string ShortLabelJson = """
        {
          "resposta": {
            "item": {
              "id": "199001673",
              "titol": "Ranma",
              "formats": [ { "id": "PUFF_SDIBU" } ],
              "agrupacions": [ { "id": "PUAG_3PPANIM" }, { "id": "PUAG_3CANEVA" }, { "id": "PUAG_ANI3CAT" } ],
              "info": { "desc": "De què va?", "text": "<p><strong>Origen:</strong> Japó</p>" }
            }
          }
        }
        """;

    [Fact]
    public void From_ReadsTheTitleAndTheFormatsOfTheProgram()
    {
        var details = Read(DoraemonJson)!;

        Assert.Equal("Doraemon", details.Title);
        Assert.Equal(["PUFF_SDIBU"], details.Formats);
    }

    [Fact]
    public void From_ReadsTheCollectionsOfTheProgram()
    {
        var details = Read(DoraemonJson)!;

        Assert.Equal(["PUSX3_S3", "PUA_NOVS3"], details.Groups);
    }

    [Fact]
    public void From_ReadsTheCardOfTheProgramOutOfTheSynopsis()
    {
        var details = Read(DoraemonJson)!;

        Assert.Equal("Doraemon", details.OriginalTitle);
        Assert.Equal(["Japó"], details.Countries);
    }

    [Fact]
    public void From_ReadsTheSingleCollectionAndTheShortLabelAsAnObject()
    {
        // 3Cat sends a single value as an object instead of a one-item array.
        var details = Read(ShortLabelJson)!;

        Assert.Equal(["PUFF_SDIBU"], details.Formats);
        Assert.Equal(["PUAG_3PPANIM", "PUAG_3CANEVA", "PUAG_ANI3CAT"], details.Groups);
        Assert.Equal(["Japó"], details.Countries);
        Assert.True(ProgramClassifier.IsAnime(details.Groups, details.Countries));
    }

    [Fact]
    public void Countries_ReadsTheLabelWithTheColonAfterTheTag()
    {
        // «Bola de Drac» writes the label inside the tag and the colon after it.
        const string Html = "<strong>Títol original</strong>: Dragon Ball<br><strong>País d'origen</strong>: Japó<br>";

        Assert.Equal(["Japó"], ProgramDetailText.Countries([Html]));
        Assert.Equal("Dragon Ball", ProgramDetailText.OriginalTitle([Html]));
    }

    [Fact]
    public void Countries_ReadsTheLabelWrittenWithNonBreakingSpaces()
    {
        // «Shin chan» writes the spaces of the label as entities, which are not a separator.
        const string Html = "<p><strong>Títol&nbsp;original:</strong>&nbsp;Crayon Shin-chan&nbsp;</p>"
            + "<p><strong>Origen:</strong>&nbsp;Japó&nbsp;</p>";

        Assert.Equal(["Japó"], ProgramDetailText.Countries([Html]));
        Assert.Equal("Crayon Shin-chan", ProgramDetailText.OriginalTitle([Html]));
    }

    [Fact]
    public void Countries_DoesNotReadTheLabelOfTheOriginalTitle()
    {
        // «El xai Shaun» labels the original title with the short form, which is not a country.
        const string Html = "<strong>Títol origen:</strong> Shaun the sheep<br>"
            + "<strong>Origen:</strong> Gran Bretanya<br>";

        Assert.Equal(["Gran Bretanya"], ProgramDetailText.Countries([Html]));
    }

    [Fact]
    public void OriginalTitle_WithoutATitle_IsNull()
    {
        // «Naruto» leaves the title to the next tag, so only the quote is published.
        Assert.Null(ProgramDetailText.OriginalTitle(["<strong>Títol original:</strong> “<em>Naruto</em>”"]));
    }

    [Fact]
    public void OriginalTitle_OfATitleWithTwoNames_KeepsTheFirstOne()
    {
        // «Ranma» publishes both the Japanese and the romanised title in the same line.
        const string Html = "<strong>Títol original:</strong> \"Ranma 1/2\" o \"Ranma nibun no ichi\"";

        Assert.Equal("Ranma 1/2", ProgramDetailText.OriginalTitle([Html]));
    }

    [Theory]
    [InlineData("<p><strong>País d'origen:</strong> Japó</p>", "Japó")]
    [InlineData("<p><strong>País d'origen:</strong>&nbsp;Japó&nbsp;</p>", "Japó")]
    [InlineData("<p><strong>Origen:</strong> Japó</p>", "Japó")]
    [InlineData("<p><strong>País:</strong> Japó</p>", "Japó")]
    [InlineData("<p>País de origen: Japó</p>", "Japó")]
    [InlineData("<p><strong>País d'origen: </strong>Japó (2007)</p>", "Japó")]
    public void Countries_ReadsTheLabelWithTheSpellingsOfTheCatalog(string html, string expected)
    {
        Assert.Equal([expected], ProgramDetailText.Countries([html]));
    }

    [Theory]
    [InlineData(
        "<p><strong>País d'origen:</strong> França</p><p><strong>País d'origen:</strong> Japó</p>",
        new[] { "França", "Japó" })]
    [InlineData("<p><strong>País d'origen:</strong> França, Japó</p>", new[] { "França", "Japó" })]
    [InlineData("<p><strong>País d'origen:</strong> França/Japó</p>", new[] { "França", "Japó" })]
    [InlineData("<p><strong>País d'origen:</strong> França i Japó</p>", new[] { "França", "Japó" })]
    public void Countries_SplitsTheCoproductions(string html, string[] expected)
    {
        Assert.Equal(expected, ProgramDetailText.Countries([html]));
    }

    [Fact]
    public void Countries_ReadsEveryCountryOfEverySection()
    {
        var countries = ProgramDetailText.Countries(
        [
            "<p><strong>País d'origen:</strong> França</p>",
            null,
            "   ",
            "<p><strong>País d'origen:</strong> Japó</p>"
        ]);

        Assert.Equal(["França", "Japó"], countries);
    }

    [Fact]
    public void Countries_DoesNotRepeatACountry()
    {
        Assert.Equal(
            ["Japó"],
            ProgramDetailText.Countries(["<p><strong>País d'origen:</strong> Japó, Japó</p>"]));
    }

    [Fact]
    public void Countries_IgnoresTheSentencesThatAreNotACountry()
    {
        // A card that sends the reader elsewhere must not pass as a country.
        Assert.Empty(ProgramDetailText.Countries(["<p><strong>País:</strong> vegeu la fitxa tècnica del programa</p>"]));
        Assert.Empty(ProgramDetailText.Countries(["<p>Sinopsi sense fitxa tècnica.</p>"]));
    }

    [Fact]
    public void OriginalTitle_WithoutTheCard_IsNull()
    {
        Assert.Null(ProgramDetailText.OriginalTitle(["<p>\"Doraemon\" és una sèrie d'animació japonesa.</p>"]));
    }

    [Fact]
    public void From_WithoutTheCard_ReadsNoCountry()
    {
        const string Json = """
            { "resposta": { "item": { "id": "1", "titol": "Mironins", "agrupacions": [] } } }
            """;

        var details = Read(Json)!;

        Assert.Empty(details.Groups);
        Assert.Empty(details.Countries);
        Assert.Null(details.OriginalTitle);
        Assert.False(ProgramClassifier.IsAnime(details.Groups, details.Countries));
    }

    [Fact]
    public void From_WithoutProgram_ReturnsNull()
    {
        Assert.Null(ThreeCatProgramDetails.From(null));
    }

    private static ThreeCatProgramDetails? Read(string json)
    {
        var envelope = JsonSerializer.Deserialize<ThreeCatProgramDetailEnvelope>(json, ThreeCatClient.JsonOptions);

        return ThreeCatProgramDetails.From(envelope!.Response!.Item);
    }
}
