using Meridian.Execution;
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
    private readonly Func<IOrderManager?>? _orderManagerAccessor;
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
    /// <param name="orderManagerAccessor">
    /// Optional accessor for the order book. When supplied, the limit is measured against the
    /// filled position plus the remaining quantity of working orders on the same side, so a
    /// series of resting orders cannot together breach a limit each passes alone. It is an
    /// accessor rather than an instance because the order manager itself depends on the risk
    /// validator that owns this rule.
    /// </param>
    public PositionLimitRule(
        IPositionTracker positionTracker,
        Func<decimal?> maxPositionSize,
        ILogger<PositionLimitRule> logger,
        Func<IOrderManager?>? orderManagerAccessor = null)
    {
        _positionTracker = positionTracker ?? throw new ArgumentNullException(nameof(positionTracker));
        _maxPositionSize = maxPositionSize ?? throw new ArgumentNullException(nameof(maxPositionSize));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _orderManagerAccessor = orderManagerAccessor;
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
        // Worst-case base position: the filled quantity plus every working order on the same
        // side as this request. Five resting 90-share buys against a 100-share limit used to
        // pass one by one because only filled positions were counted.
        var baseQuantity = (currentPosition?.Quantity ?? 0m) + GetWorkingQuantitySameSide(request);
        var context = Interop.RiskInterop.CreateContext(
            request,
            baseQuantity,
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
            var currentQuantity = baseQuantity;
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

    /// <summary>
    /// Signed remaining quantity of working orders for the request's symbol on the request's
    /// side (positive for buys, negative for sells). Opposite-side orders are ignored: they may
    /// be cancelled, so they cannot be relied on to offset this order.
    /// </summary>
    private decimal GetWorkingQuantitySameSide(OrderRequest request)
    {
        var orderManager = _orderManagerAccessor?.Invoke();
        if (orderManager is null)
            return 0m;

        var isBuy = request.Side == OrderSide.Buy;
        var working = 0m;
        foreach (var order in orderManager.GetExposureReservingOrders())
        {
            if (!string.Equals(order.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase))
                continue;
            if ((order.Side == OrderSide.Buy) != isBuy)
                continue;
            // An amendment is validated as its full amended quantity while the original order is
            // still in the book (the probe's ClientOrderId is that order's id); counting both
            // would charge the order twice.
            if (request.ClientOrderId is { Length: > 0 } clientOrderId &&
                string.Equals(order.OrderId, clientOrderId, StringComparison.Ordinal))
                continue;
            // A broker-native notional order routes dollars: its Quantity is a placeholder, not
            // units, so adding it to a share position would reject on a dollar amount. The rule
            // has no price to convert it, and the notional-based rules already reserve it by
            // value, so it is left out of the unit projection rather than mis-measured.
            if (order.RoutedNotional is > 0m)
                continue;

            var remaining = Math.Abs(order.Quantity) - Math.Abs(order.FilledQuantity);
            if (remaining > 0m)
                working += remaining;
        }

        return isBuy ? working : -working;
    }
}
