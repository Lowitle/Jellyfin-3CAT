using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat;

/// <summary>
/// Applies the configured refresh interval to the scheduled task.
/// </summary>
public static class ThreeCatTriggerScheduler
{
    /// <summary>
    /// Updates the triggers of <see cref="ThreeCatRefreshTask"/> with the configured interval.
    /// </summary>
    /// <param name="taskManager">Task manager.</param>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns><c>true</c> when the task was found and updated.</returns>
    public static bool Apply(ITaskManager taskManager, PluginConfiguration configuration, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(taskManager);
        ArgumentNullException.ThrowIfNull(configuration);

        var worker = taskManager.ScheduledTasks
            .FirstOrDefault(task => task.ScheduledTask is ThreeCatRefreshTask);

        if (worker is null)
        {
            logger?.LogWarning("3Cat: la tarea programada no está registrada todavía; reinicia el servidor");
            return false;
        }

        worker.Triggers = ThreeCatRefreshTask.BuildTriggers(configuration.RefreshIntervalHours);
        worker.ReloadTriggerEvents();

        logger?.LogInformation(
            "3Cat: intervalo de actualización aplicado ({Hours} h)",
            configuration.RefreshIntervalHours);

        return true;
    }
}
