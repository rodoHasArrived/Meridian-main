using Meridian.Execution;
using Meridian.Execution.Sdk;
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
        var drawdownPercent = (_initialCapital - portfolioValue) / _initialCapital * 100m;

        if (drawdownPercent >= _maxDrawdownPercent)
        {
            _logger.LogWarning("Circuit breaker triggered: drawdown {Drawdown:F2}% >= threshold {Threshold:F2}%",
                drawdownPercent, _maxDrawdownPercent);

            return Task.FromResult(RiskValidationResult.Rejected(
                $"Drawdown circuit breaker: {drawdownPercent:F2}% drawdown exceeds {_maxDrawdownPercent:F2}% threshold"));
        }

        return Task.FromResult(RiskValidationResult.Approved());
    }
}
