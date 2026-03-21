namespace Meridian.Infrastructure.Adapters.NYSE;

/// <summary>
/// Configuration options for NYSE Direct Connection data source.
/// </summary>
public sealed record NYSEOptions
{
    /// <summary>
    /// NYSE Connect API Key for authentication.
    /// Can be set via environment variable NYSE_API_KEY.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// NYSE Connect API Secret for authentication.
    /// Can be set via environment variable NYSE_API_SECRET.
    /// </summary>
    public string? ApiSecret { get; init; }

    /// <summary>
    /// Client ID for OAuth authentication with NYSE Connect.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Base URL for NYSE REST API.
    /// Default: https://api.nyse.com/v1
    /// </summary>
    public string BaseUrl { get; init; } = "https://api.nyse.com/v1";

    /// <summary>
    /// WebSocket URL for NYSE real-time streaming.
    /// Default: wss://stream.nyse.com/v1
    /// </summary>
    public string WebSocketUrl { get; init; } = "wss://stream.nyse.com/v1";

    /// <summary>
    /// Whether to use the sandbox/test environment.
    /// </summary>
    public bool UseSandbox { get; init; }

    /// <summary>
    /// Sandbox/test environment base URL.
    /// </summary>
    public string SandboxBaseUrl { get; init; } = "https://sandbox.api.nyse.com/v1";

    /// <summary>
    /// Sandbox WebSocket URL.
    /// </summary>
    public string SandboxWebSocketUrl { get; init; } = "wss://sandbox.stream.nyse.com/v1";

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Reconnection delay in seconds after connection loss.
    /// </summary>
    public int ReconnectDelaySeconds { get; init; } = 5;

    /// <summary>
    /// Maximum reconnection attempts before giving up.
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 5;

    /// <summary>
    /// Whether to subscribe to Level 2 (depth) data when available.
    /// Requires additional NYSE subscription.
    /// </summary>
    public bool EnableLevel2 { get; init; }

    /// <summary>
    /// Whether to subscribe to NYSE Integrated Feed (comprehensive).
    /// </summary>
    public bool EnableIntegratedFeed { get; init; } = true;

    /// <summary>
    /// Whether to include pre-market data (4:00 AM - 9:30 AM ET).
    /// </summary>
    public bool IncludePreMarket { get; init; } = true;

    /// <summary>
    /// Whether to include after-hours data (4:00 PM - 8:00 PM ET).
    /// </summary>
    public bool IncludeAfterHours { get; init; } = true;

    /// <summary>
    /// NYSE data feed tier: Basic, Enhanced, Premium, Professional.
    /// </summary>
    public NYSEFeedTier FeedTier { get; init; } = NYSEFeedTier.Basic;

    /// <summary>
    /// Maximum symbols that can be subscribed simultaneously.
    /// </summary>
    public int MaxSubscriptions { get; init; } = 500;

    /// <summary>
    /// Gets the effective base URL based on sandbox setting.
    /// </summary>
    public string EffectiveBaseUrl => UseSandbox ? SandboxBaseUrl : BaseUrl;

    /// <summary>
    /// Gets the effective WebSocket URL based on sandbox setting.
    /// </summary>
    public string EffectiveWebSocketUrl => UseSandbox ? SandboxWebSocketUrl : WebSocketUrl;

    /// <summary>
    /// Resolves API key from environment if not set directly.
    /// </summary>
    public string? ResolveApiKey()
        => ApiKey ?? Environment.GetEnvironmentVariable("NYSE_API_KEY");

    /// <summary>
    /// Resolves API secret from environment if not set directly.
    /// </summary>
    public string? ResolveApiSecret()
        => ApiSecret ?? Environment.GetEnvironmentVariable("NYSE_API_SECRET");

    /// <summary>
    /// Resolves Client ID from environment if not set directly.
    /// </summary>
    public string? ResolveClientId()
        => ClientId ?? Environment.GetEnvironmentVariable("NYSE_CLIENT_ID");
}

/// <summary>
/// NYSE data feed subscription tiers.
/// </summary>
public enum NYSEFeedTier : byte
{
    /// <summary>
    /// Basic NYSE feed - trades and BBO quotes (15-min delayed for non-pro).
    /// </summary>
    Basic,

    /// <summary>
    /// Enhanced feed - real-time trades and quotes.
    /// </summary>
    Enhanced,

    /// <summary>
    /// Premium feed - includes Level 2 depth, trade conditions.
    /// </summary>
    Premium,

    /// <summary>
    /// Professional/Full feed - full depth, tick-by-tick, all participant IDs.
    /// </summary>
    Professional
}
