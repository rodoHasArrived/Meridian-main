namespace Meridian.Platform.Diagnostics;

/// <summary>
/// Context information passed to shutdown handlers.
/// </summary>
public readonly record struct ShutdownContext(
    ShutdownReason Reason,
    string? Message,
    DateTimeOffset RequestedAt,
    int TimeoutSeconds,
    string CorrelationId = ""
);

/// <summary>
/// Result of the shutdown operation.
/// </summary>
public readonly record struct ShutdownResult(
    bool Success,
    ShutdownReason Reason,
    DateTimeOffset StartedAt = default,
    DateTimeOffset CompletedAt = default,
    double DurationMs = 0,
    long EventsFlushed = 0,
    bool FlushTimeoutOccurred = false,
    int ComponentsDisposed = 0,
    string? ErrorMessage = null,
    string[]? Warnings = null,
    string CorrelationId = ""
);

/// <summary>
/// Progress information during shutdown.
/// </summary>
public readonly record struct ShutdownProgress(
    string Phase,
    int CurrentStep,
    int TotalSteps,
    int PercentComplete,
    DateTimeOffset Timestamp,
    string CorrelationId = ""
);

/// <summary>
/// Reason for shutdown.
/// </summary>
public enum ShutdownReason : byte
{
    Unknown,
    UserRequested,
    ProcessExit,
    SignalReceived,
    Error,
    MaintenanceWindow,
    ConfigurationChange,
    HealthCheckFailed,
    ResourceExhausted
}
