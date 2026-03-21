using System;
using System.Diagnostics;

namespace Meridian.Ui.Services;

/// <summary>
/// Default logging service for the shared UI services layer.
/// Platform-specific projects (WPF) override this with their own implementations
/// by setting the Instance property during app startup.
/// </summary>
public sealed class LoggingService
{
    private static readonly Lazy<LoggingService> _instance = new(() => new LoggingService());

    public static LoggingService Instance => _instance.Value;

    public void LogError(string message, Exception? exception = null, params (string key, string value)[] properties)
    {
        Debug.WriteLine($"[ERROR] {message} {exception?.Message}");
    }

    public void LogWarning(string message, Exception? exception = null)
    {
        Debug.WriteLine($"[WARN] {message} {exception?.Message}");
    }

    public void LogInfo(string message, params (string key, string value)[] properties)
    {
        Debug.WriteLine($"[INFO] {message}");
    }
}
