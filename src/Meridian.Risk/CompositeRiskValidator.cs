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

        // Consume a carried governed-approval token up front, independent of whether the
        // current evaluation still escalates: thresholds may have moved between parking
        // and release, and an armed approval must always be retired by the release it
        // authorized rather than surviving for replay against a later identical order.
        var approvalReleased = _escalationQueue is not null && _escalationQueue.TryConsumeApproval(request);
        if (approvalReleased)
        {
            _logger.LogInformation("Governed approval consumed; escalations are satisfied for this release");
            (warnings ??= []).Add("Escalation released by governed approval.");
        }

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
                // A release under a consumed approval passes escalations; later rules
                // still run so hard limits stay enforced.
                if (approvalReleased)
                {
                    _logger.LogInformation(
                        "Escalation from rule {RuleName} released by governed approval",
                        rule.RuleName);
                    continue;
                }

                return Escalate(rule, request, reason, warnings);
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

                case RiskRuleSeverity.Critical:
                    _logger.LogError(
                        "Risk rule {RuleName} (Critical) rejected the order and is tripping the circuit breaker",
                        rule.RuleName);
                    await TripCircuitBreakerAsync(rule, reason, ct).ConfigureAwait(false);
                    return WithWarnings(RiskValidationResult.Rejected(reason), warnings);

                default:
                    _logger.LogWarning(
                        "Risk rule {RuleName} ({Severity}) rejected the order",
                        rule.RuleName,
                        rule.Severity);
                    return WithWarnings(RiskValidationResult.Rejected(reason), warnings);
            }
        }

        return WithWarnings(RiskValidationResult.Approved(), warnings);
    }

    private RiskValidationResult Escalate(
        IRiskRule rule,
        OrderRequest request,
        string reason,
        List<string>? warnings)
    {
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

        var entry = _escalationQueue.Park(
            request,
            reason,
            ruleName: rule.RuleName,
            actor: actor,
            runId: request.StrategyId,
            correlationId: correlationId);
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

            await _operatorControls.SetCircuitBreakerAsync(
                isOpen: true,
                reason: $"Tripped by critical risk rule '{rule.RuleName}': {reason}",
                changedBy: $"risk-engine/{rule.RuleName}",
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The order rejection stands regardless; a breaker-trip failure must never
            // resurrect the order or crash the pre-trade gate.
            _logger.LogError(
                exception,
                "Failed to trip execution circuit breaker after critical risk rule {RuleName}",
                rule.RuleName);
        }
    }

    private static RiskValidationResult WithWarnings(RiskValidationResult result, List<string>? warnings) =>
        warnings is { Count: > 0 } ? result with { Warnings = warnings } : result;
}
