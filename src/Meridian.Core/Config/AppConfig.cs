using System.Text.Json.Serialization;

namespace Meridian.Core.Config;

/// <summary>
/// Root configuration model loaded from appsettings.json.
/// </summary>
/// <param name="DataRoot">Output directory root for storage sinks.</param>
/// <param name="Compress">Whether JSONL sinks should gzip. Null means use base configuration/default.</param>
/// <param name="DataSource">
/// Market data provider selector:
/// - <see cref="DataSourceKind.IB"/> uses Interactive Brokers via IMarketDataClient/IBMarketDataClient.
/// - <see cref="DataSourceKind.Alpaca"/> uses Alpaca market data via WebSocket (trades; quotes optional in future).
/// - <see cref="DataSourceKind.Yahoo"/> uses Yahoo Finance historical/backfill data.
/// - <see cref="DataSourceKind.NYSE"/> uses the NYSE market data feed.
/// - <see cref="DataSourceKind.Synthetic"/> uses the built-in synthetic historical/reference dataset for offline development.
/// </param>
/// <param name="Alpaca">Alpaca provider options (required if DataSource == DataSourceKind.Alpaca).</param>
/// <param name="IB">Interactive Brokers provider options (required if DataSource == DataSourceKind.IB).</param>
/// <param name="IBClientPortal">Interactive Brokers Client Portal HTTP settings for portfolio/account import.</param>
/// <param name="Polygon">Polygon provider options (required if DataSource == DataSourceKind.Polygon).</param>
/// <param name="Synthetic">Synthetic market-data provider configuration for offline/backtest development.</param>
/// <param name="Storage">Storage configuration options (naming convention, partitioning, etc.).</param>
/// <param name="Symbols">Symbol subscriptions.</param>
/// <param name="Backfill">Optional historical backfill defaults.</param>
/// <param name="Sources">Source registry persistence path.</param>
/// <param name="DataSources">Multiple data source configurations for real-time and historical data.</param>
/// <param name="Derivatives">Derivatives (options) data collection configuration.</param>
/// <param name="ProviderRegistry">Unified provider registry configuration controlling attribute-based discovery.</param>
/// <param name="Coordination">Multi-instance coordination configuration.</param>
/// <param name="Canonicalization">Canonicalization configuration for condition codes and venue MICs.</param>
/// <param name="Validation">Configuration for the F# validation pipeline stage.</param>
/// <param name="OfflineFirstMode">When true, enables air-gapped offline-first mode: backfill requests are queued and deferred until connectivity is restored. Default is false.</param>
/// <param name="PluginsPath">Optional directory path for loading external data source plugins. When set, plugins are loaded and registered dynamically.</param>
/// <param name="CoLocationProfile">When true, activates exchange colocation profile: low-latency GC settings and network tuning. Default is false.</param>
/// <param name="ProviderConnections">Relationship-aware provider operations configuration (connections, bindings, policies).</param>
/// <param name="FeatureCapabilities">Runtime feature capability overrides.</param>
/// <param name="ProviderModules">
/// Declares an arbitrary set of provider modules by family ID, each with credentials
/// (as environment variable names) and operational settings. When present, the Application
/// layer resolves credentials from the referenced environment variables and injects them
/// into each module before registration. Modules not listed here fall back to their
/// built-in environment variable detection.
/// </param>
public sealed record AppConfig(
    string DataRoot = "data",
    bool? Compress = null,
    [property: JsonConverter(typeof(DataSourceKindConverter))] DataSourceKind DataSource = DataSourceKind.Synthetic,
    AlpacaOptions? Alpaca = null,
    IBOptions? IB = null,
    IBClientPortalOptions? IBClientPortal = null,
    PolygonOptions? Polygon = null,
    SyntheticMarketDataConfig? Synthetic = null,
    StorageConfig? Storage = null,
    SymbolConfig[]? Symbols = null,
    BackfillConfig? Backfill = null,
    SourceRegistryConfig? Sources = null,
    DataSourcesConfig? DataSources = null,
    DerivativesConfig? Derivatives = null,
    ProviderRegistryConfig? ProviderRegistry = null,
    CoordinationConfig? Coordination = null,
    CanonicalizationConfig? Canonicalization = null,
    ValidationPipelineConfig? Validation = null,
    bool OfflineFirstMode = false,
    string? PluginsPath = null,
    bool CoLocationProfile = false,
    ProviderConnectionsConfig? ProviderConnections = null,
    FeatureCapabilityOptions? FeatureCapabilities = null,
    FundOperationsPersistenceConfig? FundOperationsPersistence = null,
    ProviderModulesConfig? ProviderModules = null,
    PipelineRuntimeConfig? Pipeline = null
)
{
    /// <summary>
    /// Preserves top-level configuration sections this model does not declare (host-level
    /// sections such as <c>ApiHost</c>, <c>PaperTrading</c>, <c>Status</c>, and
    /// <c>Connectivity</c>) across load/save round-trips, so config mutation paths
    /// (data-source/storage/symbol saves) do not silently drop operator-tuned host settings.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalSections { get; set; }
}

/// <summary>
/// Runtime tuning for the event pipeline's flush behaviour. All fields are optional; unset values
/// fall back to the pipeline's built-in defaults (final flush 30s, periodic sink flush 60s), so
/// existing configurations keep their current behaviour. Non-positive values are ignored (treated
/// as unset) by the pipeline registration.
/// </summary>
/// <param name="FinalFlushTimeoutSeconds">
/// Timeout, in seconds, for the final flush during pipeline shutdown before giving up.
/// </param>
/// <param name="SinkFlushTimeoutSeconds">
/// Per-call timeout, in seconds, for periodic sink flushes, preventing a hung sink from stalling
/// the pipeline indefinitely.
/// </param>
public sealed record PipelineRuntimeConfig(
    int? FinalFlushTimeoutSeconds = null,
    int? SinkFlushTimeoutSeconds = null
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

/// <summary>
/// Configuration for per-domain fund operations persistence cutover.
/// Controls shadow write and read mode toggles for each fund operations domain.
/// </summary>
/// <param name="DomainModes">
/// Per-domain cutover modes keyed by domain name (e.g. "FundStructure", "FundAccounts").
/// When null or empty, all domains default to shadow writes enabled with legacy in-memory reads.
/// </param>
public sealed record FundOperationsPersistenceConfig(
    Dictionary<string, DomainCutoverModeConfig>? DomainModes = null
);

/// <summary>
/// Serializable cutover mode for a single fund operations domain.
/// </summary>
/// <param name="ShadowWritesEnabled">Whether shadow projection writes are enabled for this domain.</param>
/// <param name="ReadMode">Read mode: "LegacyInMemory" (default) or "PersistedProjection".</param>
public sealed record DomainCutoverModeConfig(
    bool ShadowWritesEnabled = true,
    string ReadMode = "LegacyInMemory"
);
