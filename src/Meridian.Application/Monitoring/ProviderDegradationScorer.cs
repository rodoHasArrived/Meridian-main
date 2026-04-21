using System.Collections.Concurrent;
using Meridian.Application.Logging;
using Meridian.Contracts.RuleEvaluation;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Computes a composite health/degradation score per provider by combining
/// latency, error rate, missed heartbeats, reconnect frequency, and data quality signals.
/// Implements Roadmap H4: Graceful Provider Degradation Scoring.
/// </summary>
public sealed class ProviderDegradationScorer : IDisposable
{
    internal const string DecisionSchemaVersion = "1.0.0";
    internal const string KernelVersion = "provider-degradation-csharp-v1";
    private readonly ILogger _log = LoggingSetup.ForContext<ProviderDegradationScorer>();
    private readonly ConnectionHealthMonitor _healthMonitor;
    private readonly ProviderLatencyService _latencyService;
    private readonly ProviderDegradationConfig _config;
    private readonly ConcurrentDictionary<string, ProviderErrorTracker> _errorTrackers = new();
    private readonly Timer _scoringTimer;
    private volatile bool _isDisposed;

    private readonly ConcurrentDictionary<string, bool> _previouslyDegraded = new();

    /// <summary>
    /// Raised when a provider's degradation score crosses the configured threshold.
    /// </summary>
    public event Action<ProviderDegradedEvent>? OnProviderDegraded;

    /// <summary>
    /// Raised when a provider recovers from a degraded state.
    /// </summary>
    public event Action<ProviderRecoveredEvent>? OnProviderRecovered;

    public ProviderDegradationScorer(
        ConnectionHealthMonitor healthMonitor,
        ProviderLatencyService latencyService,
        ProviderDegradationConfig? config = null)
    {
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _latencyService = latencyService ?? throw new ArgumentNullException(nameof(latencyService));
        _config = config ?? ProviderDegradationConfig.Default;

        _scoringTimer = new Timer(
            EvaluateScores,
            null,
            TimeSpan.FromSeconds(_config.EvaluationIntervalSeconds),
            TimeSpan.FromSeconds(_config.EvaluationIntervalSeconds));

        _log.Information(
            "ProviderDegradationScorer initialized with evaluation interval {Interval}s, degradation threshold {Threshold}",
            _config.EvaluationIntervalSeconds, _config.DegradationThreshold);
    }

    /// <summary>
    /// Records an error for a provider.
    /// </summary>
    public void RecordError(string providerName, string errorType)
    {
        if (_isDisposed || string.IsNullOrEmpty(providerName))
            return;

        var tracker = _errorTrackers.GetOrAdd(providerName, _ => new ProviderErrorTracker(_config.ErrorWindowSeconds));
        tracker.RecordError(errorType);
    }

    /// <summary>
    /// Records a successful operation for a provider (reduces error rate).
    /// </summary>
    public void RecordSuccess(string providerName)
    {
        if (_isDisposed || string.IsNullOrEmpty(providerName))
            return;

        var tracker = _errorTrackers.GetOrAdd(providerName, _ => new ProviderErrorTracker(_config.ErrorWindowSeconds));
        tracker.RecordSuccess();
    }

    /// <summary>
    /// Gets the degradation score for a specific provider.
    /// Score ranges from 0.0 (fully healthy) to 1.0 (fully degraded).
    /// </summary>
    public ProviderDegradationScore GetScore(string providerName)
    {
        var connectionStatus = _healthMonitor.GetConnectionStatusByProvider(providerName);
        var latencyHistogram = _latencyService.GetHistogram(providerName);
        _errorTrackers.TryGetValue(providerName, out var errorTracker);

        return ComputeScore(providerName, connectionStatus, latencyHistogram, errorTracker);
    }

    /// <summary>
    /// Gets degradation scores for all known providers.
    /// </summary>
    public IReadOnlyList<ProviderDegradationScore> GetAllScores()
    {
        var healthSnapshot = _healthMonitor.GetSnapshot();
        var providerNames = new HashSet<string>();

        foreach (var conn in healthSnapshot.Connections)
            providerNames.Add(conn.ProviderName);
        foreach (var key in _errorTrackers.Keys)
            providerNames.Add(key);

        var scores = new List<ProviderDegradationScore>();
        foreach (var name in providerNames)
        {
            scores.Add(GetScore(name));
        }

        return scores.OrderByDescending(s => s.CompositeScore).ToList();
    }

