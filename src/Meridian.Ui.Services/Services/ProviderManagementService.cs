using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for managing multi-provider configurations including failover,
/// rate limits, capabilities, and provider switching.
/// </summary>
public sealed class ProviderManagementService
{
    private static readonly Lazy<ProviderManagementService> _instance = new(() => new ProviderManagementService());
    private readonly ApiClientService _apiClient;

    public static ProviderManagementService Instance => _instance.Value;

    private ProviderManagementService()
    {
        _apiClient = ApiClientService.Instance;
    }


    /// <summary>
    /// Gets status of all configured providers.
    /// </summary>
    public async Task<AllProvidersStatusResult> GetAllProvidersStatusAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<AllProvidersStatusResponse>(
            UiApiRoutes.ProviderStatus,
            ct);

        if (response.Success && response.Data != null)
        {
            return new AllProvidersStatusResult
            {
                Success = true,
                ActiveProvider = response.Data.ActiveProvider,
                Providers = response.Data.Providers?.ToList() ?? new List<ProviderStatusInfo>()
            };
        }

        return new AllProvidersStatusResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get provider status"
        };
    }

    /// <summary>
    /// Gets detailed information about a specific provider.
    /// </summary>
    public async Task<ProviderDetailResult> GetProviderDetailAsync(string providerName, CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<ProviderDetailResponse>(
            BuildProviderDetailRoute(providerName),
            ct);

        if (response.Success && response.Data != null)
        {
            return new ProviderDetailResult
            {
                Success = true,
                Provider = response.Data
            };
        }

        return new ProviderDetailResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }



    /// <summary>
    /// Gets the current failover configuration.
    /// </summary>
    public async Task<FailoverConfigResult> GetFailoverConfigAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<FailoverConfigResponse>(
            UiApiRoutes.ProviderFailover,
            ct);

        if (response.Success && response.Data != null)
        {
            return new FailoverConfigResult
            {
                Success = true,
                Enabled = response.Data.Enabled,
                TimeoutSeconds = response.Data.TimeoutSeconds,
                MaxRetries = response.Data.MaxRetries,
                ProviderPriority = response.Data.ProviderPriority?.ToList() ?? new List<string>(),
                CurrentPrimary = response.Data.CurrentPrimary,
                FailoverHistory = response.Data.FailoverHistory?.ToList() ?? new List<FailoverEvent>()
            };
        }

        return new FailoverConfigResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    /// <summary>
    /// Updates failover configuration.
    /// </summary>
    public async Task<OperationResult> UpdateFailoverConfigAsync(
        bool enabled,
        int timeoutSeconds,
        int maxRetries,
        List<string> providerPriority,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<OperationResult>(
            UiApiRoutes.ProviderFailover,
            new
            {
                enabled,
                timeoutSeconds,
                maxRetries,
                providerPriority
            },
            ct);

        return new OperationResult
        {
            Success = response.Success,
            Message = response.Data?.Message,
            Error = response.ErrorMessage
        };
    }

    /// <summary>
    /// Manually triggers a failover to a specific provider.
    /// </summary>
    public async Task<FailoverResult> TriggerFailoverAsync(string targetProvider, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<FailoverResponse>(
            UiApiRoutes.ProviderFailoverTrigger,
            new { targetProvider },
            ct);

        if (response.Success && response.Data != null)
        {
            return new FailoverResult
            {
                Success = response.Data.Success,
                PreviousProvider = response.Data.PreviousProvider,
                NewProvider = response.Data.NewProvider,
                Message = response.Data.Message
            };
        }

        return new FailoverResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failover failed"
        };
    }

    /// <summary>
    /// Resets the failover state to use the primary provider.
    /// </summary>
    public async Task<OperationResult> ResetFailoverAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<OperationResult>(
            UiApiRoutes.ProviderFailoverReset,
            null,
            ct);

        return new OperationResult
        {
            Success = response.Success,
            Message = response.Data?.Message,
            Error = response.ErrorMessage
        };
    }



    /// <summary>
    /// Gets rate limit status for all providers.
    /// </summary>
    public async Task<RateLimitsResult> GetRateLimitsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<RateLimitsResponse>(
            UiApiRoutes.ProviderRateLimits,
            ct);

        if (response.Success && response.Data != null)
        {
            return new RateLimitsResult
            {
                Success = true,
                Providers = response.Data.Providers?.ToList() ?? new List<ProviderRateLimit>()
            };
        }

        return new RateLimitsResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    /// <summary>
    /// Gets rate limit history for a specific provider.
    /// </summary>
    public async Task<RateLimitHistoryResult> GetRateLimitHistoryAsync(
        string providerName,
        int hours = 24,
        CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<RateLimitHistoryResponse>(
            BuildProviderRateLimitHistoryRoute(providerName, hours),
            ct);

        if (response.Success && response.Data != null)
        {
            return new RateLimitHistoryResult
            {
                Success = true,
                History = response.Data.History?.ToList() ?? new List<RateLimitDataPoint>()
            };
        }

        return new RateLimitHistoryResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }



    /// <summary>
    /// Gets capabilities of all providers.
    /// </summary>
    public async Task<ProviderCapabilitiesResult> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<ProviderCapabilitiesResponse>(
            UiApiRoutes.ProviderCapabilities,
            ct);

        if (response.Success && response.Data != null)
        {
            return new ProviderCapabilitiesResult
            {
                Success = true,
                Providers = response.Data.Providers?.ToList() ?? new List<ProviderCapabilities>()
            };
        }

        return new ProviderCapabilitiesResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }



    /// <summary>
    /// Switches to a different provider.
    /// </summary>
    public async Task<SwitchProviderResult> SwitchProviderAsync(
        string providerName,
        bool saveAsDefault = false,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<SwitchProviderResponse>(
            UiApiRoutes.ProviderSwitch,
            new { provider = providerName, saveAsDefault },
            ct);

        if (response.Success && response.Data != null)
        {
            return new SwitchProviderResult
            {
                Success = response.Data.Success,
                PreviousProvider = response.Data.PreviousProvider,
                NewProvider = response.Data.NewProvider,
                Message = response.Data.Message
            };
        }

        return new SwitchProviderResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Switch failed"
        };
    }

    /// <summary>
    /// Tests connection to a specific provider.
    /// </summary>
    public async Task<ProviderManagementTestResult> TestProviderAsync(string providerName, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<ProviderTestResponse>(
            BuildProviderTestRoute(providerName),
            null,
            ct);

        if (response.Success && response.Data != null)
        {
            return new ProviderManagementTestResult
            {
                Success = response.Data.Success,
                Provider = providerName,
                LatencyMs = response.Data.LatencyMs,
                Version = response.Data.Version,
                Error = response.Data.Error
            };
        }

        return new ProviderManagementTestResult
        {
            Success = false,
            Provider = providerName,
            Error = response.ErrorMessage ?? "Test failed"
        };
    }

    /// <summary>
    /// Gets the current provider-routing connection catalog.
    /// </summary>
    public async Task<ProviderConnectionsResult> GetProviderConnectionsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<List<ProviderConnectionDto>>(
            UiApiRoutes.ProviderRoutingConnections,
            ct);

        return response.Success && response.Data is not null
            ? new ProviderConnectionsResult
            {
                Success = true,
                Connections = response.Data.ToList()
            }
            : new ProviderConnectionsResult
            {
                Success = false,
                Error = response.ErrorMessage ?? "Failed to load provider connections."
            };
    }

    /// <summary>
    /// Gets the active provider-routing bindings.
    /// </summary>
    public async Task<ProviderBindingsResult> GetProviderBindingsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<List<ProviderBindingDto>>(
            UiApiRoutes.ProviderRoutingBindings,
            ct);

        return response.Success && response.Data is not null
            ? new ProviderBindingsResult
            {
                Success = true,
                Bindings = response.Data.ToList()
            }
            : new ProviderBindingsResult
            {
                Success = false,
                Error = response.ErrorMessage ?? "Failed to load provider bindings."
            };
    }

    /// <summary>
    /// Gets the latest provider trust snapshots.
    /// </summary>
    public async Task<ProviderTrustSnapshotsResult> GetProviderTrustSnapshotsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<List<ProviderTrustSnapshotDto>>(
            UiApiRoutes.ProviderRoutingTrustSnapshots,
            ct);

        return response.Success && response.Data is not null
            ? new ProviderTrustSnapshotsResult
            {
                Success = true,
                Snapshots = response.Data.ToList()
            }
            : new ProviderTrustSnapshotsResult
            {
                Success = false,
                Error = response.ErrorMessage ?? "Failed to load provider trust snapshots."
            };
    }

    /// <summary>
    /// Previews the selected route for a provider-routing capability request.
    /// </summary>
    public async Task<ProviderRoutePreviewQueryResult> PreviewRouteAsync(RoutePreviewRequest request, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<RoutePreviewResponse>(
            UiApiRoutes.ProviderRoutingPreview,
            request,
            ct);

        return response.Success && response.Data is not null
            ? new ProviderRoutePreviewQueryResult
            {
                Success = true,
                Preview = response.Data
            }
            : new ProviderRoutePreviewQueryResult
            {
                Success = false,
                Error = response.ErrorMessage ?? "Failed to preview provider route."
            };
    }

    internal static string BuildProviderDetailRoute(string providerName)
        => UiApiRoutes.WithParam(UiApiRoutes.ProviderById, "providerName", providerName);

    internal static string BuildProviderRateLimitHistoryRoute(string providerName, int hours)
        => UiApiRoutes.WithQuery(
            UiApiRoutes.WithParam(UiApiRoutes.ProviderRateLimitHistory, "providerName", providerName),
            string.Create(CultureInfo.InvariantCulture, $"hours={hours}"));

    internal static string BuildProviderTestRoute(string providerName)
        => UiApiRoutes.WithParam(UiApiRoutes.ProviderTest, "providerName", providerName);

}


