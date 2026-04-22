using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.ProviderSdk;
using Microsoft.Extensions.Hosting;

namespace Meridian.Application.ProviderRouting;

/// <summary>
/// Captures kernel quality/trustworthiness telemetry for provider-routing evaluations.
/// </summary>
public sealed class KernelObservabilityService
{
    private static readonly JsonSerializerOptions HashJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, DomainKernelState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _determinismHashes = new(StringComparer.Ordinal);
    private readonly bool _determinismChecksEnabled;

    public KernelObservabilityService(IHostEnvironment? environment = null)
    {
        var diagnosticsFlag = Environment.GetEnvironmentVariable("MERIDIAN_KERNEL_DIAGNOSTICS");
        var forceDeterminism = string.Equals(
            Environment.GetEnvironmentVariable("MERIDIAN_KERNEL_DETERMINISM_CHECKS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        _determinismChecksEnabled = forceDeterminism ||
            !string.Equals(environment?.EnvironmentName, "Production", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(diagnosticsFlag, "true", StringComparison.OrdinalIgnoreCase);
    }

    public KernelExecutionScope BeginExecution(ProviderRouteContext context)
        => new(context.Capability.ToString(), Stopwatch.GetTimestamp());

    public void RecordResult(
        ProviderRouteContext context,
        ProviderRouteResult result,
        IReadOnlyDictionary<string, ProviderConnectionHealthSnapshot> healthByConnection,
        in KernelExecutionScope scope)
    {
        var domain = scope.Domain;
        var state = _states.GetOrAdd(domain, static _ => new DomainKernelState());
        var latencyMs = Stopwatch.GetElapsedTime(scope.StartTimestamp).TotalMilliseconds;
        var selectedScore = ResolveTrustScore(result, healthByConnection);
        var severity = ClassifySeverity(result);
        var hasReasonCodes = result.SelectedDecision?.ReasonCodes.Any(static code => !string.IsNullOrWhiteSpace(code)) == true;

        bool deterministicMismatch = false;
        if (_determinismChecksEnabled)
        {
            var inputHash = ComputeHash(context);
            var outputHash = ComputeHash(new
            {
                selected = result.SelectedDecision?.ConnectionId,
                gate = result.PolicyGate,
                requiresManualApproval = result.RequiresManualApproval,
                reasons = result.SelectedDecision?.ReasonCodes,
                fallbacks = result.SelectedDecision?.FallbackConnectionIds
            });

            var prior = _determinismHashes.GetOrAdd(inputHash, outputHash);
            deterministicMismatch = !string.Equals(prior, outputHash, StringComparison.Ordinal);
            PrometheusMetrics.RecordKernelDeterminismCheck(domain, !deterministicMismatch);

            if (_determinismHashes.Count > 4096)
            {
                foreach (var key in _determinismHashes.Keys.Take(512))
                {
                    _determinismHashes.TryRemove(key, out _);
                }
            }
        }

        var snapshot = state.Record(
            DateTimeOffset.UtcNow,
            latencyMs,
            selectedScore,
            severity,
            hasReasonCodes,
            deterministicMismatch);

        PrometheusMetrics.RecordKernelExecution(domain, latencyMs);
        PrometheusMetrics.SetKernelReasonCoverage(domain, snapshot.ReasonCodeCoveragePercent);
        PrometheusMetrics.SetKernelDriftScore(domain, "score", snapshot.ScoreDrift);
        PrometheusMetrics.SetKernelDriftScore(domain, "severity", snapshot.SeverityDrift);
        PrometheusMetrics.SetKernelCriticalSeverityRate(domain, snapshot.CriticalRateShortWindow, snapshot.CriticalJumpAlertTriggered);
    }

    public KernelObservabilitySnapshot GetSnapshot()
    {
        var domainSnapshots = _states
            .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static entry => entry.Value.ToSnapshot(entry.Key))
            .ToArray();

        var alertCount = domainSnapshots.Sum(static item => item.CriticalJumpAlertCount);
        return new KernelObservabilitySnapshot(
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Domains: domainSnapshots,
            AlertCount: alertCount,
            DeterminismChecksEnabled: _determinismChecksEnabled);
    }

    private static double ResolveTrustScore(
        ProviderRouteResult result,
        IReadOnlyDictionary<string, ProviderConnectionHealthSnapshot> healthByConnection)
    {
        if (result.SelectedDecision is null)
            return 0;

        if (healthByConnection.TryGetValue(result.SelectedDecision.ConnectionId, out var health))
            return Math.Clamp(health.Score, 0, 100);

        return result.SelectedDecision.IsHealthy ? 100 : 25;
    }

    private static KernelSeverityLevel ClassifySeverity(ProviderRouteResult result)
    {
        if (!result.IsSuccess)
            return KernelSeverityLevel.Critical;

        if (result.SelectedDecision is null)
            return KernelSeverityLevel.Critical;

        if (!result.SelectedDecision.IsHealthy)
            return KernelSeverityLevel.High;

        return result.RequiresManualApproval ? KernelSeverityLevel.Medium : KernelSeverityLevel.Low;
    }

    private static string ComputeHash<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, HashJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}

public readonly record struct KernelExecutionScope(string Domain, long StartTimestamp);

public sealed record KernelObservabilitySnapshot(
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<KernelDomainSnapshot> Domains,
    int AlertCount,
    bool DeterminismChecksEnabled);

public sealed record KernelDomainSnapshot(
    string Domain,
    long Evaluations,
    double ThroughputPerMinute,
    KernelLatencyPercentiles Latency,
    double ReasonCodeCoveragePercent,
    double ScoreDrift,
    double SeverityDrift,
    double CriticalRateShortWindow,
    double CriticalRateLongWindow,
    int CriticalJumpAlertCount,
    bool CriticalJumpActive,
    long DeterminismMismatches,
    DateTimeOffset LastUpdatedUtc);

public sealed record KernelLatencyPercentiles(double P50Ms, double P95Ms, double P99Ms);

internal enum KernelSeverityLevel
{
    Low,
    Medium,
    High,
    Critical
}

internal sealed class DomainKernelState
{
    private readonly object _sync = new();
    private readonly Queue<KernelPoint> _points = new();
    private readonly Queue<DateTimeOffset> _throughputWindow = new();

    private const int MaxPoints = 240;
    private static readonly TimeSpan ThroughputWindowSize = TimeSpan.FromMinutes(1);

    private long _evaluations;
    private long _withReasons;
    private long _determinismMismatches;
    private int _criticalJumpAlertCount;
    private bool _criticalJumpActive;
    private DateTimeOffset _lastUpdatedUtc;

    public DomainKernelSnapshotState Record(
        DateTimeOffset now,
        double latencyMs,
        double score,
        KernelSeverityLevel severity,
        bool hasReasonCodes,
        bool deterministicMismatch)
    {
        lock (_sync)
        {
            _evaluations++;
            if (hasReasonCodes)
                _withReasons++;

            if (deterministicMismatch)
                _determinismMismatches++;

            _points.Enqueue(new KernelPoint(latencyMs, score, severity, now));
            while (_points.Count > MaxPoints)
            {
                _points.Dequeue();
            }

            _throughputWindow.Enqueue(now);
            PruneThroughputWindow(now);

            var sample = _points.ToArray();
            var shortWindow = sample.TakeLast(Math.Min(30, sample.Length)).ToArray();
            var longWindow = sample.TakeLast(Math.Min(120, sample.Length)).ToArray();

            var scoreShort = shortWindow.Length == 0 ? 0 : shortWindow.Average(static p => p.Score);
            var scoreLong = longWindow.Length == 0 ? scoreShort : longWindow.Average(static p => p.Score);
            var scoreDrift = Math.Abs(scoreShort - scoreLong);

            var severityShort = shortWindow.Length == 0 ? 0 : shortWindow.Average(static p => (double)p.Severity);
            var severityLong = longWindow.Length == 0 ? severityShort : longWindow.Average(static p => (double)p.Severity);
            var severityDrift = Math.Abs(severityShort - severityLong);

            var criticalShort = shortWindow.Length == 0
                ? 0
                : shortWindow.Count(static p => p.Severity == KernelSeverityLevel.Critical) / (double)shortWindow.Length;
            var criticalLong = longWindow.Length == 0
                ? criticalShort
                : longWindow.Count(static p => p.Severity == KernelSeverityLevel.Critical) / (double)longWindow.Length;

            var jumpActive = IsCriticalJump(criticalShort, criticalLong);
            var jumpTriggered = jumpActive && !_criticalJumpActive;
            _criticalJumpActive = jumpActive;
            if (jumpTriggered)
            {
                _criticalJumpAlertCount++;
            }

            _lastUpdatedUtc = now;

            return new DomainKernelSnapshotState(
                ReasonCodeCoveragePercent: _evaluations == 0 ? 0 : (_withReasons * 100.0 / _evaluations),
                ScoreDrift: scoreDrift,
                SeverityDrift: severityDrift,
                CriticalRateShortWindow: criticalShort,
                CriticalRateLongWindow: criticalLong,
                CriticalJumpAlertTriggered: jumpTriggered,
                ThroughputPerMinute: _throughputWindow.Count);
        }
    }

    public KernelDomainSnapshot ToSnapshot(string domain)
    {
        lock (_sync)
        {
            PruneThroughputWindow(DateTimeOffset.UtcNow);
            var latencies = _points.Select(static p => p.LatencyMs).OrderBy(static value => value).ToArray();
            var shortWindow = _points.TakeLast(Math.Min(30, _points.Count)).ToArray();
            var longWindow = _points.TakeLast(Math.Min(120, _points.Count)).ToArray();
            var criticalShort = shortWindow.Length == 0
                ? 0
                : shortWindow.Count(static p => p.Severity == KernelSeverityLevel.Critical) / (double)shortWindow.Length;
            var criticalLong = longWindow.Length == 0
                ? criticalShort
                : longWindow.Count(static p => p.Severity == KernelSeverityLevel.Critical) / (double)longWindow.Length;
            return new KernelDomainSnapshot(
                Domain: domain,
                Evaluations: _evaluations,
                ThroughputPerMinute: _throughputWindow.Count,
                Latency: new KernelLatencyPercentiles(
                    P50Ms: Percentile(latencies, 0.50),
                    P95Ms: Percentile(latencies, 0.95),
                    P99Ms: Percentile(latencies, 0.99)),
                ReasonCodeCoveragePercent: _evaluations == 0 ? 0 : (_withReasons * 100.0 / _evaluations),
                ScoreDrift: CalculateDrift(shortWindow, longWindow, static p => p.Score),
                SeverityDrift: CalculateDrift(shortWindow, longWindow, static p => (double)p.Severity),
                CriticalRateShortWindow: criticalShort,
                CriticalRateLongWindow: criticalLong,
                CriticalJumpAlertCount: _criticalJumpAlertCount,
                CriticalJumpActive: _criticalJumpActive,
                DeterminismMismatches: _determinismMismatches,
                LastUpdatedUtc: _lastUpdatedUtc);
        }
    }

    private void PruneThroughputWindow(DateTimeOffset now)
    {
        while (_throughputWindow.Count > 0 && now - _throughputWindow.Peek() > ThroughputWindowSize)
        {
            _throughputWindow.Dequeue();
        }
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
            return 0;

        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        index = Math.Clamp(index, 0, sorted.Count - 1);
        return sorted[index];
    }

    private static bool IsCriticalJump(double shortRate, double longRate)
    {
        if (shortRate < 0.25)
            return false;

        if (longRate == 0)
            return shortRate >= 0.35;

        return shortRate >= longRate * 2 && (shortRate - longRate) >= 0.15;
    }

    private static double CalculateDrift(
        IReadOnlyList<KernelPoint> shortWindow,
        IReadOnlyList<KernelPoint> longWindow,
        Func<KernelPoint, double> selector)
    {
        if (shortWindow.Count == 0 && longWindow.Count == 0)
            return 0;

        var shortValue = shortWindow.Count == 0 ? 0 : shortWindow.Average(selector);
        var longValue = longWindow.Count == 0 ? shortValue : longWindow.Average(selector);
        return Math.Abs(shortValue - longValue);
    }

    private sealed record KernelPoint(double LatencyMs, double Score, KernelSeverityLevel Severity, DateTimeOffset Timestamp);
}

internal sealed record DomainKernelSnapshotState(
    double ReasonCodeCoveragePercent,
    double ScoreDrift,
    double SeverityDrift,
    double CriticalRateShortWindow,
    double CriticalRateLongWindow,
    bool CriticalJumpAlertTriggered,
    double ThroughputPerMinute);
