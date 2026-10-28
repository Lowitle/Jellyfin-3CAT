using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Library;
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
    private readonly ITaskManager _taskManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<ThreeCatPlayController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreeCatPlayController"/> class.
    /// </summary>
    /// <param name="client">3Cat API client.</param>
    /// <param name="taskManager">Task manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="libraryMonitor">Library monitor, used to make Jellyfin look at the new folder.</param>
    /// <param name="logger">Logger.</param>
    public ThreeCatPlayController(
        ThreeCatClient client,
        ITaskManager taskManager,
        ILibraryManager libraryManager,
        ILibraryMonitor libraryMonitor,
        ILogger<ThreeCatPlayController> logger)
    {
        _client = client;
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
