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
    private readonly ILogger<PositionLimitRule> _logger;
    private readonly Func<IReadOnlyList<OrderState>?>? _workingOrders;

    public PositionLimitRule(
        IPositionTracker positionTracker,
        decimal maxPositionSize,
        ILogger<PositionLimitRule> logger,
        Func<IReadOnlyList<OrderState>?>? workingOrders = null)
        : this(positionTracker, () => maxPositionSize, logger, workingOrders)
    {
    }

    /// <summary>
    /// Creates a rule whose limit is read per evaluation, so operator-tuned limits take
    /// effect without rebuilding the rule. A <see langword="null"/> limit means no limit
    /// is configured and the rule approves.
    /// </summary>
    /// <param name="positionTracker">Source of the filled position per symbol.</param>
    /// <param name="maxPositionSize">The per-symbol position ceiling; <see langword="null"/> disarms the rule.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="workingOrders">
    /// Orders whose quantity is still working (normally
    /// <see cref="IOrderManager.GetExposureReservingOrders"/>). The position tracker reports
    /// filled quantity only, so without this two resting buys of 80 against a limit of 100 each
    /// pass on their own and reach 160 when both fill. When supplied, the order is measured
    /// against the worst case its own side can reach: a buy against the position plus every
    /// working buy, a sell against the position less every working sell. Working orders on the
    /// opposite side are deliberately not netted off, because nothing guarantees they fill.
    /// </param>
    public PositionLimitRule(
        IPositionTracker positionTracker,
        Func<decimal?> maxPositionSize,
        ILogger<PositionLimitRule> logger,
        Func<IReadOnlyList<OrderState>?>? workingOrders = null)
    {
        _positionTracker = positionTracker ?? throw new ArgumentNullException(nameof(positionTracker));
        _maxPositionSize = maxPositionSize ?? throw new ArgumentNullException(nameof(maxPositionSize));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workingOrders = workingOrders;
    }

    /// <summary>
    /// Quantity still working on <paramref name="side"/> for the request's symbol, excluding the
    /// order the request itself replaces: an amendment is revalidated under its original order
    /// id and carries the full amended quantity, so counting the original too would charge the
    /// same order twice. Broker-side exit legs of one parent are one-cancels-other, so each
    /// parent contributes only its largest leg on the side.
    /// </summary>
    internal static decimal MeasureWorkingQuantity(
        IReadOnlyList<OrderState>? workingOrders,
        OrderRequest request,
        OrderSide side)
    {
        if (workingOrders is not { Count: > 0 })
        {
            return 0m;
        }

        var total = 0m;
        Dictionary<string, decimal>? largestLegByParent = null;
        foreach (var order in workingOrders)
        {
            if (order is null
                || order.Side != side
                || !string.Equals(order.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase)
                || (request.ClientOrderId is not null
                    && string.Equals(order.OrderId, request.ClientOrderId, StringComparison.Ordinal)))
            {
                continue;
            }

            var remaining = Math.Abs(order.Quantity) - Math.Abs(order.FilledQuantity);
            if (remaining <= 0m)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(order.ParentOrderId))
            {
                total += remaining;
                continue;
            }

            largestLegByParent ??= new Dictionary<string, decimal>(StringComparer.Ordinal);
            if (!largestLegByParent.TryGetValue(order.ParentOrderId, out var largest) || remaining > largest)
            {
                largestLegByParent[order.ParentOrderId] = remaining;
            }
        }

        if (largestLegByParent is not null)
        {
            foreach (var leg in largestLegByParent.Values)
            {
                total += leg;
            }
        }

        return total;
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
        var filledQuantity = currentPosition?.Quantity ?? 0m;

        // Worst case on the order's own side: every working order in the same direction fills.
        var isSell = request.Side != OrderSide.Buy;
        var workingSameSide = MeasureWorkingQuantity(
            _workingOrders?.Invoke(),
            request,
            isSell ? OrderSide.Sell : OrderSide.Buy);
        var effectiveQuantity = isSell
            ? filledQuantity - workingSameSide
            : filledQuantity + workingSameSide;
        var context = Interop.RiskInterop.CreateContext(
            request,
            effectiveQuantity,
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
            if (workingSameSide > 0m)
            {
                reason += FormattableString.Invariant(
                    $" Includes {workingSameSide} still working on the same side (filled position {filledQuantity}).");
            }
            var escalated = string.Equals(decision.DecisionKind, "escalate", StringComparison.OrdinalIgnoreCase);
            _logger.LogWarning(
                "Position limit rule rejected order for {Symbol}: {Reason}",
                LogSanitizer.Sanitize(request.Symbol),
                LogSanitizer.Sanitize(reason));

            // Report the projected position the rule actually evaluated, not the current one.
            // A long 50 followed by a sell of 200 is rejected at -150 against a limit of 100;
            // recording 50 would make the evidence look like it was inside the limit.
            var currentQuantity = effectiveQuantity;
            var signedOrderQuantity = isSell
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
