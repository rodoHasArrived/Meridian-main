namespace Meridian.Application.Config;

/// <summary>
/// Configuration for an individual data source (real-time or historical).
/// Allows users to configure multiple data sources with different providers and settings.
/// </summary>
public sealed record DataSourceConfig(
    // <summary>
    // Unique identifier for this data source configuration.
    // </summary>
    string Id,

    // <summary>
    // Display name for this data source.
    // </summary>
    string Name,

    // <summary>
    // The provider type for this data source.
    // </summary>
    DataSourceKind Provider = DataSourceKind.IB,

    // <summary>
    // Whether this data source is enabled for collection.
    // </summary>
    bool Enabled = true,

    // <summary>
    // Type of data: RealTime, Historical, or Both.
    // </summary>
    DataSourceType Type = DataSourceType.RealTime,

    // <summary>
    // Priority for data source selection (lower = higher priority).
    // Used when multiple sources provide the same data.
    // </summary>
    int Priority = 100,

    // <summary>
    // Alpaca-specific options (if Provider == Alpaca).
    // </summary>
    AlpacaOptions? Alpaca = null,

    // <summary>
    // Polygon-specific options (if Provider == Polygon).
    // </summary>
    PolygonOptions? Polygon = null,

    // <summary>
    // Interactive Brokers-specific options (if Provider == IB).
    // </summary>
    IBOptions? IB = null,

    // <summary>
    // Symbols to subscribe from this data source.
    // If null, uses the global symbol list.
    // </summary>
    string[]? Symbols = null,

    // <summary>
    // Description for this data source configuration.
    // </summary>
    string? Description = null,

    // <summary>
    // Tags for categorizing data sources.
    // </summary>
    string[]? Tags = null
);

/// <summary>
/// Type of data source.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DataSourceType>))]
public enum DataSourceType : byte
{
    /// <summary>
    /// Real-time streaming data (trades, quotes, market depth).
    /// </summary>
    RealTime = 0,

    /// <summary>
    /// Historical data (daily bars, minute bars, etc.).
    /// </summary>
    Historical = 1,

    /// <summary>
    /// Both real-time and historical data.
    /// </summary>
    Both = 2
}

/// <summary>
/// Polygon.io API configuration options.
/// </summary>
public sealed record PolygonOptions(
    // <summary>
    // Polygon API key.
    // </summary>
    string? ApiKey = null,

    // <summary>
    // Whether to use delayed data (15 minutes).
    // </summary>
    bool UseDelayed = false,

    // <summary>
    // Feed type: stocks, options, forex, crypto.
    // </summary>
    string Feed = "stocks",

    // <summary>
    // Subscribe to trades.
    // </summary>
    bool SubscribeTrades = true,

    // <summary>
    // Subscribe to quotes.
    // </summary>
    bool SubscribeQuotes = false,

    // <summary>
    // Subscribe to aggregates (per-minute bars).
    // </summary>
    bool SubscribeAggregates = false
);

/// <summary>
/// Interactive Brokers connection options.
/// </summary>
public sealed record IBOptions(
    // <summary>
    // TWS/Gateway host address.
    // </summary>
    string Host = "127.0.0.1",

    // <summary>
    // TWS/Gateway port (7496 for live, 7497 for paper).
    // </summary>
    int Port = 7496,

    // <summary>
    // Client ID for the IB connection.
    // </summary>
    int ClientId = 0,

    // <summary>
    // Whether to use paper trading account.
    // </summary>
    bool UsePaperTrading = false,

    // <summary>
    // Subscribe to Level 2 market depth.
    // </summary>
    bool SubscribeDepth = true,

    // <summary>
    // Number of depth levels to request.
    // </summary>
    int DepthLevels = 10,

    // <summary>
    // Whether to request tick-by-tick data.
    // </summary>
    bool TickByTick = true
);

/// <summary>
/// Collection of data source configurations.
/// </summary>
public sealed record DataSourcesConfig(
    // <summary>
    // List of configured data sources.
    // </summary>
    DataSourceConfig[]? Sources = null,

    // <summary>
    // Default data source ID for real-time data.
    // </summary>
    string? DefaultRealTimeSourceId = null,

    // <summary>
    // Default data source ID for historical data.
    // </summary>
    string? DefaultHistoricalSourceId = null,

    // <summary>
    // Whether to enable automatic failover between sources.
    // </summary>
    bool EnableFailover = true,

    // <summary>
    // Timeout in seconds before failover to next source.
    // </summary>
    int FailoverTimeoutSeconds = 30,

    // <summary>
    // Health check interval in seconds.
    // </summary>
    int HealthCheckIntervalSeconds = 10,

    // <summary>
    // Whether to automatically recover to primary when it becomes healthy.
    // </summary>
    bool AutoRecover = true,

    // <summary>
    // Configured failover rules.
    // </summary>
    FailoverRuleConfig[]? FailoverRules = null,

    // <summary>
    // Symbol mappings configuration.
    // </summary>
    SymbolMappingsConfig? SymbolMappings = null
);

/// <summary>
/// Configuration for a failover rule.
/// </summary>
public sealed record FailoverRuleConfig(
    // <summary>
    // Unique identifier for this rule.
    // </summary>
    string Id,

    // <summary>
    // The primary provider ID.
    // </summary>
    string PrimaryProviderId,

    // <summary>
    // Ordered list of backup provider IDs.
    // </summary>
    string[] BackupProviderIds,

    // <summary>
    // Number of consecutive failures before triggering failover.
    // </summary>
    int FailoverThreshold = 3,

    // <summary>
    // Number of consecutive successes required for recovery.
    // </summary>
    int RecoveryThreshold = 5,

    // <summary>
    // Minimum data quality score (0-100). 0 = disabled.
    // </summary>
    double DataQualityThreshold = 0,

    // <summary>
    // Maximum acceptable latency in ms. 0 = disabled.
    // </summary>
    double MaxLatencyMs = 0
);

/// <summary>
/// Configuration for symbol mappings.
/// </summary>
public sealed record SymbolMappingsConfig(
    // <summary>
    // Path to persist symbol mappings.
    // </summary>
    string? PersistencePath = null,

    // <summary>
    // List of symbol mappings.
    // </summary>
    SymbolMappingConfig[]? Mappings = null
);

/// <summary>
/// Configuration for a single symbol mapping.
/// </summary>
public sealed record SymbolMappingConfig(
    // <summary>
    // The canonical (normalized) symbol used internally.
    // </summary>
    string CanonicalSymbol,

    // <summary>
    // Symbol used by Interactive Brokers.
    // </summary>
    string? IbSymbol = null,

    // <summary>
    // Symbol used by Alpaca.
    // </summary>
    string? AlpacaSymbol = null,

    // <summary>
    // Symbol used by Polygon.
    // </summary>
    string? PolygonSymbol = null,

    // <summary>
    // Symbol used by Yahoo Finance.
    // </summary>
    string? YahooSymbol = null,

    // <summary>
    // Security name.
    // </summary>
    string? Name = null,

    // <summary>
    // Optional FIGI identifier.
    // </summary>
    string? Figi = null
);
