using System.Text.Json.Serialization;

namespace Meridian.Application.Config;

/// <summary>
/// Root configuration model loaded from appsettings.json.
/// </summary>
/// <param name="DataRoot">Output directory root for storage sinks.</param>
/// <param name="Compress">Whether JSONL sinks should gzip. Null means use base configuration/default.</param>
/// <param name="DataSource">
/// Market data provider selector:
/// - <see cref="DataSourceKind.IB"/> uses Interactive Brokers via IMarketDataClient/IBMarketDataClient.
/// - <see cref="DataSourceKind.Alpaca"/> uses Alpaca market data via WebSocket (trades; quotes optional in future).
/// - <see cref="DataSourceKind.StockSharp"/> uses StockSharp connectors (Rithmic, IQFeed, CQG, IB, etc.).
/// - <see cref="DataSourceKind.NYSE"/> uses the NYSE market data feed.
/// - <see cref="DataSourceKind.Synthetic"/> uses the built-in synthetic historical/reference dataset for offline development.
/// </param>
/// <param name="Alpaca">Alpaca provider options (required if DataSource == DataSourceKind.Alpaca).</param>
/// <param name="IB">Interactive Brokers provider options (required if DataSource == DataSourceKind.IB).</param>
/// <param name="Polygon">Polygon provider options (required if DataSource == DataSourceKind.Polygon).</param>
/// <param name="StockSharp">StockSharp connector configuration (required if DataSource == DataSourceKind.StockSharp).</param>
/// <param name="Synthetic">Synthetic market-data provider configuration for offline/backtest development.</param>
/// <param name="Storage">Storage configuration options (naming convention, partitioning, etc.).</param>
/// <param name="Symbols">Symbol subscriptions.</param>
/// <param name="Backfill">Optional historical backfill defaults.</param>
/// <param name="Sources">Source registry persistence path.</param>
/// <param name="DataSources">Multiple data source configurations for real-time and historical data.</param>
/// <param name="ProviderConnections">Relationship-aware provider connections, bindings, policies, presets, and certifications.</param>
/// <param name="Derivatives">Derivatives (options) data collection configuration.</param>
/// <param name="ProviderRegistry">Unified provider registry configuration controlling attribute-based discovery.</param>
/// <param name="Coordination">Multi-instance coordination configuration.</param>
/// <param name="Canonicalization">Canonicalization configuration for condition codes and venue MICs.</param>
/// <param name="Validation">Configuration for the F# validation pipeline stage.</param>
/// <param name="OfflineFirstMode">When true, enables air-gapped offline-first mode: backfill requests are queued and deferred until connectivity is restored. Default is false.</param>
/// <param name="PluginsPath">Optional directory path for loading external data source plugins. When set, plugins are loaded and registered dynamically.</param>
/// <param name="CoLocationProfile">When true, activates exchange colocation profile: low-latency GC settings and network tuning. Default is false.</param>
public sealed record AppConfig(
    string DataRoot = "data",
    bool? Compress = null,
    [property: JsonConverter(typeof(DataSourceKindConverter))] DataSourceKind DataSource = DataSourceKind.Synthetic,
    AlpacaOptions? Alpaca = null,
    IBOptions? IB = null,
    PolygonOptions? Polygon = null,
    StockSharpConfig? StockSharp = null,
    SyntheticMarketDataConfig? Synthetic = null,
    StorageConfig? Storage = null,
    SymbolConfig[]? Symbols = null,
    BackfillConfig? Backfill = null,
    SourceRegistryConfig? Sources = null,
    DataSourcesConfig? DataSources = null,
    ProviderConnectionsConfig? ProviderConnections = null,
    DerivativesConfig? Derivatives = null,
    ProviderRegistryConfig? ProviderRegistry = null,
    CoordinationConfig? Coordination = null,
    CanonicalizationConfig? Canonicalization = null,
    ValidationPipelineConfig? Validation = null,
    bool OfflineFirstMode = false,
    string? PluginsPath = null,
    bool CoLocationProfile = false
);

/// <summary>
/// Configuration for the unified provider registry (Phase 1.2).
/// Controls how streaming, backfill, and symbol search providers are discovered and registered.
/// </summary>
/// <param name="UseAttributeDiscovery">
/// When true, <c>DataSourceAttribute</c>-decorated types are discovered via reflection
/// and automatically registered as streaming factories in the <c>ProviderRegistry</c>,
/// replacing manual lambda registration. Default is false (manual registration).
/// </param>
public sealed record ProviderRegistryConfig(
    bool UseAttributeDiscovery = false
);

/// <summary>
/// Storage configuration for file naming and organization.
/// Conversion to StorageOptions is available via extension methods in the Application layer.
/// </summary>
public sealed record StorageConfig(
    // <summary>
    // File naming convention: Flat, BySymbol, ByDate, ByType.
    // </summary>
    string NamingConvention = "BySymbol",

    // <summary>
    // Date partitioning: None, Daily, Hourly, Monthly.
    // </summary>
    string DatePartition = "Daily",

    // <summary>
    // Whether to include provider name in file path.
    // </summary>
    bool IncludeProvider = false,

    // <summary>
    // Optional file name prefix.
    // </summary>
    string? FilePrefix = null,

    // <summary>
    // Optional storage profile preset (Research, LowLatency, Archival).
    // </summary>
    string? Profile = null,

    // <summary>
    // Optional retention window (days). Files older than this are deleted during writes.
    // </summary>
    int? RetentionDays = null,

    // <summary>
    // Optional cap on total bytes (across all files). Oldest files are removed first when exceeded.
    // Value is expressed in megabytes for readability.
    // </summary>
    long? MaxTotalMegabytes = null,

    // <summary>
    // Whether to enable Parquet storage as an additional sink alongside JSONL.
    // When enabled, events are written to both JSONL and Parquet via CompositeSink.
    // Superseded by Sinks when that list is non-empty.
    // </summary>
    bool EnableParquetSink = false,

    // <summary>
    // Explicit list of storage sink plugin IDs to activate (e.g., ["jsonl", "parquet"]).
    // When non-empty, overrides EnableParquetSink and drives dynamic sink composition.
    // </summary>
    List<string>? Sinks = null
);

/// <summary>
/// Source registry configuration - only PersistencePath is used.
/// </summary>
public sealed record SourceRegistryConfig(
    string? PersistencePath = null
);

/// <summary>
/// Configuration for the F# validation pipeline stage.
/// When enabled, every incoming <see cref="Meridian.Domain.Events.MarketEvent"/>
/// is validated against the F# Railway-Oriented validators before it is persisted.
/// Events that fail validation are written to the dead-letter sink instead of primary storage.
/// </summary>
/// <param name="Enabled">
/// When <see langword="true"/>, the F# validation stage is activated.
/// Defaults to <see langword="false"/> to preserve backward-compatible behaviour.
/// </param>
/// <param name="UseRealTimeMode">
/// When <see langword="true"/>, stricter real-time configuration is applied:
/// timestamp max-age drops from 5 minutes to 5 seconds, and sequence numbers
/// are checked for continuity. Disable for historical backfill or replay scenarios.
/// </param>
public sealed record ValidationPipelineConfig(
    bool Enabled = false,
    bool UseRealTimeMode = false
);
