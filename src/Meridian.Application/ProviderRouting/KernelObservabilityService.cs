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
        var domain = context.Capability.ToString();
        var state = _states.GetOrAdd(domain, static _ => new DomainKernelState());
        var latencyMs = Stopwatch.GetElapsedTime(scope.StartTimestamp).TotalMilliseconds;
        var selectedScore = ResolveTrustScore(result, healthByConnection);
        var severity = ClassifySeverity(result);
        var hasReasonCodes = HasStructuredReasons(result);

        bool deterministicMismatch = false;
        if (_determinismChecksEnabled)
        {
            var inputHash = ComputeHash(context);
            var outputHash = ComputeHash(BuildDeterministicOutputSignature(result));

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
        PrometheusMetrics.SetKernelThroughputPerMinute(domain, snapshot.ThroughputPerMinute);
        PrometheusMetrics.SetKernelLatencyPercentile(domain, "p50", snapshot.Latency.P50Ms);
        PrometheusMetrics.SetKernelLatencyPercentile(domain, "p95", snapshot.Latency.P95Ms);
        PrometheusMetrics.SetKernelLatencyPercentile(domain, "p99", snapshot.Latency.P99Ms);
        PrometheusMetrics.SetKernelReasonCoverage(domain, snapshot.ReasonCodeCoveragePercent);
        PrometheusMetrics.SetKernelDriftScore(domain, "score", snapshot.ScoreDrift);
        PrometheusMetrics.SetKernelDriftScore(domain, "severity", snapshot.SeverityDrift);
        PrometheusMetrics.SetKernelCriticalSeverityRate(
            domain,
            snapshot.CriticalRateShortWindow,
            snapshot.CriticalJumpActive,
            snapshot.CriticalJumpAlertTriggered);
    }

    public KernelObservabilitySnapshot GetSnapshot()
    {
        var domainSnapshots = _states
            .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static entry => entry.Value.ToSnapshot(entry.Key))
            .ToArray();

        var alertCount = domainSnapshots.Sum(static item => item.CriticalJumpAlertCount);
        var activeAlertCount = domainSnapshots.Count(static item => item.CriticalJumpActive);
        return new KernelObservabilitySnapshot(
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Domains: domainSnapshots,
            AlertCount: alertCount,
            ActiveAlertCount: activeAlertCount,
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

    private static bool HasStructuredReasons(ProviderRouteResult result)
        => result.SelectedDecision?.ReasonCodes.Any(static code => !string.IsNullOrWhiteSpace(code)) == true ||
           result.Candidates.Any(static candidate => candidate.ReasonCodes.Any(static code => !string.IsNullOrWhiteSpace(code)));

    private static object BuildDeterministicOutputSignature(ProviderRouteResult result)
        => new
        {
            selected = result.SelectedDecision is null
                ? null
                : new
                {
                    result.SelectedDecision.ConnectionId,
                    result.SelectedDecision.ProviderFamilyId,
                    Capability = result.SelectedDecision.Capability.ToString(),
                    SafetyMode = result.SelectedDecision.SafetyMode.ToString(),
                    result.SelectedDecision.ScopeRank,
                    result.SelectedDecision.Priority,
                    result.SelectedDecision.IsHealthy,
                    Reasons = result.SelectedDecision.ReasonCodes.ToArray(),
                    Fallbacks = result.SelectedDecision.FallbackConnectionIds.ToArray(),
                    result.SelectedDecision.PolicyGate
                },
            result.RequiresManualApproval,
            result.PolicyGate,
            Candidates = result.Candidates.Select(static candidate => new
            {
                candidate.ConnectionId,
                candidate.ProviderFamilyId,
                Capability = candidate.Capability.ToString(),
                SafetyMode = candidate.SafetyMode.ToString(),
                candidate.ScopeRank,
                candidate.Priority,
                candidate.IsHealthy,
                Reasons = candidate.ReasonCodes.ToArray(),
                Fallbacks = candidate.FallbackConnectionIds.ToArray(),
                candidate.PolicyGate
            }).ToArray(),
            SkippedCandidates = result.SkippedCandidates.ToArray()
        };

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
    int ActiveAlertCount,
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
    int CriticalRateShortWindowSamples,
    int CriticalRateLongWindowSamples,
    int CriticalJumpAlertCount,
    bool CriticalJumpActive,
    KernelCriticalSeverityAlertThresholds CriticalJumpThresholds,
    long DeterminismMismatches,
    DateTimeOffset LastUpdatedUtc);

public sealed record KernelLatencyPercentiles(double P50Ms, double P95Ms, double P99Ms);

public sealed record KernelCriticalSeverityAlertThresholds(
    int MinimumSampleCount,
    double MinimumShortRate,
    double ZeroBaselineShortRate,
    double RelativeMultiplier,
    double AbsoluteIncrease);

