using System.Collections.Concurrent;
using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Core.Monitoring;
using Meridian.Infrastructure.Adapters.Core;
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
    private readonly ConcurrentDictionary<string, Action<FailoverTransitionRequest>> _transitionHandlers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, FailoverTransitionRequest> _pendingTransitions = new();
    private readonly IConnectionHealthMonitor _healthMonitor;
    private readonly object _failoverGate = new();
    private readonly CancellationTokenSource _shutdownCts = new();
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
    public void Start(
        DataSourcesConfig config,
        IReadOnlyDictionary<string, string>? initialActiveProviderIds = null)
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

        var requestedInitialProviders = initialActiveProviderIds?.ToDictionary(
            static entry => entry.Key,
            static entry => entry.Value,
            StringComparer.OrdinalIgnoreCase);
        if (requestedInitialProviders is not null)
        {
            foreach (var ruleId in requestedInitialProviders.Keys)
            {
                if (!rules.Any(rule => string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException(
                        $"Initial active provider was supplied for unknown failover rule '{ruleId}'.",
                        nameof(initialActiveProviderIds));
                }
            }
        }

        foreach (var rule in rules)
        {
            var initialProviderId = rule.PrimaryProviderId;
            if (requestedInitialProviders?.TryGetValue(rule.Id, out var requestedProviderId) == true)
            {
                var configuredProviderIds = new[] { rule.PrimaryProviderId }
                    .Concat(rule.BackupProviderIds);
                if (!configuredProviderIds.Any(providerId =>
                        ProviderIdentity.EqualsId(providerId, requestedProviderId)))
                {
                    throw new ArgumentException(
                        $"Initial active provider '{requestedProviderId}' is not configured for failover rule '{rule.Id}'.",
                        nameof(initialActiveProviderIds));
                }

                initialProviderId = requestedProviderId;
            }

            _ruleStates[rule.Id] = new FailoverRuleState(rule, initialProviderId);
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
        var key = ProviderIdentity.NormalizeId(providerId);
        _providerHealth.GetOrAdd(key, _ => new ProviderHealthState(key));
        _healthMonitor.RegisterConnection(key, key);
        _log.Debug("Registered provider {ProviderId} for failover monitoring", key);
    }

    /// <summary>
    /// Registers the runtime that can execute provider switches for a rule. Rule state is committed
    /// only after that runtime confirms the connection and subscription hand-off completed.
    /// </summary>
    public IDisposable RegisterTransitionHandler(
        string ruleId,
        Action<FailoverTransitionRequest> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentNullException.ThrowIfNull(handler);

        lock (_failoverGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!_transitionHandlers.TryAdd(ruleId, handler))
            {
                throw new InvalidOperationException(
                    $"A streaming failover transition handler is already registered for rule '{ruleId}'.");
            }
        }

        return new TransitionHandlerRegistration(_transitionHandlers, ruleId, handler);
    }

    /// <summary>
    /// Returns whether a live runtime is registered to execute transitions for the rule.
    /// </summary>
    public bool HasLiveTransitionHandler(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            return false;

        lock (_failoverGate)
            return !_isDisposed && _transitionHandlers.ContainsKey(ruleId);
    }

    /// <summary>
    /// Records a successful operation for a provider (resets failure counter, increments success counter).
    /// </summary>
    public void RecordSuccess(string providerId)
    {
        var key = ProviderIdentity.NormalizeId(providerId);
        if (_providerHealth.TryGetValue(key, out var state))
        {
            state.RecordSuccess();
        }
        _healthMonitor.RecordDataReceived(key);

        EvaluateHealth(null);
    }

    /// <summary>
    /// Records a failure for a provider.
    /// </summary>
    public void RecordFailure(string providerId, string reason)
    {
        var key = ProviderIdentity.NormalizeId(providerId);
        if (_providerHealth.TryGetValue(key, out var state))
        {
            state.RecordFailure(reason);
            _log.Warning("Provider {ProviderId} failure recorded: {Reason} (consecutive: {Count})",
                key, reason, state.ConsecutiveFailures);
        }

        EvaluateHealth(null);
    }

    /// <summary>
    /// Records latency for a provider. If latency exceeds the configured threshold for any rule,
    /// it counts as a failure for that rule.
    /// </summary>
    public void RecordLatency(string providerId, double latencyMs)
    {
        var key = ProviderIdentity.NormalizeId(providerId);
        if (_providerHealth.TryGetValue(key, out var state))
        {
            state.RecordLatency(latencyMs);
        }

        EvaluateHealth(null);
    }

    /// <summary>
    /// Forces failover for a specific rule to a target provider.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the transition was handed to the live runtime. Use
    /// <see cref="ForceFailoverAsync"/> to await the committed outcome.
    /// </returns>
    public bool ForceFailover(string ruleId, string targetProviderId)
    {
        FailoverTransitionRequest transition;
        if (_isDisposed)
            return false;

        if (!_ruleStates.TryGetValue(ruleId, out var ruleState))
        {
            _log.Warning("Force failover requested for unknown rule {RuleId}", ruleId);
            return false;
        }

        lock (_failoverGate)
        {
            if (_isDisposed)
                return false;

            var allProviderIds = new[] { ruleState.Rule.PrimaryProviderId }
                .Concat(ruleState.Rule.BackupProviderIds);

            var targetKey = ProviderIdentity.NormalizeId(targetProviderId);
            if (!allProviderIds.Any(id => ProviderIdentity.EqualsId(id, targetKey)))
            {
                _log.Warning("Target provider {TargetProviderId} is not in rule {RuleId} provider list",
                    targetKey, ruleId);
                return false;
            }

            var previousProviderId = ruleState.CurrentActiveProviderId;
            if (ProviderIdentity.EqualsId(previousProviderId, targetKey) || ruleState.HasPendingTransition)
                return false;
            if (!_transitionHandlers.ContainsKey(ruleId))
                return false;

            transition = CreateTransitionLocked(
                ruleState,
                previousProviderId,
                targetKey,
                isRecovery: ProviderIdentity.EqualsId(targetKey, ruleState.Rule.PrimaryProviderId),
                reason: "Manual force failover",
                cancellationToken: CancellationToken.None,
                reevaluateOnRejection: false);
        }

        return DispatchTransition(transition);
    }

    /// <summary>
    /// Forces a failover and waits until the registered runtime either commits or rejects the
    /// provider hand-off. A <see langword="false"/> result never changes the active provider.
    /// </summary>
    public async Task<bool> ForceFailoverAsync(
        string ruleId,
        string targetProviderId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_isDisposed)
            return false;

        FailoverTransitionRequest transition;
        if (!_ruleStates.TryGetValue(ruleId, out var ruleState))
            return false;

        lock (_failoverGate)
        {
            if (_isDisposed)
                return false;

            var targetKey = ProviderIdentity.NormalizeId(targetProviderId);
            var allProviderIds = new[] { ruleState.Rule.PrimaryProviderId }
                .Concat(ruleState.Rule.BackupProviderIds);
            if (!allProviderIds.Any(id => ProviderIdentity.EqualsId(id, targetKey)) ||
                ProviderIdentity.EqualsId(ruleState.CurrentActiveProviderId, targetKey) ||
                ruleState.HasPendingTransition ||
                !_transitionHandlers.ContainsKey(ruleId))
            {
                return false;
            }

            transition = CreateTransitionLocked(
                ruleState,
                ruleState.CurrentActiveProviderId,
                targetKey,
                isRecovery: ProviderIdentity.EqualsId(targetKey, ruleState.Rule.PrimaryProviderId),
                reason: "Manual force failover",
                cancellationToken: ct,
                reevaluateOnRejection: false);
        }

        if (!DispatchTransition(transition))
            return false;

        try
        {
            return await transition.Completion.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            transition.TryCancel("The provider transition request was cancelled.");
            throw;
        }
    }

    /// <summary>
    /// Records an active-provider connection failure and performs an immediate, coordinator-owned
    /// hand-off to the next configured healthy provider. Each candidate transition remains
    /// two-phase: the coordinator commits its active provider only after the runtime confirms the
    /// connection and subscription hand-off.
    /// </summary>
    public async Task<bool> FailoverAfterConnectionFailureAsync(
        string ruleId,
        string failedProviderId,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failedProviderId);
        ct.ThrowIfCancellationRequested();

        var failedProviderKey = ProviderIdentity.NormalizeId(failedProviderId);
        if (_providerHealth.TryGetValue(failedProviderKey, out var failedHealth))
        {
            failedHealth.RecordFailure(reason);
            _log.Warning(
                "Provider {ProviderId} connection failure recorded: {Reason} (consecutive: {Count})",
                failedProviderKey,
                reason,
                failedHealth.ConsecutiveFailures);
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            FailoverTransitionRequest transition;
            var dispatch = false;
            lock (_failoverGate)
            {
                if (_isDisposed || !_ruleStates.TryGetValue(ruleId, out var ruleState))
                    return false;

                if (!ProviderIdentity.EqualsId(ruleState.CurrentActiveProviderId, failedProviderKey))
                    return true;
                if (!_transitionHandlers.ContainsKey(ruleId))
                    return false;

                if (ruleState.PendingTransitionId is { } pendingTransitionId &&
                    _pendingTransitions.TryGetValue(pendingTransitionId, out var pendingTransition))
                {
                    transition = pendingTransition;
                }
                else
                {
                    var nextProvider = FindNextHealthyProvider(
                        ruleState.Rule,
                        failedProviderKey,
                        includePrimary: false);
                    if (nextProvider is null)
                        return false;

                    transition = CreateTransitionLocked(
                        ruleState,
                        failedProviderKey,
                        nextProvider,
                        isRecovery: false,
                        reason,
                        ct,
                        reevaluateOnRejection: false);
                    dispatch = true;
                }
            }

            if (dispatch && !DispatchTransition(transition))
                return false;

            try
            {
                if (await transition.Completion.WaitAsync(ct).ConfigureAwait(false))
                    return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                transition.TryCancel("The provider connection failover request was cancelled.");
                throw;
            }

            // A rejected candidate is marked unavailable by CompleteTransition. Loop so the same
            // connection attempt can try the next configured provider without bypassing state.
        }
    }

    /// <summary>
    /// Gets the current active provider ID for a given rule.
    /// </summary>
    public string? GetActiveProviderId(string ruleId)
    {
        lock (_failoverGate)
            return _ruleStates.TryGetValue(ruleId, out var state) ? state.CurrentActiveProviderId : null;
    }

    /// <summary>
    /// Gets the current failover state for all rules.
    /// </summary>
    public IReadOnlyList<FailoverRuleSnapshot> GetRuleSnapshots()
    {
        lock (_failoverGate)
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
            List<FailoverTransitionRequest> transitions = [];
            lock (_failoverGate)
            {
                foreach (var kvp in _ruleStates)
                {
                    if (EvaluateRule(kvp.Value) is { } transition)
                        transitions.Add(transition);
                }
            }

            foreach (var transition in transitions)
                DispatchTransition(transition);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during failover health evaluation");
        }
    }

    private FailoverTransitionRequest? EvaluateRule(FailoverRuleState ruleState)
    {
        if (ruleState.HasPendingTransition)
            return null;

        var rule = ruleState.Rule;
        var activeId = ruleState.CurrentActiveProviderId;

        // Check if the active provider is unhealthy
        if (_providerHealth.TryGetValue(activeId, out var activeHealth))
        {
            var failoverReason = $"Consecutive failures ({activeHealth.ConsecutiveFailures}) exceeded threshold ({rule.FailoverThreshold})";
            var shouldFailover = activeHealth.ConsecutiveFailures >= rule.FailoverThreshold;

            // Also check latency threshold if configured
            if (!shouldFailover && rule.MaxLatencyMs > 0 && activeHealth.AverageLatencyMs > rule.MaxLatencyMs)
            {
                _log.Warning("Provider {ProviderId} exceeds latency threshold: {Latency:F1}ms > {Max:F1}ms",
                    activeId, activeHealth.AverageLatencyMs, rule.MaxLatencyMs);
                failoverReason = $"Latency ({activeHealth.AverageLatencyMs:F1}ms) exceeded threshold ({rule.MaxLatencyMs:F1}ms)";
                shouldFailover = true;
            }

            if (shouldFailover)
            {
                // Try to failover to the next healthy backup
                var nextProvider = FindNextHealthyProvider(
                    rule,
                    activeId,
                    includePrimary: !ruleState.IsInFailoverState);
                if (nextProvider != null)
                {
                    var previousId = activeId;
                    _log.Warning("Automatic failover requested for rule {RuleId}: {From} -> {To} (failures: {Failures})",
                        rule.Id, previousId, nextProvider, activeHealth.ConsecutiveFailures);
                    return CreateTransitionLocked(
                        ruleState,
                        previousId,
                        nextProvider,
                        isRecovery: false,
                        reason: failoverReason,
                        cancellationToken: CancellationToken.None,
                        reevaluateOnRejection: true);
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
            var primaryId = ProviderIdentity.NormalizeId(r.PrimaryProviderId);
            if (_providerHealth.TryGetValue(primaryId, out var primaryHealth))
            {
                if (primaryHealth.ConsecutiveSuccesses >= r.RecoveryThreshold &&
                    IsLatencyWithinRule(primaryHealth, r))
                {
                    var previousId = ruleState.CurrentActiveProviderId;
                    _log.Information("Auto-recovery requested for rule {RuleId}: {From} -> {To} (primary recovered with {Successes} consecutive successes)",
                        r.Id, previousId, primaryId, primaryHealth.ConsecutiveSuccesses);
                    return CreateTransitionLocked(
                        ruleState,
                        previousId,
                        primaryId,
                        isRecovery: true,
                        reason: "Primary provider met the configured recovery threshold.",
                        cancellationToken: CancellationToken.None,
                        reevaluateOnRejection: true);
                }
            }
        }

        return null;
    }

    private FailoverTransitionRequest CreateTransitionLocked(
        FailoverRuleState ruleState,
        string fromProviderId,
        string toProviderId,
        bool isRecovery,
        string reason,
        CancellationToken cancellationToken,
        bool reevaluateOnRejection)
    {
        var transitionId = Guid.NewGuid();
        var transition = new FailoverTransitionRequest(
            transitionId,
            ruleState.Rule.Id,
            fromProviderId,
            toProviderId,
            isRecovery,
            reason,
            cancellationToken,
            _shutdownCts.Token,
            reevaluateOnRejection,
            CompleteTransition);
        ruleState.MarkTransitionPending(transitionId);
        _pendingTransitions[transitionId] = transition;
        return transition;
    }

    private bool DispatchTransition(FailoverTransitionRequest transition)
    {
        if (_isDisposed)
        {
            transition.TryCancel("Streaming failover service is shutting down.");
            return false;
        }

        if (!_transitionHandlers.TryGetValue(transition.RuleId, out var handler))
        {
            transition.TryCancel(
                $"No live streaming provider runtime is registered for failover rule '{transition.RuleId}'.");
            return false;
        }

        try
        {
            handler(transition);
            // Dispatch acceptance and transition outcome are intentionally distinct. A provider
            // can reject synchronously (for example, an immediate connection failure); callers
            // awaiting the ticket must still observe that rejection and, where applicable, try
            // the next candidate.
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Streaming failover transition handler failed synchronously for rule {RuleId}",
                transition.RuleId);
            transition.TryReject("The provider transition handler failed.");
            return false;
        }
    }

    private bool CompleteTransition(
        FailoverTransitionRequest transition,
        bool succeeded,
        string? rejectionReason,
        Action? commitRuntimeState)
    {
        FailoverTriggeredEvent? triggered = null;
        FailoverRecoveredEvent? recovered = null;
        var reevaluate = false;

        lock (_failoverGate)
        {
            if (!_ruleStates.TryGetValue(transition.RuleId, out var ruleState) ||
                !ruleState.IsPendingTransition(transition.TransitionId))
            {
                return false;
            }

            if (_isDisposed || transition.CancellationToken.IsCancellationRequested)
            {
                _pendingTransitions.TryRemove(transition.TransitionId, out _);
                ruleState.ClearPendingTransition();
                return false;
            }

            if (succeeded && commitRuntimeState is not null)
            {
                try
                {
                    // The runtime supplies a non-blocking local state swap. Executing it while the
                    // coordinator gate is held makes local and coordinator commit indivisible with
                    // respect to shutdown and competing transition requests.
                    commitRuntimeState();
                }
                catch (Exception ex)
                {
                    _log.Error(
                        ex,
                        "Streaming failover runtime commit failed for rule {RuleId}: {From} -> {To}",
                        transition.RuleId,
                        transition.FromProviderId,
                        transition.ToProviderId);
                    succeeded = false;
                    rejectionReason = "The provider runtime could not commit the prepared hand-off.";
                }
            }

            _pendingTransitions.TryRemove(transition.TransitionId, out _);
            ruleState.ClearPendingTransition();

            if (!succeeded)
            {
                var rejectedProviderId = ProviderIdentity.NormalizeId(transition.ToProviderId);
                var rejectedHealth = _providerHealth.GetOrAdd(
                    rejectedProviderId,
                    static providerId => new ProviderHealthState(providerId));
                rejectedHealth.MarkUnavailable(
                    rejectionReason ?? "Provider transition was rejected.",
                    ruleState.Rule.FailoverThreshold);

                _log.Warning(
                    "Streaming failover transition rejected for rule {RuleId}: {From} -> {To}. {Reason}",
                    transition.RuleId,
                    transition.FromProviderId,
                    transition.ToProviderId,
                    rejectionReason);
                reevaluate = transition.ReevaluateOnRejection;
            }
            else
            {
                if (!transition.IsRecovery)
                {
                    var primaryProviderId = ProviderIdentity.NormalizeId(
                        ruleState.Rule.PrimaryProviderId);
                    if (_providerHealth.TryGetValue(primaryProviderId, out var primaryHealth))
                    {
                        // Recovery evidence is epoch-scoped. Successes observed before entering
                        // failover cannot justify an immediate switch back to the primary.
                        primaryHealth.ResetSuccessStreak();
                    }
                }

                ruleState.SwitchTo(transition.ToProviderId);
                ruleState.MarkFailoverState(!transition.IsRecovery);

                if (transition.IsRecovery)
                {
                    recovered = new FailoverRecoveredEvent(
                        transition.RuleId,
                        transition.FromProviderId,
                        transition.ToProviderId,
                        DateTimeOffset.UtcNow);
                }
                else
                {
                    triggered = new FailoverTriggeredEvent(
                        transition.RuleId,
                        transition.FromProviderId,
                        transition.ToProviderId,
                        transition.Reason,
                        DateTimeOffset.UtcNow);
                }
            }
        }

        if (triggered is { } failover)
            RaiseFailoverTriggered(failover);
        if (recovered is { } recovery)
            RaiseFailoverRecovered(recovery);
        if (reevaluate)
            EvaluateHealth(null);

        return succeeded;
    }

    private string? FindNextHealthyProvider(
        FailoverRuleConfig rule,
        string currentActiveId,
        bool includePrimary)
    {
        var currentActiveKey = ProviderIdentity.NormalizeId(currentActiveId);

        // Build the ordered list: primary first, then backups in order
        var allProviders = new[] { rule.PrimaryProviderId }
            .Concat(rule.BackupProviderIds)
            .Select(ProviderIdentity.NormalizeId)
            .Where(id => !string.Equals(id, currentActiveKey, StringComparison.Ordinal))
            .Where(id => includePrimary || !ProviderIdentity.EqualsId(id, rule.PrimaryProviderId));

        foreach (var providerId in allProviders)
        {
            if (_providerHealth.TryGetValue(providerId, out var health))
            {
                if (health.ConsecutiveFailures < rule.FailoverThreshold &&
                    IsLatencyWithinRule(health, rule))
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

    private static bool IsLatencyWithinRule(ProviderHealthState health, FailoverRuleConfig rule)
        => rule.MaxLatencyMs <= 0 ||
            health.AverageLatencyMs <= 0 ||
            health.AverageLatencyMs <= rule.MaxLatencyMs;

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

    private void RaiseFailoverTriggered(FailoverTriggeredEvent evt)
    {
        try
        {
            OnFailoverTriggered?.Invoke(evt);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in failover triggered event handler");
        }
    }

    private void RaiseFailoverRecovered(FailoverRecoveredEvent evt)
    {
        try
        {
            OnFailoverRecovered?.Invoke(evt);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in failover recovered event handler");
        }
    }

    public void Dispose()
    {
        FailoverTransitionRequest[] pendingTransitions;
        lock (_failoverGate)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            pendingTransitions = _pendingTransitions.Values.ToArray();
        }

        _evaluationTimer?.Dispose();
        _transitionHandlers.Clear();
        _healthMonitor.OnConnectionLost -= HandleConnectionLost;
        _healthMonitor.OnConnectionRecovered -= HandleConnectionRecovered;
        _healthMonitor.OnHeartbeatMissed -= HandleHeartbeatMissed;

        _shutdownCts.Cancel();
        foreach (var transition in pendingTransitions)
            transition.TryCancel("Streaming failover service is shutting down.");
        _shutdownCts.Dispose();
    }

    private sealed class TransitionHandlerRegistration(
        ConcurrentDictionary<string, Action<FailoverTransitionRequest>> handlers,
        string ruleId,
        Action<FailoverTransitionRequest> handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (handlers.TryGetValue(ruleId, out var registered) && ReferenceEquals(registered, handler))
                handlers.TryRemove(ruleId, out _);
        }
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
    private const int MaxRecentLatencySamples = 20;
    private readonly Queue<double> _recentLatencySamples = new();

    public string ProviderId { get; }
    public int ConsecutiveFailures { get; private set; }
    public int ConsecutiveSuccesses { get; private set; }
    public DateTimeOffset? LastFailureTime { get; private set; }
    public DateTimeOffset? LastSuccessTime { get; private set; }
    public double AverageLatencyMs { get; private set; }

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

    public void MarkUnavailable(string reason, int failureThreshold)
    {
        lock (_gate)
        {
            ConsecutiveFailures = Math.Max(ConsecutiveFailures + 1, Math.Max(failureThreshold, 1));
            ConsecutiveSuccesses = 0;
            LastFailureTime = DateTimeOffset.UtcNow;
            _recentIssues.Add($"[{DateTimeOffset.UtcNow:HH:mm:ss}] {reason}");
            if (_recentIssues.Count > MaxRecentIssues)
                _recentIssues.RemoveAt(0);
        }
    }

    public void ResetSuccessStreak()
    {
        lock (_gate)
            ConsecutiveSuccesses = 0;
    }

    public void RecordLatency(double latencyMs)
    {
        if (!double.IsFinite(latencyMs) || latencyMs < 0)
            return;

        lock (_gate)
        {
            _recentLatencySamples.Enqueue(latencyMs);
            _latencySum += latencyMs;

            while (_recentLatencySamples.Count > MaxRecentLatencySamples)
            {
                _latencySum -= _recentLatencySamples.Dequeue();
            }

            AverageLatencyMs = _recentLatencySamples.Count == 0
                ? 0
                : _latencySum / _recentLatencySamples.Count;
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
    public Guid? PendingTransitionId { get; private set; }
    public bool HasPendingTransition => PendingTransitionId.HasValue;

    public FailoverRuleState(FailoverRuleConfig rule, string? initialActiveProviderId = null)
    {
        Rule = rule;
        CurrentActiveProviderId = ProviderIdentity.NormalizeId(
            initialActiveProviderId ?? rule.PrimaryProviderId);
        IsInFailoverState = !ProviderIdentity.EqualsId(
            CurrentActiveProviderId,
            rule.PrimaryProviderId);
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

    public void MarkTransitionPending(Guid transitionId) => PendingTransitionId = transitionId;

    public bool IsPendingTransition(Guid transitionId) => PendingTransitionId == transitionId;

    public void ClearPendingTransition() => PendingTransitionId = null;

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
/// A requested provider transition. The failover coordinator does not change its active-provider
/// state until the registered runtime confirms that connection and subscription restoration
/// succeeded.
/// </summary>
public sealed class FailoverTransitionRequest
{
    private readonly Func<FailoverTransitionRequest, bool, string?, Action?, bool> _complete;
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _linkedCancellation;
    private int _completionState;

    internal FailoverTransitionRequest(
        Guid transitionId,
        string ruleId,
        string fromProviderId,
        string toProviderId,
        bool isRecovery,
        string reason,
        CancellationToken cancellationToken,
        CancellationToken shutdownToken,
        bool reevaluateOnRejection,
        Func<FailoverTransitionRequest, bool, string?, Action?, bool> complete)
    {
        TransitionId = transitionId;
        RuleId = ruleId;
        FromProviderId = fromProviderId;
        ToProviderId = toProviderId;
        IsRecovery = isRecovery;
        Reason = reason;
        ReevaluateOnRejection = reevaluateOnRejection;
        _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            shutdownToken);
        CancellationToken = _linkedCancellation.Token;
        _complete = complete;
    }

    public Guid TransitionId { get; }
    public string RuleId { get; }
    public string FromProviderId { get; }
    public string ToProviderId { get; }
    public bool IsRecovery { get; }
    public string Reason { get; }
    public CancellationToken CancellationToken { get; }
    public Task<bool> Completion => _completion.Task;
    internal bool ReevaluateOnRejection { get; }

    public bool TryComplete()
        => Finish(succeeded: true, rejectionReason: null, commitRuntimeState: null);

    internal bool TryComplete(Action commitRuntimeState)
    {
        ArgumentNullException.ThrowIfNull(commitRuntimeState);
        return Finish(succeeded: true, rejectionReason: null, commitRuntimeState);
    }

    public bool TryReject(string reason)
        => Finish(succeeded: false, string.IsNullOrWhiteSpace(reason)
            ? "Provider transition was rejected."
            : reason,
            commitRuntimeState: null);

    internal bool TryCancel(string reason)
        => Finish(
            succeeded: false,
            string.IsNullOrWhiteSpace(reason)
                ? "Provider transition was cancelled."
                : reason,
            commitRuntimeState: null,
            cancelBeforeCompletion: true);

    private bool Finish(
        bool succeeded,
        string? rejectionReason,
        Action? commitRuntimeState,
        bool cancelBeforeCompletion = false)
    {
        if (Interlocked.CompareExchange(ref _completionState, 1, 0) != 0)
            return false;

        if (cancelBeforeCompletion)
        {
            try
            {
                _linkedCancellation.Cancel();
            }
            catch (AggregateException)
            {
                // A provider cancellation callback must not strand the coordinator ticket.
            }
        }

        var committed = _complete(this, succeeded, rejectionReason, commitRuntimeState);
        _completion.TrySetResult(committed);
        _linkedCancellation.Dispose();
        return committed;
    }
}

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
