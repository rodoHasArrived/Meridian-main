using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.Configuration;

/// <summary>
/// Application configuration DTO shared between core and desktop applications.
/// </summary>
public sealed class AppConfigDto
{
    [JsonPropertyName("dataRoot")]
    public string DataRoot { get; set; } = "data";

    [JsonPropertyName("compress")]
    public bool Compress { get; set; }

    [JsonPropertyName("dataSource")]
    public string DataSource { get; set; } = "IB";

    [JsonPropertyName("alpaca")]
    public AlpacaOptionsDto? Alpaca { get; set; }

    [JsonPropertyName("ib")]
    public IBOptionsDto? IB { get; set; }

    [JsonPropertyName("ibClientPortal")]
    public IBClientPortalOptionsDto? IBClientPortal { get; set; }

    [JsonPropertyName("polygon")]
    public PolygonOptionsDto? Polygon { get; set; }

    [JsonPropertyName("storage")]
    public StorageConfigDto? Storage { get; set; }

    [JsonPropertyName("symbols")]
    public SymbolConfigDto[]? Symbols { get; set; }

    [JsonPropertyName("backfill")]
    public BackfillConfigDto? Backfill { get; set; }

    [JsonPropertyName("dataSources")]
    public DataSourcesConfigDto? DataSources { get; set; }

    [JsonPropertyName("symbolGroups")]
    public SymbolGroupsConfigDto? SymbolGroups { get; set; }

    [JsonPropertyName("settings")]
    public AppSettingsDto? Settings { get; set; }

    [JsonPropertyName("derivatives")]
    public DerivativesConfigDto? Derivatives { get; set; }
}

/// <summary>
/// Alpaca provider configuration.
/// </summary>
public sealed class AlpacaOptionsDto
{
    [JsonPropertyName("keyId")]
    public string? KeyId { get; set; }

    [JsonPropertyName("secretKey")]
    public string? SecretKey { get; set; }

    [JsonPropertyName("feed")]
    public string Feed { get; set; } = "iex";

    [JsonPropertyName("useSandbox")]
    public bool UseSandbox { get; set; }

    [JsonPropertyName("subscribeQuotes")]
    public bool SubscribeQuotes { get; set; }
}

/// <summary>
/// Storage configuration.
/// </summary>
public sealed class StorageConfigDto
{
    [JsonPropertyName("namingConvention")]
    public string NamingConvention { get; set; } = "BySymbol";

    [JsonPropertyName("datePartition")]
    public string DatePartition { get; set; } = "Daily";

    [JsonPropertyName("includeProvider")]
    public bool IncludeProvider { get; set; }

    [JsonPropertyName("filePrefix")]
    public string? FilePrefix { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("retentionDays")]
    public int? RetentionDays { get; set; }

    [JsonPropertyName("maxTotalMegabytes")]
    public long? MaxTotalMegabytes { get; set; }

    /// <summary>
    /// Explicit list of storage sink plugin IDs to activate (e.g., ["jsonl", "parquet"]).
    /// When non-empty, overrides <c>EnableParquetSink</c> and drives dynamic sink composition.
    /// </summary>
    [JsonPropertyName("sinks")]
    public List<string>? Sinks { get; set; }
}

/// <summary>
/// Symbol subscription configuration.
/// </summary>
public class SymbolConfigDto
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("subscribeTrades")]
    public bool SubscribeTrades { get; set; } = true;

    [JsonPropertyName("subscribeDepth")]
    public bool SubscribeDepth { get; set; }

    [JsonPropertyName("depthLevels")]
    public int DepthLevels { get; set; } = 10;

    [JsonPropertyName("securityType")]
    public string SecurityType { get; set; } = "STK";

    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = "SMART";

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    [JsonPropertyName("primaryExchange")]
    public string? PrimaryExchange { get; set; }

    [JsonPropertyName("localSymbol")]
    public string? LocalSymbol { get; set; }

    [JsonPropertyName("instrumentType")]
    public string? InstrumentType { get; set; }

    [JsonPropertyName("strike")]
    public decimal? Strike { get; set; }

    [JsonPropertyName("right")]
    public string? Right { get; set; }

    [JsonPropertyName("lastTradeDateOrContractMonth")]
    public string? LastTradeDateOrContractMonth { get; set; }

    [JsonPropertyName("optionStyle")]
    public string? OptionStyle { get; set; }

    [JsonPropertyName("multiplier")]
    public int? Multiplier { get; set; }

    [JsonPropertyName("underlyingSymbol")]
    public string? UnderlyingSymbol { get; set; }
}

