using System.Collections.Immutable;
using System.Threading;

namespace Meridian.Infrastructure.DataSources;

/// <summary>
/// Unified base interface for all data sources (real-time and historical).
/// Provides a common abstraction for provider discovery, health monitoring,
/// and lifecycle management.
/// </summary>
public interface IDataSource : IAsyncDisposable
{

    /// <summary>
    /// Unique identifier for this data source (e.g., "alpaca", "yahoo", "ib").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Description of the data source and its capabilities.
    /// </summary>
    string Description { get; }



    /// <summary>
    /// Type of data provided: Realtime, Historical, or Hybrid.
    /// </summary>
    DataSourceType Type { get; }

    /// <summary>
    /// Category of the data source: Exchange, Broker, Aggregator, Free, Premium.
    /// </summary>
    DataSourceCategory Category { get; }

    /// <summary>
    /// Priority for source selection (lower = higher priority, tried first).
    /// </summary>
    int Priority { get; }



    /// <summary>
    /// Bitwise capabilities supported by this source.
    /// </summary>
    DataSourceCapabilities Capabilities { get; }

    /// <summary>
    /// Detailed capability information including limits and supported features.
    /// </summary>
    DataSourceCapabilityInfo CapabilityInfo { get; }

    /// <summary>
    /// Market regions/countries supported (e.g., "US", "UK", "DE").
    /// </summary>
    IReadOnlySet<string> SupportedMarkets { get; }

    /// <summary>
    /// Asset classes supported by this source.
    /// </summary>
    IReadOnlySet<AssetClass> SupportedAssetClasses { get; }



    /// <summary>
    /// Current health status including score and recent errors.
    /// </summary>
    DataSourceHealth Health { get; }

    /// <summary>
    /// Current connection/availability status.
    /// </summary>
    DataSourceStatus Status { get; }

    /// <summary>
    /// Current rate limit state for this source.
    /// </summary>
    RateLimitState RateLimitState { get; }

    /// <summary>
    /// Observable stream of health changes for this source.
    /// </summary>
    IObservable<DataSourceHealthChanged> HealthChanges { get; }



    /// <summary>
    /// Initializes the data source, validates credentials, and tests connectivity.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Validates that credentials are properly configured.
    /// Returns true if no credentials are required or if they are valid.
    /// </summary>
    Task<bool> ValidateCredentialsAsync(CancellationToken ct = default);

    /// <summary>
    /// Tests connectivity to the data source.
    /// </summary>
    Task<bool> TestConnectivityAsync(CancellationToken ct = default);

}

/// <summary>
/// Type of data provided by a data source.
/// </summary>
public enum DataSourceType : byte
{
    /// <summary>Real-time streaming data (trades, quotes, market depth).</summary>
    Realtime = 0,

    /// <summary>Historical data (daily bars, minute bars, etc.).</summary>
    Historical = 1,

    /// <summary>Both real-time and historical data.</summary>
    Hybrid = 2
}

/// <summary>
/// Category of the data source indicating its business model and reliability.
/// </summary>
public enum DataSourceCategory : byte
{
    /// <summary>Direct exchange feed (highest reliability, lowest latency).</summary>
    Exchange = 0,

    /// <summary>Broker-provided data (may have subscription requirements).</summary>
    Broker = 1,

    /// <summary>Data aggregator service (e.g., Polygon, Refinitiv).</summary>
    Aggregator = 2,

    /// <summary>Free data source (may have rate limits, no SLA).</summary>
    Free = 3,

    /// <summary>Premium paid data source (higher quality, SLA).</summary>
    Premium = 4
}

/// <summary>
/// Asset classes supported by a data source.
/// </summary>
public enum AssetClass : byte
{
    Equity,
    Option,
    Future,
    Forex,
    Crypto,
    Index,
    ETF,
    Bond,
    MutualFund,
    Commodity
}

/// <summary>
/// Bitwise capabilities supported by a data source.
/// </summary>
[Flags]
public enum DataSourceCapabilities : long
{
    None = 0,


    /// <summary>Real-time trade prints.</summary>
    RealtimeTrades = 1L << 0,

    /// <summary>Real-time BBO quotes.</summary>
    RealtimeQuotes = 1L << 1,

    /// <summary>Level 1 market depth.</summary>
    RealtimeDepthL1 = 1L << 2,

    /// <summary>Level 2 market depth.</summary>
    RealtimeDepthL2 = 1L << 3,

    /// <summary>Level 3 market depth (full order book).</summary>
    RealtimeDepthL3 = 1L << 4,

    /// <summary>Real-time aggregate bars.</summary>
    RealtimeAggregateBars = 1L << 5,



    /// <summary>Historical daily bars.</summary>
    HistoricalDailyBars = 1L << 10,

    /// <summary>Historical intraday bars (minute, hourly).</summary>
    HistoricalIntradayBars = 1L << 11,

    /// <summary>Historical tick data.</summary>
    HistoricalTicks = 1L << 12,

    /// <summary>Split/dividend adjusted prices.</summary>
    HistoricalAdjustedPrices = 1L << 13,

    /// <summary>Historical dividend data.</summary>
    HistoricalDividends = 1L << 14,

    /// <summary>Historical split data.</summary>
    HistoricalSplits = 1L << 15,

    /// <summary>Historical corporate actions.</summary>
    HistoricalCorporateActions = 1L << 16,

    /// <summary>Historical earnings data.</summary>
    HistoricalEarnings = 1L << 17,



    /// <summary>Supports historical data backfill.</summary>
    SupportsBackfill = 1L << 20,

    /// <summary>Supports real-time streaming (push).</summary>
    SupportsStreaming = 1L << 21,

