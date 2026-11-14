using System;
using System.Diagnostics;
using Meridian.Ui.Services.Services;

namespace Meridian.Ui.Services;

/// <summary>
/// Default logging service for the shared UI services layer.
/// Uses the same message rendering and structured property formatting as the
/// platform-specific logging services.
/// </summary>
public sealed class LoggingService
{
    private static readonly Lazy<LoggingService> _instance = new(() => new LoggingService());

    public static LoggingService Instance => _instance.Value;

    public void LogError(string message, Exception? exception = null, params (string key, string value)[] properties)
    {
        Debug.WriteLine(LoggingServiceBase.FormatLogEntry(LogLevel.Error, DateTime.UtcNow, message, exception, properties));
    }

    public void LogWarning(string message, Exception? exception = null)
    {
        LogWarning(message, exception, []);
    }

    public void LogWarning(string message, Exception? exception, params (string key, string value)[] properties)
    {
        Debug.WriteLine(LoggingServiceBase.FormatLogEntry(LogLevel.Warning, DateTime.UtcNow, message, exception, properties));
    }

    public void LogInfo(string message, params (string key, string value)[] properties)
    {
        Debug.WriteLine(LoggingServiceBase.FormatLogEntry(LogLevel.Info, DateTime.UtcNow, message, null, properties));
    }
}
