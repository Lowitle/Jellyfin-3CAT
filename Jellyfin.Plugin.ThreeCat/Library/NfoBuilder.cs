using System.Globalization;
using System.Xml.Linq;
using Jellyfin.Plugin.ThreeCat.Configuration;

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
    /// title or the plot written here. The original title is only written when 3Cat publishes one
    /// that differs from the Catalan title, so a library with the local metadata provider still
    /// shows «Dragon Ball» as the original title of «Bola de Drac».
    /// <para>
    /// The <c>uniqueid</c> matrix follows the numbering of the episodes: the <c>threecat</c> id is always
    /// present and default; a <c>tmdb</c> or <c>tvdb</c> id is added only when
    /// <see cref="LibrarySeries.NumberingSource"/> is that provider (and the id exists); the <c>imdb</c>
    /// id is written last. With the default source the document is byte for byte the one 1.5.0.0 wrote.
    /// </para>
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

        // 3Cat publishes the original title in the card of the program («Dragon Ball» for «Bola de
        // Drac»): Jellyfin keeps it apart from the Catalan title, which is the one the series is
        // sorted and searched by.
        if (!string.IsNullOrWhiteSpace(series.OriginalTitle)
            && !string.Equals(series.OriginalTitle.Trim(), series.Title.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            root.Add(new XElement("originaltitle", series.OriginalTitle.Trim()));
        }

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

        // The 3Cat id is always the default one: it is the key the plugin uses to recognise the series
        // and to tell whether a folder is its own. The provider id is written only when the episodes were
        // numbered by that provider, so Jellyfin never reads online episodes of a numbering the files do
        // not follow; with the default source the document is the one 1.5.0.0 wrote.
        root.Add(new XElement("uniqueid", new XAttribute("type", "threecat"), new XAttribute("default", "true"), series.ProgramId));

        var providerId = series.NumberingSource switch
        {
            MetadataSource.Tmdb => series.TmdbId,
            MetadataSource.Tvdb => series.TvdbId,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(providerId))
        {
            var type = series.NumberingSource == MetadataSource.Tmdb ? "tmdb" : "tvdb";
            root.Add(new XElement("uniqueid", new XAttribute("type", type), providerId.Trim()));
        }

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
