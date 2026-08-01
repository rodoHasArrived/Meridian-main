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

    /// <summary>
    /// The guardrail reads in-memory portfolio state, so it needs neither I/O nor the async path.
    /// </summary>
    public bool HasSyncFastPath => true;

    /// <inheritdoc />
    public RiskFinding? TryEvaluate(OrderRequest request) => _runtime.EvaluateDrawdownGuardrail();

    /// <inheritdoc />
    public Task<RiskFinding?> EvaluateAsync(OrderRequest request, CancellationToken ct = default) =>
        Task.FromResult(_runtime.EvaluateDrawdownGuardrail());
}
