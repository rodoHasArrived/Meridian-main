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
    /// Bounds a single rule's evaluation. A rule that never completes would otherwise hang the
    /// pre-trade gate for callers that supply no deadline of their own. Pass
    /// <see cref="Timeout.InfiniteTimeSpan"/> to disable.
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
                request.Symbol,
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
        if (rule.HasSyncFastPath)
        {
            return rule.TryEvaluate(request);
        }

        using var timeoutCts = CreateTimeoutSource(ct);
        var effectiveToken = timeoutCts?.Token ?? ct;

        try
        {
            if (rule is IReservingRiskRule reserving)
            {
                var reservationResult = await reserving
                    .EvaluateAndReserveAsync(request, effectiveToken)
                    .ConfigureAwait(false);

                if (reservationResult.Reservation is { } reservation)
                {
                    reservations.Add(reservation);
                }

                return reservationResult.Finding;
            }

            return await rule.EvaluateAsync(request, effectiveToken).ConfigureAwait(false);
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
                request.Symbol);

            return new RiskFinding(
                Code: EvaluationFailedCode,
                Message: $"Risk rule '{rule.RuleName}' could not be evaluated: {ex.GetType().Name}.");
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

    private static void RollbackAll(List<IRiskReservation> reservations)
    {
        foreach (var reservation in reservations)
        {
            reservation.Rollback();
        }

        reservations.Clear();
    }

    private void LogRejection(OrderRequest request, RiskValidationResult result)
    {
        var blocking = result.BlockingViolation;
        _logger.LogWarning(
            "Pre-trade risk rejected order for {Symbol}: {RuleName} ({Severity}) {Code} — {Reason}. {ViolationCount} finding(s) total.",
            request.Symbol,
            blocking?.RuleName,
            blocking?.Severity,
            blocking?.Code,
            blocking?.Message,
            result.Violations.Count);
    }
}
