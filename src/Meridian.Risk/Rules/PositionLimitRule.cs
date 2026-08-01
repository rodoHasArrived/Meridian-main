using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Risk.Rules;

/// <summary>
/// Rejects orders that would exceed a maximum position size per symbol.
/// </summary>
public sealed class PositionLimitRule : IRiskRule
{
    private readonly IPositionTracker _positionTracker;
    private readonly Func<decimal?> _maxPositionSize;
    private readonly ILogger<PositionLimitRule> _logger;

    public PositionLimitRule(
        IPositionTracker positionTracker,
        decimal maxPositionSize,
        ILogger<PositionLimitRule> logger)
        : this(positionTracker, () => maxPositionSize, logger)
    {
    }

    /// <summary>
    /// Creates a rule whose limit is read per evaluation, so operator-tuned limits take
    /// effect without rebuilding the rule. A <see langword="null"/> limit means no limit
    /// is configured and the rule approves.
    /// </summary>
    public PositionLimitRule(
        IPositionTracker positionTracker,
        Func<decimal?> maxPositionSize,
        ILogger<PositionLimitRule> logger)
    {
        _positionTracker = positionTracker ?? throw new ArgumentNullException(nameof(positionTracker));
        _maxPositionSize = maxPositionSize ?? throw new ArgumentNullException(nameof(maxPositionSize));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string RuleName => "PositionLimit";

    /// <inheritdoc />
    public RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <inheritdoc />
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var maxPositionSize = _maxPositionSize();
        if (maxPositionSize is null)
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        // The tracker contract returns an empty position for unknown symbols, but guard
        // against nullable-oblivious implementations so the pre-trade gate cannot crash.
        var currentPosition = _positionTracker.GetPosition(request.Symbol);
        var context = Interop.RiskInterop.CreateContext(
            request,
            currentPosition?.Quantity ?? 0m,
            maxPositionSize.Value,
            portfolioValue: default,
            initialCapital: default,
            maxDrawdownPercent: default);
        var decision = Interop.RiskInterop.EvaluatePositionLimit(context);

        if (!decision.Approved)
        {
            // The interop returns an array of reasons and a decision kind. Keep both: joining the
            // reasons stops the tail being silently dropped, and the kind distinguishes a policy
            // rejection from one flagged for manual review.
            var reason = decision.Reasons.Length > 0
                ? string.Join(" ", decision.Reasons)
                : "Position limit exceeded.";
            var escalated = string.Equals(decision.DecisionKind, "escalate", StringComparison.OrdinalIgnoreCase);
            _logger.LogWarning(
                "Position limit rule rejected order for {Symbol}: {Reason}",
                LogSanitizer.Sanitize(request.Symbol),
                LogSanitizer.Sanitize(reason));

            // Report the projected position the rule actually evaluated, not the current one.
            // A long 50 followed by a sell of 200 is rejected at -150 against a limit of 100;
            // recording 50 would make the evidence look like it was inside the limit.
            var currentQuantity = currentPosition?.Quantity ?? 0m;
            var signedOrderQuantity = request.Side == OrderSide.Sell
                ? -request.Quantity
                : request.Quantity;

            return Task.FromResult(RiskValidationResult.Rejected(reason) with
            {
                Code = escalated ? "POSITION_LIMIT_ESCALATED" : "POSITION_LIMIT_EXCEEDED",
                ObservedValue = currentQuantity + signedOrderQuantity,
                LimitValue = maxPositionSize.Value,
                RequiresApproval = escalated,
            });
        }

        return Task.FromResult(RiskValidationResult.Approved());
    }
}
