using System;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Severity levels for info bar notifications.
/// Maps to the shared <see cref="InfoBarSeverityLevel"/> for WPF compatibility.
/// </summary>
public enum InfoBarSeverity : byte
{
    Informational,
    Success,
    Warning,
    Error
}

/// <summary>
/// Service for managing notification bar display with appropriate durations
/// based on severity. In WPF, this uses an event-based approach.
/// Delegates shared logic to <see cref="InfoBarConstants"/> and <see cref="ErrorDetailsModel"/> in Ui.Services.
/// </summary>
public sealed class InfoBarService
{
    private static readonly Lazy<InfoBarService> _instance = new(() => new InfoBarService());
    public static InfoBarService Instance => _instance.Value;

    private InfoBarService() { }

    public static class Durations
    {
        public const int Info = InfoBarConstants.InfoDurationMs;
        public const int Success = InfoBarConstants.SuccessDurationMs;
        public const int Warning = InfoBarConstants.WarningDurationMs;
        public const int Error = InfoBarConstants.ErrorDurationMs;
        public const int Critical = InfoBarConstants.CriticalDurationMs;
    }

    /// <summary>
    /// Event raised when a notification should be shown.
    /// Pages can subscribe to this to display notifications in their status area.
    /// </summary>
    public event EventHandler<InfoBarNotificationEventArgs>? NotificationRequested;

    public async Task ShowAsync(
        InfoBarSeverity severity,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        var duration = GetDurationForSeverity(severity);

        NotificationRequested?.Invoke(this, new InfoBarNotificationEventArgs
        {
            Severity = severity,
            Title = title,
            Message = message,
            DurationMs = duration,
            IsOpen = true
        });

        if (duration > 0)
        {
            try
            {
                await Task.Delay(duration, cancellationToken);
                NotificationRequested?.Invoke(this, new InfoBarNotificationEventArgs { IsOpen = false });
            }
            catch (OperationCanceledException)
            {
                // Cancellation expected
            }
        }
    }

    public async Task ShowAsync(
        InfoBarSeverity severity,
        string title,
        string message,
        int durationMs,
        CancellationToken cancellationToken = default)
    {
        NotificationRequested?.Invoke(this, new InfoBarNotificationEventArgs
        {
            Severity = severity,
            Title = title,
            Message = message,
            DurationMs = durationMs,
            IsOpen = true
        });

        if (durationMs > 0)
        {
            try
            {
                await Task.Delay(durationMs, cancellationToken);
                NotificationRequested?.Invoke(this, new InfoBarNotificationEventArgs { IsOpen = false });
            }
            catch (OperationCanceledException) { }
        }
    }

    public async Task ShowErrorAsync(
        string title,
        string message,
        string? context = null,
        string? remedy = null,
        CancellationToken cancellationToken = default)
    {
        var fullMessage = message;
        if (!string.IsNullOrEmpty(context))
            fullMessage += $"\n\nContext: {context}";
        if (!string.IsNullOrEmpty(remedy))
            fullMessage += $"\n\nSuggestion: {remedy}";

        await ShowAsync(InfoBarSeverity.Error, title, fullMessage, cancellationToken);
    }

    public static ErrorDetails CreateErrorDetails(Exception ex, string operation)
    {
        var shared = ErrorDetailsModel.CreateFromException(ex, operation);
        return new ErrorDetails
        {
            Title = shared.Title,
            Message = shared.Message,
            Context = shared.Context,
            Remedy = shared.Remedy,
            Severity = (InfoBarSeverity)shared.Severity
        };
    }

    public static int GetDurationForSeverity(InfoBarSeverity severity)
    {
        return InfoBarConstants.GetDurationForSeverity((InfoBarSeverityLevel)severity);
    }
}

public sealed class InfoBarNotificationEventArgs : EventArgs
{
    public InfoBarSeverity Severity { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int DurationMs { get; init; }
    public bool IsOpen { get; init; }
}

public sealed class ErrorDetails
{
    public string Title { get; set; } = "Error";
    public string Message { get; set; } = string.Empty;
    public string? Context { get; set; }
    public string? Remedy { get; set; }
    public InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Error;

    public string GetFormattedMessage()
    {
        var result = Message;
        if (!string.IsNullOrEmpty(Context))
            result += $"\n\nDetails: {Context}";
        if (!string.IsNullOrEmpty(Remedy))
            result += $"\n\nSuggestion: {Remedy}";
        return result;
    }
}
