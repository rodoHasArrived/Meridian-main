using System.Text.Json.Serialization;
using Meridian.Contracts.Configuration;

namespace Meridian.Contracts.Api;

// ============================================================
// Request DTOs
// ============================================================

/// <summary>Request to update the active data source.</summary>
public record DataSourceRequest(string DataSource);

/// <summary>Request to update storage settings.</summary>
public record StorageSettingsRequest(
    string? DataRoot,
    bool Compress,
    string? NamingConvention,
    string? DatePartition,
    bool IncludeProvider,
    string? FilePrefix,
    string? Profile = null);

/// <summary>Request to create or update a data source configuration.</summary>
public record DataSourceConfigRequest(
    string? Id,
    string Name,
    string Provider = "IB",
    bool Enabled = true,
    string Type = "RealTime",
    int Priority = 100,
    AlpacaOptionsDto? Alpaca = null,
    PolygonOptionsDto? Polygon = null,
    IBOptionsDto? IB = null,
    string[]? Symbols = null,
    string? Description = null,
    string[]? Tags = null);

/// <summary>Request to toggle enabled status.</summary>
public record ToggleRequest(bool Enabled);

/// <summary>Request to set default data sources.</summary>
public record DefaultSourcesRequest(string? DefaultRealTimeSourceId, string? DefaultHistoricalSourceId);

/// <summary>Request to update failover settings.</summary>
public record FailoverSettingsRequest(bool EnableFailover, int FailoverTimeoutSeconds);

/// <summary>Request to update full failover configuration.</summary>
public record FailoverConfigRequest(
    bool EnableFailover,
    int HealthCheckIntervalSeconds = 10,
    bool AutoRecover = true,
    int FailoverTimeoutSeconds = 30);

/// <summary>Request to create or update a failover rule.</summary>
public record FailoverRuleRequest(
    string? Id,
    string PrimaryProviderId,
    string[] BackupProviderIds,
    int FailoverThreshold = 3,
    int RecoveryThreshold = 5,
    double DataQualityThreshold = 0,
    double MaxLatencyMs = 0);

/// <summary>Request to force a failover to a specific provider.</summary>
public record ForceFailoverRequest(string TargetProviderId);

/// <summary>Request to run a backfill operation.</summary>
public record BackfillRequestDto(string? Provider, string[] Symbols, DateOnly? From, DateOnly? To, string? Granularity = null);

/// <summary>Request to generate a dry-run backfill plan.</summary>
public record DryRunPlanRequest(string[] Symbols);

/// <summary>Request to create or update a symbol mapping.</summary>
public record SymbolMappingRequest(
    string CanonicalSymbol,
    string? IbSymbol = null,
    string? AlpacaSymbol = null,
    string? PolygonSymbol = null,
    string? YahooSymbol = null,
    string? Name = null,
    string? Figi = null);

// ============================================================
// Response DTOs
// ============================================================

/// <summary>Response containing provider comparison data.</summary>
public record ProviderComparisonResponse(
    DateTimeOffset Timestamp,
    ProviderMetricsResponse[] Providers,
    int TotalProviders,
    int HealthyProviders);

/// <summary>Response containing provider connection status.</summary>
public record ProviderStatusResponse(
    string ProviderId,
    string Name,
    string ProviderType,
    bool IsConnected,
    bool IsEnabled,
    int Priority,
    int ActiveSubscriptions,
    DateTimeOffset? LastHeartbeat);

/// <summary>Response containing detailed provider metrics.</summary>
public record ProviderMetricsResponse(
    string ProviderId,
    string ProviderType,
    long TradesReceived,
    long DepthUpdatesReceived,
    long QuotesReceived,
    long ConnectionAttempts,
    long ConnectionFailures,
    long MessagesDropped,
    long ActiveSubscriptions,
    double AverageLatencyMs,
    double MinLatencyMs,
    double MaxLatencyMs,
    double DataQualityScore,
    double ConnectionSuccessRate,
    DateTimeOffset Timestamp,
    bool IsSimulated = false);

