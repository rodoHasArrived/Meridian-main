using System.Collections.Concurrent;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Infrastructure.Contracts;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Failover;

/// <summary>
/// Orchestrates automatic failover between streaming market data providers.
/// Monitors provider health via <see cref="IConnectionHealthMonitor"/>, evaluates
/// configured <see cref="FailoverRuleConfig"/> rules, and triggers switchover
/// when consecutive failures exceed the threshold.
/// </summary>
/// <remarks>
/// Implements ADR-001 provider abstraction with runtime failover coordination
/// that was previously missing for streaming providers.
/// </remarks>
[ImplementsAdr("ADR-001", "Runtime streaming provider failover orchestration")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class StreamingFailoverService : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<StreamingFailoverService>();
    private readonly ConcurrentDictionary<string, ProviderHealthState> _providerHealth = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FailoverRuleState> _ruleStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly IConnectionHealthMonitor _healthMonitor;
    private readonly object _failoverGate = new();
    private Timer? _evaluationTimer;
    private volatile bool _isDisposed;

    /// <summary>
    /// Raised when a failover is triggered, providing the rule ID, the failed provider, and the new active provider.
    /// </summary>
    public event Action<FailoverTriggeredEvent>? OnFailoverTriggered;

    /// <summary>
    /// Raised when a provider recovers and becomes the active provider again.
    /// </summary>
    public event Action<FailoverRecoveredEvent>? OnFailoverRecovered;

    public StreamingFailoverService(IConnectionHealthMonitor healthMonitor)
    {
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));

        _healthMonitor.OnConnectionLost += HandleConnectionLost;
        _healthMonitor.OnConnectionRecovered += HandleConnectionRecovered;
        _healthMonitor.OnHeartbeatMissed += HandleHeartbeatMissed;
    }

    /// <summary>
    /// Starts periodic health evaluation based on the configured interval.
    /// </summary>
    public void Start(DataSourcesConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.EnableFailover)
        {
            _log.Information("Streaming failover is disabled in configuration");
            return;
        }

        var rules = config.FailoverRules ?? Array.Empty<FailoverRuleConfig>();
        if (rules.Length == 0)
        {
            _log.Information("No failover rules configured; streaming failover will not activate");
            return;
        }

        foreach (var rule in rules)
        {
            _ruleStates[rule.Id] = new FailoverRuleState(rule);
            _log.Information("Loaded failover rule {RuleId}: primary={Primary}, backups=[{Backups}], threshold={Threshold}",
                rule.Id, rule.PrimaryProviderId, string.Join(", ", rule.BackupProviderIds), rule.FailoverThreshold);
        }

        var interval = TimeSpan.FromSeconds(Math.Max(config.HealthCheckIntervalSeconds, 1));
        _evaluationTimer = new Timer(EvaluateHealth, config, interval, interval);

        _log.Information("Streaming failover started with {RuleCount} rules, evaluation interval {Interval}s",
            rules.Length, config.HealthCheckIntervalSeconds);
    }

    /// <summary>
    /// Registers a provider for health tracking.
    /// </summary>
    public void RegisterProvider(string providerId)
    {
        _providerHealth.GetOrAdd(providerId, _ => new ProviderHealthState(providerId));
        _healthMonitor.RegisterConnection(providerId, providerId);
        _log.Debug("Registered provider {ProviderId} for failover monitoring", providerId);
    }

    /// <summary>
    /// Records a successful operation for a provider (resets failure counter, increments success counter).
    /// </summary>
    public void RecordSuccess(string providerId)
    {
        if (_providerHealth.TryGetValue(providerId, out var state))
        {
            state.RecordSuccess();
        }
        _healthMonitor.RecordDataReceived(providerId);
    }

    /// <summary>
    /// Records a failure for a provider.
    /// </summary>
    public void RecordFailure(string providerId, string reason)
    {
        if (_providerHealth.TryGetValue(providerId, out var state))
        {
            state.RecordFailure(reason);
            _log.Warning("Provider {ProviderId} failure recorded: {Reason} (consecutive: {Count})",
                providerId, reason, state.ConsecutiveFailures);
        }
    }

    /// <summary>
    /// Records latency for a provider. If latency exceeds the configured threshold for any rule,
    /// it counts as a failure for that rule.
    /// </summary>
    public void RecordLatency(string providerId, double latencyMs)
    {
        if (_providerHealth.TryGetValue(providerId, out var state))
        {
            state.RecordLatency(latencyMs);
        }
    }

    /// <summary>
    /// Forces failover for a specific rule to a target provider.
    /// </summary>
    /// <returns>True if the failover was executed.</returns>
    public bool ForceFailover(string ruleId, string targetProviderId)
    {
        if (!_ruleStates.TryGetValue(ruleId, out var ruleState))
        {
            _log.Warning("Force failover requested for unknown rule {RuleId}", ruleId);
            return false;
        }

        lock (_failoverGate)
        {
            var allProviderIds = new[] { ruleState.Rule.PrimaryProviderId }
                .Concat(ruleState.Rule.BackupProviderIds);

            if (!allProviderIds.Contains(targetProviderId, StringComparer.OrdinalIgnoreCase))
            {
                _log.Warning("Target provider {TargetProviderId} is not in rule {RuleId} provider list",
                    targetProviderId, ruleId);
                return false;
            }

            var previousProviderId = ruleState.CurrentActiveProviderId;
            ruleState.SwitchTo(targetProviderId);

            _log.Information("Forced failover for rule {RuleId}: {From} -> {To}",
                ruleId, previousProviderId, targetProviderId);

            RaiseFailoverTriggered(ruleId, previousProviderId, targetProviderId, "Manual force failover");
            return true;
        }
    }

    /// <summary>
    /// Gets the current active provider ID for a given rule.
    /// </summary>
    public string? GetActiveProviderId(string ruleId)
    {
        return _ruleStates.TryGetValue(ruleId, out var state) ? state.CurrentActiveProviderId : null;
    }

    /// <summary>
    /// Gets the current failover state for all rules.
    /// </summary>
    public IReadOnlyList<FailoverRuleSnapshot> GetRuleSnapshots()
    {
        return _ruleStates.Values.Select(s => s.GetSnapshot()).ToList();
    }

    /// <summary>
    /// Gets the health state for all registered providers.
    /// </summary>
    public IReadOnlyList<ProviderHealthSnapshot> GetProviderHealthSnapshots()
    {
        return _providerHealth.Values.Select(p => p.GetSnapshot()).ToList();
    }

    private void EvaluateHealth(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            lock (_failoverGate)
            {
                foreach (var kvp in _ruleStates)
                {
                    EvaluateRule(kvp.Value);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during failover health evaluation");
        }
    }

    private void EvaluateRule(FailoverRuleState ruleState)
    {
        var rule = ruleState.Rule;
        var activeId = ruleState.CurrentActiveProviderId;

        // Check if the active provider is unhealthy
        if (_providerHealth.TryGetValue(activeId, out var activeHealth))
        {
            var shouldFailover = activeHealth.ConsecutiveFailures >= rule.FailoverThreshold;

            // Also check latency threshold if configured
            if (!shouldFailover && rule.MaxLatencyMs > 0 && activeHealth.AverageLatencyMs > rule.MaxLatencyMs)
            {
                _log.Warning("Provider {ProviderId} exceeds latency threshold: {Latency:F1}ms > {Max:F1}ms",
                    activeId, activeHealth.AverageLatencyMs, rule.MaxLatencyMs);
                shouldFailover = true;
            }

            if (shouldFailover && !ruleState.IsInFailoverState)
            {
                // Try to failover to the next healthy backup
                var nextProvider = FindNextHealthyProvider(rule, activeId);
                if (nextProvider != null)
                {
                    var previousId = activeId;
                    ruleState.SwitchTo(nextProvider);
                    ruleState.MarkFailoverState(true);

                    _log.Warning("Automatic failover triggered for rule {RuleId}: {From} -> {To} (failures: {Failures})",
                        rule.Id, previousId, nextProvider, activeHealth.ConsecutiveFailures);

                    RaiseFailoverTriggered(rule.Id, previousId, nextProvider,
                        $"Consecutive failures ({activeHealth.ConsecutiveFailures}) exceeded threshold ({rule.FailoverThreshold})");
                }
                else
                {
                    _log.Error("All providers exhausted for rule {RuleId}; no healthy backup available", rule.Id);
                }
            }
        }

        // Check for recovery: if primary is not active and has recovered, switch back
        if (ruleState.IsInFailoverState && rule is { } r)
        {
            var primaryId = r.PrimaryProviderId;
            if (_providerHealth.TryGetValue(primaryId, out var primaryHealth))
            {
                if (primaryHealth.ConsecutiveSuccesses >= r.RecoveryThreshold)
                {
                    var previousId = ruleState.CurrentActiveProviderId;
                    ruleState.SwitchTo(primaryId);
                    ruleState.MarkFailoverState(false);

                    _log.Information("Auto-recovery for rule {RuleId}: {From} -> {To} (primary recovered with {Successes} consecutive successes)",
                        r.Id, previousId, primaryId, primaryHealth.ConsecutiveSuccesses);

                    RaiseFailoverRecovered(r.Id, previousId, primaryId);
                }
            }
        }
    }

    private string? FindNextHealthyProvider(FailoverRuleConfig rule, string currentActiveId)
    {
        // Build the ordered list: primary first, then backups in order
        var allProviders = new[] { rule.PrimaryProviderId }
            .Concat(rule.BackupProviderIds)
            .Where(id => !string.Equals(id, currentActiveId, StringComparison.OrdinalIgnoreCase));

        foreach (var providerId in allProviders)
        {
            if (_providerHealth.TryGetValue(providerId, out var health))
            {
                if (health.ConsecutiveFailures < rule.FailoverThreshold)
                {
                    return providerId;
                }
            }
            else
            {
                // Provider not tracked yet — assume healthy (it hasn't failed yet)
                return providerId;
            }
        }

        return null;
    }

    private void HandleConnectionLost(ConnectionLostEvent evt)
    {
        RecordFailure(evt.ConnectionId, evt.Reason ?? "Connection lost");
    }

    private void HandleConnectionRecovered(ConnectionRecoveredEvent evt)
    {
        RecordSuccess(evt.ConnectionId);
    }

    private void HandleHeartbeatMissed(HeartbeatMissedEvent evt)
    {
        if (evt.MissedCount >= 2)
        {
            RecordFailure(evt.ConnectionId, $"Heartbeat missed ({evt.MissedCount} consecutive)");
        }
    }

    private void RaiseFailoverTriggered(string ruleId, string fromProvider, string toProvider, string reason)
    {
        try
        {
            OnFailoverTriggered?.Invoke(new FailoverTriggeredEvent(ruleId, fromProvider, toProvider, reason, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in failover triggered event handler");
        }
    }

    private void RaiseFailoverRecovered(string ruleId, string fromProvider, string toProvider)
    {
        try
        {
            OnFailoverRecovered?.Invoke(new FailoverRecoveredEvent(ruleId, fromProvider, toProvider, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in failover recovered event handler");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        _evaluationTimer?.Dispose();
        _healthMonitor.OnConnectionLost -= HandleConnectionLost;
        _healthMonitor.OnConnectionRecovered -= HandleConnectionRecovered;
        _healthMonitor.OnHeartbeatMissed -= HandleHeartbeatMissed;
    }
}

/// <summary>
/// Mutable state tracking for a single provider's health.
/// </summary>
internal sealed class ProviderHealthState
{
    private readonly object _gate = new();
    private readonly List<string> _recentIssues = new();
    private const int MaxRecentIssues = 20;

    public string ProviderId { get; }
    public int ConsecutiveFailures { get; private set; }
    public int ConsecutiveSuccesses { get; private set; }
    public DateTimeOffset? LastFailureTime { get; private set; }
    public DateTimeOffset? LastSuccessTime { get; private set; }
    public double AverageLatencyMs { get; private set; }

    private long _latencySamples;
    private double _latencySum;

    public ProviderHealthState(string providerId)
    {
        ProviderId = providerId;
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            ConsecutiveSuccesses++;
            ConsecutiveFailures = 0;
            LastSuccessTime = DateTimeOffset.UtcNow;
        }
    }

    public void RecordFailure(string reason)
    {
        lock (_gate)
        {
            ConsecutiveFailures++;
            ConsecutiveSuccesses = 0;
            LastFailureTime = DateTimeOffset.UtcNow;

            _recentIssues.Add($"[{DateTimeOffset.UtcNow:HH:mm:ss}] {reason}");
            if (_recentIssues.Count > MaxRecentIssues)
                _recentIssues.RemoveAt(0);
        }
    }

    public void RecordLatency(double latencyMs)
    {
        lock (_gate)
        {
            _latencySamples++;
            _latencySum += latencyMs;
            AverageLatencyMs = _latencySum / _latencySamples;
        }
    }

    public ProviderHealthSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new ProviderHealthSnapshot(
                ProviderId,
                ConsecutiveFailures,
                ConsecutiveSuccesses,
                LastFailureTime,
                LastSuccessTime,
                AverageLatencyMs,
                _recentIssues.ToArray());
        }
    }
}

/// <summary>
/// Mutable state tracking for a single failover rule.
/// </summary>
internal sealed class FailoverRuleState
{
    public FailoverRuleConfig Rule { get; }
    public string CurrentActiveProviderId { get; private set; }
    public bool IsInFailoverState { get; private set; }
    public DateTimeOffset? LastFailoverTime { get; private set; }
    public int FailoverCount { get; private set; }

    public FailoverRuleState(FailoverRuleConfig rule)
    {
        Rule = rule;
        CurrentActiveProviderId = rule.PrimaryProviderId;
    }

    public void SwitchTo(string providerId)
    {
        CurrentActiveProviderId = providerId;
        LastFailoverTime = DateTimeOffset.UtcNow;
        FailoverCount++;
    }

    public void MarkFailoverState(bool inFailover)
    {
        IsInFailoverState = inFailover;
    }

    public FailoverRuleSnapshot GetSnapshot()
    {
        return new FailoverRuleSnapshot(
            Rule.Id,
            Rule.PrimaryProviderId,
            Rule.BackupProviderIds,
            CurrentActiveProviderId,
            IsInFailoverState,
            LastFailoverTime,
            FailoverCount,
            Rule.FailoverThreshold,
            Rule.RecoveryThreshold);
    }
}

// --- Event and snapshot records ---

/// <summary>
/// Raised when an automatic or manual failover is triggered.
/// </summary>
public readonly record struct FailoverTriggeredEvent(
    string RuleId,
    string FromProviderId,
    string ToProviderId,
    string Reason,
    DateTimeOffset Timestamp);

/// <summary>
/// Raised when the primary provider recovers and is restored as active.
/// </summary>
public readonly record struct FailoverRecoveredEvent(
    string RuleId,
    string FromProviderId,
    string ToProviderId,
    DateTimeOffset Timestamp);

/// <summary>
/// Point-in-time snapshot of a failover rule's state.
/// </summary>
public readonly record struct FailoverRuleSnapshot(
    string RuleId,
    string PrimaryProviderId,
    string[] BackupProviderIds,
    string CurrentActiveProviderId,
    bool IsInFailoverState,
    DateTimeOffset? LastFailoverTime,
    int FailoverCount,
    int FailoverThreshold,
    int RecoveryThreshold);

/// <summary>
/// Point-in-time snapshot of a provider's health.
/// </summary>
public readonly record struct ProviderHealthSnapshot(
    string ProviderId,
    int ConsecutiveFailures,
    int ConsecutiveSuccesses,
    DateTimeOffset? LastFailureTime,
    DateTimeOffset? LastSuccessTime,
    double AverageLatencyMs,
    string[] RecentIssues);
