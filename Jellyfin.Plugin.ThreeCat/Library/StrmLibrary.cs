using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Writes a series folder (.strm + .nfo + .srt) that Jellyfin can import as a TV show.
/// </summary>
public static class StrmLibrary
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Extensions of the files this class writes, plus the artwork the synchronization saves next to them.
    /// </summary>
    private static readonly HashSet<string> ManagedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".strm", ".nfo", ".srt", ".jpg", ".jpeg", ".png", ".webp"
    };

    /// <summary>
    /// Gets the folder that holds a series.
    /// </summary>
    /// <param name="rootPath">Folder the series are written to.</param>
    /// <param name="seriesTitle">Series title.</param>
    /// <returns>The series folder.</returns>
    public static string SeriesFolder(string rootPath, string seriesTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesTitle);

        return Path.Combine(rootPath, LibraryNaming.SeriesFolderName(seriesTitle));
    }

    /// <summary>
    /// Writes the episodes of a series into <paramref name="rootPath"/>.
    /// </summary>
    /// <param name="rootPath">Library root folder.</param>
    /// <param name="series">Series metadata (title, plot, genres, tags...).</param>
    /// <param name="episodes">Episodes to write.</param>
    /// <param name="options">Write options.</param>
    /// <returns>The result of the operation.</returns>
    public static LibraryWriteResult Write(
        string rootPath,
        LibrarySeries series,
        IEnumerable<LibraryEpisode> episodes,
        LibraryWriteOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentException.ThrowIfNullOrWhiteSpace(series.Title);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(options);

        var seriesFolder = SeriesFolder(rootPath, series.Title);
        Directory.CreateDirectory(seriesFolder);

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesWritten = 0;
        var count = 0;

        if (options.WriteNfo)
        {
            filesWritten += WriteText(
                Path.Combine(seriesFolder, "tvshow.nfo"),
                NfoBuilder.BuildSeries(series),
                written) ? 1 : 0;
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var episode in episodes)
        {
            var seasonFolder = Path.Combine(
                seriesFolder,
                string.Format(CultureInfo.InvariantCulture, "Season {0}", episode.Season));
            Directory.CreateDirectory(seasonFolder);

            var baseName = Unique(
                LibraryNaming.EpisodeFileName(series.Title, episode.Season, episode.Episode, episode.Title),
                usedNames);

            var strmPath = Path.Combine(seasonFolder, baseName + ".strm");
            filesWritten += WriteText(strmPath, episode.Url + "\n", written) ? 1 : 0;

            if (options.WriteNfo)
            {
                filesWritten += WriteText(
                    Path.Combine(seasonFolder, baseName + ".nfo"),
                    NfoBuilder.BuildEpisode(series.Title, episode),
                    written) ? 1 : 0;
            }

            if (options.WriteSubtitles)
            {
                foreach (var subtitle in DistinctLanguages(episode.Subtitles))
                {
                    var srtPath = Path.Combine(seasonFolder, $"{baseName}.{subtitle.Language}.srt");
                    filesWritten += WriteText(srtPath, ToCrlf(subtitle.Srt), written) ? 1 : 0;
                }
            }

            count++;
        }

        var removed = options.DeleteObsolete ? DeleteObsolete(seriesFolder, written) : 0;

        return new LibraryWriteResult(seriesFolder, count, filesWritten, removed);
    }

    /// <summary>
    /// Keeps the first subtitle of every language so two 3Cat tracks of the same language never
    /// write the same file twice.
    /// </summary>
    /// <param name="subtitles">Subtitles of the episode.</param>
    /// <returns>The subtitles to write, one per language.</returns>
    private static IEnumerable<LibrarySubtitle> DistinctLanguages(IReadOnlyList<LibrarySubtitle>? subtitles)
    {
        if (subtitles is null)
        {
            yield break;
        }

        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subtitle in subtitles)
        {
            if (string.IsNullOrWhiteSpace(subtitle.Srt))
            {
                continue;
            }

            var language = string.IsNullOrWhiteSpace(subtitle.Language) ? "ca" : subtitle.Language.Trim().ToLowerInvariant();

            // The language ends up in a file name: keep only ASCII letters and digits.
            language = new string(language.Where(char.IsAsciiLetterOrDigit).ToArray());
            if (language.Length == 0)
            {
                // 3Cat publishes practically everything in Catalan, so it is the least wrong guess.
                language = "ca";
            }

            if (languages.Add(language))
            {
                yield return subtitle with { Language = language };
            }
        }
    }

    /// <summary>
    /// Deletes a series folder written by this plugin, so a program that moved to another library leaves
    /// nothing behind.
    /// </summary>
    /// <param name="folder">Series folder to delete.</param>
    /// <param name="programId">3Cat program id the folder has to belong to.</param>
    /// <returns><c>true</c> when the folder was deleted.</returns>
    /// <remarks>
    /// It is deliberately conservative: the folder has to hold a <c>tvshow.nfo</c> whose
    /// <c>uniqueid type="threecat"</c> is the program, and nothing but files of the plugin (episodes,
    /// metadata, subtitles and artwork). A folder that holds anything else is kept, and the caller only
    /// logs it: the folders of the library can be shared with the user.
    /// </remarks>
    public static bool TryDeleteSeriesFolder(string? folder, string? programId)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(programId))
        {
            return false;
        }

        string path;
        string nfo;

        try
        {
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim()));
            nfo = Path.Combine(path, "tvshow.nfo");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!Directory.Exists(path) || !File.Exists(nfo))
        {
            return false;
        }

        try
        {
            if (!BelongsTo(nfo, programId.Trim()))
            {
                return false;
            }

            if (Directory
                .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Any(file => !ManagedExtensions.Contains(Path.GetExtension(file))))
            {
                return false;
            }

            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Tells whether a <c>tvshow.nfo</c> names a 3Cat program in its <c>threecat</c> unique id.
    /// </summary>
    /// <param name="nfo">Path of the <c>tvshow.nfo</c>.</param>
    /// <param name="programId">Program id.</param>
    /// <returns>Whether the file belongs to the program.</returns>
    private static bool BelongsTo(string nfo, string programId)
    {
        var id = XDocument
            .Load(nfo)
            .Descendants("uniqueid")
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute("type"),
                "threecat",
                StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();

        return string.Equals(id, programId, StringComparison.Ordinal);
    }

    private static string Unique(string baseName, HashSet<string> used)
    {
        if (used.Add(baseName))
        {
            return baseName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = string.Format(CultureInfo.InvariantCulture, "{0} [{1}]", baseName, suffix);
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool WriteText(string path, string content, HashSet<string> written)
    {
        written.Add(path);

        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, Utf8NoBom);
        return true;
    }

    private static string ToCrlf(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);

    private static int DeleteObsolete(string seriesFolder, HashSet<string> written)
    {
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(seriesFolder, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (extension is not (".strm" or ".nfo" or ".srt"))
            {
                continue;
            }

            if (string.Equals(Path.GetFileName(file), "tvshow.nfo", StringComparison.OrdinalIgnoreCase)
                || written.Contains(file))
            {
                continue;
            }

            File.Delete(file);
            removed++;
        }

        foreach (var directory in Directory.EnumerateDirectories(seriesFolder))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        return removed;
    }
}
