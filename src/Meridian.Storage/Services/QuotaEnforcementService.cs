using System.Collections.Concurrent;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Policies;
using Microsoft.Extensions.Logging;

namespace Meridian.Storage.Services;

/// <summary>
/// Service that enforces per-source, per-symbol, and per-event-type storage quotas.
/// Tracks usage in real-time and applies configured enforcement policies when quotas are exceeded.
/// </summary>
[ImplementsAdr("ADR-002", "Capacity management with quota enforcement")]
public sealed class QuotaEnforcementService : IQuotaEnforcementService
{
    private readonly StorageOptions _options;
    private readonly ISourceRegistry? _sourceRegistry;
    private readonly ILogger<QuotaEnforcementService> _logger;
    private readonly ConcurrentDictionary<string, QuotaUsage> _globalUsage = new();
    private readonly ConcurrentDictionary<string, QuotaUsage> _sourceUsage = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, QuotaUsage> _symbolUsage = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, QuotaUsage> _eventTypeUsage = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonlStoragePolicy? _pathParser;
    private DateTime _lastScanUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    private static readonly string[] DataExtensions = { ".jsonl", ".jsonl.gz", ".jsonl.zst", ".jsonl.lz4", ".parquet" };

    public QuotaEnforcementService(
        StorageOptions options,
        ILogger<QuotaEnforcementService> logger,
        ISourceRegistry? sourceRegistry = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sourceRegistry = sourceRegistry;
        _pathParser = new JsonlStoragePolicy(options, sourceRegistry);
    }

    /// <inheritdoc />
    public QuotaCheckResult CheckQuota(string symbol, string source, string eventType, long additionalBytes)
    {
        var quotas = _options.Quotas;
        if (quotas == null)
            return QuotaCheckResult.Allowed;

        // Check global quota
        if (quotas.Global != null)
        {
            var globalUsage = GetGlobalUsage();
            if (globalUsage.TotalBytes + additionalBytes > quotas.Global.MaxBytes)
            {
                return ApplyPolicy(quotas.Global.Enforcement, "global",
                    globalUsage.TotalBytes, quotas.Global.MaxBytes, additionalBytes);
            }
        }

        // Check per-source quota
        if (quotas.PerSource != null && quotas.PerSource.TryGetValue(source, out var sourceQuota))
        {
            var sourceUsage = GetSourceUsage(source);
            if (sourceUsage.TotalBytes + additionalBytes > sourceQuota.MaxBytes)
            {
                return ApplyPolicy(sourceQuota.Enforcement, $"source:{source}",
                    sourceUsage.TotalBytes, sourceQuota.MaxBytes, additionalBytes);
            }

            if (sourceQuota.MaxFiles.HasValue && sourceUsage.FileCount >= sourceQuota.MaxFiles.Value)
            {
                return ApplyPolicy(sourceQuota.Enforcement, $"source:{source} (files)",
                    sourceUsage.FileCount, sourceQuota.MaxFiles.Value, 1);
            }
        }

        // Check per-symbol quota
        if (quotas.PerSymbol != null && quotas.PerSymbol.TryGetValue(symbol, out var symbolQuota))
        {
            var symbolUsage = GetSymbolUsage(symbol);
            if (symbolUsage.TotalBytes + additionalBytes > symbolQuota.MaxBytes)
            {
                return ApplyPolicy(symbolQuota.Enforcement, $"symbol:{symbol}",
                    symbolUsage.TotalBytes, symbolQuota.MaxBytes, additionalBytes);
            }
        }

        // Check per-event-type quota
        if (quotas.PerEventType != null && quotas.PerEventType.TryGetValue(eventType, out var typeQuota))
        {
            var typeUsage = GetEventTypeUsage(eventType);
            if (typeUsage.TotalBytes + additionalBytes > typeQuota.MaxBytes)
            {
                return ApplyPolicy(typeQuota.Enforcement, $"eventType:{eventType}",
                    typeUsage.TotalBytes, typeQuota.MaxBytes, additionalBytes);
            }
        }

        return QuotaCheckResult.Allowed;
    }

    /// <inheritdoc />
    public void RecordUsage(string filePath, long bytes)
    {
        var parsed = _pathParser?.TryParsePath(filePath);
        var symbol = parsed?.Symbol ?? "Unknown";
        var source = parsed?.Source ?? "Unknown";
        var eventType = parsed?.EventType ?? "Unknown";

        // Update global
        _globalUsage.AddOrUpdate("global",
            _ => new QuotaUsage { TotalBytes = bytes, FileCount = 1 },
            (_, u) => { u.TotalBytes += bytes; u.FileCount++; return u; });

        // Update source
        _sourceUsage.AddOrUpdate(source,
            _ => new QuotaUsage { TotalBytes = bytes, FileCount = 1 },
            (_, u) => { u.TotalBytes += bytes; u.FileCount++; return u; });

        // Update symbol
        _symbolUsage.AddOrUpdate(symbol,
            _ => new QuotaUsage { TotalBytes = bytes, FileCount = 1 },
            (_, u) => { u.TotalBytes += bytes; u.FileCount++; return u; });

        // Update event type
        _eventTypeUsage.AddOrUpdate(eventType,
            _ => new QuotaUsage { TotalBytes = bytes, FileCount = 1 },
            (_, u) => { u.TotalBytes += bytes; u.FileCount++; return u; });
    }

