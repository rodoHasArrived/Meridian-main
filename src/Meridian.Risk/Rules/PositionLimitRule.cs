using Meridian.Execution;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Risk.Rules;

/// <summary>
/// Rejects orders that would exceed a maximum position size per symbol.
/// </summary>
public sealed class PositionLimitRule : IRiskRule
{
    private readonly IPositionTracker _positionTracker;
    private readonly decimal _maxPositionSize;
    private readonly ILogger<PositionLimitRule> _logger;

    public PositionLimitRule(
        IPositionTracker positionTracker,
        decimal maxPositionSize,
        ILogger<PositionLimitRule> logger)
    {
        _positionTracker = positionTracker ?? throw new ArgumentNullException(nameof(positionTracker));
        _maxPositionSize = maxPositionSize;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string RuleName => "PositionLimit";

    /// <inheritdoc />
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        var currentPosition = _positionTracker.GetPosition(request.Symbol);
        var projectedQty = currentPosition.Quantity + (request.Side == OrderSide.Buy ? request.Quantity : -request.Quantity);

        if (Math.Abs(projectedQty) > _maxPositionSize)
        {
            return Task.FromResult(RiskValidationResult.Rejected(
                $"Position limit exceeded: projected {projectedQty} > max {_maxPositionSize} for {request.Symbol}"));
        }

        return Task.FromResult(RiskValidationResult.Approved());
    }
}
