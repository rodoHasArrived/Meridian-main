using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Risk;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Meridian.Risk rule adapter for the operator-tuned drawdown circuit breaker. Evaluates the
/// live portfolio drawdown against the threshold managed (and dashboard-reported) by
/// <see cref="RiskRuleRuntimeService"/>, so the enforced <see cref="CompositeRiskValidator"/>
/// and the risk dashboard can never disagree about the guardrail.
/// </summary>
public sealed class DrawdownGuardrailRule : IRiskRule
{
    private readonly RiskRuleRuntimeService _runtime;

    public DrawdownGuardrailRule(RiskRuleRuntimeService runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <inheritdoc />
    public string RuleName => "DrawdownCircuitBreaker";

    /// <inheritdoc />
    public RiskRuleSeverity Severity => RiskRuleSeverity.Critical;

    /// <inheritdoc />
    public RiskValidationResult? TryEvaluate(OrderRequest request) => _runtime.EvaluateDrawdownGuardrail();

    /// <inheritdoc />
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default) =>
        Task.FromResult(_runtime.EvaluateDrawdownGuardrail());
}
