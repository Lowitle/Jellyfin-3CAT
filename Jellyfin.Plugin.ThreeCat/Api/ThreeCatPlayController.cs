using System.Globalization;
using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.Providers;
using Jellyfin.Plugin.ThreeCat.Sync;
using Jellyfin.Plugin.ThreeCat.Tasks;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Api;

/// <summary>
/// Endpoints used by the configuration page.
/// </summary>
[ApiController]
[Authorize]
[Route("ThreeCat")]
public sealed class ThreeCatPlayController : ControllerBase
{
    private readonly ThreeCatClient _client;
    private readonly OpenTvIdsClient _dataset;
    private readonly TmdbClient _tmdb;
    private readonly ITaskManager _taskManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<ThreeCatPlayController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreeCatPlayController"/> class.
    /// </summary>
    /// <param name="client">3Cat API client.</param>
    /// <param name="dataset">Identity dataset of <c>open-tv-ids</c>.</param>
    /// <param name="tmdb">TMDB client, used by the preview of the numbering.</param>
    /// <param name="taskManager">Task manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="libraryMonitor">Library monitor, used to make Jellyfin look at the new folder.</param>
    /// <param name="logger">Logger.</param>
    public ThreeCatPlayController(
        ThreeCatClient client,
        OpenTvIdsClient dataset,
        TmdbClient tmdb,
        ITaskManager taskManager,
        ILibraryManager libraryManager,
        ILibraryMonitor libraryMonitor,
        ILogger<ThreeCatPlayController> logger)
    {
        _client = client;
        _dataset = dataset;
        _tmdb = tmdb;
        _taskManager = taskManager;
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a program reference (a <c>programatv_id</c> or the URL of any of its videos) and
    /// reports its title and how many videos the catalog publishes.
    /// </summary>
    /// <remarks>
    /// 3Cat does not publish a search endpoint, so the page validates what the user types with this
    /// call instead of offering a list of programs.
    /// </remarks>
    /// <param name="value">Program id or URL of one of its videos.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved program, or 404 when the reference does not point at any program.</returns>
    [HttpGet("Programs/Resolve")]
    public async Task<ActionResult<ThreeCatProgramSummary>> ResolveAsync(
        [FromQuery] string? value,
        CancellationToken cancellationToken)
    {
        var reference = ThreeCatClient.NormalizeReference(value);
        if (reference.Length == 0)
        {
            return BadRequest("Escribe el id del programa o pega la URL de cualquiera de sus vídeos.");
        }

        var page = await _client
            .GetProgramVideosAsync(reference, maxItems: 0, cancellationToken)
            .ConfigureAwait(false);

        var programId = reference;

        // The reference may be the id of a video instead of the programatv_id: the catalog comes back
        // empty and the media endpoint publishes informacio.programa_id.
        if (page.Items.Count == 0)
        {
            var media = await _client.GetMediaAsync(reference, cancellationToken).ConfigureAwait(false);
            var resolved = media?.Info?.ProgramId;

            if (string.IsNullOrWhiteSpace(resolved))
            {
                _logger.LogWarning("3Cat: «{Reference}» no apunta a ningún programa", value);

                return NotFound(
                    $"«{value?.Trim()}» no devuelve ningún vídeo. Comprueba el id del programa en 3Cat "
                    + "o pega la URL de cualquiera de sus vídeos.");
            }

            programId = resolved;
            page = await _client
                .GetProgramVideosAsync(programId, maxItems: 0, cancellationToken)
                .ConfigureAwait(false);
        }

        if (page.Items.Count == 0)
        {
            return NotFound($"El programa {programId} no publica ningún vídeo disponible ahora mismo.");
        }

        var videos = page.Items.Count(video => !string.IsNullOrWhiteSpace(video.Id));
        var message = page.Truncated
            ? $"Se han leído {videos} de {page.TotalItems} vídeos ({page.TotalPages} páginas)."
            : $"El catálogo publica {videos} vídeos.";

        return Ok(new ThreeCatProgramSummary(
            programId,
            PickTitle(programId, page.Items),
            videos,
            page.TotalItems,
            page.TotalPages,
            page.Truncated,
            message));
    }

    /// <summary>
    /// Lists every program of the 3Cat catalog in a single response.
    /// </summary>
    /// <remarks>
    /// The configuration page cannot offer a table of programs without the whole catalog: 3Cat pages the
    /// catalog of a program, not the list of programs, and its search endpoint does not filter or sort on
    /// the server. This endpoint reads every page of the search endpoint (five requests of 500 programs)
    /// and returns the flat list, which the page then filters, sorts and pages in the browser. The web and
    /// radio programs are included and flagged with <see cref="ThreeCatCatalogProgram.HasVideo"/> =
    /// <c>false</c>, so the page is the one that hides them. With <c>ClassifyPrograms</c> on, each program
    /// is also tagged with the kind 3Cat files it under («Sèrie», «Documental»...) and the page can filter
    /// by it, with «Anime» instead of «Animació» for the programs that are anime; a sweep that fails only
    /// leaves the programs without a category.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every program of the catalog.</returns>
    [HttpGet("Programs/All")]
    public async Task<ActionResult<IReadOnlyList<ThreeCatCatalogProgram>>> ListAllProgramsAsync(
        CancellationToken cancellationToken)
    {
        var taxonomy = await ReadTaxonomyAsync(cancellationToken).ConfigureAwait(false);
        var anime = await ReadAnimeAsync(taxonomy, cancellationToken).ConfigureAwait(false);
        var programs = await _client.ListAllProgramsAsync(taxonomy, anime, cancellationToken).ConfigureAwait(false);

        return Ok(programs);
    }

    /// <summary>
    /// Reads the classification of the catalog, or <c>null</c> when it is turned off or 3Cat does not
    /// answer the facets.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The facets of each program, keyed by <c>programatv_id</c>.</returns>
    /// <remarks>
    /// The catalog is more important than its classification: a sweep that fails (24 requests) must not
    /// leave the page without programs, so the failures are logged and the page falls back to a catalog
    /// without categories.
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, ThreeCatProgramTaxonomy>?> ReadTaxonomyAsync(
        CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!configuration.ClassifyPrograms)
        {
            return null;
        }

        try
        {
            return await _client.GetProgramTaxonomyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "3Cat: no se pudo leer la clasificación de los programas del catálogo");

            return null;
        }
    }

    /// <summary>
    /// Reads the anime of the catalog, or <c>null</c> when it is turned off or 3Cat does not answer.
    /// </summary>
    /// <param name="taxonomy">Facets of each program, or <c>null</c> when the catalog is unclassified.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ids of the anime, or <c>null</c> to leave every animated program as «Animació».</returns>
    /// <remarks>
    /// 3Cat publishes no anime facet and files anime under the same format as the rest of the animation,
    /// so the only source is the page of each program: 175 of the 2201 programs of the catalog. They are
    /// read six at a time (about two seconds) and the client caches them for six hours, so the table of
    /// the configuration page is built from memory after the first read. A read that fails is logged and
    /// the page falls back to the catalog without anime, which is what it shows when the setting is off.
    /// </remarks>
    private async Task<IReadOnlySet<string>?> ReadAnimeAsync(
        IReadOnlyDictionary<string, ThreeCatProgramTaxonomy>? taxonomy,
        CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!configuration.SeparateAnime || taxonomy is not { Count: > 0 })
        {
            return null;
        }

        var animation = taxonomy
            .Where(entry => ProgramClassifier.IsAnimation(entry.Value.Formats, entry.Value.Themes))
            .Select(entry => entry.Key)
            .ToList();

        if (animation.Count == 0)
        {
            return null;
        }

        try
        {
            var details = await _client.GetProgramDetailsAsync(animation, cancellationToken).ConfigureAwait(false);

            return details
                .Where(entry => ProgramClassifier.IsAnime(entry.Value.Groups, entry.Value.Countries))
                .Select(entry => entry.Key)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "3Cat: no se pudo leer el anime de los programas del catálogo");

            return null;
        }
    }

    /// <summary>
    /// Checks that the destination folder exists and is writable by the Jellyfin process.
    /// </summary>
    /// <param name="path">Folder to check.</param>
    /// <returns>The result of the check.</returns>
    [HttpGet("Path/Check")]
    public ActionResult<LibraryPathStatus> CheckPath([FromQuery] string? path)
        => Ok(LibraryPath.Check(path));

    /// <summary>
    /// Lists the libraries of the server with their folders, so the page can offer a destination per
    /// program.
    /// </summary>
    /// <returns>The libraries that index at least one folder.</returns>
    /// <remarks>
    /// A program assigned to a library is written in the folder that library indexes: Jellyfin indexes a
    /// folder in every library that points at it, so no library has to be created for it. The list is read
    /// on every request, so a library created while the page is open shows up when the page is reloaded.
    /// </remarks>
    [HttpGet("Libraries")]
    public ActionResult<IReadOnlyList<ThreeCatLibrary>> ListLibraries()
    {
        var libraries = _libraryManager.GetVirtualFolders() ?? [];

        return Ok(libraries
            .Where(library => library.Locations is { Length: > 0 })
            .Select(library => new ThreeCatLibrary(
                library.Name,
                library.CollectionType?.ToString(),
                library.Locations!))
            .ToList());
    }

    /// <summary>
    /// Creates a «Programas de TV» library that points at the destination folder, or adds the folder
    /// to the library that already has that name.
    /// </summary>
    /// <remarks>
    /// The status is always returned with HTTP 200 and a <c>success</c> flag, so the configuration page
    /// can show the reason of a rejection instead of a generic error.
    /// </remarks>
    /// <param name="path">Destination folder; the configured one when omitted.</param>
    /// <param name="name">Library name; «3Cat» when omitted.</param>
    /// <returns>The result of the operation.</returns>
    [HttpPost("Library/Create")]
    public ActionResult<LibraryProvisionStatus> CreateLibrary(
        [FromQuery] string? path,
        [FromQuery] string? name)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var target = string.IsNullOrWhiteSpace(path) ? configuration.DestinationPath : path;

        var status = LibraryProvisioner.Provision(_libraryManager, target, name, _logger, _libraryMonitor);

        if (status.Success)
        {
            _logger.LogInformation(
                "3Cat: biblioteca {Library} → {Outcome} ({Message})",
                status.LibraryName,
                status.Outcome,
                status.Message);
        }
        else
        {
            _logger.LogWarning("3Cat: no se pudo preparar la biblioteca: {Message}", status.Message);
        }

        return Ok(status);
    }

    /// <summary>
    /// Applies the configured refresh interval to the scheduled task.
    /// </summary>
    /// <returns>No content.</returns>
    [HttpPost("Triggers")]
    public ActionResult ApplyTriggers()
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        ThreeCatTriggerScheduler.Apply(_taskManager, configuration, _logger);
        return NoContent();
    }

    /// <summary>
    /// Starts the refresh task immediately.
    /// </summary>
    /// <remarks>
    /// The stock <c>ScheduledTasks/Running/{id}</c> endpoint looks the task up by its GUID, not by its
    /// key, so the configuration page cannot use it directly without a round trip.
    /// </remarks>
    /// <returns>No content when the task was queued; 404 when it is not registered yet.</returns>
    [HttpPost("Refresh")]
    public ActionResult RunNow()
    {
        var worker = _taskManager.ScheduledTasks
            .FirstOrDefault(task => task.ScheduledTask is ThreeCatRefreshTask);

        if (worker is null)
        {
            _logger.LogWarning("3Cat: la tarea programada no está registrada; no se puede ejecutar");
            return NotFound("La tarea programada no está registrada todavía. Reinicia Jellyfin.");
        }

        _ = _taskManager.Execute(worker, new TaskOptions());
        return NoContent();
    }

    /// <summary>
    /// Reports the state of the identity dataset and how many configured programs resolve to each
    /// provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The state of the dataset.</returns>
    [HttpGet("Dataset")]
    public Task<ActionResult<ThreeCatDatasetStatus>> DatasetAsync(CancellationToken cancellationToken)
        => DatasetStatusAsync(forceRefresh: false, cancellationToken);

    /// <summary>
    /// Downloads the identity dataset again and reports its state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The state of the dataset.</returns>
    [HttpPost("Dataset/Refresh")]
    public Task<ActionResult<ThreeCatDatasetStatus>> RefreshDatasetAsync(CancellationToken cancellationToken)
        => DatasetStatusAsync(forceRefresh: true, cancellationToken);

    /// <summary>
    /// Previews the numbering a program would get with a provider, without writing a single file.
    /// </summary>
    /// <remarks>
    /// It is the table the design asks for before renumerating a series: every video of 3Cat next to the
    /// season, the episode and the evidence that paired it. It reads the catalog, the dataset and TMDB the
    /// same way the refresh task does, so what it shows is what the task would write.
    /// </remarks>
    /// <param name="programId">
    /// Program reference: its id, the URL of its page, its title, or the id or the URL of any of its videos.
    /// </param>
    /// <param name="tmdbId">Id of the series in TMDB (or the URL of its page), when the user knows it.</param>
    /// <param name="source">Source to try (<c>tmdb</c>); the configured one when empty.</param>
    /// <param name="anchors">
    /// Anchors typed by the user («T1=1; T20=856»). They win over every other evidence: they are the only thing
    /// that ties a dub to the original when the titles are translated and the dates are the ones of the dub.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The mapping table, with the message that explains why it is empty when it is. The endpoint answers
    /// a table even when the program cannot be read: an empty page is the failure nobody can diagnose.
    /// </returns>
    [HttpGet("Mapping")]
    public async Task<ActionResult<ThreeCatMappingReport>> MappingAsync(
        [FromQuery] string? programId,
        [FromQuery] string? tmdbId,
        [FromQuery] string? source,
        [FromQuery] string? anchors,
        CancellationToken cancellationToken)
    {
        var typed = programId?.Trim() ?? string.Empty;
        if (typed.Length == 0)
        {
            return BadRequest("Escribe el id del programa, la URL de su página, su título o el id de un vídeo.");
        }

        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var lookup = await LookupAsync(typed, cancellationToken).ConfigureAwait(false);

        var ordered = lookup.Videos
            .Where(video => !string.IsNullOrWhiteSpace(video.Id))
            .OrderBy(video => video.Broadcast ?? video.Published ?? DateTimeOffset.MinValue)
            .ThenBy(video => video.Id, StringComparer.Ordinal)
            .ToList();

        var resolved = lookup.ProgramId ?? typed;

        var title = FirstNonEmpty(
            lookup.Title,
            ordered.Count > 0 ? PickTitle(resolved, ordered) : null,
            resolved);

        var program = (configuration.Programs ?? []).FirstOrDefault(
            candidate => string.Equals(
                ThreeCatClient.NormalizeReference(candidate.Id),
                resolved,
                StringComparison.Ordinal));

        var preferred = ParseSource(source) ?? program?.Override ?? configuration.MetadataSource;
        var knownTmdb = Resolve(
            ProgramReference.TmdbId(tmdbId),
            ProgramReference.TmdbId(program?.TmdbId));

        var videos = ordered
            .Select(video => new ProviderVideo(
                FirstNonEmpty(video.Title, video.FriendlyName),
                video.Broadcast ?? video.Published,
                video.Id,
                video.Chapter))
            .ToList();

        // The anchors of the preview: the ones typed in the request, or the ones saved with the program.
        var anchorText = string.IsNullOrWhiteSpace(anchors) ? program?.Anchors : anchors;
        var anchorList = ProviderAnchors.Parse(anchorText);

        ProviderNumberingResult? result = null;
        string? externalId = null;
        var message = lookup.Message ?? string.Empty;

        if (message.Length == 0)
        {
            if (preferred != MetadataSource.Tmdb)
            {
                message = "La vista previa del emparejamiento solo está disponible para la fuente TMDB.";
            }
            else
            {
                if (knownTmdb is null)
                {
                    var dataset = await _dataset
                        .LoadAsync(configuration.OpenTvIdsUrl, forceRefresh: false, cancellationToken)
                        .ConfigureAwait(false);

                    knownTmdb = Resolve(null, dataset.Find(resolved)?.Ids?.Tmdb);
                }

                if (knownTmdb is null)
                {
                    message = "No hay id de TMDB para este programa: escríbelo en el segundo campo o en la"
                        + " columna TMDB de la tabla de programas.";
                }
                else if (!TmdbClient.IsConfigured(configuration.TmdbApiKey))
                {
                    externalId = knownTmdb;
                    message = "No hay clave de API de TMDB guardada: escríbela arriba, en «Clave de API de"
                        + " TMDB», y guarda los ajustes.";
                }
                else
                {
                    externalId = knownTmdb;

                    var episodes = await _tmdb
                        .GetEpisodesAsync(
                            knownTmdb,
                            configuration.TmdbApiKey,
                            configuration.TmdbLanguage,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (episodes.Count == 0)
                    {
                        message = $"TMDB no publica los episodios de la serie {knownTmdb}: comprueba que el id"
                            + " es el de una serie y no el de una película.";
                    }
                    else
                    {
                        result = ProviderNumbering.Assign(videos, episodes, title, anchorList);
                        message = result.UseThreeCat
                            ? $"La cobertura es del {result.Coverage:P0}: la serie se numeraría con 3Cat."
                            : $"La serie se numeraría con TMDB: {result.Matched} de {result.Assignable} vídeos emparejados.";

                        if (result.Anchors > 0 && result.AnchorsPlaced < result.Anchors)
                        {
                            message += $" Solo se han podido colocar {result.AnchorsPlaced} de {result.Anchors}"
                                + " anclas: revisa las que nombran un capítulo que no existe o un episodio ya"
                                + " ocupado por otra ancla.";
                        }

                        if (!result.DatesReliable && result.Anchors == 0 && result.ByCounter == 0)
                        {
                            message += " Las fechas de 3Cat no coinciden con las de TMDB (son las del doblaje),"
                                + " así que no se han usado: sin anclas no hay forma de emparejar los vídeos.";
                        }
                        else if (!result.DatesReliable && result.ByCounter > 0)
                        {
                            message += $" Las fechas de 3Cat son las del doblaje y no se han usado: los"
                                + $" {result.ByCounter} vídeos que no empareja ninguna otra evidencia se emparejan"
                                + " por el contador «capitol» de 3Cat, que numera el programa entero.";

                            if (result.CounterOffset != 0)
                            {
                                message += $" El contador va desfasado {result.CounterOffset} respecto a TMDB"
                                    + " (lo dicen tus anclas).";
                            }
                        }

                        // Why the counter stayed out of the way, when it did: it is the difference between a
                        // preview that fails and a preview that says what to look at.
                        if (result.ByCounter == 0 && result.Anchors == 0)
                        {
                            var highestCounter = result.Blocks
                                .Select(block => Math.Max(block.FirstCounter ?? 0, block.LastCounter ?? 0))
                                .DefaultIfEmpty(0)
                                .Max();

                            message += CounterHint(
                                highestCounter,
                                episodes.Count(episode => episode.Season > 0 && episode.Episode > 0));
                        }

                        // The counter counts emissions of 3Cat and 3Cat splits episodes, so the shift is the
                        // difference between the two catalogs and the user has to know it was applied.
                        if (result.ByShift > 0)
                        {
                            message += $" 3Cat emite en varias partes episodios que TMDB numera una sola vez, así que"
                                + $" su contador va desfasado: {result.Votes} títulos escritos en otro idioma sitúan"
                                + $" {result.ByShift} vídeos en el episodio que les toca.";
                        }

                        if (result.ByPart > 0)
                        {
                            message += $" {result.ByPart} vídeos son la segunda o tercera parte de un episodio y se"
                                + " escriben como «part1» y «part2» del mismo episodio, que es como los publicó 3Cat.";
                        }
                    }
                }
            }
        }

        var rows = new List<ThreeCatMappingRow>();
        if (result is not null)
        {
            for (var index = 0; index < ordered.Count; index++)
            {
                var slot = result.Slots[index];
                var episode = result.Episodes[index];

                rows.Add(new ThreeCatMappingRow(
                    ordered[index].Id!,
                    FirstNonEmpty(ordered[index].Title, ordered[index].FriendlyName),
                    ordered[index].Broadcast ?? ordered[index].Published,
                    slot.Season,
                    slot.Episode,
                    slot.IsDuplicate ? "duplicado" : MethodName(result.Methods[index]),
                    episode?.Title,
                    episode?.AirDate,
                    ordered[index].Chapter,
                    slot.Part));
            }
        }

        return Ok(new ThreeCatMappingReport(
            resolved,
            title,
            preferred.ToString(),
            externalId,
            ordered.Count,
            result?.Matched ?? 0,
            result?.ByDate ?? 0,
            result?.ByTitle ?? 0,
            result?.ByOrder ?? 0,
            result?.ByAnchor ?? 0,
            result?.ByCounter ?? 0,
            result?.ByShift ?? 0,
            result?.ByPart ?? 0,
            result?.Votes ?? 0,
            result?.CounterOffset ?? 0,
            result?.Anchors ?? anchorList.Count,
            result?.AnchorsPlaced ?? 0,
            result?.DatesReliable ?? false,
            result?.Specials ?? 0,
            result?.Duplicates ?? 0,
            result?.Coverage ?? 0,
            result?.UseThreeCat ?? true,
            message,
            rows,
            result?.Blocks ?? [],
            lookup.Candidates.Select(match => new ThreeCatProgramCandidate(match.Id, match.Title)).ToList()));
    }

    /// <summary>
    /// Resolves what the user typed in the preview of the mapping into a program of 3Cat.
    /// </summary>
    /// <param name="typed">Program id, URL of its page, slug, title, or id or URL of one of its videos.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The program and its catalog, or the reason why it cannot be read and the programs the reference
    /// could mean.
    /// </returns>
    /// <remarks>
    /// A number is the id of a program or of one of its videos, which the catalog and the media endpoint
    /// answer in two requests. Anything else (the URL of the program page, its slug or its title) is
    /// resolved against the program catalog the configuration page already keeps in memory, because 3Cat
    /// publishes no endpoint that maps a program URL to its <c>programatv_id</c>.
    /// </remarks>
    private async Task<ProgramLookup> LookupAsync(string typed, CancellationToken cancellationToken)
    {
        var reference = ThreeCatClient.NormalizeReference(typed);

        if (ProgramReference.IsId(reference))
        {
            var page = await _client
                .GetProgramVideosAsync(reference, maxItems: 0, cancellationToken)
                .ConfigureAwait(false);

            if (page.Items.Count > 0)
            {
                return new ProgramLookup(reference, null, page.Items, null, []);
            }

            var media = await _client.GetMediaAsync(reference, cancellationToken).ConfigureAwait(false);
            var programId = media?.Info?.ProgramId;

            if (!string.IsNullOrWhiteSpace(programId)
                && !string.Equals(programId, reference, StringComparison.Ordinal))
            {
                page = await _client
                    .GetProgramVideosAsync(programId, maxItems: 0, cancellationToken)
                    .ConfigureAwait(false);

                if (page.Items.Count > 0)
                {
                    return new ProgramLookup(programId, null, page.Items, null, []);
                }
            }
        }

        var programs = await _client
            .ListAllProgramsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (programs.Count == 0)
        {
            return new ProgramLookup(
                null,
                null,
                [],
                "No se pudo leer el catálogo de programas de 3Cat: prueba con el id del programa.",
                []);
        }

        var matches = ProgramReference.Match(programs, typed);
        if (matches.Count == 0)
        {
            var hint = ProgramReference.TmdbId(typed) is { } id
                ? $"«{typed}» parece un id de TMDB ({id}): el primer campo es el programa de 3Cat y el"
                    + " segundo el id de TMDB."
                : $"No encuentro «{typed}» en el catálogo de 3Cat: escribe el id del programa, la URL de su"
                    + " página o su título como lo publica 3Cat.";

            return new ProgramLookup(null, null, [], hint, []);
        }

        var best = matches[0];
        if (matches.Count > 1 && matches[1].Score == best.Score)
        {
            return new ProgramLookup(
                null,
                null,
                [],
                $"«{typed}» encaja con {matches.Count} programas del catálogo: elige uno y vuelve a pedir la"
                    + " vista previa.",
                matches);
        }

        var catalog = await _client
            .GetProgramVideosAsync(best.Id, maxItems: 0, cancellationToken)
            .ConfigureAwait(false);

        if (catalog.Items.Count == 0)
        {
            return new ProgramLookup(
                best.Id,
                best.Title,
                [],
                $"«{best.Title}» ({best.Id}) no publica ningún vídeo disponible ahora mismo: puede ser un"
                    + " programa de radio o de la web.",
                []);
        }

        return new ProgramLookup(best.Id, best.Title, catalog.Items, null, []);
    }

    /// <summary>
    /// Reads the dataset (or its local copy) and counts the configured programs it resolves.
    /// </summary>
    /// <param name="forceRefresh">Whether to download the dataset again.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The state of the dataset.</returns>
    private async Task<ActionResult<ThreeCatDatasetStatus>> DatasetStatusAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var dataset = await _dataset
            .LoadAsync(configuration.OpenTvIdsUrl, forceRefresh, cancellationToken)
            .ConfigureAwait(false);

        var programs = configuration.Programs ?? [];
        var withTmdb = 0;
        var withTvdb = 0;
        var withImdb = 0;

        foreach (var program in programs)
        {
            var ids = dataset.Find(ThreeCatClient.NormalizeReference(program.Id))?.Ids;

            if (Resolve(program.TmdbId, ids?.Tmdb) is not null)
            {
                withTmdb++;
            }

            if (Resolve(program.TvdbId, ids?.Tvdb) is not null)
            {
                withTvdb++;
            }

            if (!string.IsNullOrWhiteSpace(ids?.Imdb))
            {
                withImdb++;
            }
        }

        var date = dataset.Updated is { } updated
            ? $", actualizado el {updated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : string.Empty;

        var message = dataset.Count == 0
            ? "No hay dataset de identificadores: las series se numeran con 3Cat."
            : $"Dataset: {dataset.Count} identidades{date}"
                + (dataset.FromCache ? " (copia local)." : ".");

        return Ok(new ThreeCatDatasetStatus(
            dataset.Count,
            dataset.Updated,
            dataset.FromCache,
            string.IsNullOrWhiteSpace(configuration.OpenTvIdsUrl)
                ? OpenTvIdsClient.DefaultUrl
                : configuration.OpenTvIdsUrl.Trim(),
            programs.Count,
            withTmdb,
            withTvdb,
            withImdb,
            message));
    }

    /// <summary>
    /// Id of a provider: the one typed by the user wins over the one of the dataset.
    /// </summary>
    /// <param name="manual">Id typed by the user.</param>
    /// <param name="dataset">Id published by the dataset.</param>
    /// <returns>The id to use, or <c>null</c> when there is none.</returns>
    private static string? Resolve(string? manual, string? dataset)
        => !string.IsNullOrWhiteSpace(manual)
            ? manual.Trim()
            : string.IsNullOrWhiteSpace(dataset) ? null : dataset.Trim();

    /// <summary>
    /// Reads the source the page asks the preview for.
    /// </summary>
    /// <param name="value">Value of the query string.</param>
    /// <returns>The source, or <c>null</c> when the value names none.</returns>
    private static MetadataSource? ParseSource(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "TMDB" => MetadataSource.Tmdb,
            "TVDB" => MetadataSource.Tvdb,
            "3CAT" or "THREECAT" => MetadataSource.ThreeCat,
            _ => null
        };

    /// <summary>
    /// Names the evidence of a pairing for the table of the page.
    /// </summary>
    /// <param name="match">Evidence of the pairing.</param>
    /// <returns>The name in Spanish.</returns>
    private static string MethodName(ProviderMatch match)
        => match switch
        {
            ProviderMatch.Date => "fecha",
            ProviderMatch.Title => "título",
            ProviderMatch.Order => "orden",
            ProviderMatch.Anchor => "ancla",
            ProviderMatch.Counter => "capitol",
            ProviderMatch.CounterShift => "capitol ajustado",
            ProviderMatch.Part => "parte",
            _ => "sin evidencia"
        };

    /// <summary>
    /// Explains why the counter of 3Cat did not pair a single video, when it did not.
    /// </summary>
    /// <param name="highestCounter">Highest <c>capitol</c> the catalog publishes, or 0 when it publishes none.</param>
    /// <param name="listedEpisodes">Episodes the provider lists, once the specials are left out.</param>
    /// <returns>The sentence to append to the message of the preview, or an empty string when there is no counter.</returns>
    /// <remarks>
    /// The two reasons the pass stays out of the way are worth telling apart: a provider that lists a fraction of
    /// the program cannot be read by position —«Detectiu Conan» announces 1.206 chapters and TMDB lists 1.150, so
    /// the reach is half of the counter—, and a counter whose numbers are not the ones of the program (it
    /// restarts, or it repeats them) is the counter of the season.
    /// </remarks>
    internal static string CounterHint(int highestCounter, int listedEpisodes)
    {
        if (highestCounter <= 0)
        {
            return string.Empty;
        }

        var needed = (int)Math.Ceiling(highestCounter * ProviderNumbering.MinimumCounterReach);

        return listedEpisodes < needed
            ? $" El contador «capitol» de 3Cat anuncia {highestCounter} capítulos y el proveedor lista"
                + $" {listedEpisodes}: no se ha usado, porque hacen falta al menos {needed} para fiarse de la"
                + " posición que numera."
            : " El contador «capitol» de 3Cat no se ha usado: sus números no son los de un contador del programa"
                + " entero (se repiten o reinician con cada temporada).";
    }

    /// <summary>
    /// First value that is not empty.
    /// </summary>
    /// <param name="values">Values to look at, in order of preference.</param>
    /// <returns>The first one with text, trimmed, or an empty string.</returns>
    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string PickTitle(string programId, IReadOnlyList<ThreeCatVideo> videos)
    {
        var title = videos
            .SelectMany(video => video.Programs ?? [])
            .Select(program => program.Title)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        title ??= videos
            .Select(video => video.Bands?
                .OrderByDescending(band => band.Main)
                .Select(band => band.Description)
                .FirstOrDefault(description => !string.IsNullOrWhiteSpace(description)))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return string.IsNullOrWhiteSpace(title) ? programId : title!.Trim();
    }
}

