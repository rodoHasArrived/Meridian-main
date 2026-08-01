using Meridian.Execution;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Risk;

/// <summary>
/// Composite risk validator that runs every registered rule against an order and resolves one
/// aggregate decision from their findings.
/// <para>
/// Two properties distinguish this from a first-failure gate. Every rule is evaluated, so the
/// operator sees the complete violation set rather than whichever rule happened to run first. And
/// each rule's declared <see cref="IRiskRule.Severity"/> — not anything the rule chooses per order —
/// decides whether its finding blocks or merely annotates.
/// </para>
/// </summary>
public sealed class CompositeRiskValidator : IRiskValidator
{
    /// <summary>Code used when a rule throws instead of returning a finding.</summary>
    public const string EvaluationFailedCode = "RISK_RULE_EVALUATION_FAILED";

    private readonly IReadOnlyList<IRiskRule> _rules;
    private readonly ILogger<CompositeRiskValidator> _logger;
    private readonly TimeSpan _perRuleTimeout;

    public CompositeRiskValidator(
        IEnumerable<IRiskRule> rules,
        ILogger<CompositeRiskValidator> logger)
        : this(rules, logger, TimeSpan.FromSeconds(5))
    {
    }

    /// <param name="perRuleTimeout">
    /// Bounds a single <em>asynchronous</em> rule evaluation. A rule that never completes would
    /// otherwise hang the pre-trade gate for callers that supply no deadline of their own. Pass
    /// <see cref="Timeout.InfiniteTimeSpan"/> to disable.
    /// <para>
    /// This does <b>not</b> bound <see cref="IRiskRule.TryEvaluate"/>, nor any synchronous work a
    /// rule performs before it returns its <see cref="Task"/>. Both run on the calling thread, and
    /// a synchronous call cannot be abandoned — bounding them would need a thread hop on every
    /// evaluation, which is not worth it on the pre-trade path, and would leak the blocked thread
    /// anyway. The contract is therefore that rules do not block: they either declare
    /// <see cref="IRiskRule.HasSyncFastPath"/> because they need no I/O, or they return a task
    /// promptly and do their waiting inside it, where this timeout applies.
    /// </para>
    /// </param>
    public CompositeRiskValidator(
        IEnumerable<IRiskRule> rules,
        ILogger<CompositeRiskValidator> logger,
        TimeSpan perRuleTimeout)
    {
        _rules = rules?
            .Select(static (rule, index) => new { Rule = rule, Index = index })
            .OrderBy(static entry => entry.Rule.Priority)
            .ThenBy(static entry => entry.Index)
            .Select(static entry => entry.Rule)
            .ToList()
            .AsReadOnly() ?? throw new ArgumentNullException(nameof(rules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Rejected here so a bad composition fails at startup rather than per order.
        // CreateTimeoutSource runs before EvaluateRuleAsync's fail-closed handler, so a value
        // CancelAfter refuses would throw ArgumentOutOfRangeException outside it — and the OMS calls
        // the validator outside its own submission handler, so every order would come back as an
        // unstructured exception instead of a risk decision. A misconfigured gate should be
        // impossible to start, not silently fatal on the first trade.
        if (perRuleTimeout != Timeout.InfiniteTimeSpan &&
            (perRuleTimeout < TimeSpan.Zero || perRuleTimeout.TotalMilliseconds > int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(perRuleTimeout),
                perRuleTimeout,
                $"Per-rule timeout must be non-negative and at most {int.MaxValue} ms, or {nameof(Timeout)}.{nameof(Timeout.InfiniteTimeSpan)} to disable.");
        }

        _perRuleTimeout = perRuleTimeout;
    }

    /// <inheritdoc />
    public async Task<RiskValidationOutcome> ValidateOrderAsync(
        OrderRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Priority is carried from the rule that produced each finding rather than recovered later
        // from RuleName. A name is a display label and nothing enforces its uniqueness: with two
        // rules sharing one, a name lookup returns the first match's priority, so a low-priority
        // rule's violation could sort ahead of a higher-priority one and be reported as the reason
        // the order was rejected.
        var violations = new List<(RiskViolation Violation, int Priority)>();
        var reservations = new List<IRiskReservation>();

        try
        {
            foreach (var rule in _rules)
            {
                ct.ThrowIfCancellationRequested();
                var violation = await EvaluateRuleAsync(rule, request, reservations, ct).ConfigureAwait(false);
                if (violation is not null)
                {
                    violations.Add((violation, rule.Priority));
                }
            }

            // Cancellation during the final rule would otherwise go unnoticed: the loop's check
            // runs before each rule, so a rule that reserves and then returns just after the token
            // is cancelled has no later check to catch it. Without this, a cancelled submission
            // transfers its reservation and the OMS conservatively commits it.
            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            // Ownership has not transferred yet, so cleanup is ours. A leaked reservation
            // permanently consumes capacity and would eventually block every later order.
            RollbackAll(reservations);
            throw;
        }

        // OrderBy is stable, so rules of equal severity and priority stay in registration order.
        var ordered = violations
            .OrderByDescending(static entry => entry.Violation.Severity)
            .ThenBy(static entry => entry.Priority)
            .Select(static entry => entry.Violation)
            .ToList();

        var result = RiskValidationResult.FromViolations(ordered);

        if (!result.IsApproved)
        {
            // Nothing will be routed, so no reserved capacity should survive this call.
            RollbackAll(reservations);
            LogRejection(request, result);
            return new RiskValidationOutcome(result, []);
        }

        if (ordered.Count > 0)
        {
            _logger.LogInformation(
                "Pre-trade risk admitted order for {Symbol} as {Decision} with {ViolationCount} finding(s).",
                ExecutionLogText.ForLog(request.Symbol),
                result.Decision,
                ordered.Count);
        }

        return new RiskValidationOutcome(result, reservations);
    }

    /// <summary>
    /// Evaluates one rule and attributes its finding, returning <see langword="null"/> when the rule
    /// is satisfied.
    /// <para>
    /// This returns the <see cref="RiskViolation"/> rather than the finding so that an engine
    /// failure can be constructed here, where it is known to be one. Inferring it downstream by
    /// comparing against <see cref="EvaluationFailedCode"/> would make that public code a second
    /// admission lever: a rule legitimately reporting it on an <see cref="RiskRuleSeverity.Info"/>
    /// finding would be silently promoted to a rejection, which is exactly the severity-can-be-
    /// contradicted problem this design removes.
    /// </para>
    /// </summary>
    private async Task<RiskViolation?> EvaluateRuleAsync(
        IRiskRule rule,
        OrderRequest request,
        List<IRiskReservation> reservations,
        CancellationToken ct)
    {
        try
        {
            // Reserving is checked before the synchronous fast path. A rule that declares both would
            // otherwise never reach EvaluateAndReserveAsync, silently admitting concurrent orders
            // without consuming any of the finite capacity it exists to protect — the one failure
            // here that is invisible rather than loud.
            if (rule is IReservingRiskRule reserving)
            {
                using var reservingTimeout = CreateTimeoutSource(ct);
                var reservationResult = await WithHardTimeoutAsync(
                        reserving.EvaluateAndReserveAsync(request, reservingTimeout?.Token ?? ct),
                        ct)
                    .ConfigureAwait(false);

                if (reservationResult.Reservation is { } reservation)
                {
                    reservations.Add(reservation);
                }

                return ToViolation(rule, reservationResult.Finding);
            }

            // The synchronous fast path runs inside this handler, not before it. The production
            // DrawdownGuardrailRule uses it, and a failure reading portfolio state must become a
            // structured risk rejection rather than an unstructured submission failure.
            //
            // It is deliberately NOT wrapped in the hard timeout: a synchronous call cannot be
            // abandoned, so wrapping it would bound only the wait and leak the blocked thread while
            // still hanging the submission. A rule opts into this path by declaring it needs no
            // I/O, so blocking here is a contract violation rather than a case to time out.
            //
            // No timeout source is built for it either. This branch runs on every order for every
            // synchronous rule, and a linked CTS plus a scheduled CancelAfter is real per-order cost
            // on the pre-trade path for a token nothing here observes.
            if (rule.HasSyncFastPath)
            {
                return ToViolation(rule, rule.TryEvaluate(request));
            }

            using var evaluationTimeout = CreateTimeoutSource(ct);
            var finding = await WithHardTimeoutAsync(
                    rule.EvaluateAsync(request, evaluationTimeout?.Token ?? ct),
                    ct)
                .ConfigureAwait(false);

            return ToViolation(rule, finding);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Genuine caller cancellation. Propagate so the outer handler releases reservations.
            throw;
        }
        catch (Exception ex)
        {
            // Everything else — including a rule's own timeout surfacing as
            // OperationCanceledException while the caller's token is still live — is an evaluation
            // failure. Treating it as cancellation would let the failure escape the gate entirely,
            // leaving the order neither admitted with a recorded decision nor explicitly rejected.
            _logger.LogError(
                ex,
                "Risk rule {RuleName} failed to evaluate order for {Symbol}; failing closed.",
                rule.RuleName,
                ExecutionLogText.ForLog(request.Symbol));

            // Critical regardless of the rule's declared severity — the one exception, and it is an
            // engine fault rather than a finding about the order. Preserving an Info or Warning
            // rule's severity would admit the order after its gate failed.
            return new RiskViolation(
                RuleName: rule.RuleName,
                Severity: RiskRuleSeverity.Critical,
                Code: EvaluationFailedCode,
                Message: $"Risk rule '{rule.RuleName}' could not be evaluated: {ex.GetType().Name}.");
        }
    }

    /// <summary>
    /// Bounds an evaluation that may ignore its cancellation token. Passing a token that is
    /// cancelled after a delay only helps for rules that observe it; a rule that never completes
    /// would otherwise hang the pre-trade gate for callers that supply no deadline of their own.
    /// <para>
    /// <c>Task.WaitAsync</c> tears its timer down as soon as the evaluation completes. A hand-rolled
    /// <c>WhenAny(evaluation, Task.Delay(...))</c> leaves the losing delay scheduled for the full
    /// timeout, so a rejected burst — which still evaluates every rule — would accumulate one live
    /// timer per rule per order on the pre-trade path.
    /// </para>
    /// </summary>
    private async Task<T> WithHardTimeoutAsync<T>(Task<T> evaluation, CancellationToken ct)
    {
        if (_perRuleTimeout == Timeout.InfiniteTimeSpan)
        {
            return await evaluation.ConfigureAwait(false);
        }

        return await evaluation.WaitAsync(_perRuleTimeout, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// As <see cref="WithHardTimeoutAsync{T}(Task{T}, CancellationToken)"/>, but for a reserving
    /// rule: an abandoned evaluation that completes later may still have taken capacity, so its
    /// reservation is released rather than leaked.
    /// </summary>
    private async Task<RiskRuleReservationResult> WithHardTimeoutAsync(
        Task<RiskRuleReservationResult> evaluation,
        CancellationToken ct)
    {
        if (_perRuleTimeout == Timeout.InfiniteTimeSpan)
        {
            return await evaluation.ConfigureAwait(false);
        }

        try
        {
            return await evaluation.WaitAsync(_perRuleTimeout, ct).ConfigureAwait(false);
        }
        // Both paths abandon the evaluation before it hands its reservation back. Cancellation
        // matters as much as timeout here: a rule that ignores its token still completes later, and
        // the outer handler cannot release a reservation it was never given. Leaking one
        // permanently consumes capacity, so repeated cancellations would starve the rule.
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            // Do not await the abandoned evaluation, but do not leak whatever it reserved either.
            _ = evaluation.ContinueWith(
                static task => task.Result.Reservation?.Rollback(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            throw;
        }
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
    /// Attributes a finding to its declaring rule, carrying that rule's declared severity and
    /// nothing else. The finding's own code never influences severity — engine failures are
    /// constructed at the point of failure instead.
    /// </summary>
    private static RiskViolation? ToViolation(IRiskRule rule, RiskFinding? finding) =>
        finding is null
            ? null
            : new RiskViolation(
                RuleName: rule.RuleName,
                Severity: rule.Severity,
                Code: finding.Code,
                Message: finding.Message,
                ObservedValue: finding.ObservedValue,
                LimitValue: finding.LimitValue,
                RequiresAcknowledgement: finding.RequiresAcknowledgement);

    /// <summary>
    /// Releases every reservation taken so far, continuing past any that fails.
    /// <para>
    /// Two reasons this neither stops nor throws. Stopping would leave a faulty rule's neighbours
    /// pending, so one bad callback would permanently consume another rule's capacity. And this runs
    /// on the failure path — inside the cancellation/evaluation-failure handler, and before a
    /// rejection returns — where the original outcome is the caller's answer; replacing it with a
    /// cleanup exception would hide why the order was actually blocked.
    /// </para>
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
                _logger.LogError(
                    ex,
                    "A risk reservation could not be released; its capacity stays consumed until the window expires.");
            }
        }

        reservations.Clear();
    }

    /// <summary>
    /// Records the rejection. Both the symbol and the blocking reason can carry caller-supplied
    /// text — rules embed the symbol in their reason — so both are rendered through
    /// <see cref="ExecutionLogText"/> rather than handed to the logger raw.
    /// </summary>
    private void LogRejection(OrderRequest request, RiskValidationResult result)
    {
        var blocking = result.BlockingViolation;
        _logger.LogWarning(
            "Pre-trade risk rejected order for {Symbol}: {RuleName} ({Severity}) {Code} — {Reason}. {ViolationCount} finding(s) total.",
            ExecutionLogText.ForLog(request.Symbol),
            blocking?.RuleName,
            blocking?.Severity,
            blocking?.Code,
            ExecutionLogText.ForLog(blocking?.Message),
            result.Violations.Count);
    }
}
