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

    /// <summary>
    /// Lower values run first. Rules with the same priority preserve registration order.
    /// </summary>
    int Priority => 0;

    /// <summary>
    /// Severity used for logging and operator attribution when this rule rejects an order.
    /// </summary>
    RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <summary>
    /// Optional synchronous fast path for rules that do not need I/O or F# interop.
    /// Return <see langword="null"/> to fall back to <see cref="EvaluateAsync"/>.
    /// </summary>
    RiskValidationResult? TryEvaluate(OrderRequest request) => null;

    /// <summary>Evaluates whether the order passes this risk rule.</summary>
    Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default);
}

/// <summary>
/// Severity attached to a rule's rejection, mapped to real outcomes by
/// <see cref="CompositeRiskValidator"/>:
/// <list type="bullet">
/// <item><description><see cref="Info"/> / <see cref="Warning"/> — the order proceeds; the breach is surfaced as a warning flag.</description></item>
/// <item><description><see cref="Error"/> — the order is rejected.</description></item>
/// <item><description><see cref="Escalate"/> — the order is parked for governed approval instead of routed.</description></item>
/// <item><description><see cref="Critical"/> — the order is rejected and the execution circuit breaker trips.</description></item>
/// </list>
/// </summary>
public enum RiskRuleSeverity
{
    Info,
    Warning,
    Error,
    Escalate,
    Critical
}