/// <summary>
/// Result of resolving a program reference.
/// </summary>
/// <param name="ProgramId">Program id (<c>programatv_id</c>) the catalog has to be read from.</param>
/// <param name="Title">Title of the program, used as the name of the series folder.</param>
/// <param name="Videos">Videos read from the catalog.</param>
/// <param name="TotalItems">Videos the catalog reports.</param>
/// <param name="TotalPages">Pages the catalog reports.</param>
/// <param name="Truncated">Whether the read stopped before the last page.</param>
/// <param name="Message">Message shown to the user.</param>
public sealed record ThreeCatProgramSummary(
    string ProgramId,
    string Title,
    int Videos,
    int TotalItems,
    int TotalPages,
    bool Truncated,
    string Message);

/// <summary>
/// A library of the server, as offered by the page to pick the folder of a program.
/// </summary>
/// <param name="Name">Name of the library.</param>
/// <param name="CollectionType">
/// Type of content (<c>tvshows</c>, <c>movies</c>...), or <c>null</c> for a library that mixes them.
/// </param>
/// <param name="Locations">Folders the library indexes.</param>
public sealed record ThreeCatLibrary(
    string Name,
    string? CollectionType,
    IReadOnlyList<string> Locations);

/// <summary>
/// State of the identity dataset, as shown by the configuration page.
/// </summary>
/// <param name="Records">Identities the dataset publishes.</param>
/// <param name="Updated">Date of the copy, or <c>null</c> when there is none.</param>
/// <param name="FromCache">Whether the identities come from the local copy.</param>
/// <param name="Url">URL the dataset is read from.</param>
/// <param name="Programs">Programs configured in the plugin.</param>
/// <param name="WithTmdb">Configured programs that resolve to a TMDB id.</param>
/// <param name="WithTvdb">Configured programs that resolve to a TVDB id.</param>
/// <param name="WithImdb">Configured programs that resolve to an IMDb id.</param>
/// <param name="Message">Message shown to the user.</param>
public sealed record ThreeCatDatasetStatus(
    int Records,
    DateTimeOffset? Updated,
    bool FromCache,
    string Url,
    int Programs,
    int WithTmdb,
    int WithTvdb,
    int WithImdb,
    string Message);

