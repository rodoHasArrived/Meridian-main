using System.Diagnostics;
using System.Threading;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Monitors system resources including disk space, memory, and CPU usage.
/// Provides health checks and alerts for resource constraints.
/// </summary>
public sealed class SystemHealthChecker : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<SystemHealthChecker>();
    private readonly SystemHealthConfig _config;
    private readonly Timer _checkTimer;
    private readonly List<string> _monitoredPaths = new();
    private SystemHealthSnapshot _lastSnapshot;
    private volatile bool _isDisposed;

    /// <summary>
    /// Event raised when a health warning is triggered.
    /// </summary>
    public event Action<SystemHealthWarning>? OnHealthWarning;

    /// <summary>
    /// Event raised when a critical health issue is detected.
    /// </summary>
    public event Action<SystemHealthWarning>? OnCriticalIssue;

    public SystemHealthChecker(SystemHealthConfig? config = null)
    {
        _config = config ?? SystemHealthConfig.Default;
        _checkTimer = new Timer(CheckHealth, null, TimeSpan.Zero, TimeSpan.FromSeconds(_config.CheckIntervalSeconds));
        _lastSnapshot = new SystemHealthSnapshot();

        _log.Information("SystemHealthChecker initialized with check interval {Interval}s", _config.CheckIntervalSeconds);
    }

    /// <summary>
    /// Adds a path to monitor for disk space.
    /// </summary>
    public void AddMonitoredPath(string path)
    {
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
        {
            lock (_monitoredPaths)
            {
                if (!_monitoredPaths.Contains(path))
                {
                    _monitoredPaths.Add(path);
                    _log.Debug("Added disk space monitoring for path: {Path}", path);
                }
            }
        }
    }

    /// <summary>
    /// Gets the current system health snapshot.
    /// </summary>
    public SystemHealthSnapshot GetSnapshot()
    {
        return _lastSnapshot;
    }

    /// <summary>
    /// Gets disk space information for all monitored paths.
    /// </summary>
    public IReadOnlyList<DiskSpaceInfo> GetDiskSpaceInfo()
    {
        var result = new List<DiskSpaceInfo>();

        string[] pathsToCheck;
        lock (_monitoredPaths)
        {
            pathsToCheck = _monitoredPaths.ToArray();
        }

        foreach (var path in pathsToCheck)
        {
            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(path) ?? path);
                if (driveInfo.IsReady)
                {
                    var totalGb = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    var freeGb = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    var usedGb = totalGb - freeGb;
                    var usedPercent = (usedGb / totalGb) * 100;

                    result.Add(new DiskSpaceInfo(
                        Path: path,
                        DriveName: driveInfo.Name,
                        TotalSpaceGb: totalGb,
                        FreeSpaceGb: freeGb,
                        UsedSpaceGb: usedGb,
                        UsedPercent: usedPercent,
                        IsCritical: freeGb < _config.CriticalDiskSpaceGb,
                        IsWarning: freeGb < _config.WarningDiskSpaceGb
                    ));
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to get disk space for path: {Path}", path);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets memory information.
    /// </summary>
    public MemoryInfo GetMemoryInfo()
    {
        var process = Process.GetCurrentProcess();
        var workingSetMb = process.WorkingSet64 / (1024.0 * 1024.0);
        var privateMb = process.PrivateMemorySize64 / (1024.0 * 1024.0);
        var gcMemoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        var gcInfo = GC.GetGCMemoryInfo();
        var heapSizeMb = gcInfo.HeapSizeBytes / (1024.0 * 1024.0);

        return new MemoryInfo(
            WorkingSetMb: workingSetMb,
            PrivateMemoryMb: privateMb,
            GcMemoryMb: gcMemoryMb,
            HeapSizeMb: heapSizeMb,
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2),
            IsWarning: workingSetMb > _config.WarningMemoryMb,
            IsCritical: workingSetMb > _config.CriticalMemoryMb
        );
    }

    /// <summary>
    /// Gets all current health warnings.
    /// </summary>
    public IReadOnlyList<SystemHealthWarning> GetCurrentWarnings()
    {
        var warnings = new List<SystemHealthWarning>();

        // Check disk space
        foreach (var disk in GetDiskSpaceInfo())
        {
            if (disk.IsCritical)
            {
                warnings.Add(new SystemHealthWarning(
                    Category: HealthWarningCategory.DiskSpace,
                    Severity: HealthWarningSeverity.Critical,
                    Message: $"Critical disk space on {disk.DriveName}: {disk.FreeSpaceGb:F1} GB free ({100 - disk.UsedPercent:F1}%)",
                    Details: new Dictionary<string, object>
                    {
                        ["path"] = disk.Path,
                        ["drive"] = disk.DriveName,
                        ["freeGb"] = disk.FreeSpaceGb,
                        ["totalGb"] = disk.TotalSpaceGb,
                        ["usedPercent"] = disk.UsedPercent
                    }
                ));
            }
            else if (disk.IsWarning)
            {
                warnings.Add(new SystemHealthWarning(
                    Category: HealthWarningCategory.DiskSpace,
                    Severity: HealthWarningSeverity.Warning,
                    Message: $"Low disk space on {disk.DriveName}: {disk.FreeSpaceGb:F1} GB free ({100 - disk.UsedPercent:F1}%)",
                    Details: new Dictionary<string, object>
                    {
                        ["path"] = disk.Path,
                        ["drive"] = disk.DriveName,
                        ["freeGb"] = disk.FreeSpaceGb,
                        ["totalGb"] = disk.TotalSpaceGb,
                        ["usedPercent"] = disk.UsedPercent
                    }
                ));
            }
        }

        // Check memory
        var memory = GetMemoryInfo();
        if (memory.IsCritical)
        {
            warnings.Add(new SystemHealthWarning(
                Category: HealthWarningCategory.Memory,
                Severity: HealthWarningSeverity.Critical,
                Message: $"Critical memory usage: {memory.WorkingSetMb:F0} MB (threshold: {_config.CriticalMemoryMb} MB)",
                Details: new Dictionary<string, object>
                {
                    ["workingSetMb"] = memory.WorkingSetMb,
                    ["privateMemoryMb"] = memory.PrivateMemoryMb,
                    ["gcMemoryMb"] = memory.GcMemoryMb,
                    ["threshold"] = _config.CriticalMemoryMb
                }
            ));
        }
        else if (memory.IsWarning)
        {
            warnings.Add(new SystemHealthWarning(
                Category: HealthWarningCategory.Memory,
                Severity: HealthWarningSeverity.Warning,
                Message: $"High memory usage: {memory.WorkingSetMb:F0} MB (threshold: {_config.WarningMemoryMb} MB)",
                Details: new Dictionary<string, object>
                {
                    ["workingSetMb"] = memory.WorkingSetMb,
                    ["privateMemoryMb"] = memory.PrivateMemoryMb,
                    ["gcMemoryMb"] = memory.GcMemoryMb,
                    ["threshold"] = _config.WarningMemoryMb
                }
            ));
        }

        return warnings;
    }

    /// <summary>
    /// Gets the overall health status.
    /// </summary>
    public SystemHealthStatus GetOverallStatus()
    {
        var warnings = GetCurrentWarnings();

        if (warnings.Any(w => w.Severity == HealthWarningSeverity.Critical))
            return SystemHealthStatus.Critical;

        if (warnings.Any(w => w.Severity == HealthWarningSeverity.Warning))
            return SystemHealthStatus.Warning;

        return SystemHealthStatus.Healthy;
    }

    private void CheckHealth(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var diskInfo = GetDiskSpaceInfo();
            var memoryInfo = GetMemoryInfo();
            var warnings = GetCurrentWarnings();

            _lastSnapshot = new SystemHealthSnapshot
            {
                Timestamp = DateTimeOffset.UtcNow,
                DiskInfo = diskInfo.ToList(),
                MemoryInfo = memoryInfo,
                Warnings = warnings.ToList(),
                OverallStatus = GetOverallStatus()
            };

            // Raise events for new warnings
            foreach (var warning in warnings)
            {
                try
                {
                    if (warning.Severity == HealthWarningSeverity.Critical)
                    {
                        _log.Error("CRITICAL: {Message}", warning.Message);
                        OnCriticalIssue?.Invoke(warning);
                    }
                    else
                    {
                        _log.Warning("Health warning: {Message}", warning.Message);
                        OnHealthWarning?.Invoke(warning);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in health warning event handler");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during system health check");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _checkTimer.Dispose();
    }
}

/// <summary>
/// Configuration for system health thresholds.
/// </summary>
public sealed record SystemHealthConfig
{
    /// <summary>
    /// Disk space warning threshold in GB.
    /// </summary>
    public double WarningDiskSpaceGb { get; init; } = 10.0;

    /// <summary>
    /// Disk space critical threshold in GB.
    /// </summary>
    public double CriticalDiskSpaceGb { get; init; } = 2.0;

    /// <summary>
    /// Memory warning threshold in MB.
    /// </summary>
    public double WarningMemoryMb { get; init; } = 1024.0;

    /// <summary>
    /// Memory critical threshold in MB.
    /// </summary>
    public double CriticalMemoryMb { get; init; } = 2048.0;

    /// <summary>
    /// How often to check system health in seconds.
    /// </summary>
    public int CheckIntervalSeconds { get; init; } = 30;

    public static SystemHealthConfig Default => new();
}

/// <summary>
/// Snapshot of system health at a point in time.
/// </summary>
public sealed class SystemHealthSnapshot
{
    public DateTimeOffset Timestamp { get; init; }
    public IReadOnlyList<DiskSpaceInfo> DiskInfo { get; init; } = Array.Empty<DiskSpaceInfo>();
    public MemoryInfo MemoryInfo { get; init; }
    public IReadOnlyList<SystemHealthWarning> Warnings { get; init; } = Array.Empty<SystemHealthWarning>();
    public SystemHealthStatus OverallStatus { get; init; }
}

/// <summary>
/// Information about disk space for a monitored path.
/// </summary>
public readonly record struct DiskSpaceInfo(
    string Path,
    string DriveName,
    double TotalSpaceGb,
    double FreeSpaceGb,
    double UsedSpaceGb,
    double UsedPercent,
    bool IsCritical,
    bool IsWarning
);

/// <summary>
/// Information about memory usage.
/// </summary>
public readonly record struct MemoryInfo(
    double WorkingSetMb,
    double PrivateMemoryMb,
    double GcMemoryMb,
    double HeapSizeMb,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    bool IsWarning,
    bool IsCritical
);

/// <summary>
/// A health warning from the system health checker.
/// </summary>
public sealed record SystemHealthWarning(
    HealthWarningCategory Category,
    HealthWarningSeverity Severity,
    string Message,
    IReadOnlyDictionary<string, object>? Details = null
)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Category of health warning.
/// </summary>
public enum HealthWarningCategory : byte
{
    DiskSpace,
    Memory,
    Cpu,
    Network,
    DataQuality,
    Connection
}

/// <summary>
/// Severity of health warning.
/// </summary>
public enum HealthWarningSeverity : byte
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// Overall system health status.
/// </summary>
public enum SystemHealthStatus : byte
{
    Healthy,
    Warning,
    Critical
}
