using System.Net.WebSockets;

namespace Meridian.Infrastructure.Resilience;

/// <summary>
/// Shared lifecycle states for provider connection supervision.
/// </summary>
public enum ProviderConnectionLifecycleState
{
    NotConfigured = 0,
    Configured = 1,
    Connecting = 2,
    Connected = 3,
    Degraded = 4,
    Reconnecting = 5,
    Disconnecting = 6,
    Disconnected = 7,
    Failed = 8,
    Disabled = 9
}

/// <summary>
/// Normalized provider failure categories used by retry policy, diagnostics, and health surfaces.
/// </summary>
public enum ProviderFailureKind
{
    Unknown = 0,
    TransientNetworkFailure = 1,
    ProviderRateLimit = 2,
    AuthenticationOrAuthorizationFailure = 3,
    InvalidSubscription = 4,
    ProviderOutage = 5,
    MalformedProviderResponse = 6,
    LocalConfigurationError = 7,
    Cancelled = 8
}

/// <summary>
/// Safe provider connection diagnostics. It intentionally excludes URIs, credentials,
/// headers, account IDs, and payload data.
/// </summary>
/// <remarks>
/// The retained <see cref="WebSocketState"/> field is <see cref="WebSocketState.None"/> for
/// polling, raw-socket, simulated, and default contract diagnostics.
/// </remarks>
public sealed record WebSocketConnectionDiagnostics(
    string ProviderName,
    ProviderConnectionLifecycleState LifecycleState,
    WebSocketState WebSocketState,
    bool IsConnected,
    bool IsReconnecting,
    int ReconnectAttempts,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    DateTimeOffset? LastHeartbeatReceivedAt,
    DateTimeOffset? LastMessageReceivedAt,
    DateTimeOffset? LastReconnectAttemptAt,
    string? LastError,
    ProviderFailureKind? LastFailureKind,
    TimeSpan? ConnectionAge,
    TimeSpan? IdleDuration,
    int ActiveSubscriptions = 0,
    int FailedSubscriptions = 0,
    int RecoveringSubscriptions = 0,
    DateTimeOffset? LastSubscriptionMessageAt = null);
