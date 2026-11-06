using System.Globalization;

namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Writes a series folder (.strm + .nfo + .srt) that Jellyfin can import as a TV show.
/// </summary>
public static class StrmLibrary
{
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
            filesWritten += LibraryIO.WriteText(
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
                LibraryNaming.EpisodeFileName(series.Title, episode.Season, episode.Episode, episode.Title, episode.Part),
                usedNames);

            var strmPath = Path.Combine(seasonFolder, baseName + ".strm");
            filesWritten += LibraryIO.WriteText(strmPath, episode.Url + "\n", written) ? 1 : 0;

            if (options.WriteNfo)
            {
                filesWritten += LibraryIO.WriteText(
                    Path.Combine(seasonFolder, baseName + ".nfo"),
                    NfoBuilder.BuildEpisode(series.Title, episode),
                    written) ? 1 : 0;
            }

            if (options.WriteSubtitles)
            {
                foreach (var subtitle in LibraryNaming.CleanSubtitles(episode.Subtitles))
                {
                    var srtPath = Path.Combine(seasonFolder, $"{baseName}.{subtitle.Language}.srt");
                    filesWritten += LibraryIO.WriteText(srtPath, LibraryIO.ToCrlf(subtitle.Srt), written) ? 1 : 0;
                }
            }

            count++;
        }

        var removed = options.DeleteObsolete ? DeleteObsolete(seriesFolder, written) : 0;

        return new LibraryWriteResult(seriesFolder, count, filesWritten, removed);
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
        var path = LibraryIO.Normalize(folder);
        if (path is null || string.IsNullOrWhiteSpace(programId) || !Directory.Exists(path))
        {
            return false;
        }

        if (!LibraryIO.BelongsTo(Path.Combine(path, "tvshow.nfo"), programId))
        {
            return false;
        }

        try
        {
            if (!LibraryIO.HoldsOnlyManagedFiles(path))
            {
                return false;
            }

            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
