using System.Globalization;
using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the pass that pairs a video with the episode its <c>capitol</c> announces.
/// </summary>
/// <remarks>
/// It is the answer to «Detectiu Conan»: 385 videos dubbed from 2001 to 2025 against a TMDB that lists the
/// episodes of a single absolute season, with the catalog of 3Cat full of holes (it jumps from the chapter 135 to
/// the 857). Counting videos instead of reading the counter numbers the 857 as the 136 and loses the whole tail.
/// </remarks>
public sealed class ProviderCounterTests
{
    [Fact]
    public void Assign_PairsByTheCounterAndKeepsTheHolesOfTheCatalog()
    {
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC1 - El detectiu més famós", "2001-05-17", "1", 1),
                Video("T1xC2 - El famós detectiu", "2001-05-18", "2", 2),
                Video("T3xC47 - L'últim de la temporada", "2003-04-09", "3", 135),
                Video("T20xC40 - Vint anys després", "2021-01-11", "4", 857),
                Video("T21xC1 - El misteri teatral", "2021-01-12", "5", 859)
            ],
            Episodes(900),
            anchors: ProviderAnchors.Parse("T1=1"));

        // The video 3Cat calls 857 is the episode 857, not the fourth video of the list.
        Assert.Equal(857, result.Slots[3].Episode);
        Assert.Equal(859, result.Slots[4].Episode);
        Assert.Equal(ProviderMatch.Counter, result.Methods[4]);
        Assert.Equal(0, result.CounterOffset);

        // The anchor places its own pair and the counter does the rest: the fill of the anchor is not walked.
        Assert.Equal(1, result.ByAnchor);
        Assert.Equal(4, result.ByCounter);
        Assert.False(result.UseThreeCat);
    }

    [Fact]
    public void Assign_FollowsTheCounterAndNotTheChapterOfTheTitle()
    {
        // 3Cat publishes the chapter 4 of «T22» before the 2 and the 3: the counter is what orders them.
        var result = ProviderNumbering.Assign(
            [
                Video("T22xC1 - Una", "2021-01-01", "1", 902),
                Video("T22xC4 - Cuatro", "2021-01-02", "2", 903),
                Video("T22xC2 - Dos", "2021-01-03", "3", 904)
            ],
            Episodes(910));

        Assert.Equal(902, result.Slots[0].Episode);
        Assert.Equal(903, result.Slots[1].Episode);
        Assert.Equal(904, result.Slots[2].Episode);
        Assert.Equal(3, result.ByCounter);
    }

    [Fact]
    public void Assign_ReadsTheCounterWhenTheCatalogDatesItsChaptersOutOfOrder()
    {
        // «Detectiu Conan» as 3Cat publishes it: the chapter 40 of the program is dated after the 41 and the 53
        // after the 79, because the catalog carries the dates of the dub and carries them wrong. The counter is
        // still the numbering of the program, and reading it as an order threw the whole series to the season 0.
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC41 - Cuarenta y uno", "2001-07-07", "1", 41),
                Video("T1xC40 - Cuarenta", "2001-07-08", "2", 40),
                Video("T2xC36 - Setenta y nueve", "2001-08-13", "3", 79),
                Video("T2xC10 - Cincuenta y tres", "2001-08-14", "4", 53)
            ],
            Episodes(100));

        Assert.Equal(41, result.Slots[0].Episode);
        Assert.Equal(40, result.Slots[1].Episode);
        Assert.Equal(79, result.Slots[2].Episode);
        Assert.Equal(53, result.Slots[3].Episode);
        Assert.Equal(ProviderMatch.Counter, result.Methods[3]);
        Assert.Equal(4, result.ByCounter);
        Assert.False(result.UseThreeCat);
    }

    [Fact]
    public void Assign_CalibratesTheOffsetWithASingleAnchor()
    {
        // «T1xC1=2» says the counter of 3Cat runs one episode behind the one of TMDB, and every video follows.
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC1 - Uno", "2001-05-17", "1", 1),
                Video("T2xC1 - Dos", "2001-07-11", "2", 44)
            ],
            Episodes(100),
            anchors: ProviderAnchors.Parse("T1xC1=2"));

        Assert.Equal(2, result.Slots[0].Episode);
        Assert.Equal(45, result.Slots[1].Episode);
        Assert.Equal(1, result.CounterOffset);
        Assert.Equal(1, result.ByAnchor);
        Assert.Equal(1, result.ByCounter);
    }

    [Fact]
    public void Assign_LeavesOutTheChaptersTheProviderDoesNotPublishYet()
    {
        // The tail of «Detectiu Conan»: 1.206 chapters in 3Cat against 1.150 episodes in TMDB.
        var result = ProviderNumbering.Assign(
            [
                Video("T28xC43 - Uno", "2024-01-01", "1", 1150),
                Video("T29xC1 - Dos", "2024-01-02", "2", 1168)
            ],
            Episodes(1150));

        Assert.Equal(1150, result.Slots[0].Episode);
        Assert.Equal(0, result.Slots[1].Season);
        Assert.Equal(1, result.ByCounter);
    }

    [Fact]
    public void Assign_DoesNotReadASeasonCounterAsTheCounterOfTheProgram()
    {
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC1 - Uno", "2001-05-17", "1", 1),
                Video("T1xC2 - Dos", "2001-05-18", "2", 2),
                Video("T2xC1 - Tres", "2001-07-11", "3", 1),
                Video("T2xC2 - Cuatro", "2001-07-12", "4", 2)
            ],
            Episodes(4));

        Assert.Equal(0, result.ByCounter);
        Assert.DoesNotContain(ProviderMatch.Counter, result.Methods);
    }

    [Fact]
    public void Assign_DoesNotReadAReEmissionAsTheCounterOfTheProgram()
    {
        // The chapter 300 of the catalog is followed by a re-emission of the 2: the counter is not the order.
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC1 - Uno", "2001-05-17", "1", 1),
                Video("T9xC9 - Nueve", "2001-05-18", "2", 300),
                Video("T1xC2 - Dos", "2001-05-19", "3", 2)
            ],
            Episodes(400));

        Assert.Equal(0, result.ByCounter);
    }

    [Fact]
    public void Assign_IgnoresTheCounterWhenTheProviderListsAFractionOfIt()
    {
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC1 - Uno", "2001-05-17", "1", 1),
                Video("T1xC2 - Dos", "2001-05-18", "2", 2),
                Video("T1xC3 - Tres", "2001-05-19", "3", 3)
            ],
            Episodes(2));

        Assert.Equal(0, result.ByCounter);
    }

    [Fact]
    public void Assign_DoesNotTakeAnEpisodeTheDatesAlreadyClaimed()
    {
        // The dates are the same emission (the provider lists the videos on their own days), so they win.
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC1 - Uno", "2020-01-01", "1", 5),
                Video("T1xC2 - Dos", "2020-01-02", "2", 6)
            ],
            [
                Episode(1, 1, "Prov A", "2020-01-01"),
                Episode(1, 2, "Prov B", "2020-01-02"),
                Episode(1, 3, "Prov C", "2020-01-03"),
                Episode(1, 4, "Prov D", "2020-01-04"),
                Episode(1, 5, "Prov E", "2020-01-05"),
                Episode(1, 6, "Prov F", "2020-01-06")
            ]);

        Assert.True(result.DatesReliable);
        Assert.Equal(ProviderMatch.Date, result.Methods[0]);
        Assert.Equal(1, result.Slots[0].Episode);
        Assert.Equal(2, result.Slots[1].Episode);
        Assert.Equal(0, result.ByCounter);
    }

    private static List<ProviderEpisode> Episodes(int count)
    {
        var episodes = new List<ProviderEpisode>();

        for (var episode = 1; episode <= count; episode++)
        {
            episodes.Add(Episode(1, episode, "Episodio " + episode.ToString(CultureInfo.InvariantCulture), null));
        }

        return episodes;
    }

    private static ProviderVideo Video(string? title, string? date, string? id = null, int? chapter = null)
        => new(title, ParseMoment(date), id, chapter);

    private static ProviderEpisode Episode(int season, int episode, string? title, string? date)
        => new(season, episode, title, date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));

    private static DateTimeOffset? ParseMoment(string? date)
        => date is null ? null : DateTimeOffset.Parse(date, CultureInfo.InvariantCulture);
}
