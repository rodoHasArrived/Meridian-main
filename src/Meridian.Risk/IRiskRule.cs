using Meridian.Execution;
using Meridian.Execution.Sdk;

namespace Meridian.Risk;

/// <summary>
/// Individual risk rule that evaluates a single constraint (position limit, drawdown, etc.).
/// </summary>
public interface IRiskRule
{
    /// <summary>Human-readable name for logging.</summary>
    string RuleName { get; }

    /// <summary>Evaluates whether the order passes this risk rule.</summary>
    Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default);
}
