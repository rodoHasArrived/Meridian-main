using System.Text.Json.Serialization;

namespace Meridian.Contracts.Api;

/// <summary>
/// Typed response returned by <c>GET /api/providers/rate-limits</c>.
/// </summary>
public sealed record ProviderRateLimitsResponse(
    [property: JsonPropertyName("providers")] IReadOnlyList<ProviderRateLimitSnapshotDto> Providers,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp);

/// <summary>
/// Minimal typed projection of <c>GET /api/providers/health</c> used by operator clients.
/// A null connection value means no runtime diagnostic probe exists; it must not be treated as false.
/// </summary>
public sealed record ProviderConnectionHealthResponse(
    [property: JsonPropertyName("providers")] IReadOnlyList<ProviderConnectionHealthSnapshotDto> Providers,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp);

public sealed record ProviderConnectionHealthSnapshotDto(
    [property: JsonPropertyName("providerId")] string ProviderId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("isEnabled")] bool IsEnabled,
    [property: JsonPropertyName("isConnected")] bool? IsConnected,
    [property: JsonPropertyName("connectionState")] string ConnectionState,
    [property: JsonPropertyName("diagnosticsAvailable")] bool DiagnosticsAvailable,
    [property: JsonPropertyName("lastFailureKind")] string? LastFailureKind,
    [property: JsonPropertyName("reconnectAttempts")] int? ReconnectAttempts = null);

/// <summary>
/// Provider rate-limit configuration plus an optional live runtime snapshot.
/// Existing identity and capability fields are retained for wire compatibility.
/// </summary>
public sealed record ProviderRateLimitSnapshotDto(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("priority")] int Priority,
    [property: JsonPropertyName("capabilities")] ProviderRateLimitCapabilitiesDto Capabilities,
    [property: JsonPropertyName("surface")] string Surface,
    [property: JsonPropertyName("stateAvailable")] bool StateAvailable,
    [property: JsonPropertyName("observedAt")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("requestsInWindow")] int? RequestsInWindow,
    [property: JsonPropertyName("maxRequestsPerWindow")] int MaxRequestsPerWindow,
    [property: JsonPropertyName("remainingRequests")] int? RemainingRequests,
    [property: JsonPropertyName("windowSeconds")] double WindowSeconds,
    [property: JsonPropertyName("usageRatio")] double? UsageRatio,
    [property: JsonPropertyName("isRateLimited")] bool IsRateLimited,
    [property: JsonPropertyName("resetAt")] DateTimeOffset? ResetAt,
    [property: JsonPropertyName("reason")] string? Reason)
{
    /// <summary>Unit of the compatibility request-named counters.</summary>
    [JsonPropertyName("quotaUnit")]
    public string QuotaUnit { get; init; } = "requests";

    [JsonPropertyName("dailyUnitsUsed")]
    public int? DailyUnitsUsed { get; init; }

    [JsonPropertyName("dailyMaximum")]
    public int? DailyMaximum { get; init; }

    [JsonPropertyName("dailyResetsAt")]
    public DateTimeOffset? DailyResetsAt { get; init; }

    [JsonPropertyName("dailyResetPolicy")]
    public string? DailyResetPolicy { get; init; }

    [JsonPropertyName("isThrottled")]
    public bool IsThrottled => IsRateLimited;

    [JsonPropertyName("status")]
    public string Status => !StateAvailable ? "unavailable" : IsRateLimited ? "rate-limited" : "available";
}

/// <summary>
/// Historical-data capabilities preserved from the previous rate-limit endpoint shape.
/// </summary>
public sealed record ProviderRateLimitCapabilitiesDto(
    [property: JsonPropertyName("adjustedPrices")] bool AdjustedPrices,
    [property: JsonPropertyName("intraday")] bool Intraday,
    [property: JsonPropertyName("dividends")] bool Dividends,
    [property: JsonPropertyName("splits")] bool Splits,
    [property: JsonPropertyName("quotes")] bool Quotes,
    [property: JsonPropertyName("trades")] bool Trades,
    [property: JsonPropertyName("auctions")] bool Auctions,
    [property: JsonPropertyName("supportedMarkets")] IReadOnlyList<string> SupportedMarkets);

/// <summary>
/// Bounded process-lifetime rate-limit history; older events may have been evicted.
/// </summary>
public sealed record ProviderRateLimitHistoryResponse(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("periodHours")] int PeriodHours,
    [property: JsonPropertyName("history")] IReadOnlyList<ProviderRateLimitEventDto> History,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("isAvailable")] bool IsAvailable,
    [property: JsonPropertyName("message")] string Message);

/// <summary>
/// A rate-limit observation or subsequent provider rotation.
/// </summary>
public sealed record ProviderRateLimitEventDto(
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("requestsUsed")] int RequestsUsed,
    [property: JsonPropertyName("usageRatio")] double UsageRatio,
    [property: JsonPropertyName("wasRateLimited")] bool WasRateLimited,
    [property: JsonPropertyName("resetAt")] DateTimeOffset? ResetAt,
    [property: JsonPropertyName("eventType")] string EventType = "rate-limited",
    [property: JsonPropertyName("nextProvider")] string? NextProvider = null,
    [property: JsonPropertyName("httpStatus")] int? HttpStatus = null);
