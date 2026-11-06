using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the grouping of the videos of a program of cinema into films.
/// </summary>
/// <remarks>
/// A program of cinema is not always a single film: 3Cat files a two-part TV movie and a slot of films
/// under the same kind, so the title of the video is the only thing that tells them apart.
/// </remarks>
public sealed class ThreeCatMovieGroupingTests
{
    [Fact]
    public void GroupFilms_JoinsThePartsOfOneFilm()
    {
        var groups = ThreeCatSyncService.GroupFilms(
        [
            Episode("Ermessenda (1a part)", "2011-05-01"),
            Episode("Ermessenda (2a part)", "2011-05-02")
        ]);

        Assert.Single(groups);
        Assert.Equal("Ermessenda", groups[0].Title);
        Assert.Equal(2, groups[0].Episodes.Count);
    }

    [Fact]
    public void GroupFilms_KeepsOneFilmPerTitle()
    {
        var groups = ThreeCatSyncService.GroupFilms(
        [
            Episode("Pa negre", "2018-01-01"),
            Episode("Estiu 1993", "2018-01-08")
        ]);

        Assert.Equal(["Pa negre", "Estiu 1993"], groups.Select(group => group.Title));
        Assert.All(groups, group => Assert.Single(group.Episodes));
    }

    [Fact]
    public void GroupFilms_OrdersTheFilmsByAirDate()
    {
        var groups = ThreeCatSyncService.GroupFilms(
        [
            Episode("Estiu 1993", "2018-01-08"),
            Episode("Pa negre", "2018-01-01")
        ]);

        Assert.Equal(["Pa negre", "Estiu 1993"], groups.Select(group => group.Title));
    }

    [Fact]
    public void GroupFilms_IgnoresTheCaseAndTheSpacingOfTheTitle()
    {
        // The catalog writes the same film twice with a different casing when it republishes it.
        var groups = ThreeCatSyncService.GroupFilms(
        [
            Episode("Pa negre", "2018-01-01"),
            Episode("PA NEGRE ", "2018-06-01")
        ]);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Episodes.Count);
    }

    private static LibraryEpisode Episode(string title, string aired) => new()
    {
        Title = title,
        Url = "https://mp4.3cat.cat/video/3251892.mp4",
        Aired = DateTimeOffset.Parse(aired, System.Globalization.CultureInfo.InvariantCulture)
    };
}