/// <summary>
/// Reports provider-status query success, the active provider, and individual provider states.
/// </summary>
public sealed class AllProvidersStatusResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ActiveProvider { get; set; }
    public List<ProviderStatusInfo> Providers { get; set; } = new();
}

/// <summary>
/// Carries API provider states and the currently active provider.
/// </summary>
public sealed class AllProvidersStatusResponse
{
    public string? ActiveProvider { get; set; }
    public List<ProviderStatusInfo>? Providers { get; set; }
}

/// <summary>
/// Describes provider configuration, connection lifecycle, recent failures, subscriptions, and event activity.
/// </summary>
public sealed class ProviderStatusInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsConnected { get; set; }
    public bool IsActive { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? LifecycleState { get; set; }
    public string? WebSocketState { get; set; }
    public bool? IsReconnecting { get; set; }
    public DateTimeOffset? LastHeartbeatReceivedAt { get; set; }
    public DateTimeOffset? LastMessageReceivedAt { get; set; }
    public DateTimeOffset? LastReconnectAttemptAt { get; set; }
    public int? ReconnectAttempts { get; set; }
    public string? LastFailureKind { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public string? LastError { get; set; }
    public double LatencyMs { get; set; }
    public int ActiveSubscriptions { get; set; }
    public long EventsReceived { get; set; }
}

/// <summary>
/// Reports provider-detail query success and the returned provider description.
/// </summary>
public sealed class ProviderDetailResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public ProviderDetailResponse? Provider { get; set; }
}