internal enum KernelSeverityLevel
{
    Low,
    Medium,
    High,
    Critical
}

internal sealed class DomainKernelState
{
    private const int ShortWindowSize = 30;
    private const int LongWindowSize = 120;
    private readonly object _sync = new();
    private readonly Queue<KernelPoint> _points = new();
    private readonly Queue<DateTimeOffset> _throughputWindow = new();

    private const int MaxPoints = 240;
    private static readonly TimeSpan ThroughputWindowSize = TimeSpan.FromMinutes(1);
    private static readonly KernelCriticalSeverityAlertThresholds CriticalJumpThresholds = new(
        MinimumSampleCount: 20,
        MinimumShortRate: 0.25,
        ZeroBaselineShortRate: 0.35,
        RelativeMultiplier: 2.0,
        AbsoluteIncrease: 0.15);

    private long _evaluations;
    private long _withReasons;
    private long _determinismMismatches;
    private int _criticalJumpAlertCount;
    private bool _criticalJumpAlertActive;
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
            while (_throughputWindow.Count > 0 && now - _throughputWindow.Peek() > ThroughputWindowSize)
            {
                _throughputWindow.Dequeue();
            }

            var sample = _points.ToArray();
            var latencies = sample.Select(static p => p.LatencyMs).OrderBy(static value => value).ToArray();
            var latency = BuildLatencyPercentiles(latencies);
            var shortWindow = sample.TakeLast(Math.Min(ShortWindowSize, sample.Length)).ToArray();
            var longWindow = sample.TakeLast(Math.Min(LongWindowSize, sample.Length)).ToArray();

            var scoreDrift = CalculateScoreDistributionShift(shortWindow, longWindow);
            var severityDrift = CalculateSeverityDistributionShift(shortWindow, longWindow);
            var criticalState = EvaluateCriticalJump(shortWindow, longWindow);

            if (criticalState.IsActive && !_criticalJumpAlertActive)
            {
                _criticalJumpAlertCount++;
            }

            _criticalJumpAlertActive = criticalState.IsActive;
            _lastUpdatedUtc = now;

