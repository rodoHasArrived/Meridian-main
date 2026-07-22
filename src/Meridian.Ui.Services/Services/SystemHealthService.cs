using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for monitoring system health, connection diagnostics, and anomaly detection.
/// </summary>
public sealed class SystemHealthService
{
    private static readonly Lazy<SystemHealthService> _instance = new(() => new SystemHealthService());
    private readonly ISystemHealthApiClient _apiClient;

    public static SystemHealthService Instance => _instance.Value;

    private SystemHealthService()
        : this(new ApiClientSystemHealthApiClient(ApiClientService.Instance))
    {
    }

    internal SystemHealthService(ISystemHealthApiClient apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    /// <summary>
    /// Gets overall system health summary.
    /// </summary>
    public async Task<SystemHealthSummary?> GetHealthSummaryAsync(CancellationToken ct = default)
    {
        return (await _apiClient.GetWithResponseAsync<SystemHealthSummary>(UiApiRoutes.HealthSummary, ct).ConfigureAwait(false)).DataOrLoggedNull("Get system health summary");
    }

    /// <summary>
    /// Gets connection health for all providers.
    /// </summary>
    public async Task<List<ProviderHealth>?> GetProviderHealthAsync(CancellationToken ct = default)
    {
        return (await _apiClient.GetWithResponseAsync<List<ProviderHealth>>(UiApiRoutes.HealthProviders, ct).ConfigureAwait(false)).DataOrLoggedNull("Get provider health");
    }

    /// <summary>
    /// Gets the shared provider readiness command-center model.
    /// </summary>
    public async Task<ProviderReadinessSummaryDto?> GetProviderReadinessAsync(CancellationToken ct = default)
    {
        return (await _apiClient.GetWithResponseAsync<ProviderReadinessSummaryDto>(UiApiRoutes.ProviderReadiness, ct).ConfigureAwait(false)).DataOrLoggedNull("Get provider readiness");
    }

    /// <summary>
    /// Gets detailed diagnostics for a specific provider.
    /// </summary>
    public async Task<ProviderDiagnosticsSnapshot?> GetProviderDiagnosticsAsync(string provider, CancellationToken ct = default)
    {
        return (await _apiClient.GetWithResponseAsync<ProviderDiagnosticsSnapshot>(BuildProviderDiagnosticsRoute(provider), ct).ConfigureAwait(false)).DataOrLoggedNull("Get provider diagnostics");
    }

    /// <summary>
    /// Gets storage health status.
    /// </summary>
    public async Task<StorageHealth?> GetStorageHealthAsync(CancellationToken ct = default)
    {
        return (await _apiClient.GetWithResponseAsync<StorageHealth>(UiApiRoutes.HealthStorage, ct).ConfigureAwait(false)).DataOrLoggedNull("Get storage health");
    }

    /// <summary>
    /// Gets recent system events and errors.
    /// </summary>
    public async Task<List<SystemEvent>?> GetRecentEventsAsync(int limit = 50, CancellationToken ct = default)
    {
        return (await _apiClient.GetWithResponseAsync<List<SystemEvent>>(BuildRecentEventsRoute(limit), ct).ConfigureAwait(false)).DataOrLoggedNull("Get recent system events");
    }

    /// <summary>
    /// Gets system resource metrics.
    /// </summary>
    public async Task<SystemMetrics?> GetSystemMetricsAsync(CancellationToken ct = default)
    {
        return (await _apiClient.GetWithResponseAsync<SystemMetrics>(UiApiRoutes.HealthMetrics, ct).ConfigureAwait(false)).DataOrLoggedNull("Get system metrics");
    }

    /// <summary>
    /// Runs a connection test for a provider.
    /// </summary>
    public async Task<ConnectionTestResult?> TestConnectionAsync(string provider, CancellationToken ct = default)
    {
        return (await _apiClient.PostWithResponseAsync<ConnectionTestResult>(BuildProviderTestRoute(provider), null, ct).ConfigureAwait(false)).DataOrLoggedNull("Test provider connection");
    }

    /// <summary>
    /// Generates a diagnostic bundle.
    /// </summary>
    public async Task<DiagnosticBundle?> GenerateDiagnosticBundleAsync(CancellationToken ct = default)
    {
        return (await _apiClient.PostWithResponseAsync<DiagnosticBundle>(UiApiRoutes.HealthDiagnosticsBundle, null, ct).ConfigureAwait(false)).DataOrLoggedNull("Generate diagnostic bundle");
    }

    internal static string BuildProviderDiagnosticsRoute(string provider)
        => UiApiRoutes.WithParam(UiApiRoutes.HealthProviderDiagnostics, "provider", provider);

    internal static string BuildRecentEventsRoute(int limit)
        => UiApiRoutes.WithQuery(
            UiApiRoutes.HealthEvents,
            string.Create(CultureInfo.InvariantCulture, $"limit={limit}"));

    internal static string BuildProviderTestRoute(string provider)
        => UiApiRoutes.WithParam(UiApiRoutes.HealthProviderTest, "provider", provider);
}

internal interface ISystemHealthApiClient
{
    Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class;

    Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Default implementations adapt legacy nullable-result implementations (e.g. test doubles)
    /// onto the <see cref="ApiResponse{T}"/> seam; a null result maps to a 404-style failure so
    /// <see cref="ApiResponseExtensions.DataOrLoggedNull{T}"/> stays silent, matching the legacy
    /// null path. Production implementations override these with true response pass-throughs.
    /// </summary>
    async Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default) where T : class
    {
        var data = await GetAsync<T>(endpoint, ct);
        return data is null
            ? ApiResponse<T>.Fail("No data returned.", statusCode: 404)
            : ApiResponse<T>.Ok(data);
    }

