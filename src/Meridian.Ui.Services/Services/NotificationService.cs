using System;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Notification type levels.
/// </summary>
public enum NotificationType : byte
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Default no-op notification service for the shared UI services layer.
/// Platform-specific projects (WPF) override this with their own implementations
/// by setting the Instance property during app startup.
/// </summary>
public sealed class NotificationService
{
    private static readonly Lazy<NotificationService> _instance = new(() => new NotificationService());

    public static NotificationService Instance => _instance.Value;

    public Task NotifyErrorAsync(string title, string message, Exception? exception = null)
        => Task.CompletedTask;

    public Task NotifyWarningAsync(string title, string message)
        => Task.CompletedTask;

    public Task NotifyAsync(string title, string message, NotificationType type = NotificationType.Info)
        => Task.CompletedTask;

    public Task NotifyBackfillCompleteAsync(bool success, int symbolCount, int barsWritten, TimeSpan duration)
        => Task.CompletedTask;

    public Task NotifyScheduledJobAsync(string jobName, bool started, bool success = true)
        => Task.CompletedTask;

    public Task NotifyStorageWarningAsync(double usedPercent, long freeSpaceBytes)
        => Task.CompletedTask;
}
