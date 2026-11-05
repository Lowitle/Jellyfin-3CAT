using System.Globalization;
using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the anchors of the mapping: the text the user writes («T1=1») and the run of videos each one numbers.
/// </summary>
/// <remarks>
/// They cover the case the rest of the evidence cannot solve —a series dubbed years after the original, with the
/// titles translated and the air dates of the dub— which is the one that left «Detectiu Conan» with 46 false
/// positives by date and 339 videos in the season of specials.
/// </remarks>
public sealed class ProviderAnchorTests
{
    [Theory]
    [InlineData("T1=1", 1, null, 1, 1)]
    [InlineData("T20=856", 20, null, 1, 856)]
    [InlineData("T1xC1=S01E01", 1, 1, 1, 1)]
    [InlineData("T22xC6=S01E906", 22, 6, 1, 906)]
    [InlineData("T22C6=E906", 22, 6, 1, 906)]
    [InlineData("t22 x c 6 = s1 e 906", 22, 6, 1, 906)]
    [InlineData("T3xC4=S02E07", 3, 4, 2, 7)]
    public void Parse_ReadsTheAnchor(string text, int season, int? chapter, int targetSeason, int targetEpisode)
    {
        var anchor = Assert.Single(ProviderAnchors.Parse(text));

        Assert.Equal(season, anchor.Season);
        Assert.Equal(chapter, anchor.Chapter);
        Assert.Equal(targetSeason, anchor.TargetSeason);
        Assert.Equal(targetEpisode, anchor.TargetEpisode);
    }

    [Fact]
    public void Parse_ReadsSeveralAnchorsSeparatedByAnything()
    {
        var anchors = ProviderAnchors.Parse("T1=1; T20=856, T22xC6=906\nT27=1098");

        Assert.Equal(4, anchors.Count);
        Assert.Equal(20, anchors[1].Season);
        Assert.Equal(906, anchors[2].TargetEpisode);
        Assert.Equal(1098, anchors[3].TargetEpisode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("conan")]
    [InlineData("T1=")]
    [InlineData("=1")]
    [InlineData("T1=episodio uno")]
    public void Parse_IgnoresWhatIsNotAnAnchor(string? text)
        => Assert.Empty(ProviderAnchors.Parse(text));

    [Fact]
    public void Parse_KeepsTheValidAnchorsOfAMixedText()
        => Assert.Single(ProviderAnchors.Parse("T1=1; no es un ancla; T2=x"));

    [Fact]
    public void Assign_NumbersTheSeasonOfAnAnchorByOrder()
    {
        // The dates belong to another emission, which is what the gate of the air dates detects.
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC1 - El detectiu més famós", "2001-05-17"),
                Video("T1xC2 - El famós detectiu", "2001-05-18"),
                Video("T1xC3 - Compte amb els ídols", "2001-05-21")
            ],
            [
                Episode(1, 1, "Airs 1996-01-08", "1996-01-08"),
                Episode(1, 2, "Airs 1996-01-15", "1996-01-15"),
                Episode(1, 3, "Airs 1996-01-22", "1996-01-22"),
                Episode(1, 4, "Airs 1996-01-29", "1996-01-29")
            ],
            anchors: ProviderAnchors.Parse("T1=1"));