    /// <summary>Supports polling (pull).</summary>
    SupportsPolling = 1L << 22,

    /// <summary>Supports WebSocket connections.</summary>
    SupportsWebSocket = 1L << 23,

    /// <summary>Supports batch/bulk requests.</summary>
    SupportsBatchRequests = 1L << 24,

    /// <summary>Supports symbol search/lookup.</summary>
    SupportsSymbolSearch = 1L << 25,

    /// <summary>Supports multiple simultaneous subscriptions.</summary>
    SupportsMultiSubscription = 1L << 26,



    /// <summary>Provides exchange-level timestamps.</summary>
    ExchangeTimestamps = 1L << 30,

    /// <summary>Provides sequence numbers for ordering.</summary>
    SequenceNumbers = 1L << 31,

    /// <summary>Provides trade condition codes.</summary>
    TradeConditions = 1L << 32,

    /// <summary>Provides market maker/participant IDs.</summary>
    ParticipantIds = 1L << 33,

    /// <summary>Provides consolidated tape.</summary>
    ConsolidatedTape = 1L << 34,

}

/// <summary>
/// Detailed capability information including limits and supported features.
/// </summary>
public sealed record DataSourceCapabilityInfo(
    DataSourceCapabilities Capabilities,
    DateOnly? MinHistoricalDate = null,
    TimeSpan? MaxHistoricalLookback = null,
    int? MaxSymbolsPerSubscription = null,
    int? MaxDepthLevels = null,
    TimeSpan? MinBarResolution = null,
    IReadOnlyList<string>? SupportedBarIntervals = null,
    int? MaxRequestsPerMinute = null,
    int? MaxRequestsPerHour = null,
    int? MaxRequestsPerDay = null
)
{
    /// <summary>
    /// Creates a default capability info with the given capabilities.
    /// </summary>
    public static DataSourceCapabilityInfo Default(DataSourceCapabilities capabilities)
        => new(capabilities);
}

/// <summary>
/// Current operational status of a data source.
/// </summary>
public enum DataSourceStatus : byte
{
    /// <summary>Not initialized.</summary>
    Uninitialized,

    /// <summary>Initializing.</summary>
    Initializing,

    /// <summary>Connected and operational.</summary>
    Connected,

    /// <summary>Disconnected but can reconnect.</summary>
    Disconnected,

    /// <summary>Reconnecting after connection loss.</summary>
    Reconnecting,

    /// <summary>Configuration or credential error.</summary>
    ConfigurationError,

    /// <summary>Rate limited, temporarily unavailable.</summary>
    RateLimited,

    /// <summary>Service unavailable.</summary>
    Unavailable,

    /// <summary>Disabled by configuration.</summary>
    Disabled
}

/// <summary>
/// Health status of a data source.
/// </summary>
public sealed record DataSourceHealth(
    bool IsHealthy,
    double Score,
    string? Message = null,
    DateTimeOffset LastChecked = default,
    TimeSpan? LastResponseTime = null,
    int ConsecutiveFailures = 0,
    IReadOnlyList<DataSourceError>? RecentErrors = null
)
{
    /// <summary>
    /// Creates a healthy status with a 100% score.
    /// </summary>
    public static DataSourceHealth Healthy(TimeSpan? responseTime = null)
        => new(true, 100.0, LastChecked: DateTimeOffset.UtcNow, LastResponseTime: responseTime);

    /// <summary>
    /// Creates a degraded health status.
    /// </summary>
    public static DataSourceHealth Degraded(double score, string message, int failures = 0)
        => new(score >= 50, score, message, DateTimeOffset.UtcNow, ConsecutiveFailures: failures);

    /// <summary>
    /// Creates an unhealthy status.
    /// </summary>
    public static DataSourceHealth Unhealthy(string message, int failures = 0)
        => new(false, 0.0, message, DateTimeOffset.UtcNow, ConsecutiveFailures: failures);
}

/// <summary>
/// Record of a data source error for diagnostic purposes.
/// </summary>
public sealed record DataSourceError(
    string Operation,
    string Message,
    DateTimeOffset OccurredAt,
    string? ExceptionType = null,
    string? StackTrace = null
);

/// <summary>
/// Event raised when a data source's health changes.
/// </summary>
public sealed record DataSourceHealthChanged(
    string SourceId,
    DataSourceHealth PreviousHealth,
    DataSourceHealth CurrentHealth,
    DateTimeOffset ChangedAt
)
{
    public bool BecameHealthy => CurrentHealth.IsHealthy && !PreviousHealth.IsHealthy;
    public bool BecameUnhealthy => !CurrentHealth.IsHealthy && PreviousHealth.IsHealthy;
}

/// <summary>
/// Current rate limit state for a data source.
/// </summary>
public sealed record RateLimitState(
    bool CanMakeRequest,
    int RemainingRequests,
    int MaxRequests,
    TimeSpan? ResetIn = null,
    DateTimeOffset? ResetAt = null
)
{
    /// <summary>Available with unlimited requests.</summary>
    public static RateLimitState Available => new(true, int.MaxValue, int.MaxValue);

    /// <summary>Throttled, cannot make requests.</summary>
    public static RateLimitState Throttled(TimeSpan resetIn)
        => new(false, 0, 0, resetIn, DateTimeOffset.UtcNow.Add(resetIn));

    /// <summary>Available with limited remaining requests.</summary>
    public static RateLimitState Limited(int remaining, int max, TimeSpan? resetIn = null)
        => new(remaining > 0, remaining, max, resetIn,
            resetIn.HasValue ? DateTimeOffset.UtcNow.Add(resetIn.Value) : null);
}