/// <summary>Response containing failover configuration.</summary>
public record FailoverConfigResponse(
    bool EnableFailover,
    int HealthCheckIntervalSeconds,
    bool AutoRecover,
    int FailoverTimeoutSeconds,
    FailoverRuleResponse[] Rules);

/// <summary>Response containing a failover rule.</summary>
public record FailoverRuleResponse(
    string Id,
    string PrimaryProviderId,
    string[] BackupProviderIds,
    int FailoverThreshold,
    int RecoveryThreshold,
    double DataQualityThreshold,
    double MaxLatencyMs,
    bool IsInFailoverState,
    string? CurrentActiveProviderId);

/// <summary>Response containing provider health information.</summary>
public record ProviderHealthResponse(
    string ProviderId,
    int ConsecutiveFailures,
    int ConsecutiveSuccesses,
    DateTimeOffset? LastIssueTime,
    DateTimeOffset? LastSuccessTime,
    HealthIssueResponse[] RecentIssues);

/// <summary>Response containing a health issue.</summary>
public record HealthIssueResponse(
    string Type,
    string? Message,
    DateTimeOffset Timestamp);

/// <summary>Response containing a symbol mapping.</summary>
public record SymbolMappingResponse(
    string CanonicalSymbol,
    string? IbSymbol,
    string? AlpacaSymbol,
    string? PolygonSymbol,
    string? YahooSymbol,
    string? Name,
    string? Figi);

/// <summary>Response for setting storage profiles.</summary>
public sealed record StorageProfileResponse(
    [property: JsonPropertyName("profile")] string Profile,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("description")] string Description);

// ============================================================
// Standardized Provider Template Output
// ============================================================

/// <summary>
/// Unified provider template output for standardized UI consumption.
/// Combines provider catalog metadata with runtime status into a single
/// consistent structure that both Web and desktop can consume without
/// provider-specific conditionals.
/// </summary>
public sealed class ProviderTemplateOutput
{
    // --- Identity ---
    [JsonPropertyName("providerId")]
    public string ProviderId { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("providerType")]
    public string ProviderType { get; init; } = "backfill";

