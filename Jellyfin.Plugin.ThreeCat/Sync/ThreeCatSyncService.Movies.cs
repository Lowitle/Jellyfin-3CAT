using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Film level synchronization: the programs 3Cat files under «Cinema» are written as movies instead of as
/// a one-episode series.
/// </summary>
/// <remarks>
/// A program of cinema is not numbered —there is no season, no episode and no provider to match—, so this
/// branch never resolves a numbering. Each title the program publishes is a film of its own, and the
/// videos that share a title are the parts 3Cat splits a film in, which Jellyfin reads as versions of the
/// same film.
/// </remarks>
public sealed partial class ThreeCatSyncService
{
    /// <summary>
    /// Groups the videos of a program of cinema by film.
    /// </summary>
    /// <param name="episodes">Episodes built from the catalog, in air order.</param>
    /// <returns>One entry per film, with the episodes that belong to it.</returns>
    /// <remarks>
    /// The key is the title without the part 3Cat appends to it —«(1a part)», «(part 2)», «(2/2)»—, which is
    /// what turns «Ermessenda (1a part)» and «Ermessenda (2a part)» into one film with two files, and
    /// «Cinema de nit: X» and «Cinema de nit: Y» into two films.
    /// </remarks>
    internal static IReadOnlyList<(string Title, IReadOnlyList<LibraryEpisode> Episodes)> GroupFilms(
        IEnumerable<LibraryEpisode> episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);

        var groups = new List<(string Title, List<LibraryEpisode> Episodes, DateTimeOffset? Aired)>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var episode in episodes)
        {
            var title = EpisodeNumbering.StripPart(episode.Title).Trim();

            if (title.Length == 0)
            {
                title = episode.Title.Trim();
            }

            if (index.TryGetValue(title, out var position))
            {
                groups[position].Episodes.Add(episode);
                continue;
            }

            index[title] = groups.Count;
            groups.Add((title, [episode], episode.Aired));
        }

        return groups
            .OrderBy(group => group.Aired ?? DateTimeOffset.MinValue)
            .Select(group => (group.Title, (IReadOnlyList<LibraryEpisode>)group.Episodes))
            .ToList();
    }

    private async Task<int> SyncMoviesAsync(
        PluginConfiguration configuration,
        ThreeCatProgramReference reference,
        string programId,
        string seriesTitle,
        ProgramClassification classification,
        List<ThreeCatVideo> videos,
        string root,
        string? mirrorRoot,
        Action<double> reportProgress,
        CancellationToken cancellationToken)
    {
        var episodes = new List<LibraryEpisode>();
        var expired = 0;
        var skipped = 0;
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < videos.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var video = videos[index];

            if (configuration.PruneExpired && video.Expires is { } expires && expires <= now)
            {
                expired++;
                reportProgress((index + 1d) / videos.Count);
                continue;
            }

            // A film has no season and no episode: the numbers of the loop are only what makes the title of
            // a video without metadata readable («Episodio 3»).
            var episode = await BuildEpisodeAsync(
                configuration,
                seriesTitle,
                video,
                1,
                index + 1,
                0,
                cancellationToken).ConfigureAwait(false);

            if (episode is null)
            {
                skipped++;
            }
            else
            {
                episodes.Add(episode);
            }

            reportProgress((index + 1d) / videos.Count);
        }

        if (episodes.Count == 0)
        {
            _logger.LogWarning(
                "3Cat: no se pudo resolver ninguna película de «{Program}» ({Skipped} sin URL reproducible, "
                + "{Expired} caducadas)",
                seriesTitle,
                skipped,
                expired);

            return 0;
        }

        var options = new LibraryWriteOptions
        {
            WriteNfo = configuration.WriteNfo,
            WriteSubtitles = configuration.WriteSubtitles,
            DeleteObsolete = configuration.DeleteObsolete
        };

        var program = FindProgramNode(videos);
        var filesWritten = 0;
        var filesRemoved = 0;
        var folders = new List<string>();
        var mirrored = new List<string>();

        foreach (var (title, group) in GroupFilms(episodes))
        {
            var first = group[0];
            var runtime = group.Aggregate(TimeSpan.Zero, (total, episode) => total + (episode.Runtime ?? TimeSpan.Zero));

            var movie = new LibraryMovie
            {
                Title = title,
                ProgramId = programId,
                Year = first.Aired?.Year,
                Plot = first.Plot,
                Runtime = runtime > TimeSpan.Zero ? runtime : null,
                PremiereDate = first.Aired,
                Genres = classification.Genres,
                Country = "ES"
            };

            var parts = group
                .Select((episode, part) => new LibraryMovieFile
                {
                    Url = episode.Url,
                    Part = group.Count > 1 ? part + 1 : 0,
                    Subtitles = episode.Subtitles,
                    VideoId = episode.VideoId
                })
                .ToList();

            var result = MovieLibrary.Write(root, movie, parts, options);
            filesWritten += result.FilesWritten;
            filesRemoved += result.FilesRemoved;
            folders.Add(result.SeriesPath);

            if (program is not null)
            {
                await SaveArtworkAsync(program, result.SeriesPath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SaveImageAsync(first.ThumbnailUrl, result.SeriesPath, "fanart", cancellationToken)
                    .ConfigureAwait(false);
            }

            _libraryMonitor.ReportFileSystemChanged(result.SeriesPath);

            _logger.LogInformation(
                "3Cat: {Film} ({Year}) → {Parts} archivo(s) en {Folder}",
                title,
                first.Aired?.Year,
                parts.Count,
                result.SeriesPath);

            if (mirrorRoot is null)
            {
                continue;
            }

            var mirror = MovieLibrary.Write(mirrorRoot, movie, parts, options);
            filesWritten += mirror.FilesWritten;
            filesRemoved += mirror.FilesRemoved;
            mirrored.Add(mirror.SeriesPath);
            CopyArtworkFiles(result.SeriesPath, mirror.SeriesPath);
            _libraryMonitor.ReportFileSystemChanged(mirror.SeriesPath);
        }

        // A film that 3Cat has expired or renamed is a folder this run did not write: removing it is what
        // keeps the library in step with the catalog. Only the folders of this program are deleted.
        filesRemoved += MovieLibrary.DeleteUnwrittenMovies(root, programId, folders);

        if (mirrorRoot is not null)
        {
            filesRemoved += MovieLibrary.DeleteUnwrittenMovies(mirrorRoot, programId, mirrored);

            _logger.LogInformation(
                "3Cat: {Program} → copia de {Films} película(s) en {Mirror}",
                seriesTitle,
                folders.Count,
                mirrorRoot);
        }

        // The folder of the program is the category, not the folder of a film: it is what lets the next run
        // remove every film of the program when it changes of kind or of library.
        RemoveMovedSeries(reference, programId, root);
        RemoveMovedMirror(reference, programId, mirrorRoot);

        _logger.LogInformation(
            "3Cat: {Program} → {Films} película(s) ({Skipped} sin URL, {Expired} caducadas), "
            + "{Files} archivos escritos, {Removed} obsoletos eliminados",
            seriesTitle,
            folders.Count,
            skipped,
            expired,
            filesWritten,
            filesRemoved);

        return filesWritten;
    }
}
