namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Interface for scheduling and managing background tasks.
/// Shared across desktop applications.
/// Part of C1 improvement (service deduplication).
/// </summary>
public interface IBackgroundTaskSchedulerService
{
    Task ScheduleTaskAsync(string taskName, Func<CancellationToken, Task> task, TimeSpan interval, CancellationToken cancellationToken = default);
    Task CancelTaskAsync(string taskName);
    bool IsTaskRunning(string taskName);
}