    // --- Status (runtime) ---
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; init; }

    [JsonPropertyName("isConnected")]
    public bool IsConnected { get; init; }

    [JsonPropertyName("isHealthy")]
    public bool IsHealthy { get; init; } = true;

    // --- Configuration ---
    [JsonPropertyName("requiresCredentials")]
    public bool RequiresCredentials { get; init; }

    [JsonPropertyName("hasValidCredentials")]
    public bool HasValidCredentials { get; init; }

    [JsonPropertyName("credentialFields")]
    public CredentialFieldOutput[] CredentialFields { get; init; } = Array.Empty<CredentialFieldOutput>();

    // --- Capabilities (standardized) ---
    [JsonPropertyName("capabilities")]
    public ProviderCapabilityOutput Capabilities { get; init; } = new();

    // --- Notes and Warnings (standardized template output) ---
    [JsonPropertyName("notes")]
    public string[] Notes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("warnings")]
    public string[] Warnings { get; init; } = Array.Empty<string>();

    // --- Rate Limits ---
    [JsonPropertyName("rateLimit")]
    public ProviderRateLimitOutput? RateLimit { get; init; }

    // --- Markets and Data Types ---
    [JsonPropertyName("supportedMarkets")]
    public string[] SupportedMarkets { get; init; } = new[] { "US" };

    [JsonPropertyName("dataTypes")]
    public string[] DataTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Creates a ProviderTemplateOutput from a ProviderCatalogEntry.
    /// </summary>
    public static ProviderTemplateOutput FromCatalogEntry(ProviderCatalogEntry entry)
    {
        return new ProviderTemplateOutput
        {
            ProviderId = entry.ProviderId,
            DisplayName = entry.DisplayName,
            Description = entry.Description,
            ProviderType = entry.ProviderType.ToString().ToLowerInvariant(),
            RequiresCredentials = entry.RequiresCredentials,
            CredentialFields = entry.CredentialFields
                .Select(f => new CredentialFieldOutput
                {
                    Name = f.Name,
                    DisplayName = f.DisplayName,
                    EnvironmentVariable = f.EnvironmentVariable,
                    Required = f.Required,
                    DefaultValue = f.DefaultValue
                }).ToArray(),
            Capabilities = new ProviderCapabilityOutput
            {
                SupportsStreaming = entry.Capabilities.SupportsStreaming,
                SupportsMarketDepth = entry.Capabilities.SupportsMarketDepth,
                MaxDepthLevels = entry.Capabilities.MaxDepthLevels,
                SupportsAdjustedPrices = entry.Capabilities.SupportsAdjustedPrices,
                SupportsDividends = entry.Capabilities.SupportsDividends,
                SupportsSplits = entry.Capabilities.SupportsSplits,
                SupportsIntraday = entry.Capabilities.SupportsIntraday,
                SupportsTrades = entry.Capabilities.SupportsTrades,
                SupportsQuotes = entry.Capabilities.SupportsQuotes,
                SupportsAuctions = entry.Capabilities.SupportsAuctions
            },
            Notes = entry.Notes,
            Warnings = entry.Warnings,
            RateLimit = entry.RateLimit != null
                ? new ProviderRateLimitOutput
                {
                    MaxRequestsPerWindow = entry.RateLimit.MaxRequestsPerWindow,
                    WindowSeconds = entry.RateLimit.WindowSeconds,
                    MinDelayMs = entry.RateLimit.MinDelayMs,
                    Description = entry.RateLimit.Description
                }
                : null,
            SupportedMarkets = entry.SupportedMarkets,
            DataTypes = entry.DataTypes
        };
    }
}

/// <summary>
/// Credential field output for UI form generation.
/// </summary>
public sealed class CredentialFieldOutput
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("environmentVariable")]
    public string? EnvironmentVariable { get; init; }

    [JsonPropertyName("required")]
    public bool Required { get; init; }

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; init; }

    [JsonPropertyName("hasValue")]
    public bool HasValue { get; init; }
}

/// <summary>
/// Standardized capability output for UI consumption.
/// </summary>
public sealed class ProviderCapabilityOutput
{
    [JsonPropertyName("supportsStreaming")]
    public bool SupportsStreaming { get; init; }

    [JsonPropertyName("supportsMarketDepth")]
    public bool SupportsMarketDepth { get; init; }

    [JsonPropertyName("maxDepthLevels")]
    public int? MaxDepthLevels { get; init; }

    [JsonPropertyName("supportsAdjustedPrices")]
    public bool SupportsAdjustedPrices { get; init; }

    [JsonPropertyName("supportsDividends")]
    public bool SupportsDividends { get; init; }

    [JsonPropertyName("supportsSplits")]
    public bool SupportsSplits { get; init; }

    [JsonPropertyName("supportsIntraday")]
    public bool SupportsIntraday { get; init; }

    [JsonPropertyName("supportsTrades")]
    public bool SupportsTrades { get; init; }

    [JsonPropertyName("supportsQuotes")]
    public bool SupportsQuotes { get; init; }

    [JsonPropertyName("supportsAuctions")]
    public bool SupportsAuctions { get; init; }
}

/// <summary>
/// Rate limit output for UI display.
/// </summary>
public sealed class ProviderRateLimitOutput
{
    [JsonPropertyName("maxRequestsPerWindow")]
    public int MaxRequestsPerWindow { get; init; }

    [JsonPropertyName("windowSeconds")]
    public int WindowSeconds { get; init; }

    [JsonPropertyName("minDelayMs")]
    public int MinDelayMs { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}