    /// <inheritdoc />
    public async Task<QuotaScanResult> ScanAndUpdateAsync(CancellationToken ct = default)
    {
        await _scanLock.WaitAsync(ct);
        try
        {
            _globalUsage.Clear();
            _sourceUsage.Clear();
            _symbolUsage.Clear();
            _eventTypeUsage.Clear();

            if (!Directory.Exists(_options.RootPath))
            {
                return new QuotaScanResult(DateTime.UtcNow, 0, 0, new List<QuotaViolation>());
            }

            var files = Directory.EnumerateFiles(_options.RootPath, "*", SearchOption.AllDirectories)
                .Where(f => DataExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            long totalBytes = 0;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists)
                        continue;
                    RecordUsage(file, info.Length);
                    totalBytes += info.Length;
                }
                catch (IOException) { /* Skip inaccessible files */ }
                catch (UnauthorizedAccessException) { /* Skip files we can't access */ }
            }

            _lastScanUtc = DateTime.UtcNow;

            // Check for violations
            var violations = DetectViolations();

            _logger.LogInformation(
                "Quota scan complete: {FileCount} files, {TotalBytes} bytes, {Violations} violations",
                files.Count, totalBytes, violations.Count);

            return new QuotaScanResult(DateTime.UtcNow, files.Count, totalBytes, violations);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    /// <inheritdoc />
    public QuotaStatusReport GetStatus()
    {
        var quotas = _options.Quotas;
        var entries = new List<QuotaStatusEntry>();

        if (quotas?.Global != null)
        {
            var usage = GetGlobalUsage();
            entries.Add(new QuotaStatusEntry("global", "Global",
                usage.TotalBytes, quotas.Global.MaxBytes,
                usage.FileCount, quotas.Global.MaxFiles,
                quotas.Global.Enforcement));
        }

        if (quotas?.PerSource != null)
        {
            foreach (var kvp in quotas.PerSource)
            {
                var usage = GetSourceUsage(kvp.Key);
                entries.Add(new QuotaStatusEntry($"source:{kvp.Key}", $"Source: {kvp.Key}",
                    usage.TotalBytes, kvp.Value.MaxBytes,
                    usage.FileCount, kvp.Value.MaxFiles,
                    kvp.Value.Enforcement));
            }
        }

        if (quotas?.PerSymbol != null)
        {
            foreach (var kvp in quotas.PerSymbol)
            {
                var usage = GetSymbolUsage(kvp.Key);
                entries.Add(new QuotaStatusEntry($"symbol:{kvp.Key}", $"Symbol: {kvp.Key}",
                    usage.TotalBytes, kvp.Value.MaxBytes,
                    usage.FileCount, kvp.Value.MaxFiles,
                    kvp.Value.Enforcement));
            }
        }

        if (quotas?.PerEventType != null)
        {
            foreach (var kvp in quotas.PerEventType)
            {
                var usage = GetEventTypeUsage(kvp.Key);
                entries.Add(new QuotaStatusEntry($"type:{kvp.Key}", $"EventType: {kvp.Key}",
                    usage.TotalBytes, kvp.Value.MaxBytes,
                    usage.FileCount, kvp.Value.MaxFiles,
                    kvp.Value.Enforcement));
            }
        }

        return new QuotaStatusReport(
            GeneratedAtUtc: DateTime.UtcNow,
            LastScanUtc: _lastScanUtc,
            Entries: entries,
            Violations: DetectViolations());
    }

    private QuotaUsage GetGlobalUsage()
    {
        return _globalUsage.TryGetValue("global", out var usage) ? usage : new QuotaUsage();
    }

    private QuotaUsage GetSourceUsage(string source)
    {
        return _sourceUsage.TryGetValue(source, out var usage) ? usage : new QuotaUsage();
    }

    private QuotaUsage GetSymbolUsage(string symbol)
    {
        return _symbolUsage.TryGetValue(symbol, out var usage) ? usage : new QuotaUsage();
    }

    private QuotaUsage GetEventTypeUsage(string eventType)
    {
        return _eventTypeUsage.TryGetValue(eventType, out var usage) ? usage : new QuotaUsage();
    }