    /// <summary>
    /// Gets providers ranked by health (least degraded first).
    /// </summary>
    public IReadOnlyList<string> GetProvidersByHealth()
    {
        return GetAllScores()
            .OrderBy(s => s.CompositeScore)
            .Select(s => s.ProviderName)
            .ToList();
    }

    /// <summary>
    /// Returns true if the provider is considered degraded.
    /// </summary>
    public bool IsDegraded(string providerName)
    {
        var score = GetScore(providerName);
        return score.CompositeScore >= _config.DegradationThreshold;
    }

    private ProviderDegradationScore ComputeScore(
        string providerName,
        ConnectionStatus? connectionStatus,
        ProviderLatencyHistogram? latencyHistogram,
        ProviderErrorTracker? errorTracker)
    {
        var decisionReasons = new List<DecisionReason>(capacity: 4);
        // Component 1: Connection health (0.0 = healthy, 1.0 = disconnected)
        double connectionScore = 0.0;
        if (connectionStatus.HasValue)
        {
            var conn = connectionStatus.Value;
            if (!conn.IsConnected)
            {
                connectionScore = 1.0;
                var connectionContribution = connectionScore * _config.ConnectionWeight;
                decisionReasons.Add(new DecisionReason(
                    RuleId: "provider-degradation.connection-connectivity",
                    Weight: connectionContribution,
                    ReasonCode: "CONNECTION_DISCONNECTED",
                    HumanExplanation: "Provider connection is disconnected.",
                    Severity: DecisionSeverity.Critical,
                    EvidenceRefs:
                    [
                        $"provider:{providerName}",
                        $"component-score:{connectionScore:F4}",
                        $"coefficient:{_config.ConnectionWeight:F4}"
                    ]));
            }
            else
            {
                // Missed heartbeats contribute to degradation
                connectionScore = Math.Min(1.0, conn.MissedHeartbeats / (double)_config.MaxMissedHeartbeatsForFullDegradation);
                if (connectionScore > 0)
                {
                    var connectionContribution = connectionScore * _config.ConnectionWeight;
                    decisionReasons.Add(new DecisionReason(
                        RuleId: "provider-degradation.connection-heartbeats",
                        Weight: connectionContribution,
                        ReasonCode: "MISSED_HEARTBEATS",
                        HumanExplanation: $"Provider missed {conn.MissedHeartbeats} heartbeat(s).",
                        Severity: DecisionSeverity.Warning,
                        EvidenceRefs:
                        [
                            $"provider:{providerName}",
                            $"component-score:{connectionScore:F4}",
                            $"coefficient:{_config.ConnectionWeight:F4}"
                        ]));
                }
            }
        }

        // Component 2: Latency degradation (0.0 = within threshold, 1.0 = at/above max threshold)
        double latencyScore = 0.0;
        if (latencyHistogram != null)
        {
            var p95 = latencyHistogram.P95Ms;
            if (p95 > _config.LatencyThresholdMs)
            {
                latencyScore = Math.Min(1.0,
                    (p95 - _config.LatencyThresholdMs) / (_config.LatencyMaxMs - _config.LatencyThresholdMs));
                var latencyContribution = latencyScore * _config.LatencyWeight;
                decisionReasons.Add(new DecisionReason(
                    RuleId: "provider-degradation.latency-p95",
                    Weight: latencyContribution,
                    ReasonCode: "LATENCY_P95_HIGH",
                    HumanExplanation: $"P95 latency {p95:F1}ms exceeded threshold {_config.LatencyThresholdMs:F1}ms.",
                    Severity: DecisionSeverity.Warning,
                    EvidenceRefs:
                    [
                        $"p95-latency-ms:{p95:F1}",
                        $"component-score:{latencyScore:F4}",
                        $"coefficient:{_config.LatencyWeight:F4}"
                    ]));
            }
        }

        // Component 3: Error rate (0.0 = no errors, 1.0 = at/above max error rate)
        double errorScore = 0.0;
        double errorRate = 0.0;
        if (errorTracker != null)
        {
            errorRate = errorTracker.GetErrorRate();
            if (errorRate > _config.ErrorRateThreshold)
            {
                errorScore = Math.Min(1.0,
                    (errorRate - _config.ErrorRateThreshold) / (1.0 - _config.ErrorRateThreshold));
                var errorContribution = errorScore * _config.ErrorRateWeight;
                decisionReasons.Add(new DecisionReason(
                    RuleId: "provider-degradation.error-rate",
                    Weight: errorContribution,
                    ReasonCode: "ERROR_RATE_HIGH",
                    HumanExplanation: $"Error rate {errorRate:P1} exceeded threshold {_config.ErrorRateThreshold:P1}.",
                    Severity: DecisionSeverity.Error,
                    EvidenceRefs:
                    [
                        $"error-rate:{errorRate:F4}",
                        $"component-score:{errorScore:F4}",
                        $"coefficient:{_config.ErrorRateWeight:F4}"
                    ]));
            }
        }

        // Component 4: Reconnect frequency (0.0 = stable, 1.0 = unstable)
        double reconnectScore = 0.0;
        if (connectionStatus.HasValue && connectionStatus.Value.ReconnectCount > 0)
        {
            var uptimeHours = connectionStatus.Value.UptimeDuration.TotalHours;
            if (uptimeHours > 0)
            {
                var reconnectsPerHour = connectionStatus.Value.ReconnectCount / uptimeHours;
                reconnectScore = Math.Min(1.0, reconnectsPerHour / _config.MaxReconnectsPerHour);
                if (reconnectScore > 0)
                {
                    var reconnectContribution = reconnectScore * _config.ReconnectWeight;
                    decisionReasons.Add(new DecisionReason(
                        RuleId: "provider-degradation.reconnect-frequency",
                        Weight: reconnectContribution,
                        ReasonCode: "RECONNECT_RATE_HIGH",
                        HumanExplanation: $"Reconnect frequency {reconnectsPerHour:F2}/hr indicates unstable connectivity.",
                        Severity: DecisionSeverity.Warning,
                        EvidenceRefs:
                        [
                            $"reconnects-per-hour:{reconnectsPerHour:F2}",
                            $"component-score:{reconnectScore:F4}",
                            $"coefficient:{_config.ReconnectWeight:F4}"
                        ]));
                }
            }
        }

        // Weighted composite score
        var composite =
            connectionScore * _config.ConnectionWeight +
            latencyScore * _config.LatencyWeight +
            errorScore * _config.ErrorRateWeight +
            reconnectScore * _config.ReconnectWeight;

        composite = Math.Clamp(composite, 0.0, 1.0);
        var decision = new DecisionResult<double>(
            Score: composite,
            Reasons: decisionReasons,
            Trace: new DecisionTrace(
                SchemaVersion: DecisionSchemaVersion,
                KernelVersion: KernelVersion,
                EvaluatedAt: DateTimeOffset.UtcNow,
                CorrelationId: null,
                Metadata: new Dictionary<string, string?>
                {
                    ["kernel"] = "provider-degradation",
                    ["provider"] = providerName
                }));

        var scoreReasons = BuildReasons(
            connectionScore * _config.ConnectionWeight,
            latencyScore * _config.LatencyWeight,
            errorScore * _config.ErrorRateWeight,
            reconnectScore * _config.ReconnectWeight);

        return new ProviderDegradationScore(
            ProviderName: providerName,
            CompositeScore: decision.Score,
            ConnectionScore: connectionScore,
            LatencyScore: latencyScore,
            ErrorRateScore: errorScore,
            ReconnectScore: reconnectScore,
            ErrorRate: errorRate,
            IsDegraded: decision.Score >= _config.DegradationThreshold,
            P95LatencyMs: latencyHistogram?.P95Ms ?? 0,
            IsConnected: connectionStatus?.IsConnected ?? false,
            Reasons: scoreReasons,
            EvaluatedAt: decision.Trace.EvaluatedAt,
            Decision: decision);
    }

