using System;
using System.Text;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Represents the severity level of a log entry.
/// </summary>
public enum LogLevel : byte
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// Event arguments for log entry events.
/// </summary>
public sealed class LogEntryEventArgs : EventArgs
{
    public LogLevel Level { get; }
    public DateTime Timestamp { get; }
    public string Message { get; }
    public Exception? Exception { get; }
    public (string key, string value)[] Properties { get; }

    public LogEntryEventArgs(
        LogLevel level,
        DateTime timestamp,
        string message,
        Exception? exception,
        (string key, string value)[] properties)
    {
        Level = level;
        Timestamp = timestamp;
        Message = message;
        Exception = exception;
        Properties = properties;
    }
}

/// <summary>
/// Abstract base class for structured logging shared between platforms.
/// Provides log formatting, level filtering, and event raising.
/// Platform-specific output (Debug.WriteLine, file, etc.) is delegated to derived classes.
/// Part of Phase 2 service extraction.
/// </summary>
public abstract class LoggingServiceBase : Contracts.ILoggingService
{
    private readonly object _lock = new();

    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public event EventHandler<LogEntryEventArgs>? LogWritten;

    public void LogInfo(string message, params (string key, string value)[] properties)
        => Log(LogLevel.Info, message, null, properties);

    public void LogWarning(string message, params (string key, string value)[] properties)
        => Log(LogLevel.Warning, message, null, properties);

    public void LogError(string message, Exception? ex = null)
        => Log(LogLevel.Error, message, ex, []);

    public void LogDebug(string message, params (string key, string value)[] properties)
        => Log(LogLevel.Debug, message, null, properties);

    // Explicit interface implementations for shared ILoggingService contract
    void Contracts.ILoggingService.LogWarning(string message, Exception? exception)
    {
        if (exception != null)
            LogWarning(message, ("exception", exception.GetType().Name), ("exceptionMessage", exception.Message));
        else
            LogWarning(message);
    }

    void Contracts.ILoggingService.LogError(string message, Exception? exception, params (string key, string value)[] properties)
    {
        if (exception != null)
            Log(LogLevel.Error, message, exception, properties);
        else
            Log(LogLevel.Error, message, null, properties);
    }

    private void Log(LogLevel level, string message, Exception? exception, (string key, string value)[] properties)
    {
        if (level < MinimumLevel)
            return;

        var timestamp = DateTime.UtcNow;
        var formattedMessage = FormatLogEntry(level, timestamp, message, exception, properties);

        lock (_lock)
        {
            WriteOutput(formattedMessage);
        }

        LogWritten?.Invoke(this, new LogEntryEventArgs(level, timestamp, message, exception, properties));
    }

    /// <summary>
    /// Writes the formatted log entry to the platform-specific output.
    /// </summary>
    protected abstract void WriteOutput(string formattedMessage);

    public static string FormatLogEntry(
        LogLevel level,
        DateTime timestamp,
        string message,
        Exception? exception,
        (string key, string value)[] properties)
    {
        var sb = new StringBuilder();

        sb.Append('[');
        sb.Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        sb.Append("] [");
        sb.Append(GetLevelString(level));
        sb.Append("] ");
        sb.Append(message);

        if (properties.Length > 0)
        {
            sb.Append(" {");
            for (var i = 0; i < properties.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(properties[i].key);
                sb.Append('=');
                sb.Append(properties[i].value);
            }
            sb.Append('}');
        }

        if (exception is not null)
        {
            sb.AppendLine();
            sb.Append("  Exception: ");
            sb.Append(exception.GetType().Name);
            sb.Append(" - ");
            sb.Append(exception.Message);

            if (exception.StackTrace is not null)
            {
                sb.AppendLine();
                sb.Append("  StackTrace: ");
                sb.Append(exception.StackTrace);
            }

            if (exception.InnerException is not null)
            {
                sb.AppendLine();
                sb.Append("  InnerException: ");
                sb.Append(exception.InnerException.GetType().Name);
                sb.Append(" - ");
                sb.Append(exception.InnerException.Message);
            }
        }

        return sb.ToString();
    }

    private static string GetLevelString(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        _ => "UNKN "
    };
}
