using System;
using System.Diagnostics;
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
/// Hosts with a real notification surface implement
/// <see cref="Contracts.INotificationService"/> via <c>NotificationServiceBase</c>
/// (see <c>Meridian.Wpf.Services.NotificationService</c>); shared code holding only
/// this default gets no-op behaviour.
/// </summary>
public sealed class NotificationService
{
    private static readonly Lazy<NotificationService> _instance = new(() => new NotificationService());

    public static NotificationService Instance => _instance.Value;

    public Task NotifyErrorAsync(string title, string message, Exception? exception = null)
    {
        Discarded(NotificationType.Error, title, message);
        return Task.CompletedTask;
    }

    public Task NotifyWarningAsync(string title, string message)
    {
        Discarded(NotificationType.Warning, title, message);
        return Task.CompletedTask;
    }

    public Task NotifyAsync(string title, string message, NotificationType type = NotificationType.Info)
    {
        Discarded(type, title, message);
        return Task.CompletedTask;
    }

    public Task NotifyBackfillCompleteAsync(bool success, int symbolCount, int barsWritten, TimeSpan duration)
    {
        Discarded(
            success ? NotificationType.Success : NotificationType.Error,
            "Backfill complete",
            $"success={success}, symbols={symbolCount}, bars={barsWritten}, duration={duration}");
        return Task.CompletedTask;
    }

    public Task NotifyScheduledJobAsync(string jobName, bool started, bool success = true)
    {
        Discarded(
            success ? NotificationType.Info : NotificationType.Error,
            "Scheduled job",
            $"{jobName}: started={started}, success={success}");
        return Task.CompletedTask;
    }

    public Task NotifyStorageWarningAsync(double usedPercent, long freeSpaceBytes)
    {
        Discarded(
            NotificationType.Warning,
            "Storage warning",
            $"used={usedPercent:0.##}%, free={freeSpaceBytes} bytes");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records that a notification was dropped because no real notification surface is wired.
    /// Without this trace, hosts that forget to register a concrete
    /// <see cref="Contracts.INotificationService"/> silently lose every notification with no
    /// diagnostic signal. Kept at debug level so a correctly wired host pays nothing.
    /// </summary>
    private static void Discarded(NotificationType type, string title, string message)
        => Debug.WriteLine(
            $"[NotificationService] Discarded (no notification surface wired) {type}: {title} - {message}");
}
