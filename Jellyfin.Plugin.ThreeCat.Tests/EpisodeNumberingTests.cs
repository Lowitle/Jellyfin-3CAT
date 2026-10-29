using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the numbering the plugin reads from the 3Cat titles.
/// </summary>
public sealed class EpisodeNumberingTests
{
    [Theory]
    [InlineData("T22xC6 - Visita als inferns de l'amor (Episodi d'Oita)", 22, 6)]
    [InlineData("T1xC1 - El detectiu més famós d'aquest segle", 1, 1)]
    [InlineData("T 9 x C 4 - 03/10/2016", 9, 4)]
    [InlineData("T22C6 - Un cas d'ovnis", 22, 6)]
    [InlineData("t22xc6 - Minúscules", 22, 6)]
    [InlineData("T22xC6", 22, 6)]
    public void Parse_ReadsTheNumberingOfTheTitle(string title, int season, int episode)
    {
        Assert.Equal((season, episode), EpisodeNumbering.Parse(title));
    }

    [Theory]
    [InlineData("Especial confinament")]
    [InlineData("Polònia, collita 2019")]
    [InlineData("Crackòvia - T9xC4 - Gran Reserva")]
    [InlineData("Tarda oberta - Entrevista")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_ReturnsNullWithoutNumbering(string? title)
    {
        Assert.Null(EpisodeNumbering.Parse(title));
    }

    [Theory]
    [InlineData("T22xC6 - Visita als inferns de l'amor", "Visita als inferns de l'amor")]
    [InlineData("T1xC18 - 15 06 2026", "15 06 2026")]
    [InlineData("T9xC4 - 03/10/2016", "03/10/2016")]
    [InlineData("T22xC6Visita", "Visita")]
    [InlineData("T22xC6", "T22xC6")]
    [InlineData("Especial confinament", "Especial confinament")]
    public void StripNumber_RemovesTheNumbering(string title, string expected)
    {
        Assert.Equal(expected, EpisodeNumbering.StripNumber(title));
    }

    [Fact]
    public void WithAirDate_PutsTheDateFirst()
    {
        var aired = new DateTimeOffset(2026, 10, 27, 21, 32, 0, TimeSpan.FromHours(2));

        Assert.Equal(
            "2026-10-27 - Especial confinament",
            EpisodeNumbering.WithAirDate("Especial confinament", aired));
    }

    [Fact]
    public void WithAirDate_KeepsATitleThatAlreadyStartsWithTheDate()
    {
        var aired = new DateTimeOffset(2016, 10, 3, 22, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal("03/10/2016 - T9xC4", EpisodeNumbering.WithAirDate("03/10/2016 - T9xC4", aired));
    }

    [Fact]
    public void WithAirDate_ReturnsTheTitleWithoutADate()
    {
        Assert.Equal("Especial", EpisodeNumbering.WithAirDate("Especial", null));
        Assert.Equal("2026-10-27", EpisodeNumbering.WithAirDate(string.Empty, new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Assign_UsesTheSeasonAndChapterOfTheTitle()
    {
        string?[] titles =
        [
            "T22xC1 - Un cas d'ovnis",
            "T21xC6 - L'hospital del Kamaitachi",
            "T22xC6 - Visita als inferns"
        ];

        var slots = EpisodeNumbering.Assign(titles);

        Assert.Equal(new EpisodeSlot(22, 1, false), slots[0]);
        Assert.Equal(new EpisodeSlot(21, 6, false), slots[1]);
        Assert.Equal(new EpisodeSlot(22, 6, false), slots[2]);
    }

    [Fact]
    public void Assign_SendsTheVideosWithoutNumberingToTheSpecials()
    {
        string?[] titles =
        [
            "T1xC1 - El primer",
            "Especial confinament",
            "T1xC2 - El segon",
            "Polònia, collita 2019"
        ];

        var slots = EpisodeNumbering.Assign(titles);

        Assert.Equal(new EpisodeSlot(1, 1, false), slots[0]);
        Assert.Equal(new EpisodeSlot(0, 1, false), slots[1]);
        Assert.Equal(new EpisodeSlot(1, 2, false), slots[2]);
        Assert.Equal(new EpisodeSlot(0, 2, false), slots[3]);
    }

    [Fact]
    public void Assign_KeepsTheSeasonOneOfAProgramThatNumbersNothing()
    {
        string?[] titles = ["El temps de demà", "L'entrevista del dia", "La tertúlia"];

        var slots = EpisodeNumbering.Assign(titles);

        Assert.Equal(new EpisodeSlot(1, 1, false), slots[0]);
        Assert.Equal(new EpisodeSlot(1, 2, false), slots[1]);
        Assert.Equal(new EpisodeSlot(1, 3, false), slots[2]);
    }

    [Fact]
    public void Assign_DropsTheVideoPublishedTwice()
    {
        string?[] titles =
        [
            "T18xC22 - 16/02/2023",
            "T18xC22 -  16/02/2023"
        ];

        var slots = EpisodeNumbering.Assign(titles);

        Assert.Equal(new EpisodeSlot(18, 22, false), slots[0]);
        Assert.True(slots[1].IsDuplicate);
    }

    [Fact]
    public void Assign_SendsARepeatedNumberingWithAnotherTitleToTheSpecials()
    {
        string?[] titles =
        [
            "T9xC4 - 03/10/2016",
            "T9xC4 - Gran Reserva"
        ];

        var slots = EpisodeNumbering.Assign(titles);

        Assert.Equal(new EpisodeSlot(9, 4, false), slots[0]);
        Assert.Equal(new EpisodeSlot(0, 1, false), slots[1]);
    }
}
