using System.Xml.Linq;
using Jellyfin.Plugin.ThreeCat.Configuration;
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
    public void BuildSeries_WritesTheOriginalTitleOnlyWhenItSaysSomethingNew()
    {
        var series = new LibrarySeries
        {
            Title = "Bola de Drac",
            OriginalTitle = " Dragon Ball ",
            ProgramId = "73476",
            Genres = ["Anime", "Animació"]
        };

        var root = XDocument.Parse(NfoBuilder.BuildSeries(series)).Root!;

        Assert.Equal("Bola de Drac", root.Element("title")!.Value);
        Assert.Equal("Dragon Ball", root.Element("originaltitle")!.Value);
        Assert.Equal(["Anime", "Animació"], root.Elements("genre").Select(element => element.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bola de drac")]
    public void BuildSeries_OmitsTheOriginalTitleWhenItIsMissingOrRepeatsTheTitle(string? originalTitle)
    {
        var series = new LibrarySeries
        {
            Title = "Bola de Drac",
            OriginalTitle = originalTitle,
            ProgramId = "73476"
        };

        var root = XDocument.Parse(NfoBuilder.BuildSeries(series)).Root!;

        Assert.Null(root.Element("originaltitle"));
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

    [Fact]
    public void BuildMovie_WritesTheFieldsJellyfinReads()
    {
        var movie = new LibraryMovie
        {
            Title = "Ermessenda",
            ProgramId = "119987484",
            Year = 2011,
            Plot = "La comtessa Ermessenda de Carcassona.",
            Runtime = TimeSpan.FromMinutes(120),
            PremiereDate = new DateTimeOffset(2011, 5, 1, 22, 0, 0, TimeSpan.Zero),
            Genres = ["Cinema", "Ficció històrica"],
            Tags = ["Cinema"],
            Country = " ES "
        };

        var root = XDocument.Parse(NfoBuilder.BuildMovie(movie)).Root!;

        Assert.Equal("movie", root.Name.LocalName);
        Assert.Equal("Ermessenda", root.Element("title")!.Value);
        Assert.Equal("La comtessa Ermessenda de Carcassona.", root.Element("plot")!.Value);
        Assert.Equal("2011", root.Element("year")!.Value);
        Assert.Equal("2011-05-01", root.Element("premiered")!.Value);
        Assert.Equal("120", root.Element("runtime")!.Value);
        Assert.Equal("ES", root.Element("country")!.Value);
        Assert.Equal("3Cat", root.Element("studio")!.Value);
        Assert.Equal(["Cinema", "Ficció històrica"], root.Elements("genre").Select(element => element.Value));
        Assert.Equal(["Cinema", "3Cat"], root.Elements("tag").Select(element => element.Value));
    }

    [Fact]
    public void BuildMovie_WritesNoProviderId()
    {
        // The identity dataset resolves the ids of the series of 3Cat: a TMDB id of the television record
        // on a film would make Jellyfin fetch the metadata of another work.
        var movie = new LibraryMovie { Title = "Ermessenda", ProgramId = "119987484" };

        var root = XDocument.Parse(NfoBuilder.BuildMovie(movie)).Root!;

        var uniqueId = root.Elements("uniqueid").Single();
        Assert.Equal("threecat", uniqueId.Attribute("type")!.Value);
        Assert.Equal("true", uniqueId.Attribute("default")!.Value);
        Assert.Equal("119987484", uniqueId.Value);
        Assert.Single(root.Elements("uniqueid"));
    }

    [Fact]
    public void BuildMovie_WithoutAYearOrADateWritesNoDate()
    {
        var movie = new LibraryMovie { Title = "Pa negre", ProgramId = "1" };

        var root = XDocument.Parse(NfoBuilder.BuildMovie(movie)).Root!;

        Assert.Null(root.Element("year"));
        Assert.Null(root.Element("premiered"));
        Assert.Null(root.Element("runtime"));
        Assert.Equal("Película de Pa negre en 3Cat.", root.Element("plot")!.Value);
    }

    [Fact]
    public void BuildSeries_WithTheDefaultSourceKeepsTheOutputOfThePreviousVersion()
    {
        var series = new LibrarySeries { Title = "Merlí", ProgramId = "42" };

        // XElement indents with Environment.NewLine, so the comparison normalises it to keep the test
        // stable on Windows and on the Linux of the CI.
        var nfo = NfoBuilder.BuildSeries(series).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<tvshow>\n"
            + "  <title>Merlí</title>\n"
            + "  <sorttitle>Merlí</sorttitle>\n"
            + "  <plot>Programas de Merlí en 3Cat.</plot>\n"
            + "  <studio>3Cat</studio>\n"
            + "  <tag>3Cat</tag>\n"
            + "  <uniqueid type=\"threecat\" default=\"true\">42</uniqueid>\n"
            + "</tvshow>\n",
            nfo);
    }

    [Fact]
    public void BuildSeries_WithTheDefaultSourceWritesNoProviderIdEvenWhenItHasOne()
    {
        var series = new LibrarySeries
        {
            Title = "Bola de Drac",
            ProgramId = "73476",
            TmdbId = "62715",
            TvdbId = "295068"
        };

        var root = XDocument.Parse(NfoBuilder.BuildSeries(series)).Root!;

        Assert.Equal(
            ["threecat"],
            root.Elements("uniqueid").Select(element => element.Attribute("type")!.Value));
    }

    [Fact]
    public void BuildSeries_WritesTheTmdbIdWithTheTmdbSource()
    {
        var series = new LibrarySeries
        {
            Title = "Bola de Drac Súper",
            ProgramId = "73476",
            NumberingSource = MetadataSource.Tmdb,
            TmdbId = " 62715 ",
            TvdbId = "295068",
            ImdbId = "tt4644488"
        };

        var ids = XDocument.Parse(NfoBuilder.BuildSeries(series))
            .Root!
            .Elements("uniqueid")
            .ToList();

        Assert.Equal(
            ["threecat", "tmdb", "imdb"],
            ids.Select(element => element.Attribute("type")!.Value));
        Assert.Equal("true", ids[0].Attribute("default")!.Value);
        Assert.Equal("62715", ids[1].Value);
        Assert.Null(ids[1].Attribute("default"));
        Assert.Equal("tt4644488", ids[2].Value);
    }

    [Fact]
    public void BuildSeries_WritesTheTvdbIdWithTheTvdbSource()
    {
        var series = new LibrarySeries
        {
            Title = "Bola de Drac Súper",
            ProgramId = "73476",
            NumberingSource = MetadataSource.Tvdb,
            TmdbId = "62715",
            TvdbId = " 295068 "
        };

        var ids = XDocument.Parse(NfoBuilder.BuildSeries(series))
            .Root!
            .Elements("uniqueid")
            .ToList();

        Assert.Equal(["threecat", "tvdb"], ids.Select(element => element.Attribute("type")!.Value));
        Assert.Equal("295068", ids[1].Value);
    }

    [Fact]
    public void BuildSeries_WritesNoProviderIdWhenTheSelectedSourceHasNoId()
    {
        var series = new LibrarySeries
        {
            Title = "Merlí",
            ProgramId = "42",
            NumberingSource = MetadataSource.Tvdb,
            TmdbId = "1234"
        };

        var root = XDocument.Parse(NfoBuilder.BuildSeries(series)).Root!;

        Assert.Equal(
            ["threecat"],
            root.Elements("uniqueid").Select(element => element.Attribute("type")!.Value));
    }
}