/// <summary>
/// A row of the preview of the numbering: a video of 3Cat and the episode it would be written as.
/// </summary>
/// <param name="VideoId">3Cat video id.</param>
/// <param name="Title">Title published by 3Cat.</param>
/// <param name="Aired">Air date published by 3Cat.</param>
/// <param name="Season">Season the video would be written in; 0 is the season of specials.</param>
/// <param name="Episode">Episode inside its season.</param>
/// <param name="Method">Evidence that paired it («fecha», «título», «orden», «sin evidencia»).</param>
/// <param name="ProviderTitle">Title the provider publishes for that episode.</param>
/// <param name="ProviderAirDate">Air date the provider publishes for that episode.</param>
/// <param name="Chapter">
/// Counter <c>capitol</c> 3Cat publishes for the video, which is what tells where its catalog is not continuous
/// and what makes an anchor easy to write; <c>null</c> when 3Cat publishes none.
/// </param>
/// <param name="Part">
/// Part of the episode the video would be written as, or 0 when the video is the whole episode: 3Cat emits as two
/// videos the episodes it splits and the provider lists them once, and the two files end up in the same episode as
/// <c>- part1</c> and <c>- part2</c>.
/// </param>
public sealed record ThreeCatMappingRow(
    string VideoId,
    string Title,
    DateTimeOffset? Aired,
    int Season,
    int Episode,
    string Method,
    string? ProviderTitle,
    DateOnly? ProviderAirDate,
    int? Chapter,
    int Part);

