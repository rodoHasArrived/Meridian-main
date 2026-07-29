using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Risk;

/// <summary>
/// Composite risk validator that runs multiple risk rules in sequence, mapping each rule's
/// severity to a real outcome:
/// <list type="bullet">
/// <item><description><see cref="RiskRuleSeverity.Info"/> / <see cref="RiskRuleSeverity.Warning"/> —
/// the breach becomes a warning flag on the result and evaluation continues.</description></item>
/// <item><description><see cref="RiskRuleSeverity.Error"/> — the order is rejected.</description></item>
/// <item><description><see cref="RiskRuleSeverity.Escalate"/> (or a rule returning
/// <see cref="RiskValidationResult.Escalated"/>) — the order is parked in the governed
/// approval queue instead of routed; a valid one-shot approval token on the order releases
/// it past the escalation.</description></item>
/// <item><description><see cref="RiskRuleSeverity.Critical"/> — the order is rejected and the
/// execution circuit breaker trips, halting all further routing until an operator closes it.</description></item>
/// </list>
/// Rules evaluate by priority; warning flags accumulate across rules and are carried on the
/// final result whether approved or not.
/// </summary>
public sealed class CompositeRiskValidator : IRiskValidator
{
    private readonly IReadOnlyList<IRiskRule> _rules;
    private readonly ILogger<CompositeRiskValidator> _logger;
    private readonly ExecutionOperatorControlService? _operatorControls;
    private readonly RiskEscalationQueueService? _escalationQueue;

    // Fail-closed latch: set when a critical rule demanded the circuit breaker but the
    // durable trip failed. While latched, every order is rejected and the trip is retried,
    // so the promised global halt holds even when control persistence is unavailable.
    private volatile bool _breakerTripPending;
    private string _breakerTripReason = string.Empty;

