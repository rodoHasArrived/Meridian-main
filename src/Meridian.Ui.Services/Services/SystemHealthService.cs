using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for monitoring system health, connection diagnostics, and anomaly detection.
/// </summary>
public sealed class SystemHealthService
{
    private static readonly Lazy<SystemHealthService> _instance = new(() => new SystemHealthService());
    public static SystemHealthService Instance => _instance.Value;

    private SystemHealthService() { }

    /// <summary>
    /// Gets overall system health summary.
    /// </summary>
    public async Task<SystemHealthSummary?> GetHealthSummaryAsync(CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<SystemHealthSummary>("/api/health/summary", ct);
    }

    /// <summary>
    /// Gets connection health for all providers.
    /// </summary>
    public async Task<List<ProviderHealth>?> GetProviderHealthAsync(CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<List<ProviderHealth>>("/api/health/providers", ct);
    }

    /// <summary>
    /// Gets detailed diagnostics for a specific provider.
    /// </summary>
    public async Task<ProviderDiagnostics?> GetProviderDiagnosticsAsync(string provider, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<ProviderDiagnostics>($"/api/health/providers/{provider}/diagnostics", ct);
    }

    /// <summary>
    /// Gets storage health status.
    /// </summary>
    public async Task<StorageHealth?> GetStorageHealthAsync(CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<StorageHealth>("/api/health/storage", ct);
    }

    /// <summary>
    /// Gets recent system events and errors.
    /// </summary>
    public async Task<List<SystemEvent>?> GetRecentEventsAsync(int limit = 50, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<List<SystemEvent>>($"/api/health/events?limit={limit}", ct);
    }

    /// <summary>
    /// Gets system resource metrics.
    /// </summary>
    public async Task<SystemMetrics?> GetSystemMetricsAsync(CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<SystemMetrics>("/api/health/metrics", ct);
    }

    /// <summary>
    /// Runs a connection test for a provider.
    /// </summary>
    public async Task<ConnectionTestResult?> TestConnectionAsync(string provider, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.PostAsync<ConnectionTestResult>($"/api/health/providers/{provider}/test", null, ct);
    }

    /// <summary>
    /// Generates a diagnostic bundle.
    /// </summary>
    public async Task<DiagnosticBundle?> GenerateDiagnosticBundleAsync(CancellationToken ct = default)
    {
        return await ApiClientService.Instance.PostAsync<DiagnosticBundle>("/api/health/diagnostics/bundle", null, ct);
    }
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
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsConnected { get; set; }
    public string Status { get; set; } = "Unknown";
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
    public double LatencyMs { get; set; }
    public int ReconnectAttempts { get; set; }
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
