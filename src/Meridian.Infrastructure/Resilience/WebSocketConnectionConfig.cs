using System.Threading;

namespace Meridian.Infrastructure.Resilience;

/// <summary>
/// Centralized configuration for WebSocket connections.
/// Provides sensible defaults used by all streaming providers (Alpaca, Polygon, etc.)
/// to eliminate duplicate configuration across providers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Profile Selection Guide:</b>
/// </para>
/// <list type="bullet">
///   <item>
///     <term><see cref="Default"/></term>
///     <description>
///       Use for providers with fast reconnection (Alpaca, Polygon).
///       5 retries with 2s base delay provides ~62s total retry time.
///       Suitable for providers with reliable infrastructure.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Resilient"/></term>
///     <description>
///       Use for providers with slower reconnection or on unreliable networks (StockSharp, IB Gateway).
///       10 retries with 3s base delay and longer circuit breaker duration.
///       Suitable when provider infrastructure is slower or network is unstable.
///     </description>
///   </item>
///   <item>
///     <term><see cref="HighFrequency"/></term>
///     <description>
///       Use for tick-by-tick data where latency is critical.
///       Shorter timeouts (15s) ensure faster failure detection.
///       Trade-off: May disconnect more frequently on slow networks.
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>Current Provider Assignments:</b>
/// </para>
/// <list type="bullet">
///   <item>Alpaca: Default (fast cloud infrastructure)</item>
///   <item>Polygon: Default (fast cloud infrastructure)</item>
///   <item>StockSharp: Resilient (broker-dependent latency)</item>
///   <item>Interactive Brokers: Resilient (TWS/Gateway may have delays)</item>
/// </list>
/// </remarks>
public sealed record WebSocketConnectionConfig
{
    /// <summary>
    /// Maximum number of connection retry attempts.
    /// </summary>
    public int MaxRetries { get; init; } = 5;

    /// <summary>
    /// Base delay for exponential backoff between retries.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum delay between retries (caps exponential backoff).
    /// </summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Number of failures before circuit breaker opens.
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; init; } = 5;

    /// <summary>
    /// Duration circuit breaker stays open before allowing retry.
    /// </summary>
    public TimeSpan CircuitBreakerDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for individual connection operations.
    /// </summary>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Interval between heartbeat pings.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout waiting for heartbeat response before considering connection stale.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum number of reconnection attempts after connection loss.
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 10;

    /// <summary>
    /// Maximum total size (bytes) of a single WebSocket message after all frames are
    /// assembled. Messages exceeding this limit are discarded and a warning is logged.
    /// Default is 10 MiB — large enough for any normal market data payload.
    /// </summary>
    public int MaxMessageSizeBytes { get; init; } = 10 * 1024 * 1024; // 10 MiB

    /// <summary>
    /// Default configuration used by all streaming providers.
    /// These values match the previously duplicated settings in Alpaca and Polygon clients.
    /// </summary>
    public static WebSocketConnectionConfig Default { get; } = new();

    /// <summary>
    /// Configuration optimized for high-frequency data (shorter timeouts).
    /// </summary>
    public static WebSocketConnectionConfig HighFrequency { get; } = new()
    {
        RetryBaseDelay = TimeSpan.FromSeconds(1),
        MaxRetryDelay = TimeSpan.FromSeconds(15),
        OperationTimeout = TimeSpan.FromSeconds(15),
        HeartbeatInterval = TimeSpan.FromSeconds(15),
        HeartbeatTimeout = TimeSpan.FromSeconds(5)
    };

    /// <summary>
    /// Configuration optimized for unreliable networks (more retries, longer timeouts).
    /// </summary>
    public static WebSocketConnectionConfig Resilient { get; } = new()
    {
        MaxRetries = 10,
        RetryBaseDelay = TimeSpan.FromSeconds(3),
        MaxRetryDelay = TimeSpan.FromSeconds(60),
        CircuitBreakerDuration = TimeSpan.FromSeconds(60),
        OperationTimeout = TimeSpan.FromSeconds(60),
        MaxReconnectAttempts = 20
    };
}
