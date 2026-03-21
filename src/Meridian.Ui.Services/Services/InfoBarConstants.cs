namespace Meridian.Ui.Services.Services;

/// <summary>
/// Platform-agnostic severity levels for info bar notifications.
/// Shared across desktop platforms.
/// </summary>
public enum InfoBarSeverityLevel : byte
{
    Informational,
    Success,
    Warning,
    Error
}

/// <summary>
/// Duration configuration for different severity levels.
/// Shared across InfoBarService implementations.
/// </summary>
public static class InfoBarConstants
{
    /// <summary>Informational messages - 4 seconds (user acknowledgment optional).</summary>
    public const int InfoDurationMs = 4000;

    /// <summary>Success messages - 3 seconds (quick confirmation).</summary>
    public const int SuccessDurationMs = 3000;

    /// <summary>Warning messages - 6 seconds (user should notice).</summary>
    public const int WarningDurationMs = 6000;

    /// <summary>Error messages - 10 seconds (requires attention).</summary>
    public const int ErrorDurationMs = 10000;

    /// <summary>Critical errors - no auto-dismiss (manual close required).</summary>
    public const int CriticalDurationMs = 0;

    /// <summary>Gets the recommended duration for a severity level.</summary>
    public static int GetDurationForSeverity(InfoBarSeverityLevel severity) => severity switch
    {
        InfoBarSeverityLevel.Informational => InfoDurationMs,
        InfoBarSeverityLevel.Success => SuccessDurationMs,
        InfoBarSeverityLevel.Warning => WarningDurationMs,
        InfoBarSeverityLevel.Error => ErrorDurationMs,
        _ => InfoDurationMs
    };
}

/// <summary>
/// Contains detailed error information for user display.
/// Shared across desktop platforms.
/// </summary>
public sealed class ErrorDetailsModel
{
    public string Title { get; set; } = "Error";
    public string Message { get; set; } = string.Empty;
    public string? Context { get; set; }
    public string? Remedy { get; set; }
    public InfoBarSeverityLevel Severity { get; set; } = InfoBarSeverityLevel.Error;

    /// <summary>
    /// Gets the full formatted message including context and remedy.
    /// </summary>
    public string GetFormattedMessage()
    {
        var result = Message;
        if (!string.IsNullOrEmpty(Context))
            result += $"\n\nDetails: {Context}";
        if (!string.IsNullOrEmpty(Remedy))
            result += $"\n\nSuggestion: {Remedy}";
        return result;
    }

    /// <summary>
    /// Creates a user-friendly error message with context and remedies from an exception.
    /// </summary>
    public static ErrorDetailsModel CreateFromException(Exception ex, string operation)
    {
        return ex switch
        {
            OperationCanceledException => new ErrorDetailsModel
            {
                Title = "Operation Cancelled",
                Message = $"The {operation} was cancelled.",
                Context = "User cancelled the operation or the request timed out.",
                Remedy = "If this was unexpected, try the operation again.",
                Severity = InfoBarSeverityLevel.Warning
            },
            TimeoutException => new ErrorDetailsModel
            {
                Title = "Request Timeout",
                Message = $"The {operation} took too long to complete.",
                Context = "The server may be busy or unresponsive.",
                Remedy = "Wait a moment and try again. If the problem persists, check your connection.",
                Severity = InfoBarSeverityLevel.Error
            },
            UnauthorizedAccessException => new ErrorDetailsModel
            {
                Title = "Access Denied",
                Message = $"Permission denied for {operation}.",
                Context = "You may not have the required permissions.",
                Remedy = "Check your credentials or contact your administrator.",
                Severity = InfoBarSeverityLevel.Error
            },
            System.Net.Http.HttpRequestException httpEx => new ErrorDetailsModel
            {
                Title = "Connection Error",
                Message = $"Failed to connect while {operation}.",
                Context = httpEx.Message,
                Remedy = "Check your internet connection and ensure the collector service is running.",
                Severity = InfoBarSeverityLevel.Error
            },
            System.IO.IOException ioEx => new ErrorDetailsModel
            {
                Title = "File System Error",
                Message = $"Error accessing files during {operation}.",
                Context = ioEx.Message,
                Remedy = "Ensure you have proper permissions and sufficient disk space.",
                Severity = InfoBarSeverityLevel.Error
            },
            ArgumentException argEx => new ErrorDetailsModel
            {
                Title = "Invalid Input",
                Message = $"Invalid data provided for {operation}.",
                Context = argEx.Message,
                Remedy = "Check your input values and try again.",
                Severity = InfoBarSeverityLevel.Warning
            },
            InvalidOperationException invEx => new ErrorDetailsModel
            {
                Title = "Invalid Operation",
                Message = $"Cannot perform {operation} in the current state.",
                Context = invEx.Message,
                Remedy = "Ensure the application is in the correct state before retrying.",
                Severity = InfoBarSeverityLevel.Warning
            },
            _ => new ErrorDetailsModel
            {
                Title = "Unexpected Error",
                Message = $"An error occurred during {operation}.",
                Context = ex.Message,
                Remedy = "Try the operation again. If the problem persists, check the logs or restart the application.",
                Severity = InfoBarSeverityLevel.Error
            }
        };
    }
}
