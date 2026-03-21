namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Connection states shared between WPF desktop applications.
/// Part of Phase 6C.2 service deduplication.
/// </summary>
public enum ConnectionState : byte
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error
}

/// <summary>
/// Connection settings shared between desktop applications.
/// </summary>
public sealed class ConnectionSettings
{
    public bool AutoReconnectEnabled { get; set; } = true;
    public int MaxReconnectAttempts { get; set; } = 10;
    public int InitialReconnectDelayMs { get; set; } = 2000;
    public int MaxReconnectDelayMs { get; set; } = 300000;
    public int HealthCheckIntervalSeconds { get; set; } = 5;
    public string ServiceUrl { get; set; } = "http://localhost:8080";
    public int ServiceTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Connection state change event args.
/// </summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionState OldState { get; init; }
    public ConnectionState NewState { get; init; }
    public string Provider { get; init; } = string.Empty;
}

/// <summary>
/// Connection state event args for ViewModel binding.
/// </summary>
public sealed class ConnectionStateEventArgs : EventArgs
{
    public ConnectionState State { get; init; }
    public string Provider { get; init; } = string.Empty;
}

/// <summary>
/// Reconnect attempt event args.
/// </summary>
public sealed class ReconnectEventArgs : EventArgs
{
    public int AttemptNumber { get; init; }
    public int DelayMs { get; init; }
    public string Provider { get; init; } = string.Empty;
}

/// <summary>
/// Reconnect failed event args.
/// </summary>
public sealed class ReconnectFailedEventArgs : EventArgs
{
    public int AttemptNumber { get; init; }
    public string Error { get; init; } = string.Empty;
    public bool WillRetry { get; init; }
}

/// <summary>
/// Connection health event args with remediation guidance.
/// </summary>
public sealed class ConnectionHealthEventArgs : EventArgs
{
    public bool IsHealthy { get; init; }
    public double LatencyMs { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Categorized error type for structured error handling.
    /// </summary>
    public ConnectionErrorCategory ErrorCategory { get; init; }

    /// <summary>
    /// Actionable remediation guidance for the user.
    /// </summary>
    public string? RemediationGuidance { get; init; }
}

/// <summary>
/// Categorized connection error types for structured error handling and decision support.
/// </summary>
public enum ConnectionErrorCategory : byte
{
    None,
    NetworkUnreachable,
    ServiceNotRunning,
    AuthenticationFailed,
    RateLimited,
    Timeout,
    EndpointNotFound,
    ServerError,
    Unknown
}
