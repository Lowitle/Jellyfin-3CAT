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

        // The series is always written in the destination folder of the plugin, which is the library the
        // plugin owns and the one Jellyfin reimports after a migration. The folder a program can be
        // assigned to is a second copy, so a library the user curates («Anime», «Infantil»...) keeps
        // seeing the program without taking over its files.
        var root = configuration.DestinationPath.Trim();
        var seriesPath = StrmLibrary.SeriesFolder(root, seriesTitle);
        var mirrorRoot = ResolveMirrorRoot(configuration, reference);
        var mirrorPath = mirrorRoot is null ? null : StrmLibrary.SeriesFolder(mirrorRoot, seriesTitle);

        // 1.5.0.0 renames every episode file, so a folder written by an older build holds the same
        // episodes under the old names. It is deleted before writing it again — only when it is a
        // folder of this plugin for this program — so the library does not show both schemes. The
        // mirror is migrated too: the episode names must match on both sides.
        if (configuration.NamingVersion < PluginConfiguration.CurrentNamingVersion)
        {
            DeleteForMigration(seriesPath, programId, seriesTitle);
            if (mirrorPath is not null)
            {
                DeleteForMigration(mirrorPath, programId, seriesTitle);
            }
        }

        // 3Cat publishes the numbering in the title («T22xC6 - …»), the one its website shows. The
        // catalog fields «capitol» (a counter that runs over the whole program) and
        // «capitol_temporada» (an index that restarts in every published batch) do not match it,
        // so they are not used: they were the source of «S06E907» for a chapter of the season 22.
        var ordered = usable
            .OrderBy(video => video.Broadcast ?? video.Published ?? DateTimeOffset.MinValue)
            .ThenBy(video => video.Id, StringComparer.Ordinal)
            .ToList();

        // The air order is what the specials and the programs without numbering are numbered with,
        // so the oldest video of a season keeps its number when 3Cat repeats one.
        var slots = EpisodeNumbering.Assign(
            ordered
                .Select(video => FirstNonEmptyOrNull(video.Title, video.FriendlyName))
                .ToList());

        var episodes = new List<LibraryEpisode>();
        var expired = new List<(int Season, int Episode, string Title)>();
        var skipped = 0;
        var duplicates = 0;
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < ordered.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var video = ordered[index];
            var slot = slots[index];

            if (slot.IsDuplicate)
            {
                // The same video published twice: writing it again would leave two files of the same
                // episode in the season folder.
                duplicates++;
                reportProgress((index + 1d) / ordered.Count);
                continue;
            }

            var season = slot.Season;
            var number = slot.Episode;

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
            var pruned = PruneExpiredFiles(seriesPath, seriesTitle, expired)
                + PruneExpiredFiles(mirrorPath, seriesTitle, expired);

            if (pruned > 0)
            {
                _libraryMonitor.ReportFileSystemChanged(seriesPath);
                if (mirrorPath is not null)
                {
                    _libraryMonitor.ReportFileSystemChanged(mirrorPath);
                }
            }

            _logger.LogWarning(
                "3Cat: no se pudo resolver ningún vídeo de «{Series}» ({Skipped} sin URL reproducible, "
                + "{Expired} caducados, {Duplicates} duplicados)",
                seriesTitle,
                skipped,
                expired.Count,
                duplicates);

            return 0;
        }

        // The kind of program goes first in the genres of the series: Jellyfin filters the library by
        // them, so «Documental» or «Animació» is a filter the user gets for free.
        var (classification, originalTitle) = await ClassifyProgramAsync(
            configuration,
            programId,
            usable,
            cancellationToken).ConfigureAwait(false);

        var options = new LibraryWriteOptions
        {
            WriteNfo = configuration.WriteNfo,
            WriteSubtitles = configuration.WriteSubtitles,
            DeleteObsolete = configuration.DeleteObsolete
        };

        var series = BuildSeriesMetadata(programId, seriesTitle, classification, originalTitle);
        var result = StrmLibrary.Write(root, series, episodes, options);
        var program = FindProgramNode(usable);

        if (program is not null)
        {
            await SaveArtworkAsync(program, result.SeriesPath, cancellationToken).ConfigureAwait(false);
        }

        var removedExpired = PruneExpiredFiles(result.SeriesPath, seriesTitle, expired);
        var mirror = result with { FilesWritten = 0, FilesRemoved = 0 };

        // The mirror is a second, independent write: Jellyfin indexes each library folder on its own, so
        // the copy shares nothing with the first one — not even the «watched» state — and removing it
        // never touches the library of the plugin.
        if (mirrorRoot is not null)
        {
            mirror = StrmLibrary.Write(mirrorRoot, series, episodes, options);
            CopyArtwork(result.SeriesPath, mirror.SeriesPath);
            removedExpired += PruneExpiredFiles(mirror.SeriesPath, seriesTitle, expired);
            _libraryMonitor.ReportFileSystemChanged(mirror.SeriesPath);
        }

        RemoveMovedSeries(reference, programId, result.SeriesPath);
        RemoveMovedMirror(reference, programId, mirrorRoot is null ? null : mirror.SeriesPath);

        _libraryMonitor.ReportFileSystemChanged(result.SeriesPath);

        if (mirrorRoot is not null)
        {
            _logger.LogInformation(
                "3Cat: {Series} → copia en {Mirror} ({Files} archivos escritos, {Removed} obsoletos eliminados)",
                seriesTitle,
                mirrorRoot,
                mirror.FilesWritten,
                mirror.FilesRemoved);
        }

        _logger.LogInformation(
            "3Cat: {Series} → {Episodes} episodios ({Skipped} sin URL, {Expired} caducados, "
            + "{Duplicates} duplicados), {Files} archivos escritos, {Removed} obsoletos eliminados",
            seriesTitle,
            result.EpisodesWritten,
            skipped,
            expired.Count,
            duplicates,
            result.FilesWritten,
            result.FilesRemoved + removedExpired);

        return result.FilesWritten + mirror.FilesWritten;
    }

    /// <summary>
    /// Resolves the folder of the second library a program is mirrored to: the folder the user assigned,
    /// or <c>null</c> when the program is not mirrored.
    /// </summary>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="reference">Program reference.</param>
    /// <returns>The library root of the mirror, or <c>null</c> when there is none.</returns>
    /// <remarks>
    /// A folder that is not mounted, is not writable or was moved is ignored: one bad assignment must not
    /// leave a program without its episodes, and the warning says which program and which folder were
    /// skipped. A folder that is the destination folder of the plugin is not a mirror either, or every
    /// episode would be written twice in the same place.
    /// </remarks>
    private string? ResolveMirrorRoot(PluginConfiguration configuration, ThreeCatProgramReference reference)
    {
        var configured = reference.DestinationPath?.Trim();

        if (string.IsNullOrEmpty(configured) || LibraryPath.SamePath(configured, configuration.DestinationPath.Trim()))
        {
            return null;
        }

        var check = LibraryPath.Check(configured);
        if (check.IsValid && check.Exists && check.Writable)
        {
            return check.Path;
        }

        _logger.LogWarning(
            "3Cat: la carpeta «{Path}» asignada al programa {ProgramId} no se puede usar ({Reason}); "
            + "el programa se escribe solo en la carpeta de destino {Root}",
            configured,
            reference.Id,
            check.Error ?? "no existe o no admite escritura",
            configuration.DestinationPath.Trim());

        return null;
    }

    /// <summary>
    /// Deletes the series folder an older build wrote, before it is written again with the numbering of
    /// the current version.
    /// </summary>
    /// <param name="seriesPath">Series folder, or <c>null</c> when there is none.</param>
    /// <param name="programId">3Cat program id, used to make sure the folder is the one of this program.</param>
    /// <param name="seriesTitle">Series title, for the log.</param>
    private void DeleteForMigration(string? seriesPath, string programId, string seriesTitle)
    {
        if (string.IsNullOrEmpty(seriesPath) || !StrmLibrary.TryDeleteSeriesFolder(seriesPath, programId))
        {
            return;
        }

        var parent = Path.GetDirectoryName(seriesPath);
        if (!string.IsNullOrEmpty(parent))
        {
            _libraryMonitor.ReportFileSystemChanged(parent);
        }

        _logger.LogInformation(
            "3Cat: se eliminó la carpeta de «{Series}» ({Path}) para reescribirla con la numeración nueva",
            seriesTitle,
            seriesPath);
    }

    /// <summary>
    /// Removes the series folder a previous run wrote when the series has moved to another folder, and
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
        RemoveMovedFolder(previous, seriesPath, programId, "biblioteca del plugin");
    }

    /// <summary>
    /// Removes the mirrored series folder a previous run wrote — when the series has moved inside the
    /// second library, has been renamed or the user has cleared the mirror — and records the folder of
    /// this run.
    /// </summary>
    /// <param name="reference">Program reference, whose <c>WrittenMirror</c> is updated.</param>
    /// <param name="programId">3Cat program id, used to make sure the old folder is the one of this program.</param>
    /// <param name="seriesPath">Mirrored folder written by this run, or <c>null</c> when there is no mirror.</param>
    private void RemoveMovedMirror(ThreeCatProgramReference reference, string programId, string? seriesPath)
    {
        var previous = reference.WrittenMirror?.Trim();
        reference.WrittenMirror = seriesPath ?? string.Empty;
        RemoveMovedFolder(previous, seriesPath, programId, "biblioteca espejo");
    }

    /// <summary>
    /// Removes the series folder of a previous run, whatever the reason the series is not written there
    /// anymore, and only when the folder is the one of this program.
    /// </summary>
    /// <param name="previous">Folder of the previous run, if any.</param>
    /// <param name="current">Folder of this run, or <c>null</c> when it is not written anymore.</param>
    /// <param name="programId">3Cat program id, used to make sure the old folder is the one of this program.</param>
    /// <param name="library">Which library the folder belongs to, for the log.</param>
    private void RemoveMovedFolder(string? previous, string? current, string programId, string library)
    {
        if (string.IsNullOrEmpty(previous)
            || (current is not null && LibraryPath.SamePath(previous, current)))
        {
            return;
        }

        if (!StrmLibrary.TryDeleteSeriesFolder(previous, programId))
        {
            _logger.LogInformation(
                "3Cat: «{Series}» se escribió antes en {Previous} ({Library}); no se elimina porque no es una "
                + "carpeta del plugin para este programa",
                Path.GetFileName(previous),
                previous,
                library);

            return;
        }

        var parent = Path.GetDirectoryName(previous);
        if (!string.IsNullOrEmpty(parent))
        {
            _libraryMonitor.ReportFileSystemChanged(parent);
        }

        _logger.LogInformation(
            "3Cat: «{Series}» ya no se escribe en {Previous} ({Library}); se eliminó la carpeta anterior",
            Path.GetFileName(previous),
            previous,
            library);
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

