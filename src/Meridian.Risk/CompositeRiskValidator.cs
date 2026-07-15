using Meridian.Execution;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Risk;

/// <summary>
/// Composite risk validator that runs multiple risk rules in sequence.
/// Evaluates rules by priority and rejects the order on the first rule failure.
/// </summary>
public sealed class CompositeRiskValidator : IRiskValidator
{
    private readonly IReadOnlyList<IRiskRule> _rules;
    private readonly ILogger<CompositeRiskValidator> _logger;

    public CompositeRiskValidator(IEnumerable<IRiskRule> rules, ILogger<CompositeRiskValidator> logger)
    {
        _rules = rules?
            .Select((rule, index) => new { Rule = rule, Index = index })
            .OrderBy(static entry => entry.Rule.Priority)
            .ThenBy(static entry => entry.Index)
            .Select(static entry => entry.Rule)
            .ToList()
            .AsReadOnly() ?? throw new ArgumentNullException(nameof(rules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RiskValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        foreach (var rule in _rules)
        {
            ct.ThrowIfCancellationRequested();
            var result = rule.TryEvaluate(request)
                ?? await rule.EvaluateAsync(request, ct).ConfigureAwait(false);

            if (!result.IsApproved)
            {
                var reason = string.IsNullOrWhiteSpace(result.RejectReason)
                    ? $"Rejected by risk rule '{rule.RuleName}'."
                    : result.RejectReason;
                _logger.LogWarning(
                    "Risk rule {RuleName} ({Severity}) rejected order for {Symbol}: {Reason}",
                    rule.RuleName,
                    rule.Severity,
                    request.Symbol,
                    reason);
                return RiskValidationResult.Rejected(reason);
            }
        }

        return RiskValidationResult.Approved();
    }
}