/// <summary>
/// Carries API provider metadata, enabled state, capabilities, rate limits, and statistics.
/// </summary>
public sealed class ProviderDetailResponse
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsConnected { get; set; }
    public string Version { get; set; } = string.Empty;
    public ProviderCapabilities? Capabilities { get; set; }
    public ProviderRateLimit? RateLimit { get; set; }
    public ProviderStatistics? Statistics { get; set; }
}

/// <summary>
/// Summarizes received market events, errors, latency, uptime, and reconnection count for a provider.
/// </summary>
public sealed class ProviderStatistics
{
    public long TotalEventsReceived { get; set; }
    public long TotalTradesReceived { get; set; }
    public long TotalQuotesReceived { get; set; }
    public long TotalErrors { get; set; }
    public double AverageLatencyMs { get; set; }
    public TimeSpan Uptime { get; set; }
    public int ReconnectCount { get; set; }
}

/// <summary>
/// Reports failover-query success with retry settings, provider priority, current primary, and history.
/// </summary>
public sealed class FailoverConfigResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool Enabled { get; set; }
    public int TimeoutSeconds { get; set; }
    public int MaxRetries { get; set; }
    public List<string> ProviderPriority { get; set; } = new();
    public string? CurrentPrimary { get; set; }
    public List<FailoverEvent> FailoverHistory { get; set; } = new();
}

/// <summary>
/// Carries API failover settings, provider priority, selected primary, and historical transitions.
/// </summary>
public sealed class FailoverConfigResponse
{
    public bool Enabled { get; set; }
    public int TimeoutSeconds { get; set; }
    public int MaxRetries { get; set; }
    public List<string>? ProviderPriority { get; set; }
    public string? CurrentPrimary { get; set; }
    public List<FailoverEvent>? FailoverHistory { get; set; }
}

