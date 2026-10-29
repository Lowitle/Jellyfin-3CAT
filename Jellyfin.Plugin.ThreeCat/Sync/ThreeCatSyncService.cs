using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Mirrors the configured 3Cat programs into .strm files.
/// </summary>
public sealed partial class ThreeCatSyncService
{
    private readonly ThreeCatClient _client;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<ThreeCatSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreeCatSyncService"/> class.
    /// </summary>
    /// <param name="client">3Cat API client.</param>
    /// <param name="libraryMonitor">Library monitor, notified after each series is updated.</param>
    /// <param name="logger">Logger.</param>
    public ThreeCatSyncService(
        ThreeCatClient client,
        ILibraryMonitor libraryMonitor,
        ILogger<ThreeCatSyncService> logger)
    {
        _client = client;
        _libraryMonitor = libraryMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Synchronizes every configured program.
    /// </summary>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="progress">Progress (0-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task RunAsync(
        PluginConfiguration configuration,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var references = (configuration.Programs ?? [])
            .Where(program => !string.IsNullOrWhiteSpace(program.Id))
            .ToList();

        if (references.Count == 0)
        {
            _logger.LogWarning("3Cat: no hay programas configurados, no se genera nada");
            progress?.Report(100);
            return;
        }

        if (string.IsNullOrWhiteSpace(configuration.DestinationPath))
        {
            throw new InvalidOperationException("3Cat: configura la carpeta de destino antes de actualizar.");
        }

        // Fails fast with an actionable message when the folder is not mounted inside the container
        // or Jellyfin cannot write into it (instead of an UnauthorizedAccessException from deep
        // inside the write path).
        LibraryPath.EnsureWritableRoot(configuration.DestinationPath);
        _logger.LogInformation("3Cat: carpeta de destino {Path}", configuration.DestinationPath.Trim());

        var totalWritten = 0;
        for (var i = 0; i < references.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = i;

            try
            {
                totalWritten += await SyncProgramAsync(
                    configuration,
                    references[index],
                    fraction => progress?.Report(
                        ((index + Math.Clamp(fraction, 0d, 1d)) / references.Count) * 100d),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "3Cat: error al sincronizar el programa {Program}", references[index].Id);
            }
        }

        progress?.Report(100);
        SaveRunState(references);
        _logger.LogInformation("3Cat: sincronización completada, {Files} archivos escritos", totalWritten);
    }

    /// <summary>
    /// Stores the series folder written for each program, so the next run can clean up after a program
    /// that moved to another library, and the naming scheme the library has now.
    /// </summary>
    /// <param name="references">Programs of the run, with the folder each one was written to.</param>
    /// <remarks>
    /// The configuration is read again from the plugin instead of saving the object of the run: the user
    /// may have saved the page while the task was writing, and that save must not be undone. The naming
    /// version is only raised once the run has finished, so an interrupted run rewrites the folders it
    /// did not reach on the next one.
    /// </remarks>
    private void SaveRunState(IReadOnlyList<ThreeCatProgramReference> references)
    {
        var plugin = Plugin.Instance;
        if (plugin is null || plugin.Configuration.Programs is not { Count: > 0 } programs)
        {
            return;
        }

        var configuration = plugin.Configuration;
        var changed = false;

        if (configuration.NamingVersion < PluginConfiguration.CurrentNamingVersion)
        {
            configuration.NamingVersion = PluginConfiguration.CurrentNamingVersion;
            changed = true;
        }

        foreach (var program in programs)
        {
            var id = ThreeCatClient.NormalizeReference(program.Id);
            var reference = references.FirstOrDefault(
                candidate => string.Equals(
                    ThreeCatClient.NormalizeReference(candidate.Id),
                    id,
                    StringComparison.Ordinal));

            if (reference is null)
            {
                continue;
            }

            if (!string.Equals(program.WrittenPath, reference.WrittenPath, StringComparison.Ordinal))
            {
                program.WrittenPath = reference.WrittenPath;
                changed = true;
            }

            if (!string.Equals(program.WrittenMirror, reference.WrittenMirror, StringComparison.Ordinal))
            {
                program.WrittenMirror = reference.WrittenMirror;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        try
        {
            plugin.UpdateConfiguration(configuration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is only a hint for the next run: the series themselves are already written.
            _logger.LogWarning(ex, "3Cat: no se pudo guardar la carpeta escrita de los programas");
        }
    }
}
