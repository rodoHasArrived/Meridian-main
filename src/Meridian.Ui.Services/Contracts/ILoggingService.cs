using System;

namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Interface for logging services used by shared UI services.
/// Implemented by platform-specific logging services (WPF).
/// </summary>
public interface ILoggingService
{
    void LogError(string message, Exception? exception = null, params (string key, string value)[] properties);
    void LogWarning(string message, Exception? exception = null);
    void LogInfo(string message, params (string key, string value)[] properties);
}
