using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Program level synchronization.
/// </summary>
public sealed partial class ThreeCatSyncService
{
    private async Task<int> SyncProgramAsync(
        PluginConfiguration configuration,
        ThreeCatProgramReference reference,
        Action<double> reportProgress,
        CancellationToken cancellationToken)
    {
        var programId = ThreeCatClient.NormalizeReference(reference.Id);
        if (programId.Length == 0)
        {
            _logger.LogWarning("3Cat: el programa «{Reference}» no contiene ningún id, se omite", reference.Id);
            reportProgress(1d);
            return 0;
        }

        var page = await _client
            .GetProgramVideosAsync(programId, configuration.MaxItemsPerProgram, cancellationToken)
            .ConfigureAwait(false);

        // The reference may be the id of any video of the program instead of the programatv_id: the
        // catalog then comes back empty and the media endpoint publishes informacio.programa_id.
        if (page.Items.Count == 0)
        {
            var media = await _client.GetMediaAsync(programId, cancellationToken).ConfigureAwait(false);
            var resolved = media?.Info?.ProgramId;

            if (!string.IsNullOrWhiteSpace(resolved)
                && !string.Equals(resolved, programId, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "3Cat: «{Reference}» es el vídeo {VideoId} del programa {ProgramId}",
                    reference.Id,
                    programId,
                    resolved);

                programId = resolved;
                page = await _client
                    .GetProgramVideosAsync(programId, configuration.MaxItemsPerProgram, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (page.Items.Count == 0)
        {
            _logger.LogWarning(
                "3Cat: el programa {ProgramId} no devuelve ningún vídeo. Comprueba el id en 3Cat; "
                + "también puedes pegar la URL de cualquiera de sus vídeos",
                programId);

            reportProgress(1d);
            return 0;
        }

        var usable = page.Items.Where(video => !string.IsNullOrWhiteSpace(video.Id)).ToList();
        var seriesTitle = ResolveSeriesTitle(usable, reference, programId);
        var root = ResolveRoot(configuration, reference);
        var seriesPath = StrmLibrary.SeriesFolder(root, seriesTitle);

        var seasonNumbered = usable.All(video => video.Season is > 0);
        var episodeNumbered = usable.All(video => video.Chapter is > 0);

        // 3Cat publishes the catalog newest first; the episodes are ordered by broadcast date so the
        // fallback numbering (programs without «capitol», typically the daily magazines) follows the
        // order the program was aired in.
        var ordered = usable
            .OrderBy(video => video.Broadcast ?? video.Published ?? DateTimeOffset.MinValue)
            .ThenBy(video => video.Id, StringComparer.Ordinal)
            .ToList();

        var episodes = new List<LibraryEpisode>();
        var expired = new List<(int Season, int Episode, string Title)>();
        var skipped = 0;
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < ordered.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var video = ordered[index];
            var season = seasonNumbered ? video.Season!.Value : 1;
            var number = episodeNumbered ? video.Chapter!.Value : index + 1;

            if (configuration.PruneExpired && video.Expires is { } expires && expires <= now)
            {
                // The MP4 answers 404 from this date on, so its .strm would be a broken episode.
                expired.Add((season, number, FirstNonEmpty(video.Title, video.FriendlyName, $"{number}")));
                reportProgress((index + 1d) / ordered.Count);
                continue;
            }

            var episode = await BuildEpisodeAsync(
                configuration,
                seriesTitle,
                video,
                season,
                number,
                cancellationToken).ConfigureAwait(false);

            if (episode is null)
            {
                skipped++;
            }
            else
            {
                episodes.Add(episode);
            }

            reportProgress((index + 1d) / ordered.Count);
        }

        if (expired.Count > 0)
        {
            _logger.LogInformation(
                "3Cat: {Expired} de {Total} vídeos de «{Series}» han caducado en 3Cat y se omiten",
                expired.Count,
                ordered.Count,
                seriesTitle);
        }

        if (episodes.Count == 0)
        {
            var pruned = PruneExpiredFiles(seriesPath, seriesTitle, expired);
            if (pruned > 0)
            {
                _libraryMonitor.ReportFileSystemChanged(seriesPath);
            }

            _logger.LogWarning(
                "3Cat: no se pudo resolver ningún vídeo de «{Series}» ({Skipped} sin URL reproducible, {Expired} caducados)",
                seriesTitle,
                skipped,
                expired.Count);

            return 0;
        }

        // The kind of program goes first in the genres of the series: Jellyfin filters the library by
        // them, so «Documental» or «Animació» is a filter the user gets for free.
        var (classification, originalTitle) = await ClassifyProgramAsync(
            configuration,
            programId,
            usable,
            cancellationToken).ConfigureAwait(false);

        var result = StrmLibrary.Write(
            root,
            BuildSeriesMetadata(programId, seriesTitle, classification, originalTitle),
            episodes,
            new LibraryWriteOptions
            {
                WriteNfo = configuration.WriteNfo,
                WriteSubtitles = configuration.WriteSubtitles,
                DeleteObsolete = configuration.DeleteObsolete
            });

        var program = FindProgramNode(usable);
        if (program is not null)
        {
            await SaveArtworkAsync(program, result.SeriesPath, cancellationToken).ConfigureAwait(false);
        }

        var removedExpired = PruneExpiredFiles(result.SeriesPath, seriesTitle, expired);

        RemoveMovedSeries(reference, programId, result.SeriesPath);

        _libraryMonitor.ReportFileSystemChanged(result.SeriesPath);

        _logger.LogInformation(
            "3Cat: {Series} → {Episodes} episodios ({Skipped} sin URL, {Expired} caducados), "
            + "{Files} archivos escritos, {Removed} obsoletos eliminados",
            seriesTitle,
            result.EpisodesWritten,
            skipped,
            expired.Count,
            result.FilesWritten,
            result.FilesRemoved + removedExpired);

        return result.FilesWritten;
    }

    /// <summary>
    /// Resolves the folder a program is written to: its own folder when the user assigned the program to
    /// another library, the destination folder of the plugin otherwise.
    /// </summary>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="reference">Program reference.</param>
    /// <returns>The folder the series folder of the program is written to.</returns>
    /// <remarks>
    /// A folder that is not mounted, is not writable or was moved falls back to the destination folder of
    /// the plugin: one bad assignment must not leave a program without its episodes, and the warning says
    /// which program and which folder were ignored.
    /// </remarks>
    private string ResolveRoot(PluginConfiguration configuration, ThreeCatProgramReference reference)
    {
        var fallback = configuration.DestinationPath.Trim();
        var configured = reference.DestinationPath?.Trim();

        if (string.IsNullOrEmpty(configured))
        {
            return fallback;
        }

        var check = LibraryPath.Check(configured);
        if (check.IsValid && check.Exists && check.Writable)
        {
            return check.Path;
        }

        _logger.LogWarning(
            "3Cat: la carpeta «{Path}» asignada al programa {ProgramId} no se puede usar ({Reason}); "
            + "se escribe en la carpeta de destino {Root}",
            configured,
            reference.Id,
            check.Error ?? "no existe o no admite escritura",
            fallback);

        return fallback;
    }

    /// <summary>
    /// Removes the series folder a previous run wrote when the program has moved to another library, and
    /// records the folder of this run.
    /// </summary>
    /// <param name="reference">Program reference, whose <c>WrittenPath</c> is updated.</param>
    /// <param name="programId">3Cat program id, used to make sure the old folder is the one of this program.</param>
    /// <param name="seriesPath">Folder written by this run.</param>
    /// <remarks>
    /// The old folder is only removed when it holds nothing but files of this plugin and its
    /// <c>tvshow.nfo</c> names this program, so a folder the user filled with something else is kept. The
    /// new path is always recorded, so a removal that fails is retried on the next run.
    /// </remarks>
    private void RemoveMovedSeries(ThreeCatProgramReference reference, string programId, string seriesPath)
    {
        var previous = reference.WrittenPath?.Trim();
        reference.WrittenPath = seriesPath;

        if (string.IsNullOrEmpty(previous) || LibraryPath.SamePath(previous, seriesPath))
        {
            return;
        }

        if (!StrmLibrary.TryDeleteSeriesFolder(previous, programId))
        {
            _logger.LogInformation(
                "3Cat: «{Series}» se escribió antes en {Previous}; no se elimina porque no es una carpeta "
                + "del plugin para este programa",
                Path.GetFileName(seriesPath),
                previous);

            return;
        }

        var parent = Path.GetDirectoryName(previous);
        if (!string.IsNullOrEmpty(parent))
        {
            _libraryMonitor.ReportFileSystemChanged(parent);
        }

        _logger.LogInformation(
            "3Cat: «{Series}» cambió de biblioteca; se eliminó la carpeta anterior {Previous}",
            Path.GetFileName(seriesPath),
            previous);
    }

    /// <summary>
    /// Resolves the series title. The catalog publishes the program name in <c>bbands.desc</c> and in
    /// <c>programes_tv.desc</c>; the configured value is only a cache and the last resort.
    /// </summary>
    private static string ResolveSeriesTitle(
        IReadOnlyList<ThreeCatVideo> videos,
        ThreeCatProgramReference reference,
        string programId)
        => FirstNonEmpty(
            videos.Select(video => video.Bands?.FirstOrDefault()?.Description).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            videos.Select(video => video.Programs?.FirstOrDefault()?.Description).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            videos.Select(video => video.Programs?.FirstOrDefault()?.Title).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            reference.Title,
            programId);

    /// <summary>
    /// Builds the series metadata. The kind of program 3Cat files the program under («Sèrie»,
    /// «Documental», «Animació»...) is the first genre, followed by the audience of its videos
    /// («Infantil») and the 3Cat themes («Ficció», «Esports»...): Jellyfin shows the genres as filters in
    /// the library.
    /// </summary>
    /// <param name="programId">3Cat program id.</param>
    /// <param name="seriesTitle">Series title.</param>
    /// <param name="classification">Kind of program and genres of the series.</param>
    /// <param name="originalTitle">
    /// Original title published in the page of the program, or <c>null</c> when the page was not read
    /// or publishes none.
    /// </param>
    /// <returns>The metadata written to the <c>tvshow.nfo</c>.</returns>
    private static LibrarySeries BuildSeriesMetadata(
        string programId,
        string seriesTitle,
        ProgramClassification classification,
        string? originalTitle)
        => new()
        {
            Title = seriesTitle,
            OriginalTitle = originalTitle,
            ProgramId = programId,
            Country = "ES",
            Genres = classification.Genres
        };

    /// <summary>
    /// Classifies the program with the formats and themes 3Cat files it under, tells the anime apart from
    /// the rest of the animation with the page of the program, and reads the audience of its videos.
    /// </summary>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="programId">3Cat program id.</param>
    /// <param name="videos">Videos of the program.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The kind of program and the genres to write to the <c>tvshow.nfo</c>, and the original title
    /// published in the page of the program when the page was read.
    /// </returns>
    /// <remarks>
    /// 3Cat publishes no endpoint that names the classification of a program: the formats and the themes
    /// are the sweep of the facets of the search endpoint, which the client caches for half an hour. The
    /// page of the program is read only when the animation has to be told apart, because it is the only
    /// place where 3Cat publishes the country of origin and the anime collections. A program that appears
    /// in no known facet, or a request that fails, keeps the themes of its videos as genres, so the series
    /// is never written without genres.
    /// </remarks>
    private async Task<(ProgramClassification Classification, string? OriginalTitle)> ClassifyProgramAsync(
        PluginConfiguration configuration,
        string programId,
        IReadOnlyList<ThreeCatVideo> videos,
        CancellationToken cancellationToken)
    {
        var themes = SeriesGenres(videos);

        if (!configuration.ClassifyPrograms)
        {
            return (new ProgramClassification(null, themes), null);
        }

        try
        {
            var taxonomy = await _client.GetProgramTaxonomyAsync(cancellationToken).ConfigureAwait(false);
            taxonomy.TryGetValue(programId, out var facets);

            var details = configuration.SeparateAnime
                && ProgramClassifier.IsAnimation(facets?.Formats, facets?.Themes)
                    ? await _client.GetProgramDetailsAsync(programId, cancellationToken).ConfigureAwait(false)
                    : null;

            var anime = details is not null && ProgramClassifier.IsAnime(details.Groups, details.Countries);

            // The audience comes with the catalog of the program, so it costs no request: the target of
            // its videos («Infantil») is what 3Cat builds the SX3 rails with, and the channel of the SX3
            // family is the fallback for a video that publishes no target.
            var children = configuration.SeparateChildren
                && ProgramClassifier.IsChildren(
                    videos.SelectMany(video => video.Targets?.Targets ?? []).Select(target => target.Id),
                    videos.SelectMany(video => video.Channels?.Channels ?? []).Select(channel => channel.Id));

            // The page of the program publishes its formats too, and it is the only source for a program
            // the sweep of the search endpoint did not reach.
            return (
                ProgramClassifier.Classify(
                    facets?.Formats ?? details?.Formats,
                    facets?.Themes,
                    themes,
                    anime,
                    children),
                details?.OriginalTitle);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "3Cat: no se pudo leer la clasificación del programa {ProgramId}; se escriben solo sus temas como géneros",
                programId);

            return (new ProgramClassification(null, themes), null);
        }
    }

    /// <summary>
    /// Reads the genres 3Cat publishes per video («Ficció», «Infantil», «Esports»...), which is the
    /// classification the plugin had before the facets of the catalog were read.
    /// </summary>
    /// <param name="videos">Videos of the program.</param>
    /// <returns>The distinct themes, the main ones when the catalog flags any.</returns>
    private static List<string> SeriesGenres(IReadOnlyList<ThreeCatVideo> videos)
    {
        var themes = videos
            .SelectMany(video => video.Themes ?? [])
            .Where(theme => !string.IsNullOrWhiteSpace(theme.Description))
            .ToList();

        var genres = themes
            .Where(theme => theme.Main)
            .Select(theme => theme.Description!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (genres.Count == 0)
        {
            genres = themes
                .Select(theme => theme.Description!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return genres;
    }

    /// <summary>
    /// Finds the <c>programes_tv</c> node, which is where the poster images are published.
    /// </summary>
    private static ThreeCatProgram? FindProgramNode(IReadOnlyList<ThreeCatVideo> videos)
        => videos
            .SelectMany(video => video.Programs ?? [])
            .FirstOrDefault(program => program.Images is { Count: > 0 });
}