    public CompositeRiskValidator(
        IEnumerable<IRiskRule> rules,
        ILogger<CompositeRiskValidator> logger,
        ExecutionOperatorControlService? operatorControls = null,
        RiskEscalationQueueService? escalationQueue = null)
    {
        _rules = rules?
            .Select(static (rule, index) => new { Rule = rule, Index = index })
            .OrderBy(static entry => entry.Rule.Priority)
            .ThenBy(static entry => entry.Index)
            .Select(static entry => entry.Rule)
            .ToList()
            .AsReadOnly() ?? throw new ArgumentNullException(nameof(rules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operatorControls = operatorControls;
        _escalationQueue = escalationQueue;
    }

    /// <inheritdoc />
    public async Task<RiskValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        List<string>? warnings = null;

        if (_breakerTripPending)
        {
            // A critical halt is owed but not yet durably applied: reject everything and
            // keep retrying the trip until it lands.
            await TryApplyPendingBreakerTripAsync(ct).ConfigureAwait(false);
            if (_breakerTripPending)
            {
                return RiskValidationResult.Rejected(
                    "Risk engine is failing closed: a critical halt is pending but the execution circuit breaker could not be opened.");
            }

            // The trip has just landed, but the operator-control gate that reads the breaker
            // already ran for THIS order earlier in the pipeline and saw it closed. Letting
            // validation continue would route exactly one order through the halt at the
            // moment it became durable — the single order the halt was raised to stop.
            return RiskValidationResult.Rejected(
                "Execution circuit breaker opened by a pending critical halt; resubmit once an operator clears it.");
        }

        // Consume a carried governed-approval token up front, independent of whether the
        // current evaluation still escalates: thresholds may have moved between parking
        // and release, and an armed approval must always be retired by the release it
        // authorized rather than surviving for replay against a later identical order.
        // The consumed entry's rule identity scopes the bypass, and a rejection later in
        // this evaluation re-arms the approval because no order routed.
        var releasedEntries = _escalationQueue?.TryConsumeApprovals(request) ?? [];
        foreach (var released in releasedEntries)
        {
            _logger.LogInformation(
                "Governed approval {EscalationId} consumed for the escalation parked by rule {RuleName}",
                released.EscalationId,
                released.RuleName ?? "unknown");
        }

        if (releasedEntries.Count > 0)
        {
            (warnings ??= []).Add("Escalation released by governed approval.");
        }

        try
        {
            foreach (var rule in _rules)
            {
                ct.ThrowIfCancellationRequested();
                var result = rule.TryEvaluate(request)
                    ?? await rule.EvaluateAsync(request, ct).ConfigureAwait(false);

                if (result.Warnings.Count > 0)
                {
                    (warnings ??= []).AddRange(result.Warnings);
                }

                if (result.IsApproved)
                {
                    continue;
                }

                var reason = string.IsNullOrWhiteSpace(result.RejectReason)
                    ? $"Rejected by risk rule '{rule.RuleName}'."
                    : result.RejectReason;

                // A rule can escalate explicitly via its result; otherwise its declared
                // severity decides the outcome of the failure.
                if (result.RequiresApproval || rule.Severity == RiskRuleSeverity.Escalate)
                {
                    // A consumed approval satisfies only the escalation it was parked for;
                    // any other escalate-capable rule still parks its own approval. An
                    // order breaching several such rules carries one token per decision.
                    if (releasedEntries.Any(entry =>
                            string.Equals(entry.RuleName, rule.RuleName, StringComparison.Ordinal)))
                    {
                        _logger.LogInformation(
                            "Escalation from rule {RuleName} released by governed approval",
                            rule.RuleName);
                        continue;
                    }

                    return RestoreOnFailure(
                        Escalate(rule, request, reason, warnings),
                        releasedEntries);
                }

                switch (rule.Severity)
                {
                    case RiskRuleSeverity.Info:
                    case RiskRuleSeverity.Warning:
                        // Order details stay out of the log: the flag is carried on the result
                        // and the OMS audit trail retains full context.
                        _logger.LogWarning(
                            "Risk rule {RuleName} ({Severity}) flagged the order without blocking",
                            rule.RuleName,
                            rule.Severity);
                        (warnings ??= []).Add($"{rule.RuleName}: {reason}");
                        continue;

                    // A Critical rule that could not value the order has established no
                    // breach. Halting the desk on it would turn a stale feed or an
                    // unpriceable symbol into a full trading outage that only an operator
                    // can clear. Refuse this order and let the next one be measured.
                    case RiskRuleSeverity.Critical when result.IsUnmeasurable:
                        _logger.LogWarning(
                            "Risk rule {RuleName} (Critical) rejected an order it could not measure; "
                                + "the circuit breaker stays closed because no breach was established",
                            rule.RuleName);
                        return RestoreOnFailure(
                            WithWarnings(RiskValidationResult.Unmeasurable(reason), warnings),
                            releasedEntries);

                    case RiskRuleSeverity.Critical:
                        _logger.LogError(
                            "Risk rule {RuleName} (Critical) rejected the order and is tripping the circuit breaker",
                            rule.RuleName);
                        await TripCircuitBreakerAsync(rule, reason, ct).ConfigureAwait(false);
                        return RestoreOnFailure(
                            WithWarnings(RiskValidationResult.Rejected(reason), warnings),
                            releasedEntries);

                    default:
                        _logger.LogWarning(
                            "Risk rule {RuleName} ({Severity}) rejected the order",
                            rule.RuleName,
                            rule.Severity);
                        return RestoreOnFailure(
                            WithWarnings(RiskValidationResult.Rejected(reason), warnings),
                            releasedEntries);
                }
            }

            var approved = WithWarnings(RiskValidationResult.Approved(), warnings);
            // Surface the consumed approvals on the approved result so the OMS can re-arm
            // them if the gateway subsequently faults before the order routes.
            return releasedEntries.Count == 0
                ? approved
                : approved with
                {
                    ConsumedApprovalId = RiskEscalationQueueService.JoinTokens(
                        releasedEntries.Select(static entry => entry.EscalationId))
                };
        }
        catch
        {
            // Cancellation or a faulting rule exits validation without routing anything,
            // so the consumed approval must not stay retired: re-arm it for retry, then
            // let the exception continue unwinding.
            RestoreOnFault(releasedEntries);
            throw;
        }
    }

    /// <summary>
    /// Re-arms a consumed approval after an exceptional validation exit (cancellation or
    /// a faulting rule): the release routed nothing, so the operator's decision must not
    /// be lost to the fault.
    /// </summary>
    private void RestoreOnFault(IReadOnlyList<RiskEscalationEntry> releasedEntries)
    {
        if (_escalationQueue is null)
        {
            return;
        }

        foreach (var released in releasedEntries)
        {
            if (_escalationQueue.TryRestoreApproval(released.EscalationId))
            {
                _logger.LogInformation(
                    "Governed approval {EscalationId} re-armed: validation exited before routing",
                    released.EscalationId);
            }
        }
    }

    /// <summary>
    /// Re-arms a consumed approval when this evaluation did not approve the order: the
    /// release routed nothing, so the operator's decision stays retryable once the
    /// blocking condition clears. A successful evaluation keeps the token retired.
    /// </summary>
    private RiskValidationResult RestoreOnFailure(
        RiskValidationResult result,
        IReadOnlyList<RiskEscalationEntry> releasedEntries)
    {
        if (result.IsApproved || _escalationQueue is null)
        {
            return result;
        }

        foreach (var released in releasedEntries)
        {
            if (_escalationQueue.TryRestoreApproval(released.EscalationId))
            {
                _logger.LogInformation(
                    "Governed approval {EscalationId} re-armed: the release was blocked before routing",
                    released.EscalationId);
            }
        }

        return result;
    }

    private static bool IsEvaluationOnly(OrderRequest request) =>
        request.Metadata is not null &&
        request.Metadata.TryGetValue(RiskEscalationQueueService.EvaluationOnlyMetadataKey, out var flag) &&
        bool.TryParse(flag, out var evaluationOnly) &&
        evaluationOnly;

    private RiskValidationResult Escalate(
        IRiskRule rule,
        OrderRequest request,
        string reason,
        List<string>? warnings)
    {
        if (IsEvaluationOnly(request))
        {
            // Decision only: report that governed approval would be required, without
            // parking an entry no one could release.
            _logger.LogInformation(
                "Risk rule {RuleName} would escalate this evaluation; no queue entry parked (evaluation-only)",
                rule.RuleName);
            return WithWarnings(
                RiskValidationResult.Escalated($"Would require governed approval: {reason}", escalationId: null),
                warnings);
        }

        if (_escalationQueue is null)
        {
            // No governed approval queue in this composition: fail closed as a plain rejection.
            _logger.LogWarning(
                "Risk rule {RuleName} escalated the order but no approval queue is configured; rejecting",
                rule.RuleName);
            return WithWarnings(RiskValidationResult.Rejected(reason), warnings);
        }

        // Retain the trusted submitting actor (stamped into metadata by the execution
        // endpoint) so segregation-of-duties checks can refuse self-approval later.
        string? actor = null;
        request.Metadata?.TryGetValue("actor", out actor);
        string? correlationId = null;
        request.Metadata?.TryGetValue("correlationId", out correlationId);
        // Live runs stamp their unique run id into metadata; StrategyId only names the
        // reusable strategy definition, which would conflate every run of that strategy.
        string? runId = null;
        request.Metadata?.TryGetValue("runId", out runId);

        RiskEscalationEntry entry;
        try
        {
            entry = _escalationQueue.Park(
                request,
                reason,
                ruleName: rule.RuleName,
                actor: actor,
                runId: string.IsNullOrWhiteSpace(runId) ? request.StrategyId : runId,
                correlationId: correlationId);
        }
        catch (Exception exception)
        {
            // The queue refused to park durably. Fail closed as a plain rejection rather
            // than reporting an escalation id no operator will ever find.
            _logger.LogError(
                exception,
                "Risk rule {RuleName} escalated the order but it could not be parked durably; rejecting",
                rule.RuleName);
            return WithWarnings(RiskValidationResult.Rejected(reason), warnings);
        }
        _logger.LogWarning(
            "Risk rule {RuleName} parked the order for governed approval ({EscalationId})",
            rule.RuleName,
            entry.EscalationId);
        return WithWarnings(
            RiskValidationResult.Escalated(
                $"Parked for governed approval ({entry.EscalationId}): {reason}",
                entry.EscalationId),
            warnings);
    }

    private async Task TripCircuitBreakerAsync(IRiskRule rule, string reason, CancellationToken ct)
    {
        if (_operatorControls is null)
        {
            return;
        }

        try
        {
            if (_operatorControls.GetSnapshot().CircuitBreaker.IsOpen)
            {
                return;
            }

            // The halt is a system-wide promise, not part of this client's request: a
            // caller disconnecting mid-persist must never silently drop it, so the trip
            // runs on its own token.
            await _operatorControls.SetCircuitBreakerAsync(
                isOpen: true,
                reason: $"Tripped by critical risk rule '{rule.RuleName}': {reason}",
                changedBy: $"risk-engine/{rule.RuleName}",
                ct: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The order rejection stands regardless, and the halt promise must too: latch
            // fail-closed so every subsequent order is rejected until the trip lands. This
            // covers cancellation as well — the latch is recorded before the exception
            // propagates, so an interrupted trip still halts routing.
            _breakerTripReason = $"Tripped by critical risk rule '{rule.RuleName}': {reason}";
            _breakerTripPending = true;
            _logger.LogError(
                exception,
                "Failed to trip execution circuit breaker after critical risk rule {RuleName}; the risk engine is now failing closed until the trip succeeds",
                rule.RuleName);

            // The latch above lives only in this process. Record the demanded halt where a
            // restart can find it, or a process recycle before the retry succeeds would
            // reload the stale closed snapshot and resume routing under an unresolved
            // critical breach.
            await _operatorControls
                .TryRecordPendingCircuitBreakerTripAsync(_breakerTripReason, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task TryApplyPendingBreakerTripAsync(CancellationToken ct)
    {
        if (_operatorControls is null)
        {
            // No controls to trip: the latch itself is the halt.
            return;
        }

        try
        {
            if (_operatorControls.GetSnapshot().CircuitBreaker.IsOpen)
            {
                _breakerTripPending = false;
                return;
            }

            await _operatorControls.SetCircuitBreakerAsync(
                isOpen: true,
                reason: _breakerTripReason,
                changedBy: "risk-engine/fail-closed-retry",
                ct: CancellationToken.None).ConfigureAwait(false);
            _breakerTripPending = false;
            _logger.LogInformation("Pending critical circuit-breaker trip applied; fail-closed latch released");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Retry of the pending circuit-breaker trip failed; the risk engine remains fail-closed");
        }
    }

    private static RiskValidationResult WithWarnings(RiskValidationResult result, List<string>? warnings) =>
        warnings is { Count: > 0 } ? result with { Warnings = warnings } : result;
}
