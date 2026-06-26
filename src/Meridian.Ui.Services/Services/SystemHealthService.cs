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
        return await _apiClient.GetAsync<SystemHealthSummary>(UiApiRoutes.HealthSummary, ct);
    }

    /// <summary>
    /// Gets connection health for all providers.
    /// </summary>
    public async Task<List<ProviderHealth>?> GetProviderHealthAsync(CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<List<ProviderHealth>>(UiApiRoutes.HealthProviders, ct);
    }

    /// <summary>
    /// Gets the shared provider readiness command-center model.
    /// </summary>
    public async Task<ProviderReadinessSummaryDto?> GetProviderReadinessAsync(CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<ProviderReadinessSummaryDto>(UiApiRoutes.ProviderReadiness, ct);
    }

    /// <summary>
    /// Gets detailed diagnostics for a specific provider.
    /// </summary>
    public async Task<ProviderDiagnostics?> GetProviderDiagnosticsAsync(string provider, CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<ProviderDiagnostics>(BuildProviderDiagnosticsRoute(provider), ct);
    }

    /// <summary>
    /// Gets storage health status.
    /// </summary>
    public async Task<StorageHealth?> GetStorageHealthAsync(CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<StorageHealth>(UiApiRoutes.HealthStorage, ct);
    }

    /// <summary>
    /// Gets recent system events and errors.
    /// </summary>
    public async Task<List<SystemEvent>?> GetRecentEventsAsync(int limit = 50, CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<List<SystemEvent>>(BuildRecentEventsRoute(limit), ct);
    }

    /// <summary>
    /// Gets system resource metrics.
    /// </summary>
    public async Task<SystemMetrics?> GetSystemMetricsAsync(CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<SystemMetrics>(UiApiRoutes.HealthMetrics, ct);
    }

    /// <summary>
    /// Runs a connection test for a provider.
    /// </summary>
    public async Task<ConnectionTestResult?> TestConnectionAsync(string provider, CancellationToken ct = default)
    {
        return await _apiClient.PostAsync<ConnectionTestResult>(BuildProviderTestRoute(provider), null, ct);
    }

    /// <summary>
    /// Generates a diagnostic bundle.
    /// </summary>
    public async Task<DiagnosticBundle?> GenerateDiagnosticBundleAsync(CancellationToken ct = default)
    {
        return await _apiClient.PostAsync<DiagnosticBundle>(UiApiRoutes.HealthDiagnosticsBundle, null, ct);
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
}

internal sealed class ApiClientSystemHealthApiClient(ApiClientService apiClient) : ISystemHealthApiClient
{
    public Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class
        => apiClient.GetAsync<T>(endpoint, ct);

    public Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default) where T : class
        => apiClient.PostAsync<T>(endpoint, body, ct);
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

public sealed class ProviderDiagnostics
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
