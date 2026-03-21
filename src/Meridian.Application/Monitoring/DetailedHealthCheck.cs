using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring.DataQuality;
using Meridian.Application.Pipeline;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Provides detailed health check information including dependencies, providers, and data quality.
/// Implements QW-32: Detailed Health Check Endpoint and QW-33: Dependency Health Checks.
/// </summary>
public sealed class DetailedHealthCheck : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<DetailedHealthCheck>();
    private readonly ConcurrentDictionary<string, DependencyHealthCheck> _dependencies = new();
    private readonly DetailedHealthCheckConfig _config;
    private readonly Timer _checkTimer;
    private volatile bool _isDisposed;

    // External service providers
    private Func<PipelineStatistics>? _pipelineStatsProvider;
    private Func<SystemHealthSnapshot>? _systemHealthProvider;
    private Func<ConnectionHealthSnapshot>? _connectionHealthProvider;
    private Func<LatencyStatistics>? _latencyStatsProvider;

    /// <summary>
    /// Event raised when a dependency becomes unhealthy.
    /// </summary>
    public event Action<DependencyUnhealthyEvent>? OnDependencyUnhealthy;

    /// <summary>
    /// Event raised when a dependency recovers.
    /// </summary>
    public event Action<DependencyRecoveredEvent>? OnDependencyRecovered;

    public DetailedHealthCheck(DetailedHealthCheckConfig? config = null)
    {
        _config = config ?? DetailedHealthCheckConfig.Default;
        _checkTimer = new Timer(
            CheckDependencies,
            null,
            TimeSpan.FromSeconds(_config.CheckIntervalSeconds),
            TimeSpan.FromSeconds(_config.CheckIntervalSeconds));

        _log.Information("DetailedHealthCheck initialized with {CheckInterval}s interval", _config.CheckIntervalSeconds);
    }

    /// <summary>
    /// Registers statistics providers for health checks.
    /// </summary>
    public void RegisterProviders(
        Func<PipelineStatistics>? pipelineStats = null,
        Func<SystemHealthSnapshot>? systemHealth = null,
        Func<ConnectionHealthSnapshot>? connectionHealth = null,
        Func<LatencyStatistics>? latencyStats = null)
    {
        _pipelineStatsProvider = pipelineStats;
        _systemHealthProvider = systemHealth;
        _connectionHealthProvider = connectionHealth;
        _latencyStatsProvider = latencyStats;
    }

    /// <summary>
    /// Registers an external dependency to monitor.
    /// </summary>
    public void RegisterDependency(
        string name,
        string type,
        Func<CancellationToken, Task<bool>> healthCheck,
        bool isCritical = true,
        TimeSpan? timeout = null)
    {
        var dependency = new DependencyHealthCheck(
            name,
            type,
            healthCheck,
            isCritical,
            timeout ?? TimeSpan.FromSeconds(_config.DependencyTimeoutSeconds));

        _dependencies.TryAdd(name, dependency);
        _log.Debug("Registered dependency health check: {Name} ({Type})", name, type);
    }

    /// <summary>
    /// Unregisters a dependency.
    /// </summary>
    public void UnregisterDependency(string name)
    {
        _dependencies.TryRemove(name, out _);
    }

    /// <summary>
    /// Gets a comprehensive detailed health report.
    /// </summary>
    public async Task<DetailedHealthReport> GetDetailedHealthAsync(CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var checks = new List<HealthCheckItem>();
        var overallStatus = DetailedHealthStatus.Healthy;

        // 1. System health checks
        var systemHealth = _systemHealthProvider?.Invoke();
        if (systemHealth != null)
        {
            checks.Add(new HealthCheckItem(
                Name: "system_memory",
                Status: GetStatusFromMemory(systemHealth.MemoryInfo),
                Message: $"Memory: {systemHealth.MemoryInfo.WorkingSetMb:F0} MB",
                DurationMs: 0,
                Details: new Dictionary<string, object>
                {
                    ["workingSetMb"] = Math.Round(systemHealth.MemoryInfo.WorkingSetMb, 2),
                    ["gcMemoryMb"] = Math.Round(systemHealth.MemoryInfo.GcMemoryMb, 2),
                    ["gen0Collections"] = systemHealth.MemoryInfo.Gen0Collections,
                    ["gen1Collections"] = systemHealth.MemoryInfo.Gen1Collections,
                    ["gen2Collections"] = systemHealth.MemoryInfo.Gen2Collections
                }));

            foreach (var disk in systemHealth.DiskInfo)
            {
                checks.Add(new HealthCheckItem(
                    Name: $"disk_{disk.DriveName.TrimEnd(':').TrimEnd('\\')}",
                    Status: disk.IsCritical ? DetailedHealthStatus.Unhealthy :
                           disk.IsWarning ? DetailedHealthStatus.Degraded : DetailedHealthStatus.Healthy,
                    Message: $"Disk {disk.DriveName}: {disk.FreeSpaceGb:F1} GB free ({100 - disk.UsedPercent:F1}%)",
                    DurationMs: 0,
                    Details: new Dictionary<string, object>
                    {
                        ["freeSpaceGb"] = Math.Round(disk.FreeSpaceGb, 2),
                        ["totalSpaceGb"] = Math.Round(disk.TotalSpaceGb, 2),
                        ["usedPercent"] = Math.Round(disk.UsedPercent, 2)
                    }));
            }
        }

        // 2. Pipeline health checks
        var pipelineStats = _pipelineStatsProvider?.Invoke();
        if (pipelineStats != null)
        {
            var queueStatus = pipelineStats.Value.QueueUtilization > 90 ? DetailedHealthStatus.Unhealthy :
                             pipelineStats.Value.QueueUtilization > 70 ? DetailedHealthStatus.Degraded :
                             DetailedHealthStatus.Healthy;

            checks.Add(new HealthCheckItem(
                Name: "pipeline_queue",
                Status: queueStatus,
                Message: $"Queue: {pipelineStats.Value.QueueUtilization:F1}% ({pipelineStats.Value.CurrentQueueSize}/{pipelineStats.Value.QueueCapacity})",
                DurationMs: 0,
                Details: new Dictionary<string, object>
                {
                    ["utilization"] = Math.Round(pipelineStats.Value.QueueUtilization, 2),
                    ["currentSize"] = pipelineStats.Value.CurrentQueueSize,
                    ["capacity"] = pipelineStats.Value.QueueCapacity,
                    ["published"] = pipelineStats.Value.PublishedCount,
                    ["consumed"] = pipelineStats.Value.ConsumedCount,
                    ["dropped"] = pipelineStats.Value.DroppedCount
                }));

            // Backpressure check (MON-18)
            var dropRate = pipelineStats.Value.PublishedCount > 0
                ? (double)pipelineStats.Value.DroppedCount / pipelineStats.Value.PublishedCount * 100
                : 0;

            var backpressureStatus = dropRate > 20 ? DetailedHealthStatus.Unhealthy :
                                     dropRate > 5 ? DetailedHealthStatus.Degraded :
                                     DetailedHealthStatus.Healthy;

            checks.Add(new HealthCheckItem(
                Name: "backpressure",
                Status: backpressureStatus,
                Message: $"Drop rate: {dropRate:F2}% ({pipelineStats.Value.DroppedCount:N0} dropped)",
                DurationMs: 0,
                Details: new Dictionary<string, object>
                {
                    ["dropRate"] = Math.Round(dropRate, 2),
                    ["droppedCount"] = pipelineStats.Value.DroppedCount,
                    ["peakQueueSize"] = pipelineStats.Value.PeakQueueSize
                }));
        }

        // 3. Connection health checks
        var connectionHealth = _connectionHealthProvider?.Invoke();
        if (connectionHealth != null)
        {
            var connStatus = connectionHealth.Value.UnhealthyConnections > 0 ? DetailedHealthStatus.Degraded :
                            connectionHealth.Value.TotalConnections == 0 ? DetailedHealthStatus.Unknown :
                            DetailedHealthStatus.Healthy;

            checks.Add(new HealthCheckItem(
                Name: "connections",
                Status: connStatus,
                Message: $"Connections: {connectionHealth.Value.HealthyConnections}/{connectionHealth.Value.TotalConnections} healthy",
                DurationMs: 0,
                Details: new Dictionary<string, object>
                {
                    ["total"] = connectionHealth.Value.TotalConnections,
                    ["healthy"] = connectionHealth.Value.HealthyConnections,
                    ["unhealthy"] = connectionHealth.Value.UnhealthyConnections,
                    ["avgLatencyMs"] = Math.Round(connectionHealth.Value.GlobalAverageLatencyMs, 2)
                }));
        }

        // 4. Latency health checks
        var latencyStats = _latencyStatsProvider?.Invoke();
        if (latencyStats != null)
        {
            var latencyStatus = latencyStats.GlobalP99Ms > 1000 ? DetailedHealthStatus.Degraded :
                               latencyStats.GlobalP99Ms > 500 ? DetailedHealthStatus.Unknown :
                               DetailedHealthStatus.Healthy;

            checks.Add(new HealthCheckItem(
                Name: "latency",
                Status: latencyStatus,
                Message: $"P99 latency: {latencyStats.GlobalP99Ms:F1}ms",
                DurationMs: 0,
                Details: new Dictionary<string, object>
                {
                    ["p50Ms"] = Math.Round(latencyStats.GlobalP50Ms, 2),
                    ["p90Ms"] = Math.Round(latencyStats.GlobalP90Ms, 2),
                    ["p99Ms"] = Math.Round(latencyStats.GlobalP99Ms, 2),
                    ["meanMs"] = Math.Round(latencyStats.GlobalMeanMs, 2),
                    ["symbolsTracked"] = latencyStats.SymbolsTracked
                }));
        }

        // 5. External dependency checks
        foreach (var kvp in _dependencies)
        {
            var dep = kvp.Value;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool isHealthy = false;
            string message = "";

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(dep.Timeout);
                isHealthy = await dep.HealthCheck(timeoutCts.Token);
                message = isHealthy ? "OK" : "Health check returned false";
            }
            catch (OperationCanceledException)
            {
                message = $"Timeout after {dep.Timeout.TotalSeconds:F1}s";
            }
            catch (Exception ex)
            {
                message = $"Error: {ex.Message}";
            }

            sw.Stop();

            var wasHealthy = dep.IsHealthy;
            dep.UpdateStatus(isHealthy);

            // Raise events on status change
            if (wasHealthy && !isHealthy)
            {
                try
                {
                    OnDependencyUnhealthy?.Invoke(new DependencyUnhealthyEvent(
                        dep.Name, dep.Type, message, DateTimeOffset.UtcNow));
                }
                catch (Exception ex) { _log.Debug(ex, "Error invoking OnDependencyUnhealthy event for {Name}", dep.Name); }
            }
            else if (!wasHealthy && isHealthy)
            {
                try
                {
                    OnDependencyRecovered?.Invoke(new DependencyRecoveredEvent(
                        dep.Name, dep.Type, DateTimeOffset.UtcNow, dep.DowntimeDuration));
                }
                catch (Exception ex) { _log.Debug(ex, "Error invoking OnDependencyRecovered event for {Name}", dep.Name); }
            }

            var depStatus = isHealthy ? DetailedHealthStatus.Healthy :
                           dep.IsCritical ? DetailedHealthStatus.Unhealthy :
                           DetailedHealthStatus.Degraded;

            checks.Add(new HealthCheckItem(
                Name: $"dependency_{dep.Name}",
                Status: depStatus,
                Message: message,
                DurationMs: sw.ElapsedMilliseconds,
                Details: new Dictionary<string, object>
                {
                    ["type"] = dep.Type,
                    ["isCritical"] = dep.IsCritical,
                    ["consecutiveFailures"] = dep.ConsecutiveFailures
                }));
        }

        // Determine overall status
        foreach (var check in checks)
        {
            if (check.Status == DetailedHealthStatus.Unhealthy)
            {
                overallStatus = DetailedHealthStatus.Unhealthy;
                break;
            }
            if (check.Status == DetailedHealthStatus.Degraded && overallStatus == DetailedHealthStatus.Healthy)
            {
                overallStatus = DetailedHealthStatus.Degraded;
            }
        }

        return new DetailedHealthReport(
            Status: overallStatus,
            Timestamp: timestamp,
            Version: "1.6.0",
            Checks: checks,
            Summary: new HealthSummary(
                TotalChecks: checks.Count,
                HealthyChecks: checks.Count(c => c.Status == DetailedHealthStatus.Healthy),
                DegradedChecks: checks.Count(c => c.Status == DetailedHealthStatus.Degraded),
                UnhealthyChecks: checks.Count(c => c.Status == DetailedHealthStatus.Unhealthy)
            ));
    }

    private static DetailedHealthStatus GetStatusFromMemory(MemoryInfo memory)
    {
        if (memory.IsCritical)
            return DetailedHealthStatus.Unhealthy;
        if (memory.IsWarning)
            return DetailedHealthStatus.Degraded;
        return DetailedHealthStatus.Healthy;
    }

    private void CheckDependencies(object? state)
    {
        if (_isDisposed)
            return;

        // Fire-and-forget the async work, but with proper exception handling in the async method
        _ = CheckDependenciesInternalAsync();
    }

    private async Task CheckDependenciesInternalAsync(CancellationToken ct = default)
    {
        try
        {
            foreach (var kvp in _dependencies)
            {
                var dep = kvp.Value;
                var wasHealthy = dep.IsHealthy;

                try
                {
                    using var cts = new CancellationTokenSource(dep.Timeout);
                    var isHealthy = await dep.HealthCheck(cts.Token).ConfigureAwait(false);
                    dep.UpdateStatus(isHealthy);

                    if (wasHealthy && !isHealthy)
                    {
                        _log.Warning("Dependency {Name} ({Type}) is now unhealthy", dep.Name, dep.Type);
                        try
                        {
                            OnDependencyUnhealthy?.Invoke(new DependencyUnhealthyEvent(
                                dep.Name, dep.Type, "Background check failed", DateTimeOffset.UtcNow));
                        }
                        catch (Exception ex) { _log.Debug(ex, "Error invoking OnDependencyUnhealthy event for {Name}", dep.Name); }
                    }
                    else if (!wasHealthy && isHealthy)
                    {
                        _log.Information("Dependency {Name} ({Type}) recovered after {Duration}", dep.Name, dep.Type, dep.DowntimeDuration);
                        try
                        {
                            OnDependencyRecovered?.Invoke(new DependencyRecoveredEvent(
                                dep.Name, dep.Type, DateTimeOffset.UtcNow, dep.DowntimeDuration));
                        }
                        catch (Exception ex) { _log.Debug(ex, "Error invoking OnDependencyRecovered event for {Name}", dep.Name); }
                    }
                }
                catch (Exception ex)
                {
                    dep.UpdateStatus(false);
                    _log.Warning(ex, "Error checking dependency {Name}", dep.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in dependency check timer");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _checkTimer.Dispose();
        _dependencies.Clear();
    }

    private sealed class DependencyHealthCheck
    {
        public string Name { get; }
        public string Type { get; }
        public Func<CancellationToken, Task<bool>> HealthCheck { get; }
        public bool IsCritical { get; }
        public TimeSpan Timeout { get; }

        private volatile bool _isHealthy = true;
        private DateTimeOffset _lastHealthyTime = DateTimeOffset.UtcNow;
        private DateTimeOffset _firstUnhealthyTime = DateTimeOffset.UtcNow;
        private int _consecutiveFailures;

        public bool IsHealthy => _isHealthy;
        public int ConsecutiveFailures => _consecutiveFailures;
        public TimeSpan DowntimeDuration => !_isHealthy ? DateTimeOffset.UtcNow - _firstUnhealthyTime : TimeSpan.Zero;

        public DependencyHealthCheck(
            string name,
            string type,
            Func<CancellationToken, Task<bool>> healthCheck,
            bool isCritical,
            TimeSpan timeout)
        {
            Name = name;
            Type = type;
            HealthCheck = healthCheck;
            IsCritical = isCritical;
            Timeout = timeout;
        }

        public void UpdateStatus(bool isHealthy)
        {
            if (isHealthy)
            {
                _isHealthy = true;
                _lastHealthyTime = DateTimeOffset.UtcNow;
                _consecutiveFailures = 0;
            }
            else
            {
                if (_isHealthy)
                {
                    _firstUnhealthyTime = DateTimeOffset.UtcNow;
                }
                _isHealthy = false;
                _consecutiveFailures++;
            }
        }
    }
}

/// <summary>
/// Configuration for detailed health checks.
/// </summary>
public sealed record DetailedHealthCheckConfig
{
    /// <summary>
    /// Interval between automatic dependency checks in seconds.
    /// </summary>
    public int CheckIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// Default timeout for dependency health checks in seconds.
    /// </summary>
    public int DependencyTimeoutSeconds { get; init; } = 10;

    public static DetailedHealthCheckConfig Default => new();
}

/// <summary>
/// Detailed health status enum.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DetailedHealthStatus : byte
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}

/// <summary>
/// Comprehensive health report.
/// </summary>
public sealed record DetailedHealthReport(
    DetailedHealthStatus Status,
    DateTimeOffset Timestamp,
    string Version,
    IReadOnlyList<HealthCheckItem> Checks,
    HealthSummary Summary
);

/// <summary>
/// Individual health check item.
/// </summary>
public sealed record HealthCheckItem(
    string Name,
    DetailedHealthStatus Status,
    string Message,
    long DurationMs,
    IReadOnlyDictionary<string, object>? Details = null
);

/// <summary>
/// Summary of health check results.
/// </summary>
public sealed record HealthSummary(
    int TotalChecks,
    int HealthyChecks,
    int DegradedChecks,
    int UnhealthyChecks
);

/// <summary>
/// Event raised when a dependency becomes unhealthy.
/// </summary>
public readonly record struct DependencyUnhealthyEvent(
    string Name,
    string Type,
    string Reason,
    DateTimeOffset Timestamp
);

/// <summary>
/// Event raised when a dependency recovers.
/// </summary>
public readonly record struct DependencyRecoveredEvent(
    string Name,
    string Type,
    DateTimeOffset Timestamp,
    TimeSpan DowntimeDuration
);
