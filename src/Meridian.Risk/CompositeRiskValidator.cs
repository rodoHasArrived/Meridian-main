using Meridian.Execution;
using Meridian.Execution.Sdk;
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
        foreach (var rule in _rules)
        {
            var result = await rule.EvaluateAsync(request, ct).ConfigureAwait(false);
            if (!result.IsApproved)
            {
                _logger.LogWarning("Risk rule {RuleName} rejected order for {Symbol}: {Reason}",
                    rule.RuleName, request.Symbol, result.RejectReason);
                return result;
            }
        }

        return RiskValidationResult.Approved();
    }
}