/// <summary>
/// Describes a provider transition, its reason, timestamp, and success state.
/// </summary>
public sealed class FailoverEvent
{
    public DateTime Timestamp { get; set; }
    public string FromProvider { get; set; } = string.Empty;
    public string ToProvider { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool Success { get; set; }
}

/// <summary>
/// Reports a failover attempt's success, previous and replacement providers, and any error.
/// </summary>
public sealed class FailoverResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? PreviousProvider { get; set; }
    public string? NewProvider { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Carries API failover success, provider transition, and an explanatory message.
/// </summary>
public sealed class FailoverResponse
{
    public bool Success { get; set; }
    public string? PreviousProvider { get; set; }
    public string? NewProvider { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Reports rate-limit query success and each provider's request budgets.
/// </summary>
public sealed class RateLimitsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderRateLimit> Providers { get; set; } = new();
}

/// <summary>
/// Carries API request-budget usage and limits grouped by provider.
/// </summary>
public sealed class RateLimitsResponse
{
    public List<ProviderRateLimit>? Providers { get; set; }
}

/// <summary>
/// Describes minute and hourly request budgets, usage, reset times, and throttling state.
/// </summary>
public sealed class ProviderRateLimit
{
    public string Provider { get; set; } = string.Empty;
    public int RequestsPerMinute { get; set; }
    public int RequestsPerHour { get; set; }
    public int RequestsUsedMinute { get; set; }
    public int RequestsUsedHour { get; set; }
    public int RequestsRemainingMinute { get; set; }
    public int RequestsRemainingHour { get; set; }
    public double UsagePercentMinute { get; set; }
    public double UsagePercentHour { get; set; }
    public DateTime? ResetTimeMinute { get; set; }
    public DateTime? ResetTimeHour { get; set; }
    public bool IsThrottled { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Reports rate-limit history query success and timestamped usage observations.
/// </summary>
public sealed class RateLimitHistoryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<RateLimitDataPoint> History { get; set; } = new();
}

/// <summary>
/// Carries API historical request-budget usage observations.
/// </summary>
public sealed class RateLimitHistoryResponse
{
    public List<RateLimitDataPoint>? History { get; set; }
}

/// <summary>
/// Records request usage, usage percentage, and throttling at a point in time.
/// </summary>
public sealed class RateLimitDataPoint
{
    public DateTime Timestamp { get; set; }
    public int RequestsUsed { get; set; }
    public double UsagePercent { get; set; }
    public bool WasThrottled { get; set; }
}

/// <summary>
/// Reports capability-query success and the capabilities advertised by providers.
/// </summary>
public sealed class ProviderCapabilitiesResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderCapabilities> Providers { get; set; } = new();
}

/// <summary>
/// Carries API capabilities advertised by each provider.
/// </summary>
public sealed class ProviderCapabilitiesResponse
{
    public List<ProviderCapabilities>? Providers { get; set; }
}

/// <summary>
/// Describes supported market-data types, asset classes, venues, intervals, and subscription limits.
/// </summary>
public sealed class ProviderCapabilities
{
    public string Provider { get; set; } = string.Empty;
    public bool SupportsRealTime { get; set; }
    public bool SupportsHistorical { get; set; }
    public bool SupportsTrades { get; set; }
    public bool SupportsQuotes { get; set; }
    public bool SupportsDepth { get; set; }
    public bool SupportsBars { get; set; }
    public bool SupportsOptions { get; set; }
    public bool SupportsCrypto { get; set; }
    public bool SupportsForex { get; set; }
    public List<string> SupportedExchanges { get; set; } = new();
    public List<string> SupportedBarIntervals { get; set; } = new();
    public int MaxSymbolsPerSubscription { get; set; }
    public int MaxDepthLevels { get; set; }
}

/// <summary>
/// Reports a provider-switch attempt's success, transition, message, and any error.
/// </summary>
public sealed class SwitchProviderResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? PreviousProvider { get; set; }
    public string? NewProvider { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Carries API provider-switch success, previous and new providers, and an explanatory message.
/// </summary>
public sealed class SwitchProviderResponse
{
    public bool Success { get; set; }
    public string? PreviousProvider { get; set; }
    public string? NewProvider { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Reports a named provider test's connectivity, latency, version, and error.
/// </summary>
public sealed class ProviderManagementTestResult
{
    public bool Success { get; set; }
    public string Provider { get; set; } = string.Empty;
    public double LatencyMs { get; set; }
    public string? Version { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Carries API provider-test success, measured latency, version, and error.
/// </summary>
public sealed class ProviderTestResponse
{
    public bool Success { get; set; }
    public double LatencyMs { get; set; }
    public string? Version { get; set; }
    public string? Error { get; set; }
}
