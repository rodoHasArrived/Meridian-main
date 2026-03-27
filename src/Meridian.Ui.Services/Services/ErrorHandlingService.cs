using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services.Collections;

namespace Meridian.Ui.Services;

/// <summary>
/// Centralized error handling service that provides consistent error processing,
/// logging, and user notification across the application.
/// </summary>
public sealed class ErrorHandlingService : IDisposable
{
    private static readonly Lazy<ErrorHandlingService> _instance = new(() => new ErrorHandlingService());
    private readonly BoundedObservableCollection<ErrorRecord> _recentErrors = new(50);
    private readonly NotificationService _notificationService;
    private readonly LoggingService _loggingService;
    private bool _disposed;

    public static ErrorHandlingService Instance => _instance.Value;

    private ErrorHandlingService()
    {
        _notificationService = NotificationService.Instance;
        _loggingService = LoggingService.Instance;
    }

    /// <summary>
    /// Gets the recent errors collection.
    /// </summary>
    public IReadOnlyList<ErrorRecord> RecentErrors => _recentErrors;

    /// <summary>
    /// Event raised when an error is handled.
    /// </summary>
    public event EventHandler<ErrorHandledEventArgs>? ErrorHandled;

    /// <summary>
    /// Handles an exception with the specified context and options.
    /// </summary>
    /// <param name="exception">The exception to handle.</param>
    /// <param name="context">Context describing where the error occurred.</param>
    /// <param name="options">Options for how to handle the error.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task HandleExceptionAsync(
        Exception exception,
        string context,
        ErrorHandlingOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= ErrorHandlingOptions.Default;