    /// <summary>
    /// Computes score and reason-code deltas between two score snapshots.
    /// </summary>
    public static ProviderDegradationScoreDelta ComputeDelta(
        in ProviderDegradationScore previous,
        in ProviderDegradationScore current)
    {
        var reasonDeltas = BuildReasonDeltas(previous.Reasons, current.Reasons);
        return new ProviderDegradationScoreDelta(
            ProviderName: current.ProviderName,
            PreviousCompositeScore: previous.CompositeScore,
            CurrentCompositeScore: current.CompositeScore,
            CompositeScoreDelta: current.CompositeScore - previous.CompositeScore,
            ReasonDeltas: reasonDeltas);
    }

    private static IReadOnlyList<ProviderScoreReason> BuildReasons(
        double connectionContribution,
        double latencyContribution,
        double errorContribution,
        double reconnectContribution)
    {
        var reasons = new List<ProviderScoreReason>(4);

        AddReason(reasons, ProviderReasonCodes.ConnectionUnstable, connectionContribution);
        AddReason(reasons, ProviderReasonCodes.LatencySpike, latencyContribution);
        AddReason(reasons, ProviderReasonCodes.ErrorRateElevated, errorContribution);
        AddReason(reasons, ProviderReasonCodes.ReconnectBurst, reconnectContribution);

        return reasons
            .OrderByDescending(r => Math.Abs(r.Contribution))
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<ProviderReasonDelta> BuildReasonDeltas(
        IReadOnlyList<ProviderScoreReason> previous,
        IReadOnlyList<ProviderScoreReason> current)
    {
        var previousByCode = previous.ToDictionary(r => r.Code, r => r.Contribution, StringComparer.Ordinal);
        var currentByCode = current.ToDictionary(r => r.Code, r => r.Contribution, StringComparer.Ordinal);

        var allCodes = previousByCode.Keys
            .Concat(currentByCode.Keys)
            .Distinct(StringComparer.Ordinal);

        return allCodes
            .Select(code =>
            {
                var oldValue = previousByCode.GetValueOrDefault(code, 0.0);
                var newValue = currentByCode.GetValueOrDefault(code, 0.0);
                return new ProviderReasonDelta(code, oldValue, newValue, newValue - oldValue);
            })
            .Where(delta => Math.Abs(delta.Delta) > 1e-12)
            .OrderByDescending(delta => Math.Abs(delta.Delta))
            .ThenBy(delta => delta.Code, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddReason(ICollection<ProviderScoreReason> reasons, string code, double contribution)
    {
        if (Math.Abs(contribution) <= 1e-12)
            return;

        reasons.Add(new ProviderScoreReason(code, contribution));
    }

    /// <summary>
    /// Triggers an immediate scoring evaluation. Exposed for testing purposes.
    /// </summary>
    internal void EvaluateNow() => EvaluateScores(null);

    private void EvaluateScores(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var scores = GetAllScores();

            foreach (var score in scores)
            {
                var wasDegrade = _previouslyDegraded.GetValueOrDefault(score.ProviderName, false);

                if (score.IsDegraded)
                {
                    _log.Warning(
                        "Provider {Provider} is degraded: composite={Score:F3} (connection={Connection:F3}, latency={Latency:F3}, errors={Errors:F3}, reconnects={Reconnects:F3})",
                        score.ProviderName, score.CompositeScore,
                        score.ConnectionScore, score.LatencyScore,
                        score.ErrorRateScore, score.ReconnectScore);

                    try
                    {
                        OnProviderDegraded?.Invoke(new ProviderDegradedEvent(
                            score.ProviderName, score.CompositeScore, DateTimeOffset.UtcNow));
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error in provider degraded event handler");
                    }

                    _previouslyDegraded[score.ProviderName] = true;
                }
                else if (wasDegrade)
                {
                    _log.Information(
                        "Provider {Provider} has recovered: composite={Score:F3}",
                        score.ProviderName, score.CompositeScore);

                    try
                    {
                        OnProviderRecovered?.Invoke(new ProviderRecoveredEvent(
                            score.ProviderName, score.CompositeScore, DateTimeOffset.UtcNow));
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error in provider recovered event handler");
                    }

                    _previouslyDegraded[score.ProviderName] = false;
                }
                else
                {
                    _previouslyDegraded[score.ProviderName] = false;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error evaluating provider degradation scores");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _scoringTimer.Dispose();
        _errorTrackers.Clear();
        _previouslyDegraded.Clear();
    }

    /// <summary>
    /// Tracks errors and successes within a sliding window for error rate calculation.
    /// </summary>
    private sealed class ProviderErrorTracker
    {
        private readonly int _windowSeconds;
        private readonly ConcurrentQueue<(DateTimeOffset Timestamp, bool IsError)> _events = new();

        public ProviderErrorTracker(int windowSeconds)
        {
            _windowSeconds = windowSeconds;
        }

        public void RecordError(string errorType)
        {
            _events.Enqueue((DateTimeOffset.UtcNow, true));
            PruneOldEntries();
        }

        public void RecordSuccess()
        {
            _events.Enqueue((DateTimeOffset.UtcNow, false));
            PruneOldEntries();
        }

        public double GetErrorRate()
        {
            PruneOldEntries();

            var entries = _events.ToArray();
            if (entries.Length == 0)
                return 0.0;

            var errorCount = entries.Count(e => e.IsError);
            return (double)errorCount / entries.Length;
        }

        private void PruneOldEntries()
        {
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-_windowSeconds);
            while (_events.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            {
                _events.TryDequeue(out _);
            }
        }
    }
}

/// <summary>
/// Configuration for provider degradation scoring.
/// </summary>
public sealed record ProviderDegradationConfig
{
    /// <summary>How often to evaluate scores (seconds).</summary>
    public int EvaluationIntervalSeconds { get; init; } = 30;

    /// <summary>Composite score at or above which a provider is considered degraded (0.0-1.0).</summary>
    public double DegradationThreshold { get; init; } = 0.6;

    /// <summary>P95 latency (ms) above which latency degradation begins.</summary>
    public double LatencyThresholdMs { get; init; } = 200;

    /// <summary>P95 latency (ms) at which latency degradation is fully maxed out.</summary>
    public double LatencyMaxMs { get; init; } = 2000;

    /// <summary>Error rate (0.0-1.0) above which error degradation begins.</summary>
    public double ErrorRateThreshold { get; init; } = 0.05;

    /// <summary>Sliding window (seconds) for error rate calculation.</summary>
    public int ErrorWindowSeconds { get; init; } = 300;

    /// <summary>Reconnects per hour at which reconnect degradation is fully maxed out.</summary>
    public double MaxReconnectsPerHour { get; init; } = 10;

    /// <summary>Number of missed heartbeats for full connection degradation score.</summary>
    public int MaxMissedHeartbeatsForFullDegradation { get; init; } = 5;

    // Weights for each component (should sum to 1.0)
    /// <summary>Weight for connection health component.</summary>
    public double ConnectionWeight { get; init; } = 0.35;

    /// <summary>Weight for latency component.</summary>
    public double LatencyWeight { get; init; } = 0.25;

    /// <summary>Weight for error rate component.</summary>
    public double ErrorRateWeight { get; init; } = 0.25;

    /// <summary>Weight for reconnect frequency component.</summary>
    public double ReconnectWeight { get; init; } = 0.15;

    public static ProviderDegradationConfig Default => new();
}

/// <summary>
/// Degradation score for a single provider.
/// </summary>
public readonly record struct ProviderDegradationScore(
    string ProviderName,
    double CompositeScore,
    double ConnectionScore,
    double LatencyScore,
    double ErrorRateScore,
    double ReconnectScore,
    double ErrorRate,
    bool IsDegraded,
    double P95LatencyMs,
    bool IsConnected,
    IReadOnlyList<ProviderScoreReason> Reasons,
    DateTimeOffset EvaluatedAt,
    DecisionResult<double> Decision);

public static class ProviderReasonCodes
{
    public const string LatencySpike = "LATENCY_SPIKE";
    public const string GapBurst = "GAP_BURST";
    public const string SequenceDuplicate = "SEQUENCE_DUPLICATE";
    public const string ManualOverride = "MANUAL_OVERRIDE";
    public const string ConnectionUnstable = "CONNECTION_UNSTABLE";
    public const string ErrorRateElevated = "ERROR_RATE_ELEVATED";
    public const string ReconnectBurst = "RECONNECT_BURST";
}

public readonly record struct ProviderScoreReason(
    string Code,
    double Contribution);

public readonly record struct ProviderReasonDelta(
    string Code,
    double PreviousContribution,
    double CurrentContribution,
    double Delta);

public readonly record struct ProviderDegradationScoreDelta(
    string ProviderName,
    double PreviousCompositeScore,
    double CurrentCompositeScore,
    double CompositeScoreDelta,
    IReadOnlyList<ProviderReasonDelta> ReasonDeltas);

/// <summary>
/// Event raised when a provider becomes degraded.
/// </summary>
public readonly record struct ProviderDegradedEvent(
    string ProviderName,
    double CompositeScore,
    DateTimeOffset Timestamp);

/// <summary>
/// Event raised when a provider recovers from degradation.
/// </summary>
public readonly record struct ProviderRecoveredEvent(
    string ProviderName,
    double CompositeScore,
    DateTimeOffset Timestamp);
