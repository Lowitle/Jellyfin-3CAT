using System.Xml.Linq;
using Jellyfin.Plugin.ThreeCat.Library;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the <c>tvshow.nfo</c> and episode NFO documents read by Jellyfin.
/// </summary>
public class NfoBuilderTests
{
    [Fact]
    public void BuildSeries_WritesTheFieldsJellyfinReads()
    {
        var series = new LibrarySeries
        {
            Title = "Doraemon, el gat còsmic",
            ProgramId = "119987484",
            Plot = "Les aventures del gat còsmic.",
            Genres = ["Infantil"],
            Tags = ["Sèrie"],
            Country = " ES "
        };

        var document = XDocument.Parse(NfoBuilder.BuildSeries(series));
        var root = document.Root!;

        Assert.Equal("tvshow", root.Name.LocalName);
        Assert.Equal("Doraemon, el gat còsmic", root.Element("title")!.Value);
        Assert.Equal("Doraemon, el gat còsmic", root.Element("sorttitle")!.Value);
        Assert.Equal("Les aventures del gat còsmic.", root.Element("plot")!.Value);
        Assert.Equal("3Cat", root.Element("studio")!.Value);
        Assert.Equal("ES", root.Element("country")!.Value);
        Assert.Equal(["Infantil"], root.Elements("genre").Select(element => element.Value));
        Assert.Equal(["Sèrie", "3Cat"], root.Elements("tag").Select(element => element.Value));

        var uniqueId = root.Elements("uniqueid").Single();
        Assert.Equal("threecat", uniqueId.Attribute("type")!.Value);
        Assert.Equal("true", uniqueId.Attribute("default")!.Value);
        Assert.Equal("119987484", uniqueId.Value);
    }

    [Fact]
    public void BuildSeries_FallsBackToADefaultPlotAndDropsDuplicates()
    {
        var series = new LibrarySeries
        {
            Title = "Telenotícies",
            ProgramId = "1",
            Genres = ["Informatius", " Informatius ", "informatius", ""],
            Tags = ["Notícies", "notícies"]
        };

        var root = XDocument.Parse(NfoBuilder.BuildSeries(series)).Root!;

        Assert.Equal("Programas de Telenotícies en 3Cat.", root.Element("plot")!.Value);
        Assert.Equal(["Informatius"], root.Elements("genre").Select(element => element.Value));
        Assert.Equal(["Notícies", "3Cat"], root.Elements("tag").Select(element => element.Value));
        Assert.Null(root.Element("country"));
        Assert.DoesNotContain(root.Elements("uniqueid"), element => element.Attribute("type")!.Value == "imdb");
    }

    [Fact]
    public void BuildSeries_WritesTheImdbIdOnlyWhenItIsConfigured()
    {
        var series = new LibrarySeries
        {
            Title = "Merlí",
            ProgramId = "42",
            ImdbId = " tt1234567 "
        };

        var root = XDocument.Parse(NfoBuilder.BuildSeries(series)).Root!;
        var imdb = root.Elements("uniqueid").Single(element => element.Attribute("type")!.Value == "imdb");

        Assert.Equal("tt1234567", imdb.Value);
    }

    [Fact]
    public void BuildSeries_StartsWithTheXmlDeclaration()
    {
        var nfo = NfoBuilder.BuildSeries(new LibrarySeries { Title = "Merlí", ProgramId = "42" });

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", nfo, StringComparison.Ordinal);
        Assert.EndsWith("\n", nfo, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEpisode_WritesTheEpisodeFields()
    {
        var episode = new LibraryEpisode
        {
            Season = 2,
            Episode = 7,
            Title = "El gat còsmic",
            Plot = "Sinopsi de l'episodi.",
            Runtime = TimeSpan.FromSeconds(1447),
            Aired = new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.FromHours(2)),
            VideoId = "3251892",
            ThumbnailUrl = "https://img.3cat.cat/thumb.jpg",
            Genres = ["Infantil"],
            Director = "Fujiko F. Fujio"
        };

        var root = XDocument.Parse(NfoBuilder.BuildEpisode("Doraemon, el gat còsmic", episode)).Root!;

        Assert.Equal("episodedetails", root.Name.LocalName);
        Assert.Equal("El gat còsmic", root.Element("title")!.Value);
        Assert.Equal("Doraemon, el gat còsmic", root.Element("showtitle")!.Value);
        Assert.Equal("2", root.Element("season")!.Value);
        Assert.Equal("7", root.Element("episode")!.Value);
        Assert.Equal("Sinopsi de l'episodi.", root.Element("plot")!.Value);
        Assert.Equal("24", root.Element("runtime")!.Value);
        Assert.Equal("2026-06-15", root.Element("aired")!.Value);
        Assert.Equal("3251892", root.Element("uniqueid")!.Value);
        Assert.Equal("threecat", root.Element("uniqueid")!.Attribute("type")!.Value);
        Assert.Equal("https://img.3cat.cat/thumb.jpg", root.Element("thumb")!.Value);
        Assert.Equal("Infantil", root.Element("genre")!.Value);
        Assert.Equal("Fujiko F. Fujio", root.Element("director")!.Value);
    }

    [Fact]
    public void BuildEpisode_OmitsTheOptionalFieldsWhenTheyAreMissing()
    {
        var episode = new LibraryEpisode
        {
            Season = 1,
            Episode = 1,
            Title = "Pilot",
            Runtime = TimeSpan.FromSeconds(30),
            Genres = ["", "  "]
        };

        var root = XDocument.Parse(NfoBuilder.BuildEpisode("Doraemon", episode)).Root!;

        Assert.Null(root.Element("plot"));
        Assert.Null(root.Element("runtime"));
        Assert.Null(root.Element("aired"));
        Assert.Null(root.Element("uniqueid"));
        Assert.Null(root.Element("thumb"));
        Assert.Null(root.Element("director"));
        Assert.Empty(root.Elements("genre"));
    }

    [Fact]
    public void BuildEpisode_RoundsTheRuntimeUp()
    {
        var episode = new LibraryEpisode
        {
            Season = 1,
            Episode = 2,
            Title = "Mig episodi",
            Runtime = TimeSpan.FromSeconds(90)
        };

        var root = XDocument.Parse(NfoBuilder.BuildEpisode("Doraemon", episode)).Root!;

        Assert.Equal("2", root.Element("runtime")!.Value);
    }
}