        var record = new ErrorRecord
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Exception = exception,
            Context = context,
            Timestamp = DateTime.UtcNow,
            Severity = ClassifyException(exception),
            WasNotified = false
        };

        // Log the error
        _loggingService.LogError(context, exception, ("severity", record.Severity.ToString()));

        // Add to recent errors
        _recentErrors.Prepend(record);

        // Notify user if requested
        if (options.NotifyUser && !ct.IsCancellationRequested)
        {
            record.WasNotified = true;
            await NotifyUserAsync(record, options, ct);
        }

        // Raise event
        ErrorHandled?.Invoke(this, new ErrorHandledEventArgs
        {
            Record = record,
            WasNotified = record.WasNotified
        });
    }

    /// <summary>
    /// Handles an error message with the specified context and options.
    /// </summary>
    public async Task HandleErrorAsync(
        string errorMessage,
        string context,
        ErrorHandlingOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= ErrorHandlingOptions.Default;

        var record = new ErrorRecord
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            ErrorMessage = errorMessage,
            Context = context,
            Timestamp = DateTime.UtcNow,
            Severity = options.Severity ?? ErrorSeverity.Error,
            WasNotified = false
        };

        // Log the error
        _loggingService.LogError(context, null, ("errorMessage", errorMessage));

        // Add to recent errors
        _recentErrors.Prepend(record);

        // Notify user if requested
        if (options.NotifyUser && !ct.IsCancellationRequested)
        {
            record.WasNotified = true;
            await NotifyUserAsync(record, options, ct);
        }

        // Raise event
        ErrorHandled?.Invoke(this, new ErrorHandledEventArgs
        {
            Record = record,
            WasNotified = record.WasNotified
        });
    }

    /// <summary>
    /// Executes an action with automatic error handling.
    /// </summary>
    /// <typeparam name="T">The return type of the action.</typeparam>
    /// <param name="action">The action to execute.</param>
    /// <param name="context">Context describing the action.</param>
    /// <param name="defaultValue">Default value to return on error.</param>
    /// <param name="options">Error handling options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The action result or default value on error.</returns>
    public async Task<T?> ExecuteWithErrorHandlingAsync<T>(
        Func<CancellationToken, Task<T>> action,
        string context,
        T? defaultValue = default,
        ErrorHandlingOptions? options = null,
        CancellationToken ct = default)
    {
        try
        {
            return await action(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(ex, context, options, ct);
            return defaultValue;
        }
    }

    /// <summary>
    /// Executes an action with automatic error handling.
    /// </summary>
    public async Task ExecuteWithErrorHandlingAsync(
        Func<CancellationToken, Task> action,
        string context,
        ErrorHandlingOptions? options = null,
        CancellationToken ct = default)
    {
        try
        {
            await action(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(ex, context, options, ct);
        }
    }

    /// <summary>
    /// Clears all recent errors.
    /// </summary>
    public void ClearErrors()
    {
        _recentErrors.Clear();
    }

    private async Task NotifyUserAsync(ErrorRecord record, ErrorHandlingOptions options, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return;

        var title = options.NotificationTitle ?? GetDefaultTitle(record.Severity);
        var message = record.Exception?.Message ?? record.ErrorMessage ?? "An unknown error occurred";

        if (options.IncludeContext && !string.IsNullOrEmpty(record.Context))
        {
            message = $"{record.Context}: {message}";
        }

        await _notificationService.NotifyErrorAsync(title, message);
    }

    private static string GetDefaultTitle(ErrorSeverity severity)
    {
        return severity switch
        {
            ErrorSeverity.Warning => "Warning",
            ErrorSeverity.Error => "Error",
            ErrorSeverity.Critical => "Critical Error",
            _ => "Notice"
        };
    }

    /// <summary>
    /// Handles an exception with guided remediation by raising an alert with
    /// playbook-based steps when applicable.
    /// </summary>
    public async Task HandleExceptionWithRemediationAsync(
        Exception exception,
        string context,
        string? alertCategory = null,
        IReadOnlyList<string>? affectedResources = null,
        CancellationToken ct = default)
    {
        // Log and record the error
        var severity = ClassifyException(exception);
        var record = new ErrorRecord
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Exception = exception,
            Context = context,
            Timestamp = DateTime.UtcNow,
            Severity = severity,
            WasNotified = true
        };

        _loggingService.LogError(context, exception, ("severity", severity.ToString()));
        _recentErrors.Prepend(record);

        // Raise an alert with remediation playbook via AlertService
        var category = alertCategory ?? ClassifyExceptionCategory(exception);
        var alertSeverity = MapToAlertSeverity(severity);
        var impact = severity >= ErrorSeverity.Critical ? BusinessImpact.High : BusinessImpact.Medium;

        AlertService.Instance.RaiseAlert(
            title: $"{context}: {exception.GetType().Name}",
            description: exception.Message,
            severity: alertSeverity,
            impact: impact,
            category: category,
            affectedResources: affectedResources);

        ErrorHandled?.Invoke(this, new ErrorHandledEventArgs
        {
            Record = record,
            WasNotified = true
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets remediation steps for a given error category from registered playbooks.
    /// </summary>
    public IReadOnlyList<RemediationStep> GetRemediationSteps(string errorCategory)
    {
        var alerts = AlertService.Instance.GetActiveAlerts();
        var matchingAlert = alerts.FirstOrDefault(a =>
            string.Equals(a.Category, errorCategory, StringComparison.OrdinalIgnoreCase) &&
            a.Playbook != null);

        return matchingAlert?.Playbook?.RemediationSteps ?? Array.Empty<RemediationStep>();
    }

    private static string ClassifyExceptionCategory(Exception exception)
    {
        return exception switch
        {
            HttpRequestException => "Connection",
            TimeoutException => "Connection",
            UnauthorizedAccessException => "Provider",
            System.IO.IOException => "Storage",
            _ => "General"
        };
    }

    private static AlertSeverity MapToAlertSeverity(ErrorSeverity severity)
    {
        return severity switch
        {
            ErrorSeverity.Info => AlertSeverity.Info,
            ErrorSeverity.Warning => AlertSeverity.Warning,
            ErrorSeverity.Error => AlertSeverity.Error,
            ErrorSeverity.Critical => AlertSeverity.Critical,
            _ => AlertSeverity.Warning
        };
    }

    private static ErrorSeverity ClassifyException(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => ErrorSeverity.Info,
            TimeoutException => ErrorSeverity.Warning,
            HttpRequestException => ErrorSeverity.Warning,
            UnauthorizedAccessException => ErrorSeverity.Error,
            InvalidOperationException => ErrorSeverity.Error,
            _ => ErrorSeverity.Error
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
    }
}

/// <summary>
/// Represents a recorded error.
/// </summary>
public sealed class ErrorRecord
{
    public string Id { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
    public string? ErrorMessage { get; init; }
    public string Context { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public ErrorSeverity Severity { get; init; }
    public bool WasNotified { get; set; }

    /// <summary>
    /// Category for matching to remediation playbooks (e.g. "Connection", "Storage", "Provider").
    /// </summary>
    public string? Category { get; init; }

    public string DisplayMessage => Exception?.Message ?? ErrorMessage ?? "Unknown error";
}

/// <summary>
/// Error severity levels.
/// </summary>
public enum ErrorSeverity : byte
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Options for error handling behavior.
/// </summary>
public sealed class ErrorHandlingOptions
{
    public static ErrorHandlingOptions Default { get; } = new();
    public static ErrorHandlingOptions Silent { get; } = new() { NotifyUser = false };
    public static ErrorHandlingOptions Verbose { get; } = new() { NotifyUser = true, IncludeContext = true };

    public bool NotifyUser { get; init; } = true;
    public bool IncludeContext { get; init; } = false;
    public string? NotificationTitle { get; init; }
    public ErrorSeverity? Severity { get; init; }
}

/// <summary>
/// Event args for error handled events.
/// </summary>
public sealed class ErrorHandledEventArgs : EventArgs
{
    public ErrorRecord Record { get; init; } = null!;
    public bool WasNotified { get; init; }
}
