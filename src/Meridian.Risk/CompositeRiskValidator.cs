using Meridian.Execution;
using Meridian.Execution.Sdk;
using Interop = Meridian.FSharp.Interop;
using Microsoft.Extensions.Logging;

namespace Meridian.Risk;

/// <summary>
/// Composite risk validator that runs multiple risk rules in sequence.
/// Rejects the order on the first rule failure.
/// </summary>
public sealed class CompositeRiskValidator : IRiskValidator
{
    private readonly IReadOnlyList<IRiskRule> _rules;
    private readonly ILogger<CompositeRiskValidator> _logger;

    public CompositeRiskValidator(IEnumerable<IRiskRule> rules, ILogger<CompositeRiskValidator> logger)
    {
        _rules = rules?.ToList().AsReadOnly() ?? throw new ArgumentNullException(nameof(rules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RiskValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var decisions = new List<Interop.RiskDecisionDto>(_rules.Count);

        foreach (var rule in _rules)
        {
            var result = await rule.EvaluateAsync(request, ct).ConfigureAwait(false);
            decisions.Add(new Interop.RiskDecisionDto
            {
                Approved = result.IsApproved,
                DecisionKind = result.IsApproved ? "approve" : "reject",
                Reasons = string.IsNullOrWhiteSpace(result.RejectReason) ? [] : [result.RejectReason],
            });
        }

        var aggregate = Interop.RiskInterop.Aggregate(decisions);
        if (!aggregate.Approved)
        {
            var reason = aggregate.Reasons.FirstOrDefault() ?? "Rejected by aggregated risk policy.";
            _logger.LogWarning("Aggregated risk policy rejected order for {Symbol}: {Reason}", request.Symbol, reason);
            return RiskValidationResult.Rejected(reason);
        }

        return RiskValidationResult.Approved();
    }
}
