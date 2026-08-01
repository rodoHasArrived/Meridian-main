using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Risk.Rules;

/// <summary>
/// Halts all new orders when portfolio drawdown exceeds a threshold.
/// </summary>
public sealed class DrawdownCircuitBreaker : IRiskRule
{
    private readonly IPositionTracker _positionTracker;
    private readonly decimal _initialCapital;
    private readonly decimal _maxDrawdownPercent;
    private readonly ILogger<DrawdownCircuitBreaker> _logger;

    public DrawdownCircuitBreaker(
        IPositionTracker positionTracker,
        decimal initialCapital,
        decimal maxDrawdownPercent,
        ILogger<DrawdownCircuitBreaker> logger)
    {
        _positionTracker = positionTracker ?? throw new ArgumentNullException(nameof(positionTracker));
        _initialCapital = initialCapital;
        _maxDrawdownPercent = maxDrawdownPercent;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string RuleName => "DrawdownCircuitBreaker";

    /// <inheritdoc />
    public RiskRuleSeverity Severity => RiskRuleSeverity.Critical;

    /// <inheritdoc />
    public Task<RiskFinding?> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var portfolioValue = _positionTracker.GetPortfolioValue();
        var context = Interop.RiskInterop.CreateContext(
            request,
            currentPositionQuantity: 0m,
            maxPositionSize: default,
            portfolioValue,
            _initialCapital,
            _maxDrawdownPercent);
        var decision = Interop.RiskInterop.EvaluateDrawdownCircuitBreaker(context);

        if (!decision.Approved)
        {
            var reason = decision.Reasons.Length > 0
                ? string.Join(" ", decision.Reasons)
                : "Drawdown circuit breaker triggered.";
            var escalated = string.Equals(decision.DecisionKind, "escalate", StringComparison.OrdinalIgnoreCase);
            _logger.LogWarning("Circuit breaker triggered for {Symbol}: {Reason}", request.Symbol, reason);

            return Task.FromResult<RiskFinding?>(new RiskFinding(
                Code: escalated ? "DRAWDOWN_CIRCUIT_BREAKER_ESCALATED" : "DRAWDOWN_CIRCUIT_BREAKER_TRIPPED",
                Message: reason,
                ObservedValue: portfolioValue,
                LimitValue: _maxDrawdownPercent,
                RequiresAcknowledgement: escalated));
        }

        return Task.FromResult<RiskFinding?>(null);
    }
}
