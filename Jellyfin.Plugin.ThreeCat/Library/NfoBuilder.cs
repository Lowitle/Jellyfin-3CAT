using System.Globalization;
using System.Xml.Linq;

namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Builds the NFO documents read natively by Jellyfin (through <c>MediaBrowser.XbmcMetadata</c>).
/// </summary>
internal static class NfoBuilder
{
    /// <summary>
    /// Builds the <c>tvshow.nfo</c> document.
    /// </summary>
    /// <param name="series">Series metadata.</param>
    /// <returns>The NFO content.</returns>
    /// <remarks>
    /// The genres are what Jellyfin shows as filters in the library («Serie», «Documental»,
    /// «Infantil»...), and the tags keep the exact 3Cat program type. The IMDb id is only written
    /// when the user allows it: Jellyfin uses it to fetch online metadata, which may replace the
    /// title or the plot written here.
    /// </remarks>
    internal static string BuildSeries(LibrarySeries series)
    {
        ArgumentNullException.ThrowIfNull(series);

        var root = new XElement(
            "tvshow",
            new XElement("title", series.Title),
            new XElement("sorttitle", series.Title),
            new XElement(
                "plot",
                string.IsNullOrWhiteSpace(series.Plot) ? $"Programas de {series.Title} en 3Cat." : series.Plot));

        if (!string.IsNullOrWhiteSpace(series.Country))
        {
            root.Add(new XElement("country", series.Country.Trim()));
        }

        root.Add(new XElement("studio", "3Cat"));

        foreach (var genre in Distinct(series.Genres))
        {
            root.Add(new XElement("genre", genre));
        }

        foreach (var tag in Distinct(series.Tags))
        {
            root.Add(new XElement("tag", tag));
        }

        root.Add(new XElement("tag", "3Cat"));
        root.Add(new XElement("uniqueid", new XAttribute("type", "threecat"), new XAttribute("default", "true"), series.ProgramId));

        if (!string.IsNullOrWhiteSpace(series.ImdbId))
        {
            root.Add(new XElement("uniqueid", new XAttribute("type", "imdb"), series.ImdbId.Trim()));
        }

        return Serialize(root);
    }

    /// <summary>
    /// Builds the NFO document of an episode.
    /// </summary>
    /// <param name="seriesTitle">Series title.</param>
    /// <param name="episode">Episode.</param>
    /// <returns>The NFO content.</returns>
    internal static string BuildEpisode(string seriesTitle, LibraryEpisode episode)
    {
        var root = new XElement(
            "episodedetails",
            new XElement("title", episode.Title),
            new XElement("showtitle", seriesTitle),
            new XElement("season", episode.Season),
            new XElement("episode", episode.Episode));

        if (!string.IsNullOrWhiteSpace(episode.Plot))
        {
            root.Add(new XElement("plot", episode.Plot));
        }

        if (episode.Runtime is { } runtime && runtime.TotalMinutes >= 1)
        {
            root.Add(new XElement(
                "runtime",
                ((int)Math.Round(runtime.TotalMinutes, MidpointRounding.AwayFromZero))
                    .ToString(CultureInfo.InvariantCulture)));
        }

        if (episode.Aired is { } aired)
        {
            root.Add(new XElement("aired", aired.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(episode.VideoId))
        {
            root.Add(new XElement("uniqueid", new XAttribute("type", "threecat"), episode.VideoId));
        }

        if (!string.IsNullOrWhiteSpace(episode.ThumbnailUrl))
        {
            root.Add(new XElement("thumb", episode.ThumbnailUrl));
        }

        foreach (var genre in episode.Genres.Where(g => !string.IsNullOrWhiteSpace(g)))
        {
            root.Add(new XElement("genre", genre));
        }

        if (!string.IsNullOrWhiteSpace(episode.Director))
        {
            root.Add(new XElement("director", episode.Director));
        }

        return Serialize(root);
    }

    private static string Serialize(XElement root)
        => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + root + "\n";

    /// <summary>
    /// Trims the values and drops the empty ones and the duplicates, so a genre never lands twice
    /// in the NFO because 3Cat published it twice.
    /// </summary>
    /// <param name="values">Values to clean.</param>
    /// <returns>The distinct values, in the original order.</returns>
    private static IEnumerable<string> Distinct(IEnumerable<string>? values)
    {
        if (values is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            var text = value?.Trim();
            if (!string.IsNullOrEmpty(text) && seen.Add(text))
            {
                yield return text;
            }
        }
    }
}
