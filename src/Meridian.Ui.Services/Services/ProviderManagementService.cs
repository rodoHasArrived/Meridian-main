using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

    #region Provider Status

    /// <summary>
    /// Gets status of all configured providers.
    /// </summary>
    public async Task<AllProvidersStatusResult> GetAllProvidersStatusAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<AllProvidersStatusResponse>(
            "/api/providers/status",
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
            $"/api/providers/{Uri.EscapeDataString(providerName)}",
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

    #endregion

    #region Failover Management

    /// <summary>
    /// Gets the current failover configuration.
    /// </summary>
    public async Task<FailoverConfigResult> GetFailoverConfigAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<FailoverConfigResponse>(
            "/api/providers/failover",
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
            "/api/providers/failover",
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
            "/api/providers/failover/trigger",
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
            "/api/providers/failover/reset",
            null,
            ct);

        return new OperationResult
        {
            Success = response.Success,
            Message = response.Data?.Message,
            Error = response.ErrorMessage
        };
    }

    #endregion

    #region Rate Limits

    /// <summary>
    /// Gets rate limit status for all providers.
    /// </summary>
    public async Task<RateLimitsResult> GetRateLimitsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<RateLimitsResponse>(
            "/api/providers/rate-limits",
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
            $"/api/providers/{Uri.EscapeDataString(providerName)}/rate-limit-history?hours={hours}",
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

    #endregion

    #region Capabilities

    /// <summary>
    /// Gets capabilities of all providers.
    /// </summary>
    public async Task<ProviderCapabilitiesResult> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<ProviderCapabilitiesResponse>(
            "/api/providers/capabilities",
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

    #endregion

    #region Provider Switching

    /// <summary>
    /// Switches to a different provider.
    /// </summary>
    public async Task<SwitchProviderResult> SwitchProviderAsync(
        string providerName,
        bool saveAsDefault = false,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<SwitchProviderResponse>(
            "/api/providers/switch",
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
            $"/api/providers/{Uri.EscapeDataString(providerName)}/test",
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

    #endregion
}

#region Result Classes

public sealed class AllProvidersStatusResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ActiveProvider { get; set; }
    public List<ProviderStatusInfo> Providers { get; set; } = new();
}

public sealed class AllProvidersStatusResponse
{
    public string? ActiveProvider { get; set; }
    public List<ProviderStatusInfo>? Providers { get; set; }
}

public sealed class ProviderStatusInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsConnected { get; set; }
    public bool IsActive { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? LastConnectedAt { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public string? LastError { get; set; }
    public double LatencyMs { get; set; }
    public int ActiveSubscriptions { get; set; }
    public long EventsReceived { get; set; }
}

public sealed class ProviderDetailResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public ProviderDetailResponse? Provider { get; set; }
}

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

public sealed class FailoverConfigResponse
{
    public bool Enabled { get; set; }
    public int TimeoutSeconds { get; set; }
    public int MaxRetries { get; set; }
    public List<string>? ProviderPriority { get; set; }
    public string? CurrentPrimary { get; set; }
    public List<FailoverEvent>? FailoverHistory { get; set; }
}

public sealed class FailoverEvent
{
    public DateTime Timestamp { get; set; }
    public string FromProvider { get; set; } = string.Empty;
    public string ToProvider { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool Success { get; set; }
}

public sealed class FailoverResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? PreviousProvider { get; set; }
    public string? NewProvider { get; set; }
    public string? Message { get; set; }
}

public sealed class FailoverResponse
{
    public bool Success { get; set; }
    public string? PreviousProvider { get; set; }
    public string? NewProvider { get; set; }
    public string? Message { get; set; }
}

public sealed class RateLimitsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderRateLimit> Providers { get; set; } = new();
}

public sealed class RateLimitsResponse
{
    public List<ProviderRateLimit>? Providers { get; set; }
}

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

public sealed class RateLimitHistoryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<RateLimitDataPoint> History { get; set; } = new();
}

public sealed class RateLimitHistoryResponse
{
    public List<RateLimitDataPoint>? History { get; set; }
}

public sealed class RateLimitDataPoint
{
    public DateTime Timestamp { get; set; }
    public int RequestsUsed { get; set; }
    public double UsagePercent { get; set; }
    public bool WasThrottled { get; set; }
}

public sealed class ProviderCapabilitiesResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderCapabilities> Providers { get; set; } = new();
}

public sealed class ProviderCapabilitiesResponse
{
    public List<ProviderCapabilities>? Providers { get; set; }
}

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

public sealed class SwitchProviderResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? PreviousProvider { get; set; }
    public string? NewProvider { get; set; }
    public string? Message { get; set; }
}

public sealed class SwitchProviderResponse
{
    public bool Success { get; set; }
    public string? PreviousProvider { get; set; }
    public string? NewProvider { get; set; }
    public string? Message { get; set; }
}

public sealed class ProviderManagementTestResult
{
    public bool Success { get; set; }
    public string Provider { get; set; } = string.Empty;
    public double LatencyMs { get; set; }
    public string? Version { get; set; }
    public string? Error { get; set; }
}

public sealed class ProviderTestResponse
{
    public bool Success { get; set; }
    public double LatencyMs { get; set; }
    public string? Version { get; set; }
    public string? Error { get; set; }
}

#endregion
