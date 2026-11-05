using System.Globalization;
using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the matcher that numbers the videos of 3Cat with the episodes of a metadata provider.
/// </summary>
public sealed class ProviderNumberingTests
{
    [Fact]
    public void Assign_MatchesTheSameAirDate()
    {
        var result = ProviderNumbering.Assign(
            [Video("T01xC1 - Uno", "2020-01-01")],
            [Episode(1, 5, "Uno", "2020-01-01")]);

        Assert.Equal(new EpisodeSlot(1, 5, false), result.Slots[0]);
        Assert.Equal(ProviderMatch.Date, result.Methods[0]);
        Assert.Equal(1, result.ByDate);
        Assert.Equal(1d, result.Coverage);
        Assert.False(result.UseThreeCat);
    }

    [Fact]
    public void Assign_MatchesAnAirDateOneDayApart()
    {
        var result = ProviderNumbering.Assign(
            [Video("Uno", "2020-01-02")],
            [Episode(1, 1, "Uno", "2020-01-01")]);

        Assert.Equal(new EpisodeSlot(1, 1, false), result.Slots[0]);
        Assert.Equal(ProviderMatch.Date, result.Methods[0]);
        Assert.Equal(1, result.ByDate);
    }

    [Fact]
    public void Assign_MatchesTheTitleIgnoringTheNumberingAndTheAccents()
    {
        var result = ProviderNumbering.Assign(
            [Video("T01xC1 - L'últim estiu", null), Video("Dos", "2020-01-05")],
            [Episode(1, 1, "Uno", "2020-01-05"), Episode(1, 2, "L'ultim estiu", "1999-03-03")]);

        Assert.Equal(new EpisodeSlot(1, 2, false), result.Slots[0]);
        Assert.Equal(ProviderMatch.Title, result.Methods[0]);
        Assert.Equal(1, result.ByTitle);
        Assert.Equal(1, result.ByDate);
    }

    [Fact]
    public void Assign_MatchesAContainedTitleWhenOnlyOneEpisodeClaimsIt()
    {
        var result = ProviderNumbering.Assign(
            [Video("La nit més llarga", null), Video("Dos", "2020-01-05")],
            [Episode(1, 7, "La nit més llarga de l'any", "2001-02-03"), Episode(1, 1, "Uno", "2020-01-05")]);

        Assert.Equal(new EpisodeSlot(1, 7, false), result.Slots[0]);
        Assert.Equal(ProviderMatch.Title, result.Methods[0]);
        Assert.Equal(1, result.ByTitle);
    }

    [Fact]
    public void Assign_KeepsTheGapOfTheProviderNumbering()
    {
        var result = ProviderNumbering.Assign(
            [Video("Uno", "2021-05-05"), Video("Dos", "2021-05-12")],
            [Episode(27, 9, "Uno", "2021-05-05"), Episode(27, 10, "Dos", "2021-05-12")]);

        // The season 27 of the provider starts at the episode 9: the file has to say 9, not 1.
        Assert.Equal(new EpisodeSlot(27, 9, false), result.Slots[0]);
        Assert.Equal(new EpisodeSlot(27, 10, false), result.Slots[1]);
    }

    [Fact]
    public void Assign_SendsTheVideoWithoutEvidenceToTheSeasonOfSpecials()
    {
        var result = ProviderNumbering.Assign(
            [Video("Uno", null), Video("Dos", null), Video("Tres", "2020-03-03")],
            [Episode(1, 1, "Tres", "2020-03-03")]);

        Assert.Equal(new EpisodeSlot(1, 1, false), result.Slots[2]);
        Assert.Equal(new EpisodeSlot(0, 1, false), result.Slots[0]);
        Assert.Equal(new EpisodeSlot(0, 2, false), result.Slots[1]);
        Assert.Equal(ProviderMatch.None, result.Methods[0]);
        Assert.Equal(2, result.Specials);

        // One video of three is below the threshold: the series keeps the numbering of 3Cat.
        Assert.True(result.UseThreeCat);
    }

    [Fact]
    public void Assign_IgnoresTheSpecialsOfTheProvider()
    {
        var result = ProviderNumbering.Assign(
            [Video("Uno", "2020-01-01")],
            [Episode(0, 1, "Especial", "2020-01-01")]);

        Assert.True(result.UseThreeCat);
        Assert.Equal(0, result.Matched);
        Assert.Equal(new EpisodeSlot(0, 1, false), result.Slots[0]);
    }

    [Fact]
    public void Assign_GivesTheEpisodeToTheOldestVideo()
    {
        var result = ProviderNumbering.Assign(
            [Video("Uno", "2020-01-01"), Video("Dos", "2020-01-01")],
            [Episode(1, 1, "Uno", "2020-01-01")]);

        Assert.Equal(new EpisodeSlot(1, 1, false), result.Slots[0]);
        Assert.Equal(0, result.Slots[1].Season);
        Assert.Equal(ProviderMatch.None, result.Methods[1]);
        Assert.False(result.UseThreeCat);
    }

