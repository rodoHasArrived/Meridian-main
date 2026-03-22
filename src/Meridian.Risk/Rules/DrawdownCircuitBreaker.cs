using Meridian.Execution;
using Meridian.Execution.Sdk;
using Interop = Meridian.FSharp.Interop;
using Microsoft.Extensions.Logging;

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
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
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
            var reason = decision.Reasons.FirstOrDefault() ?? "Drawdown circuit breaker triggered.";
            _logger.LogWarning("Circuit breaker triggered for {Symbol}: {Reason}", request.Symbol, reason);
            return Task.FromResult(RiskValidationResult.Rejected(reason));
        }

        return Task.FromResult(RiskValidationResult.Approved());
    }
}