    async Task<ApiResponse<T>> PostWithResponseAsync<T>(string endpoint, object? body = null, CancellationToken ct = default) where T : class
    {
        var data = await PostAsync<T>(endpoint, body, ct);
        return data is null
            ? ApiResponse<T>.Fail("No data returned.", statusCode: 404)
            : ApiResponse<T>.Ok(data);
    }
}

internal sealed class ApiClientSystemHealthApiClient(ApiClientService apiClient) : ISystemHealthApiClient
{
    public async Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class
        => (await apiClient.GetWithResponseAsync<T>(endpoint, ct).ConfigureAwait(false)).DataOrLoggedNull("System health API GET request");

    public async Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default) where T : class
        => (await apiClient.PostWithResponseAsync<T>(endpoint, body, ct).ConfigureAwait(false)).DataOrLoggedNull("System health API POST request");

    public Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default) where T : class
        => apiClient.GetWithResponseAsync<T>(endpoint, ct);

    public Task<ApiResponse<T>> PostWithResponseAsync<T>(string endpoint, object? body = null, CancellationToken ct = default) where T : class
        => apiClient.PostWithResponseAsync<T>(endpoint, body, ct);
}

// DTO classes for system health

public sealed class SystemHealthSummary
{
    public string OverallStatus { get; set; } = "Unknown";
    public bool IsHealthy { get; set; }
    public int ActiveConnections { get; set; }
    public int HealthyConnections { get; set; }
    public int UnhealthyConnections { get; set; }
    public double AverageLatencyMs { get; set; }
    public long TotalEventsProcessed { get; set; }
    public long EventsLast24Hours { get; set; }
    public double StorageUsedPercent { get; set; }
    public int ActiveAlerts { get; set; }
    public DateTime LastUpdated { get; set; }
    public TimeSpan Uptime { get; set; }
}

public sealed class ProviderHealth
{
    public string Provider { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsConnected { get; set; }
    public string Status { get; set; } = "Unknown";
    public string? LifecycleState { get; set; }
    public string? WebSocketState { get; set; }
    public bool? IsReconnecting { get; set; }
    public DateTimeOffset? LastHeartbeatReceivedAt { get; set; }
    public DateTimeOffset? LastMessageReceivedAt { get; set; }
    public DateTimeOffset? LastReconnectAttemptAt { get; set; }
    public int? ReconnectAttempts { get; set; }
    public string? LastFailureKind { get; set; }
    public double LatencyMs { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public DateTime? LastEventAt { get; set; }
    public int EventsPerSecond { get; set; }
    public int ErrorCount { get; set; }
    public string? LastError { get; set; }
    public List<string> Issues { get; set; } = new();
}

public sealed class ProviderDiagnosticsSnapshot
{
    public string Provider { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public string ConnectionState { get; set; } = string.Empty;
    public string? LifecycleState { get; set; }
    public string? WebSocketState { get; set; }
    public bool? IsReconnecting { get; set; }
    public double LatencyMs { get; set; }
    public int ReconnectAttempts { get; set; }
    public DateTimeOffset? LastHeartbeatReceivedAt { get; set; }
    public DateTimeOffset? LastMessageReceivedAt { get; set; }
    public DateTimeOffset? LastReconnectAttemptAt { get; set; }
    public string? LastFailureKind { get; set; }
    public DateTime? LastReconnectAt { get; set; }
    public List<string> ActiveSubscriptions { get; set; } = new();
    public Dictionary<string, int> EventCounts { get; set; } = new();
    public List<DiagnosticIssue> Issues { get; set; } = new();
    public Dictionary<string, object> Configuration { get; set; } = new();
}

public sealed class DiagnosticIssue
{
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
}

public sealed class StorageHealth
{
    public bool IsHealthy { get; set; }
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public long AvailableBytes { get; set; }
    public double UsedPercent { get; set; }
    public int TotalFiles { get; set; }
    public int CorruptedFiles { get; set; }
    public int OrphanedFiles { get; set; }
    public DateTime LastChecked { get; set; }
    public List<string> Issues { get; set; } = new();
}

public sealed class SystemEvent
{
    public string Id { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}

public sealed class SystemMetrics
{
    public double CpuUsagePercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryTotalBytes { get; set; }
    public double MemoryUsedPercent { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public double EventsPerSecond { get; set; }
    public double BytesPerSecond { get; set; }
    public int ActiveConnections { get; set; }
    public int PendingOperations { get; set; }
    public DateTime Timestamp { get; set; }
}

public sealed class ConnectionTestResult
{
    public bool Success { get; set; }
    public double LatencyMs { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

public sealed class DiagnosticBundle
{
    public string BundleId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> IncludedSections { get; set; } = new();
}