    [Fact]
    public void Assign_DropsTheVideoPublishedTwice()
    {
        var result = ProviderNumbering.Assign(
            [Video("Uno", "2020-01-01"), Video("Uno", "2020-01-01")],
            [Episode(1, 1, "Uno", "2020-01-01")]);

        Assert.False(result.Slots[0].IsDuplicate);
        Assert.True(result.Slots[1].IsDuplicate);
        Assert.Equal(1, result.Duplicates);
        Assert.Equal(1, result.Assignable);
        Assert.Equal(1d, result.Coverage);
        Assert.Equal(new EpisodeSlot(1, 1, false), result.Slots[0]);
    }

    [Fact]
    public void Assign_PairsAForcedGapByOrder()
    {
        var result = ProviderNumbering.Assign(
            [
                Video("Uno", "2020-01-01"),
                Video("Dos", "2020-01-08"),
                Video("Tres", "2020-01-15"),
                Video("Cuatro", "2020-01-22")
            ],
            [
                Episode(1, 1, "Uno", "2020-01-01"),
                Episode(1, 2, "Sin título", null),
                Episode(1, 3, "Sin título", null),
                Episode(1, 4, "Cuatro", "2020-01-22")
            ]);

        // The two episodes in the middle have neither date nor title, and the gap has as many videos as
        // episodes: there is a single way to pair them.
        Assert.Equal(new EpisodeSlot(1, 2, false), result.Slots[1]);
        Assert.Equal(new EpisodeSlot(1, 3, false), result.Slots[2]);
        Assert.Equal(ProviderMatch.Order, result.Methods[1]);
        Assert.Equal(2, result.ByOrder);
        Assert.Equal(2, result.ByDate);
        Assert.Equal(4, result.Assignable);
        Assert.Equal(1d, result.Coverage);
        Assert.False(result.UseThreeCat);
    }

    [Fact]
    public void Assign_DoesNotPairAGapWhoseDatesContradictIt()
    {
        var result = ProviderNumbering.Assign(
            [
                Video("Uno", "2020-01-01"),
                Video("Dos", "2020-01-08"),
                Video("Tres", "2020-01-15"),
                Video("Cuatro", "2020-01-22")
            ],
            [
                Episode(1, 1, "Uno", "2020-01-01"),
                Episode(1, 2, "Algo", "1996-02-05"),
                Episode(1, 3, "Otra cosa", "1996-02-12"),
                Episode(1, 4, "Cuatro", "2020-01-22")
            ]);

        // 3Cat re-emits old seasons with the date of the re-emission: pairing them with the episodes of a
        // season of 1996 because the count matches would be a silent error.
        Assert.Equal(0, result.ByOrder);
        Assert.Equal(2, result.Specials);
        Assert.Equal(0, result.Slots[1].Season);
        Assert.False(result.UseThreeCat);
    }

    [Fact]
    public void Assign_FallsBackToThreeCatBelowTheThreshold()
    {
        var result = ProviderNumbering.Assign(
            [
                Video("Uno", "2020-01-01"),
                Video("Dos", null),
                Video("Tres", null),
                Video("Cuatro", null)
            ],
            [Episode(1, 1, "Uno", "2020-01-01")]);

        Assert.True(result.UseThreeCat);
        Assert.Equal(0.25d, result.Coverage, 3);
        Assert.Equal(1, result.Matched);
        Assert.Equal(3, result.Specials);
        Assert.Contains("emparejan", result.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Assign_FallsBackToThreeCatWithoutEpisodes()
    {
        var result = ProviderNumbering.Assign([Video("Uno", "2020-01-01")], []);

        Assert.True(result.UseThreeCat);
        Assert.Equal(0, result.Matched);
        Assert.Contains("no publica episodios", result.Reason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("T22xC6 - L'últim estiu", null, "l ultim estiu")]
    [InlineData("L'ÚLTIM ESTIU", null, "l ultim estiu")]
    [InlineData("Doraemon, el gat còsmic - El gat còsmic", "Doraemon, el gat còsmic", "el gat cosmic")]
    [InlineData("  Visita als inferns  ", null, "visita als inferns")]
    [InlineData("", null, "")]
    [InlineData(null, null, "")]
    public void NormalizeTitle_ComparesTheSameTitleInDifferentSpellings(string? title, string? series, string expected)
    {
        Assert.Equal(expected, ProviderNumbering.NormalizeTitle(title, series));
    }

    [Fact]
    public void Assign_ReportsTheEpisodeOfEveryVideo()
    {
        var result = ProviderNumbering.Assign(
            [Video("Uno", "2020-01-01", "100"), Video("Dos", "2020-01-08", "200")],
            [Episode(2, 3, "Uno", "2020-01-01"), Episode(2, 4, "Dos", "2020-01-08")]);

        Assert.Equal("Uno", result.Episodes[0]!.Title);
        Assert.Equal(3, result.Episodes[0]!.Episode);
        Assert.Equal(new DateOnly(2020, 1, 8), result.Episodes[1]!.AirDate);
        Assert.Equal(ProviderMatch.Date, result.Methods[1]);
    }

    private static ProviderVideo Video(string? title, string? date, string? id = null)
        => new(title, ParseMoment(date), id);

    private static ProviderEpisode Episode(int season, int episode, string? title, string? date)
        => new(season, episode, title, date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));

    private static DateTimeOffset? ParseMoment(string? date)
        => date is null ? null : DateTimeOffset.Parse(date, CultureInfo.InvariantCulture);
}
