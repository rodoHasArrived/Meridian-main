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
        _perRuleTimeout = perRuleTimeout;
    }

    /// <inheritdoc />
    public async Task<RiskValidationOutcome> ValidateOrderAsync(
        OrderRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var violations = new List<RiskViolation>();
        var reservations = new List<IRiskReservation>();

        try
        {
            foreach (var rule in _rules)
            {
                ct.ThrowIfCancellationRequested();
                var finding = await EvaluateRuleAsync(rule, request, reservations, ct).ConfigureAwait(false);
                if (finding is not null)
                {
                    violations.Add(ToViolation(rule, finding));
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

        var ordered = violations
            .OrderByDescending(static violation => violation.Severity)
            .ThenBy(violation => RulePriority(violation.RuleName))
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

    private async Task<RiskFinding?> EvaluateRuleAsync(
        IRiskRule rule,
        OrderRequest request,
        List<IRiskReservation> reservations,
        CancellationToken ct)
    {
        using var timeoutCts = CreateTimeoutSource(ct);
        var effectiveToken = timeoutCts?.Token ?? ct;

        try
        {
            // The synchronous fast path runs inside this handler, not before it. The production
            // DrawdownGuardrailRule uses it, and a failure reading portfolio state must become a
            // structured risk rejection rather than an unstructured submission failure.
            //
            // It is deliberately NOT wrapped in the hard timeout: a synchronous call cannot be
            // abandoned, so wrapping it would bound only the wait and leak the blocked thread while
            // still hanging the submission. A rule opts into this path by declaring it needs no
            // I/O, so blocking here is a contract violation rather than a case to time out.
            if (rule.HasSyncFastPath)
            {
                return rule.TryEvaluate(request);
            }

            if (rule is IReservingRiskRule reserving)
            {
                var reservationResult = await WithHardTimeoutAsync(
                        reserving.EvaluateAndReserveAsync(request, effectiveToken),
                        ct)
                    .ConfigureAwait(false);

                if (reservationResult.Reservation is { } reservation)
                {
                    reservations.Add(reservation);
                }

                return reservationResult.Finding;
            }

            return await WithHardTimeoutAsync(
                    rule.EvaluateAsync(request, effectiveToken),
                    ct)
                .ConfigureAwait(false);
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

            return new RiskFinding(
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
    /// Attributes a finding to its declaring rule.
    /// <para>
    /// An evaluation failure is the one case where the violation does not carry the rule's declared
    /// severity. It is an engine fault rather than a finding about the order, and preserving an
    /// <see cref="RiskRuleSeverity.Info"/> or <see cref="RiskRuleSeverity.Warning"/> rule's severity
    /// would either admit the order after its gate failed, or produce a rejection whose blocking
    /// violation, reason, and code are all null.
    /// </para>
    /// </summary>
    private static RiskViolation ToViolation(IRiskRule rule, RiskFinding finding)
    {
        var severity = finding.Code == EvaluationFailedCode
            ? RiskRuleSeverity.Critical
            : rule.Severity;

        return new RiskViolation(
            RuleName: rule.RuleName,
            Severity: severity,
            Code: finding.Code,
            Message: finding.Message,
            ObservedValue: finding.ObservedValue,
            LimitValue: finding.LimitValue,
            RequiresAcknowledgement: finding.RequiresAcknowledgement);
    }

    private int RulePriority(string ruleName)
    {
        for (var i = 0; i < _rules.Count; i++)
        {
            if (string.Equals(_rules[i].RuleName, ruleName, StringComparison.Ordinal))
            {
                return _rules[i].Priority;
            }
        }

        return int.MaxValue;
    }

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