    private QuotaCheckResult ApplyPolicy(QuotaEnforcementPolicy policy, string quotaId,
        long currentUsage, long maxAllowed, long additionalRequested)
    {
        var usagePct = maxAllowed > 0 ? (double)currentUsage / maxAllowed * 100 : 100;

        return policy switch
        {
            QuotaEnforcementPolicy.Warn => new QuotaCheckResult(
                IsAllowed: true,
                QuotaId: quotaId,
                UsagePercentage: usagePct,
                Warning: $"Quota {quotaId} at {usagePct:F1}% ({currentUsage}/{maxAllowed} bytes)"),

            QuotaEnforcementPolicy.SoftLimit => new QuotaCheckResult(
                IsAllowed: true,
                QuotaId: quotaId,
                UsagePercentage: usagePct,
                Warning: $"Soft limit exceeded for {quotaId}, cleanup recommended",
                RequiresCleanup: true),

            QuotaEnforcementPolicy.HardLimit => new QuotaCheckResult(
                IsAllowed: false,
                QuotaId: quotaId,
                UsagePercentage: usagePct,
                Warning: $"Hard limit reached for {quotaId}: {currentUsage}/{maxAllowed} bytes"),

            QuotaEnforcementPolicy.DropOldest => new QuotaCheckResult(
                IsAllowed: true,
                QuotaId: quotaId,
                UsagePercentage: usagePct,
                Warning: $"Dropping oldest data for {quotaId}",
                RequiresCleanup: true),

            _ => QuotaCheckResult.Allowed
        };
    }

    private List<QuotaViolation> DetectViolations()
    {
        var violations = new List<QuotaViolation>();
        var quotas = _options.Quotas;
        if (quotas == null)
            return violations;

        if (quotas.Global != null)
        {
            var usage = GetGlobalUsage();
            if (usage.TotalBytes > quotas.Global.MaxBytes)
            {
                violations.Add(new QuotaViolation("global", "Global",
                    usage.TotalBytes, quotas.Global.MaxBytes, quotas.Global.Enforcement));
            }
        }

        if (quotas.PerSource != null)
        {
            foreach (var kvp in quotas.PerSource)
            {
                var usage = GetSourceUsage(kvp.Key);
                if (usage.TotalBytes > kvp.Value.MaxBytes)
                {
                    violations.Add(new QuotaViolation($"source:{kvp.Key}", $"Source: {kvp.Key}",
                        usage.TotalBytes, kvp.Value.MaxBytes, kvp.Value.Enforcement));
                }
            }
        }

        if (quotas.PerSymbol != null)
        {
            foreach (var kvp in quotas.PerSymbol)
            {
                var usage = GetSymbolUsage(kvp.Key);
                if (usage.TotalBytes > kvp.Value.MaxBytes)
                {
                    violations.Add(new QuotaViolation($"symbol:{kvp.Key}", $"Symbol: {kvp.Key}",
                        usage.TotalBytes, kvp.Value.MaxBytes, kvp.Value.Enforcement));
                }
            }
        }

        return violations;
    }
}

/// <summary>
/// Interface for quota enforcement service.
/// </summary>
public interface IQuotaEnforcementService
{
    QuotaCheckResult CheckQuota(string symbol, string source, string eventType, long additionalBytes);
    void RecordUsage(string filePath, long bytes);
    Task<QuotaScanResult> ScanAndUpdateAsync(CancellationToken ct = default);
    QuotaStatusReport GetStatus();
}

// Quota types
public sealed class QuotaUsage
{
    public long TotalBytes { get; set; }
    public long FileCount { get; set; }
    public long EventsToday { get; set; }
}

public sealed record QuotaCheckResult(
    bool IsAllowed,
    string? QuotaId = null,
    double UsagePercentage = 0,
    string? Warning = null,
    bool RequiresCleanup = false)
{
    public static readonly QuotaCheckResult Allowed = new(true);
}

public sealed record QuotaScanResult(
    DateTime ScannedAtUtc,
    int FilesScanned,
    long TotalBytes,
    IReadOnlyList<QuotaViolation> Violations);

public sealed record QuotaViolation(
    string QuotaId,
    string Description,
    long CurrentBytes,
    long MaxBytes,
    QuotaEnforcementPolicy Policy)
{
    public double UsagePercentage => MaxBytes > 0 ? (double)CurrentBytes / MaxBytes * 100 : 100;
    public long OverageBytes => Math.Max(0, CurrentBytes - MaxBytes);
}

public sealed record QuotaStatusReport(
    DateTime GeneratedAtUtc,
    DateTime LastScanUtc,
    IReadOnlyList<QuotaStatusEntry> Entries,
    IReadOnlyList<QuotaViolation> Violations);

public sealed record QuotaStatusEntry(
    string QuotaId,
    string DisplayName,
    long UsedBytes,
    long MaxBytes,
    long? UsedFiles,
    long? MaxFiles,
    QuotaEnforcementPolicy Policy)
{
    public double UsagePercentage => MaxBytes > 0 ? (double)UsedBytes / MaxBytes * 100 : 0;
}