            return new DomainKernelSnapshotState(
                Latency: latency,
                ReasonCodeCoveragePercent: _evaluations == 0 ? 0 : (_withReasons * 100.0 / _evaluations),
                ScoreDrift: scoreDrift,
                SeverityDrift: severityDrift,
                CriticalRateShortWindow: criticalState.ShortRate,
                CriticalRateLongWindow: criticalState.LongRate,
                CriticalRateShortWindowSamples: criticalState.ShortWindowSampleCount,
                CriticalRateLongWindowSamples: criticalState.LongWindowSampleCount,
                CriticalJumpActive: criticalState.IsActive,
                CriticalJumpAlertTriggered: criticalState.IsActive && !_criticalJumpAlertActive,
                ThroughputPerMinute: _throughputWindow.Count);
        }
    }

    public KernelDomainSnapshot ToSnapshot(string domain)
    {
        lock (_sync)
        {
            var latencies = _points.Select(static p => p.LatencyMs).OrderBy(static value => value).ToArray();
            var shortWindow = _points.TakeLast(Math.Min(ShortWindowSize, _points.Count)).ToArray();
            var longWindow = _points.TakeLast(Math.Min(LongWindowSize, _points.Count)).ToArray();
            var criticalState = EvaluateCriticalJump(shortWindow, longWindow);
            return new KernelDomainSnapshot(
                Domain: domain,
                Evaluations: _evaluations,
                ThroughputPerMinute: _throughputWindow.Count,
                Latency: BuildLatencyPercentiles(latencies),
                ReasonCodeCoveragePercent: _evaluations == 0 ? 0 : (_withReasons * 100.0 / _evaluations),
                ScoreDrift: CalculateScoreDistributionShift(shortWindow, longWindow),
                SeverityDrift: CalculateSeverityDistributionShift(shortWindow, longWindow),
                CriticalRateShortWindow: criticalState.ShortRate,
                CriticalRateLongWindow: criticalState.LongRate,
                CriticalRateShortWindowSamples: criticalState.ShortWindowSampleCount,
                CriticalRateLongWindowSamples: criticalState.LongWindowSampleCount,
                CriticalJumpAlertCount: _criticalJumpAlertCount,
                CriticalJumpActive: criticalState.IsActive,
                CriticalJumpThresholds: CriticalJumpThresholds,
                DeterminismMismatches: _determinismMismatches,
                LastUpdatedUtc: _lastUpdatedUtc);
        }
    }

    private static KernelLatencyPercentiles BuildLatencyPercentiles(IReadOnlyList<double> latencies)
        => new(
            P50Ms: Percentile(latencies, 0.50),
            P95Ms: Percentile(latencies, 0.95),
            P99Ms: Percentile(latencies, 0.99));

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
            return 0;

        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        index = Math.Clamp(index, 0, sorted.Count - 1);
        return sorted[index];
    }

    private static KernelCriticalJumpState EvaluateCriticalJump(
        IReadOnlyList<KernelPoint> shortWindow,
        IReadOnlyList<KernelPoint> longWindow)
    {
        var shortRate = shortWindow.Count == 0
            ? 0
            : shortWindow.Count(static p => p.Severity == KernelSeverityLevel.Critical) / (double)shortWindow.Count;
        var longRate = longWindow.Count == 0
            ? shortRate
            : longWindow.Count(static p => p.Severity == KernelSeverityLevel.Critical) / (double)longWindow.Count;

        if (shortWindow.Count < CriticalJumpThresholds.MinimumSampleCount ||
            longWindow.Count < CriticalJumpThresholds.MinimumSampleCount)
        {
            return new KernelCriticalJumpState(
                ShortRate: shortRate,
                LongRate: longRate,
                ShortWindowSampleCount: shortWindow.Count,
                LongWindowSampleCount: longWindow.Count,
                IsActive: false);
        }

        var isActive = IsCriticalJump(shortRate, longRate);
        return new KernelCriticalJumpState(
            ShortRate: shortRate,
            LongRate: longRate,
            ShortWindowSampleCount: shortWindow.Count,
            LongWindowSampleCount: longWindow.Count,
            IsActive: isActive);
    }

    private static bool IsCriticalJump(double shortRate, double longRate)
    {
        if (shortRate < CriticalJumpThresholds.MinimumShortRate)
            return false;

        if (longRate == 0)
            return shortRate >= CriticalJumpThresholds.ZeroBaselineShortRate;

        return shortRate >= longRate * CriticalJumpThresholds.RelativeMultiplier &&
               (shortRate - longRate) >= CriticalJumpThresholds.AbsoluteIncrease;
    }

    private static double CalculateScoreDistributionShift(IReadOnlyList<KernelPoint> shortWindow, IReadOnlyList<KernelPoint> longWindow)
        => CalculateDistributionShift(shortWindow, longWindow, static point => ScoreBucket(point.Score), bucketCount: 5);

    private static double CalculateSeverityDistributionShift(IReadOnlyList<KernelPoint> shortWindow, IReadOnlyList<KernelPoint> longWindow)
        => CalculateDistributionShift(shortWindow, longWindow, static point => (int)point.Severity, bucketCount: 4);

    private static double CalculateDistributionShift(
        IReadOnlyList<KernelPoint> shortWindow,
        IReadOnlyList<KernelPoint> longWindow,
        Func<KernelPoint, int> bucketSelector,
        int bucketCount)
    {
        if (shortWindow.Count == 0 || longWindow.Count == 0)
            return 0;

        var shortDistribution = BuildDistribution(shortWindow, bucketSelector, bucketCount);
        var longDistribution = BuildDistribution(longWindow, bucketSelector, bucketCount);

        double totalVariation = 0;
        for (var i = 0; i < bucketCount; i++)
        {
            totalVariation += Math.Abs(shortDistribution[i] - longDistribution[i]);
        }

        return totalVariation / 2.0;
    }

    private static double[] BuildDistribution(
        IReadOnlyList<KernelPoint> sample,
        Func<KernelPoint, int> bucketSelector,
        int bucketCount)
    {
        var distribution = new double[bucketCount];
        if (sample.Count == 0)
            return distribution;

        foreach (var point in sample)
        {
            var bucket = Math.Clamp(bucketSelector(point), 0, bucketCount - 1);
            distribution[bucket]++;
        }

        for (var i = 0; i < distribution.Length; i++)
        {
            distribution[i] /= sample.Count;
        }

        return distribution;
    }

    private static int ScoreBucket(double score)
    {
        var normalized = Math.Clamp(score, 0, 100);
        if (normalized < 20)
            return 0;

        if (normalized < 40)
            return 1;

        if (normalized < 60)
            return 2;

        if (normalized < 80)
            return 3;

        return 4;
    }

    private sealed record KernelPoint(double LatencyMs, double Score, KernelSeverityLevel Severity, DateTimeOffset Timestamp);

    private sealed record KernelCriticalJumpState(
        double ShortRate,
        double LongRate,
        int ShortWindowSampleCount,
        int LongWindowSampleCount,
        bool IsActive);
}

internal sealed record DomainKernelSnapshotState(
    KernelLatencyPercentiles Latency,
    double ReasonCodeCoveragePercent,
    double ScoreDrift,
    double SeverityDrift,
    double CriticalRateShortWindow,
    double CriticalRateLongWindow,
    int CriticalRateShortWindowSamples,
    int CriticalRateLongWindowSamples,
    bool CriticalJumpActive,
    bool CriticalJumpAlertTriggered,
    double ThroughputPerMinute);
