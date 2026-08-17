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

    /// <summary>
    /// Wall-clock ceiling on a single rule. A rule that never completes would otherwise hold the
    /// pre-trade path open indefinitely, which is a trading outage rather than a risk decision.
    /// </summary>
    public static readonly TimeSpan DefaultPerRuleTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _perRuleTimeout;

    public CompositeRiskValidator(
        IEnumerable<IRiskRule> rules,
        ILogger<CompositeRiskValidator> logger,
        ExecutionOperatorControlService? operatorControls = null,
        RiskEscalationQueueService? escalationQueue = null,
        TimeSpan? perRuleTimeout = null)
    {
        _rules = rules?
            .Select(static (rule, index) => new { Rule = rule, Index = index })
            .OrderBy(static entry => entry.Rule.Priority)
            .ThenBy(static entry => entry.Index)
            .Select(static entry => entry.Rule)
            .ToList()
            .AsReadOnly() ?? throw new ArgumentNullException(nameof(rules));
        // Governed approvals are scoped by rule name: a consumed token releases the escalation
        // parked by the rule whose name it carries. Two rules sharing a name would make one
        // operator decision release both, so the ambiguity is refused at composition rather than
        // discovered when an unapproved order routes.
        var duplicateName = _rules
            .GroupBy(static rule => rule.RuleName, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateName is not null)
        {
            throw new ArgumentException(
                $"Risk rule name '{duplicateName.Key}' is registered {duplicateName.Count()} times. "
                    + "Rule names scope governed approvals, so they must be unique.",
                nameof(rules));
        }

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operatorControls = operatorControls;
        _escalationQueue = escalationQueue;

        _perRuleTimeout = perRuleTimeout ?? DefaultPerRuleTimeout;
        if (_perRuleTimeout != Timeout.InfiniteTimeSpan)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(_perRuleTimeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                _perRuleTimeout.TotalMilliseconds,
                int.MaxValue);
        }
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

        // Every rule is evaluated before any decision is taken, so an order breaching three limits
        // reports three. Returning at the first blocking rule reported one and left the operator
        // to resubmit, get blocked by the next rule, and repeat. Evaluation is side-effect free by
        // contract (IRiskRule.EvaluateAsync), and the reservations that are not are settled below,
        // so evaluating past a block cannot commit anything.
        var evaluated = new List<(IRiskRule Rule, RiskValidationResult Result)>(_rules.Count);
        var reservations = new List<IRiskReservation>();
        // Priority rides alongside each violation rather than being recovered from RuleName later:
        // two rules can share a name, and the ordering below is part of the public contract.
        var violations = new List<(RiskViolation Violation, int Priority)>();

        try
        {
            foreach (var rule in _rules)
            {
                ct.ThrowIfCancellationRequested();
                var result = await EvaluateRuleAsync(rule, request, reservations, ct).ConfigureAwait(false);
                evaluated.Add((rule, result));

                // Acted on here rather than in a later pass. Waiting until every rule had been
                // awaited meant a caller cancelling in between unwound before the trip ran, and a
                // confirmed halt was silently dropped. Cancellation must not be able to veto a
                // breach that has already been established. This is also why the trip does not
                // live in the decision pass: an earlier Error rule owning the returned rejection
                // must not skip a later rule's Critical halt.
                if (!result.IsApproved
                    && !result.IsUnmeasurable
                    && rule.Severity == RiskRuleSeverity.Critical
                    && !IsEvaluationFailure(result))
                {
                    // CancellationToken.None: the halt outlives this order's submission, so a
                    // caller giving up must not abandon a trip the desk is owed. The pending-trip
                    // latch retries it if the durable write fails.
                    await TripCircuitBreakerAsync(rule, DescribeRefusal(rule, result), CancellationToken.None)
                        .ConfigureAwait(false);

                    // Logged after the trip, and guarded. A throwing logger provider between the
                    // breach and the trip would have left the breaker closed and the latch unset
                    // on a confirmed desk-wide halt, so unrelated later orders kept routing.
                    TryLogSuppressed(
                        null,
                        $"critical breach from rule '{rule.RuleName}' tripped the circuit breaker");
                }

                if (!result.IsApproved)
                {
                    // Scoped to the outcome the token can actually satisfy: an approval releases
                    // an escalation, not a hard rejection the rule has since hardened into.
                    var released =
                        (result.RequiresApproval || rule.Severity == RiskRuleSeverity.Escalate)
                        && releasedEntries.Any(entry =>
                            string.Equals(entry.RuleName, rule.RuleName, StringComparison.Ordinal));

                    violations.Add((ToViolation(rule, result, releasedByApproval: released), rule.Priority));
                }
                else if (result.Warnings.Count > 0)
                {
                    // An approved result still carrying warnings observed something worth
                    // recording. Attribute each one to this rule at a non-blocking severity so the
                    // structured set is complete without changing what the order does.
                    foreach (var warning in result.Warnings)
                    {
                        violations.Add((new RiskViolation(
                            RuleName: rule.RuleName,
                            Severity: rule.Severity is RiskRuleSeverity.Info
                                ? RiskRuleSeverity.Info
                                : RiskRuleSeverity.Warning,
                            Code: result.Code ?? "RISK_OBSERVED",
                            Message: warning,
                            ObservedValue: result.ObservedValue,
                            LimitValue: result.LimitValue), rule.Priority));
                    }
                }
            }

            // Any blocking breach outranks an escalation, regardless of priority order. Parking an
            // order that a later rule hard-rejects offers the operator a release that cannot work.
            var hasBlockingBreach = evaluated.Any(entry =>
                !entry.Result.IsApproved
                && !releasedEntries.Any(released =>
                    string.Equals(released.RuleName, entry.Rule.RuleName, StringComparison.Ordinal))
                && (IsEvaluationFailure(entry.Result)

                    // Stated as the complement, matching RiskViolation.IsBlocking: anything that is
                    // not explicitly non-blocking blocks. Listing Critical and Error let an
                    // unrecognised severity -- a cast or a numeric deserialization in host code --
                    // fall through as non-blocking here while IsBlocking called it blocking, so an
                    // earlier rule could park an order that a later one had hard-rejected.
                    // Escalate is excluded only while the rule is still asking for approval, which
                    // is the one outcome a governed release can actually satisfy.
                    || (entry.Rule.Severity is not (RiskRuleSeverity.Info or RiskRuleSeverity.Warning)
                        && !(entry.Rule.Severity is RiskRuleSeverity.Escalate || entry.Result.RequiresApproval))));

            foreach (var (rule, result) in evaluated)
            {
                if (result.Warnings.Count > 0)
                {
                    (warnings ??= []).AddRange(result.Warnings);
                }

                if (result.IsApproved)
                {
                    continue;
                }

                var reason = DescribeRefusal(rule, result);

                // P1: a rule that could not run has established nothing, whatever severity it
                // declares. Applying the declared severity here would let an Info or Warning rule
                // that threw or timed out fall into the annotate-and-continue case and admit the
                // order — routing precisely when one of its configured checks did not happen. The
                // refusal blocks, but stays unmeasurable so it cannot halt the desk.
                if (IsEvaluationFailure(result))
                {
                    TryLogSuppressed(
                        null,
                        $"risk rule '{rule.RuleName}' ({rule.Severity}) could not be evaluated; the order is refused");
                    return Block(RestoreOnFailure(
                        WithWarnings(RiskValidationResult.Unmeasurable(reason) with
                        {
                            Code = EvaluationFailedCode,
                        }, warnings),
                        releasedEntries));
                }

                // A rule can escalate explicitly via its result; otherwise its declared
                // severity decides the outcome of the failure.
                //
                // Unmeasurable is excluded: it means the rule could not evaluate the order at all,
                // usually because pricing or reference data was missing. Treating that as an
                // escalation let an operator approval release it, and the matching-token branch
                // below then skips the rule entirely -- so the order routed still unmeasured,
                // which is the one thing the Unmeasurable contract exists to prevent. An operator
                // can authorise accepting a known breach; they cannot authorise a measurement that
                // never happened.
                // Critical is excluded for the same reason, one step further: its breach has
                // already opened the circuit breaker in the side-effect pass above. Letting a token
                // release it would route the very order that halted the desk -- and route it past a
                // breaker the OMS checked *before* risk validation, so nothing downstream stops it.
                // Critical is documented to reject and halt; an approval cannot undo a halt.
                if ((result.RequiresApproval || rule.Severity == RiskRuleSeverity.Escalate)
                    && rule.Severity != RiskRuleSeverity.Critical
                    && !result.IsUnmeasurable)
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

                    // A hard breach elsewhere decides the order. Parking this escalation would
                    // offer an approval that cannot release the order, so retain the finding and
                    // let the actual blocker own the refusal below.
                    if (hasBlockingBreach)
                    {
                        continue;
                    }

                    return Block(RestoreOnFailure(
                        Escalate(rule, request, reason, warnings),
                        releasedEntries));
                }

                switch (rule.Severity)
                {
                    // An escalate-capable rule that could not measure the order, which the branch
                    // above deliberately excludes from release: an operator can authorise accepting
                    // a known breach, not a measurement that never happened. Refused here rather
                    // than left to fall through, because `continue` assumes another rule owns the
                    // refusal. When this is the only one, the order was still blocked — the
                    // violation is blocking, so RiskValidationResult.IsApproved computes false as a
                    // safety net — but it came back with no reject reason and IsUnmeasurable
                    // cleared, so the submitter was told nothing about why.
                    case RiskRuleSeverity.Escalate when result.IsUnmeasurable:
                        _logger.LogWarning(
                            "Risk rule {RuleName} (Escalate) rejected an order it could not measure; "
                                + "an approval cannot release it because nothing was measured",
                            rule.RuleName);
                        return Block(RestoreOnFailure(
                            WithWarnings(RiskValidationResult.Unmeasurable(reason), warnings),
                            releasedEntries));

                    // Reached only when a blocking breach elsewhere outranked this escalation.
                    // The order is rejected on that breach; this rule contributes its finding.
                    case RiskRuleSeverity.Escalate:
                        continue;

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
                        return Block(RestoreOnFailure(
                            WithWarnings(RiskValidationResult.Unmeasurable(reason), warnings),
                            releasedEntries));

                    // The breaker was already tripped in the side-effect pass above, which runs
                    // over every rule so an earlier rejection cannot skip it.
                    case RiskRuleSeverity.Critical:
                        return Block(RestoreOnFailure(
                            WithWarnings(RiskValidationResult.Rejected(reason), warnings),
                            releasedEntries));

                    default:
                        _logger.LogWarning(
                            "Risk rule {RuleName} ({Severity}) rejected the order",
                            rule.RuleName,
                            rule.Severity);
                        return Block(RestoreOnFailure(
                            WithWarnings(RiskValidationResult.Rejected(reason), warnings),
                            releasedEntries));
                }
            }

            var approved = WithWarnings(RiskValidationResult.Approved(), warnings) with
            {
                Violations = Ordered(),

                // Ownership of the reserved capacity moves to the caller here, and only here.
                // Passing the gate is not the same as routing — client-order-id registration,
                // journaling, or the gateway submit can still fail — so committing inside the
                // validator would consume capacity for orders that never reach a venue.
                Reservations = reservations,
            };

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
            // Cancellation or a faulting rule exits validation without routing anything, so
            // nothing may keep the capacity it reserved and the consumed approval must not stay
            // retired: release both for retry, then let the exception continue unwinding.
            //
            // Rollback runs first. RestoreOnFault touches the escalation queue, and a queue that
            // throws here would strand every reservation this evaluation took — a permanent
            // capacity leak in exchange for a re-armable token.
            RollbackAll(reservations);
            RestoreOnFault(releasedEntries);
            throw;
        }

        // Single funnel for every non-routing decision: the order does not reach a venue, so the
        // capacity is released here rather than at each of the four blocked returns, and the full
        // violation set rides along so the submitter sees every breach and not only the first.
        RiskValidationResult Block(RiskValidationResult result)
        {
            RollbackAll(reservations);
            return result with { Violations = Ordered() };
        }

        // Severity descending, then the declaring rule's priority ascending — the order
        // RiskValidationResult.Violations documents, and the order BlockingViolation relies on to
        // name the most severe breach rather than the earliest-evaluated one.
        IReadOnlyList<RiskViolation> Ordered() => violations
            .OrderBy(static entry => DecisionPrecedence(entry.Violation.Severity))
            .ThenBy(static entry => entry.Priority)
            .Select(static entry => entry.Violation)
            .ToList();
    }

    /// <summary>
    /// Orders findings the way the decision does, which is not the enum's own order: Escalate sits
    /// above Error numerically, but a hard rejection outranks a releasable escalation. Sorting by
    /// the raw enum made <see cref="RiskValidationResult.BlockingViolation"/> name the escalation
    /// an operator could release rather than the Error that actually forced the rejection.
    /// </summary>
    private static int DecisionPrecedence(RiskRuleSeverity severity) => severity switch
    {
        RiskRuleSeverity.Critical => 0,
        RiskRuleSeverity.Error => 1,
        RiskRuleSeverity.Escalate => 2,
        RiskRuleSeverity.Warning => 3,
        RiskRuleSeverity.Info => 4,

        // An unrecognised value on a fail-closed gate sorts first, so it cannot hide behind
        // findings this build knows about.
        _ => -1,
    };

    /// <summary>
    /// Stable code recorded when a rule itself fails, rather than reporting a breach.
    /// </summary>
    public const string EvaluationFailedCode = "RISK_RULE_EVALUATION_FAILED";

    /// <summary>
    /// True when the rule could not be evaluated at all — it threw, or it overran the per-rule
    /// ceiling. Distinct from a rule that ran and refused the order: nothing was measured, so the
    /// declared severity says nothing about what the outcome should be.
    /// </summary>
    private static bool IsEvaluationFailure(RiskValidationResult result) =>
        string.Equals(result.Code, EvaluationFailedCode, StringComparison.Ordinal);

    private static string DescribeRefusal(IRiskRule rule, RiskValidationResult result) =>
        string.IsNullOrWhiteSpace(result.RejectReason)
            ? $"Rejected by risk rule '{rule.RuleName}'."
            : result.RejectReason;

    /// <summary>
    /// Runs one rule under the per-rule timeout, taking its reservation when it has one, and
    /// converts any fault into a fail-closed rejection rather than letting it escape.
    /// <para>
    /// A rule that throws has not established that the order is safe, so the order is refused.
    /// The refusal is reported at <see cref="RiskRuleSeverity.Critical"/>-equivalent weight but as
    /// <see cref="RiskValidationResult.Unmeasurable"/>: the rule established no breach, so this
    /// must not trip the circuit breaker. A flaky rule would otherwise halt the whole desk.
    /// </para>
    /// </summary>
    private async Task<RiskValidationResult> EvaluateRuleAsync(
        IRiskRule rule,
        OrderRequest request,
        List<IRiskReservation> reservations,
        CancellationToken ct)
    {
        using var timeout = CreateTimeoutSource(ct);

        try
        {
            // Checked before the sync fast path so a reserving rule cannot be bypassed by one.
            if (rule is IReservingRiskRule reserving)
            {
                var reservation = reserving.EvaluateAndReserveAsync(request, timeout?.Token ?? ct);

                RiskRuleReservationResult reserved;
                try
                {
                    reserved = await Bound(reservation).ConfigureAwait(false);
                }
                catch
                {
                    // The await was abandoned — the ceiling fired, or the caller gave up — so this
                    // frame never receives the handle. A rule that ignores its token and reserves
                    // late would strand that capacity permanently, and repeated timeouts walk the
                    // ceiling down to zero. Cleanup is attached only here: on the normal path
                    // ownership transfers to the caller and rolling back would release a slot the
                    // order is about to use.
                    _ = reservation.ContinueWith(
                        static (completed, state) =>
                        {
                            if (completed.Status is not TaskStatus.RanToCompletion ||
                                completed.Result.Reservation is not { } late)
                            {
                                return;
                            }

                            var owner = (CompositeRiskValidator)state!;
                            try
                            {
                                late.Rollback();
                            }
                            catch (Exception ex)
                            {
                                owner.TryLogSuppressed(
                                    ex,
                                    "rollback of a reservation returned after its evaluation was abandoned");
                            }
                        },
                        this,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);

                    throw;
                }

                if (reserved.Reservation is not null)
                {
                    reservations.Add(reserved.Reservation);
                }

                return reserved.Result;
            }

            var fastPath = rule.TryEvaluate(request);
            if (fastPath is not null)
            {
                return fastPath;
            }

            return await Bound(rule.EvaluateAsync(request, timeout?.Token ?? ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller gave up. That is not a risk decision — let it unwind so the reservations
            // taken so far are rolled back by the outer handler.
            throw;
        }
        catch (Exception ex)
        {
            // Logging is guarded because a throwing logger provider here would replace the
            // fail-closed rejection with a logging exception: the submitter would get an
            // unstructured fault with no rejection state and no audit record, on the one path
            // whose entire purpose is to refuse safely.
            TryLogSuppressed(ex, $"risk rule '{rule.RuleName}' failed to evaluate");

            return RiskValidationResult.Unmeasurable(
                $"Risk rule '{rule.RuleName}' failed to evaluate; the order is refused because it could not be checked.") with
            {
                Code = EvaluationFailedCode,
            };
        }

        // The timeout is applied to the await, not only to the token handed to the rule. Cancelling
        // the token asks a cooperative rule to stop; it does nothing about one that ignores the
        // token or blocks inside its task, and that is exactly the rule this ceiling exists for.
        // WaitAsync abandons the wait either way, so the gate always returns.
        Task<T> Bound<T>(Task<T> evaluation) => _perRuleTimeout == Timeout.InfiniteTimeSpan
            ? evaluation.WaitAsync(ct)
            : evaluation.WaitAsync(_perRuleTimeout, ct);
    }

    private CancellationTokenSource? CreateTimeoutSource(CancellationToken ct)
    {
        if (_perRuleTimeout == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(_perRuleTimeout);
        return source;
    }

    /// <summary>
    /// Attributes a rule's refusal to the rule that raised it. Severity comes from the declaring
    /// rule, never from the result, which is what stops a rule contradicting its own severity.
    /// </summary>
    /// <param name="releasedByApproval">
    /// True when an operator's one-shot approval released this rule's escalation. The breach is
    /// still recorded — the operator's decision is evidence, not amnesia — but at a non-blocking
    /// severity, because a blocking violation on an approved result recomputes
    /// <see cref="RiskValidationResult.IsApproved"/> to false and the release could never take
    /// effect.
    /// </param>
    private static RiskViolation ToViolation(
        IRiskRule rule,
        RiskValidationResult result,
        bool releasedByApproval = false) =>
        new(
            RuleName: rule.RuleName,
            Severity: releasedByApproval
                ? RiskRuleSeverity.Warning
                : result.Code == EvaluationFailedCode ? RiskRuleSeverity.Critical : rule.Severity,
            Code: result.Code ?? "RISK_REJECTED",
            Message: string.IsNullOrWhiteSpace(result.RejectReason)
                ? $"Rejected by risk rule '{rule.RuleName}'."
                : result.RejectReason,
            ObservedValue: result.ObservedValue,
            LimitValue: result.LimitValue,
            // Cleared for a released finding. The token has already been consumed, so the
            // acknowledgement this violation asked for has happened; leaving the flag set makes an
            // ApprovedWithWarnings summary carry a violation still requesting approval, and a
            // consumer reads a completed decision as outstanding.
            RequiresAcknowledgement: !releasedByApproval && result.RequiresApproval);

    /// <summary>
    /// Releases every reservation, attempting all of them even if one throws. Stopping at the
    /// first fault would strand the rest, which is the leak this exists to prevent.
    /// </summary>
    private void RollbackAll(List<IRiskReservation> reservations)
    {
        foreach (var reservation in reservations)
        {
            try
            {
                reservation.Rollback();
            }
            catch (Exception ex)
            {
                TryLogSuppressed(ex, "risk reservation rollback");
            }
        }

        reservations.Clear();
    }

    /// <summary>
    /// Reports something that must never replace the decision being returned, or interrupt a halt
    /// already applied. The logger call is itself guarded, because the provider is exactly what may
    /// be broken. <paramref name="failure"/> is null when there is no exception to attach.
    /// </summary>
    private void TryLogSuppressed(Exception? failure, string what)
    {
        try
        {
            _logger.LogError(failure, "Suppressed failure during {What}", what);
        }
        catch
        {
            // Nothing left to report with.
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
