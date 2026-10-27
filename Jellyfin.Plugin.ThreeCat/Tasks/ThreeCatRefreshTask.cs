using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Sync;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Tasks;

/// <summary>
/// Scheduled task that mirrors the configured 3Cat programs into the library folder.
/// </summary>
public sealed class ThreeCatRefreshTask : IScheduledTask
{
    /// <summary>
    /// Key of the scheduled task (stable across versions).
    /// </summary>
    public const string TaskKey = "ThreeCatRefresh";

    private const int DefaultIntervalHours = 12;

    private readonly ThreeCatSyncService _syncService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreeCatRefreshTask"/> class.
    /// </summary>
    /// <param name="syncService">Synchronization service.</param>
    /// <param name="logger">Logger.</param>
    public ThreeCatRefreshTask(ThreeCatSyncService syncService, ILogger<ThreeCatRefreshTask> logger)
    {
        _syncService = syncService;
        ArgumentNullException.ThrowIfNull(logger);
    }

    /// <inheritdoc />
    public string Name => "Actualizar 3Cat";

    /// <inheritdoc />
    public string Key => TaskKey;

    /// <inheritdoc />
    public string Description =>
        "Descarga el catálogo de los programas de 3Cat configurados y regenera los archivos .strm, .nfo y los subtítulos de la biblioteca.";

    /// <inheritdoc />
    public string Category => "3Cat";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return _syncService.RunAsync(configuration, progress, cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => BuildTriggers(Plugin.Instance?.Configuration.RefreshIntervalHours ?? DefaultIntervalHours);

    /// <summary>
    /// Builds the triggers of the task from the configured refresh interval.
    /// </summary>
    /// <param name="refreshIntervalHours">Interval, in hours (values lower than 1 fall back to the default).</param>
    /// <returns>The triggers.</returns>
    internal static IReadOnlyList<TaskTriggerInfo> BuildTriggers(int refreshIntervalHours)
    {
        var hours = refreshIntervalHours < 1 ? DefaultIntervalHours : refreshIntervalHours;

        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(hours).Ticks
            },
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.StartupTrigger
            }
        ];
    }
}