/// <summary>
/// Preview of the numbering a program would get with a provider, which the page shows before anything is
/// written to disk.
/// </summary>
/// <param name="ProgramId">3Cat program id the catalog was read from.</param>
/// <param name="Title">Title of the series.</param>
/// <param name="Source">Source the preview used (<c>ThreeCat</c>, <c>Tmdb</c>).</param>
/// <param name="ExternalId">Provider id used, or <c>null</c> when there is none.</param>
/// <param name="Videos">Videos of the program.</param>
/// <param name="Matched">Videos paired with an episode of the provider.</param>
/// <param name="ByDate">Videos paired by air date.</param>
/// <param name="ByTitle">Videos paired by title.</param>
/// <param name="ByOrder">Videos paired by order inside a gap.</param>
/// <param name="ByAnchor">Videos paired by an anchor typed by the user.</param>
/// <param name="ByCounter">Videos paired by the <c>capitol</c> counter of 3Cat.</param>
/// <param name="ByShift">
/// Videos paired by the counter once the shift the titles read is applied: 3Cat emits as two videos the episodes it
/// splits, so the counter of the program runs ahead of the provider from the first split on.
/// </param>
/// <param name="ByPart">Videos written as a part of an episode another video of 3Cat also writes.</param>
/// <param name="Votes">Videos whose title named an episode of the provider written in the other language.</param>
/// <param name="CounterOffset">
/// Difference the anchors deduced between the counter of 3Cat and the absolute numbering of the provider; 0 when
/// both start at the same episode.
/// </param>
/// <param name="Anchors">Anchors the user typed, whether they could be placed or not.</param>
/// <param name="AnchorsPlaced">Anchors that could be placed.</param>
/// <param name="DatesReliable">
/// Whether the air dates of 3Cat were used as evidence; <c>false</c> when they belong to the emission of the dub
/// and not to the one of the provider.
/// </param>
/// <param name="Specials">Videos that would go to the season of specials.</param>
/// <param name="Duplicates">Videos 3Cat publishes twice.</param>
/// <param name="Coverage">Share of videos paired with an episode of the provider (0 to 1).</param>
/// <param name="UsesThreeCat">
/// Whether the run would keep the numbering of 3Cat, because the coverage is below the threshold or because
/// the provider could not be read.
/// </param>
/// <param name="Message">Message shown to the user.</param>
/// <param name="Rows">The table, one row per video.</param>
/// <param name="Blocks">Seasons of 3Cat, offered so the anchors can be written without reading the catalog.</param>
/// <param name="Candidates">
/// Programs the reference could mean when it is ambiguous, so the page can offer them instead of failing.
/// </param>
public sealed record ThreeCatMappingReport(
    string ProgramId,
    string Title,
    string Source,
    string? ExternalId,
    int Videos,
    int Matched,
    int ByDate,
    int ByTitle,
    int ByOrder,
    int ByAnchor,
    int ByCounter,
    int ByShift,
    int ByPart,
    int Votes,
    int CounterOffset,
    int Anchors,
    int AnchorsPlaced,
    bool DatesReliable,
    int Specials,
    int Duplicates,
    double Coverage,
    bool UsesThreeCat,
    string Message,
    IReadOnlyList<ThreeCatMappingRow> Rows,
    IReadOnlyList<ProviderBlock> Blocks,
    IReadOnlyList<ThreeCatProgramCandidate> Candidates);

/// <summary>
/// A program of 3Cat the reference of the preview could name.
/// </summary>
/// <param name="Id">Program id (<c>programatv_id</c>), the value the first field of the preview takes.</param>
/// <param name="Title">Title of the program as the catalog publishes it.</param>
public sealed record ThreeCatProgramCandidate(string Id, string Title);

/// <summary>
/// Program of 3Cat resolved from what the user typed in the preview of the mapping.
/// </summary>
/// <param name="ProgramId">Program id (<c>programatv_id</c>), or <c>null</c> when it cannot be resolved.</param>
/// <param name="Title">Title of the program as the catalog publishes it, or <c>null</c>.</param>
/// <param name="Videos">Catalog of the program, empty when it cannot be read.</param>
/// <param name="Message">Why the reference cannot be read, or <c>null</c> when it was resolved.</param>
/// <param name="Candidates">Programs the reference could mean, when it is ambiguous.</param>
internal sealed record ProgramLookup(
    string? ProgramId,
    string? Title,
    IReadOnlyList<ThreeCatVideo> Videos,
    string? Message,
    IReadOnlyList<ProgramMatch> Candidates);