/// <summary>
/// Backfill configuration.
/// </summary>
public sealed class BackfillConfigDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "stooq";

    [JsonPropertyName("symbols")]
    public string[]? Symbols { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("enableFallback")]
    public bool EnableFallback { get; set; } = true;

    [JsonPropertyName("enableSymbolResolution")]
    public bool EnableSymbolResolution { get; set; } = true;

    /// <summary>
    /// Optional per-provider backfill settings used by desktop configuration workflows.
    /// </summary>
    [JsonPropertyName("providers")]
    public BackfillProvidersConfigDto? Providers { get; set; }
}

/// <summary>
/// Backfill provider configuration container.
/// </summary>
public sealed class BackfillProvidersConfigDto
{
    [JsonPropertyName("alpaca")]
    public BackfillProviderOptionsDto? Alpaca { get; set; }

    [JsonPropertyName("polygon")]
    public BackfillProviderOptionsDto? Polygon { get; set; }

    [JsonPropertyName("tiingo")]
    public BackfillProviderOptionsDto? Tiingo { get; set; }

    [JsonPropertyName("finnhub")]
    public BackfillProviderOptionsDto? Finnhub { get; set; }

    [JsonPropertyName("stooq")]
    public BackfillProviderOptionsDto? Stooq { get; set; }

    [JsonPropertyName("yahoo")]
    public BackfillProviderOptionsDto? Yahoo { get; set; }

    [JsonPropertyName("alphaVantage")]
    public BackfillProviderOptionsDto? AlphaVantage { get; set; }

    [JsonPropertyName("nasdaqDataLink")]
    public BackfillProviderOptionsDto? NasdaqDataLink { get; set; }
}

/// <summary>
/// Generic backfill provider runtime options.
/// </summary>
public sealed class BackfillProviderOptionsDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [JsonPropertyName("rateLimitPerMinute")]
    public int? RateLimitPerMinute { get; set; }

    [JsonPropertyName("rateLimitPerHour")]
    public int? RateLimitPerHour { get; set; }

    /// <summary>
    /// Extension bag for provider-specific options not covered by the common fields.
    /// Keeps common fields typed while allowing provider-unique settings.
    /// </summary>
    [JsonPropertyName("extensions")]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>
/// Provider metadata descriptor used for dynamic UI generation and runtime transparency.
/// Drives the desktop provider settings panel without requiring custom pages per provider.
/// </summary>
public sealed class BackfillProviderMetadataDto
{
    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("dataTypes")]
    public string[] DataTypes { get; set; } = [];

    [JsonPropertyName("requiresApiKey")]
    public bool RequiresApiKey { get; set; }

    [JsonPropertyName("hasCredentials")]
    public bool HasCredentials { get; set; }

    [JsonPropertyName("freeTier")]
    public bool FreeTier { get; set; }

    [JsonPropertyName("defaultPriority")]
    public int DefaultPriority { get; set; }

    [JsonPropertyName("defaultRateLimitPerMinute")]
    public int? DefaultRateLimitPerMinute { get; set; }

    [JsonPropertyName("defaultRateLimitPerHour")]
    public int? DefaultRateLimitPerHour { get; set; }

    [JsonPropertyName("configSource")]
    public string ConfigSource { get; set; } = "default";

    [JsonPropertyName("supportedGranularities")]
    public string[] SupportedGranularities { get; set; } = [];

    [JsonPropertyName("featureFlags")]
    public Dictionary<string, bool>? FeatureFlags { get; set; }
}