        Assert.Equal(new EpisodeSlot(1, 1, false), result.Slots[0]);
        Assert.Equal(new EpisodeSlot(1, 2, false), result.Slots[1]);
        Assert.Equal(new EpisodeSlot(1, 3, false), result.Slots[2]);
        Assert.Equal(ProviderMatch.Anchor, result.Methods[2]);
        Assert.Equal(3, result.ByAnchor);
        Assert.Equal(0, result.ByDate);
        Assert.False(result.DatesReliable);
        Assert.False(result.UseThreeCat);
    }

    [Fact]
    public void Assign_StopsTheFillAtTheNextSeason()
    {
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC1 - Uno", "2001-05-17"),
                Video("T1xC2 - Dos", "2001-05-18"),
                Video("T2xC1 - Tres", "2001-07-11"),
                Video("T2xC2 - Cuatro", "2001-07-12")
            ],
            [
                Episode(1, 1, "Prov A", "1996-01-08"),
                Episode(1, 2, "Prov B", "1996-01-15"),
                Episode(1, 3, "Prov C", "1996-01-22"),
                Episode(1, 4, "Prov D", "1996-01-29")
            ],
            anchors: ProviderAnchors.Parse("T1=1"));

        Assert.Equal(new EpisodeSlot(1, 2, false), result.Slots[1]);
        // The season 2 has no anchor and no evidence: it is not numbered with the episodes of the season 1.
        Assert.Equal(new EpisodeSlot(0, 1, false), result.Slots[2]);
        Assert.Equal(new EpisodeSlot(0, 2, false), result.Slots[3]);
        Assert.Equal(2, result.ByAnchor);
    }

    [Fact]
    public void Assign_NumbersBackwardsFromAChapterAnchor()
    {
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC1 - Uno", "2001-05-17"),
                Video("T1xC2 - Dos", "2001-05-18"),
                Video("T1xC3 - Tres", "2001-05-19"),
                Video("T1xC4 - Cuatro", "2001-05-20")
            ],
            [
                Episode(1, 8, "Prov A", "1996-01-08"),
                Episode(1, 9, "Prov B", "1996-01-15"),
                Episode(1, 10, "Prov C", "1996-01-22"),
                Episode(1, 11, "Prov D", "1996-01-29")
            ],
            anchors: ProviderAnchors.Parse("T1xC3=10"));

        Assert.Equal(8, result.Slots[0].Episode);
        Assert.Equal(9, result.Slots[1].Episode);
        Assert.Equal(10, result.Slots[2].Episode);
        Assert.Equal(11, result.Slots[3].Episode);
        Assert.Equal(4, result.ByAnchor);
    }

    [Fact]
    public void Assign_DoesNotUseTheDatesOfAnotherEmission()
    {
        // The shape of «Detectiu Conan»: a daily dub of 2001 against a weekly original of 2001. Every fifth video
        // falls on the day of an episode just by accident, which was the 46 false positives of the preview.
        var videos = new List<ProviderVideo>();
        var day = new DateOnly(2001, 5, 17);
        for (var index = 0; index < 20; index++)
        {
            videos.Add(Video(
                $"T1xC{index + 1} - Capítulo {index + 1}",
                day.AddDays(index).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        var episodes = new List<ProviderEpisode>();
        var week = new DateOnly(2001, 5, 21);
        for (var index = 0; index < 6; index++)
        {
            episodes.Add(Episode(
                1,
                234 + index,
                $"Episodio {234 + index}",
                week.AddDays(index * 7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        var result = ProviderNumbering.Assign(videos, episodes);

        Assert.False(result.DatesReliable);
        Assert.Equal(0, result.ByDate);
        Assert.Equal(0, result.Matched);
        Assert.True(result.UseThreeCat);
    }

    [Fact]
    public void Assign_ReportsTheSeasonsOfThreeCatWithTheirCounter()
    {
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC1 - Uno", "2001-05-17", "1", 1),
                Video("T1xC2 - Dos", "2001-05-18", "2", 2),
                Video("T3xC1 - Tres", "2003-04-09", "3", 89)
            ],
            [Episode(1, 1, "Prov A", null)]);

        Assert.Equal(2, result.Blocks.Count);
        Assert.Equal(1, result.Blocks[0].Season);
        Assert.Equal(2, result.Blocks[0].Videos);
        Assert.Equal(1, result.Blocks[0].FirstChapter);
        Assert.Equal(2, result.Blocks[0].LastChapter);
        Assert.Equal(1, result.Blocks[0].FirstCounter);
        Assert.Equal(89, result.Blocks[1].FirstCounter);
        Assert.True(result.Blocks[1].Numbered);
    }

    [Fact]
    public void Assign_ReportsAProgramWithoutNumberingAsOneBlock()
    {
        var result = ProviderNumbering.Assign(
            [Video("El gat còsmic", "2020-01-01"), Video("La flor del Pinotxo", "2020-01-02")],
            [Episode(1, 1, "El gat còsmic", null)]);

        var block = Assert.Single(result.Blocks);

        Assert.Equal(0, block.Season);
        Assert.False(block.Numbered);
        Assert.Null(block.FirstChapter);
    }

    [Fact]
    public void Assign_PlacesEveryAnchorBeforeFillingTheRun()
    {
        // «T1=1; T2=4» over a season 1 with four videos: the pin of the season 2 wins over the fill of the season 1,
        // so the fourth video of the season 1 is left out instead of stealing the episode the user pointed at.
        var result = ProviderNumbering.Assign(
            [
                Video("T1xC1 - Uno", "2001-05-17"),
                Video("T1xC2 - Dos", "2001-05-18"),
                Video("T1xC3 - Tres", "2001-05-19"),
                Video("T1xC4 - Cuatro", "2001-05-20"),
                Video("T2xC1 - Cinco", "2001-07-11")
            ],
            [
                Episode(1, 1, "Prov A", "1996-01-08"),
                Episode(1, 2, "Prov B", "1996-01-15"),
                Episode(1, 3, "Prov C", "1996-01-22"),
                Episode(1, 4, "Prov D", "1996-01-29"),
                Episode(1, 5, "Prov E", "1996-02-05")
            ],
            anchors: ProviderAnchors.Parse("T1=1; T2=4"));

        Assert.Equal(2, result.Anchors);
        Assert.Equal(2, result.AnchorsPlaced);
        Assert.Equal(1, result.Slots[0].Episode);
        Assert.Equal(2, result.Slots[1].Episode);
        Assert.Equal(3, result.Slots[2].Episode);
        Assert.Equal(0, result.Slots[3].Season);
        Assert.Equal(4, result.Slots[4].Episode);
        Assert.Equal(1, result.Slots[4].Season);
    }

    [Fact]
    public void Assign_ReportsTheAnchorsItCouldNotPlace()
    {
        // «T9» names a season the program does not have: it is counted and reported, not silently dropped.
        var result = ProviderNumbering.Assign(
            [Video("T1xC1 - Uno", "2001-05-17")],
            [Episode(1, 1, "Prov A", "1996-01-08")],
            anchors: ProviderAnchors.Parse("T9=1; T1=1"));

        Assert.Equal(2, result.Anchors);
        Assert.Equal(1, result.AnchorsPlaced);
        Assert.Equal(1, result.ByAnchor);
    }

    private static ProviderVideo Video(string? title, string? date, string? id = null, int? chapter = null)
        => new(title, ParseMoment(date), id, chapter);

    private static ProviderEpisode Episode(int season, int episode, string? title, string? date)
        => new(season, episode, title, date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));

    private static DateTimeOffset? ParseMoment(string? date)
        => date is null ? null : DateTimeOffset.Parse(date, CultureInfo.InvariantCulture);
}