/// <summary>
/// Combined view of provider configuration with runtime health status for the desktop UI.
/// </summary>
public sealed class BackfillProviderStatusDto
{
    [JsonPropertyName("metadata")]
    public BackfillProviderMetadataDto Metadata { get; set; } = new();

    [JsonPropertyName("options")]
    public BackfillProviderOptionsDto Options { get; set; } = new();

    [JsonPropertyName("healthStatus")]
    public string HealthStatus { get; set; } = "unknown";

    [JsonPropertyName("lastUsed")]
    public DateTime? LastUsed { get; set; }

    [JsonPropertyName("requestsUsedMinute")]
    public int RequestsUsedMinute { get; set; }

    [JsonPropertyName("requestsUsedHour")]
    public int RequestsUsedHour { get; set; }

    [JsonPropertyName("isThrottled")]
    public bool IsThrottled { get; set; }

    [JsonPropertyName("effectiveConfigSource")]
    public string EffectiveConfigSource { get; set; } = "default";
}

/// <summary>
/// Dry-run backfill plan showing which providers would be selected per symbol.
/// </summary>
public sealed class BackfillDryRunPlanDto
{
    [JsonPropertyName("symbols")]
    public BackfillSymbolPlanDto[] Symbols { get; set; } = [];

    [JsonPropertyName("warnings")]
    public string[] Warnings { get; set; } = [];

    [JsonPropertyName("validationErrors")]
    public string[] ValidationErrors { get; set; } = [];
}

/// <summary>
/// Per-symbol plan entry showing the provider fallback sequence.
/// </summary>
public sealed class BackfillSymbolPlanDto
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("providerSequence")]
    public string[] ProviderSequence { get; set; } = [];

    [JsonPropertyName("selectedProvider")]
    public string? SelectedProvider { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Audit trail entry for provider configuration changes.
/// </summary>
public sealed class ProviderConfigAuditEntryDto
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("previousValue")]
    public string? PreviousValue { get; set; }

    [JsonPropertyName("newValue")]
    public string? NewValue { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "desktop";
}

/// <summary>
/// Multiple data source configuration.
/// </summary>
public sealed class DataSourcesConfigDto
{
    [JsonPropertyName("sources")]
    public DataSourceConfigDto[]? Sources { get; set; }

    [JsonPropertyName("defaultRealTimeSourceId")]
    public string? DefaultRealTimeSourceId { get; set; }

    [JsonPropertyName("defaultHistoricalSourceId")]
    public string? DefaultHistoricalSourceId { get; set; }

    [JsonPropertyName("enableFailover")]
    public bool EnableFailover { get; set; } = true;

    [JsonPropertyName("failoverTimeoutSeconds")]
    public int FailoverTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Individual data source configuration.
/// </summary>
public sealed class DataSourceConfigDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "IB";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "RealTime";

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 100;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("alpaca")]
    public AlpacaOptionsDto? Alpaca { get; set; }

    [JsonPropertyName("polygon")]
    public PolygonOptionsDto? Polygon { get; set; }

    [JsonPropertyName("ib")]
    public IBOptionsDto? IB { get; set; }

    [JsonPropertyName("symbols")]
    public string[]? Symbols { get; set; }
}

/// <summary>
/// Polygon.io API configuration options.
/// </summary>
public sealed class PolygonOptionsDto
{
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("useDelayed")]
    public bool UseDelayed { get; set; }

    [JsonPropertyName("feed")]
    public string Feed { get; set; } = "stocks";

    [JsonPropertyName("subscribeTrades")]
    public bool SubscribeTrades { get; set; } = true;

    [JsonPropertyName("subscribeQuotes")]
    public bool SubscribeQuotes { get; set; }

    [JsonPropertyName("subscribeAggregates")]
    public bool SubscribeAggregates { get; set; }
}

/// <summary>
/// Interactive Brokers connection options.
/// </summary>
public sealed class IBOptionsDto
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 7497;

    [JsonPropertyName("clientId")]
    public int ClientId { get; set; } = 1;

    [JsonPropertyName("usePaperTrading")]
    public bool UsePaperTrading { get; set; } = true;

    [JsonPropertyName("subscribeDepth")]
    public bool SubscribeDepth { get; set; } = true;

    [JsonPropertyName("depthLevels")]
    public int DepthLevels { get; set; } = 10;

    [JsonPropertyName("tickByTick")]
    public bool TickByTick { get; set; } = true;
}

/// <summary>
/// Interactive Brokers Client Portal HTTP configuration.
/// </summary>
public sealed class IBClientPortalOptionsDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = "https://localhost:5000";

    [JsonPropertyName("allowSelfSignedCertificates")]
    public bool AllowSelfSignedCertificates { get; set; } = true;
}

/// <summary>
/// Symbol groups configuration.
/// </summary>
public sealed class SymbolGroupsConfigDto
{
    [JsonPropertyName("groups")]
    public SymbolGroupDto[]? Groups { get; set; }

    [JsonPropertyName("defaultGroupId")]
    public string? DefaultGroupId { get; set; }

    [JsonPropertyName("showUngroupedSymbols")]
    public bool ShowUngroupedSymbols { get; set; } = true;

    [JsonPropertyName("sortBy")]
    public string SortBy { get; set; } = "Name";

    [JsonPropertyName("viewMode")]
    public string ViewMode { get; set; } = "Tree";
}

/// <summary>
/// Symbol group definition.
/// </summary>
public sealed class SymbolGroupDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#0078D4";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "\uE8D2";

    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    [JsonPropertyName("isExpanded")]
    public bool IsExpanded { get; set; } = true;

    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("smartCriteria")]
    public SmartGroupCriteriaDto? SmartCriteria { get; set; }
}

/// <summary>
/// Criteria for smart/dynamic symbol groups.
/// </summary>
public sealed class SmartGroupCriteriaDto
{
    [JsonPropertyName("isSmartGroup")]
    public bool IsSmartGroup { get; set; }

    [JsonPropertyName("sectorFilter")]
    public string? SectorFilter { get; set; }

    [JsonPropertyName("industryFilter")]
    public string? IndustryFilter { get; set; }

    [JsonPropertyName("exchangeFilter")]
    public string? ExchangeFilter { get; set; }

    [JsonPropertyName("minPrice")]
    public decimal? MinPrice { get; set; }

    [JsonPropertyName("maxPrice")]
    public decimal? MaxPrice { get; set; }

    [JsonPropertyName("tagsFilter")]
    public string[]? TagsFilter { get; set; }
}

/// <summary>
/// Extended symbol configuration with group membership and status.
/// </summary>
public sealed class ExtendedSymbolConfigDto : SymbolConfigDto
{
    [JsonPropertyName("groupIds")]
    public string[]? GroupIds { get; set; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("addedAt")]
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; set; }

    [JsonPropertyName("customColor")]
    public string? CustomColor { get; set; }
}

/// <summary>
/// Application UI settings.
/// </summary>
public sealed class AppSettingsDto
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "System";

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "System";

    [JsonPropertyName("compactMode")]
    public bool CompactMode { get; set; }

    [JsonPropertyName("notificationsEnabled")]
    public bool NotificationsEnabled { get; set; } = true;

    [JsonPropertyName("autoReconnectEnabled")]
    public bool AutoReconnectEnabled { get; set; } = true;

    [JsonPropertyName("maxReconnectAttempts")]
    public int MaxReconnectAttempts { get; set; } = 10;

    [JsonPropertyName("statusRefreshIntervalSeconds")]
    public int StatusRefreshIntervalSeconds { get; set; } = 2;
}
