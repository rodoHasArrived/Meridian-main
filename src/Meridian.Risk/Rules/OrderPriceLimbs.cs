using Meridian.Execution.Sdk;

namespace Meridian.Risk.Rules;

/// <summary>Whether a price limb produced something to measure, and why not when it did not.</summary>
public enum PriceLimbState
{
    /// <summary>This order shape carries no price of this kind. Nothing to measure, nothing to refuse.</summary>
    NotApplicable,

    /// <summary>The order carries a routed price but no reference to measure it against.</summary>
    Unmeasurable,

    /// <summary>A price and the reference it is meaningful against.</summary>
    Measured
}

/// <summary>
/// One measurable price on an order, paired with the reference it is meaningful against and the
/// side whose aggressive direction applies to it.
/// </summary>
/// <param name="State">Whether <paramref name="Price"/> and <paramref name="Reference"/> are set.</param>
/// <param name="Price">The operator-entered price this limb measures.</param>
/// <param name="Reference">The price it is measured against.</param>
/// <param name="Orientation">
/// The side whose aggressive direction applies. For a limit this is the order's own side; for a
/// trigger it is the mirror, because a stop is wrong-side in exactly the direction a limit of the
/// same side would be passive.
/// </param>
/// <param name="Label">How to name this limb in an operator-facing refusal.</param>
public readonly record struct PriceLimb(
    PriceLimbState State,
    decimal Price,
    decimal Reference,
    OrderSide Orientation,
    string Label)
{
    /// <summary>A limb this order shape does not carry.</summary>
    public static PriceLimb NotApplicable { get; } =
        new(PriceLimbState.NotApplicable, 0m, 0m, OrderSide.Buy, string.Empty);

    /// <summary>A routed price with no reference to measure it against.</summary>
    public static PriceLimb Unmeasurable(string label) =>
        new(PriceLimbState.Unmeasurable, 0m, 0m, OrderSide.Buy, label);
}

/// <summary>
/// Resolves the prices a pre-trade price control may measure, and the reference each is meaningful
/// against — independently of any band.
/// <para>
/// This exists so the fat-finger band and the price collar cannot disagree about <em>what</em> they
/// are measuring while disagreeing only about <em>how far</em> is too far. The two rules differ by
/// threshold and by severity, and by nothing else: which order shapes carry a measurable price,
/// which reference each price is meaningful against, and which direction counts as aggressive are
/// properties of the order, not of the band. Duplicating them would let one rule protect an order
/// shape the other refuses, which is worse than either behaviour chosen deliberately.
/// </para>
/// </summary>
public static class OrderPriceLimbs
{
    /// <summary>
    /// Whether this order shape carries any price a continuous-market control can measure. A
    /// package never does — its top-level price is the combination's net debit or credit, which
    /// belongs to no single symbol — and the listed types are the only ones whose prices mean
    /// something against the current market.
    /// </summary>
    public static bool AppliesTo(OrderRequest request) =>
        request.Legs is null or { Count: 0 }
        && request.Type is OrderType.Limit or OrderType.StopMarket or OrderType.StopLimit
            or OrderType.LimitOnOpen or OrderType.LimitOnClose;

    /// <summary>Order types whose <see cref="OrderRequest.StopPrice"/> is a fixed trigger.</summary>
    public static bool HasMeasurableTrigger(OrderRequest request) =>
        request.Type is OrderType.StopMarket or OrderType.StopLimit;

    /// <summary>Order types whose <see cref="OrderRequest.LimitPrice"/> is an operator-entered limit.</summary>
    public static bool HasMeasurableLimit(OrderRequest request) =>
        request.Type is OrderType.Limit or OrderType.StopLimit
            or OrderType.LimitOnOpen or OrderType.LimitOnClose;

    /// <summary>
    /// The side whose aggressive direction is the mirror of this one's. A stop trigger is
    /// wrong-side in exactly the direction a limit of the same side would be passive, so the
    /// trigger limb measures the mirrored side rather than inventing a second orientation rule.
    /// </summary>
    public static OrderSide Mirror(OrderSide side) =>
        side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

    /// <summary>
    /// The stop trigger and the price it is measured against.
    /// <para>
    /// A trigger is measured against the traded price, NOT the crossing touch the limit limb uses,
    /// because that is what the matcher fires it off. Measuring a trigger against the side the order
    /// would cross at reads a wide book as crossed when the matcher does not: with a 100/120 quote
    /// and the last trade at 100, a buy stop at 105 is still resting, yet against the 120 ask — or
    /// even the 110 midpoint a valuation mark would give — it looks already crossed.
    /// </para>
    /// </summary>
    public static PriceLimb ResolveTrigger(OrderRequest request, IPortfolioExposureProvider exposureProvider)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exposureProvider);

        if (!AppliesTo(request) || !HasMeasurableTrigger(request) || request.StopPrice is not > 0m)
        {
            return PriceLimb.NotApplicable;
        }

        var reference = exposureProvider.TryGetTriggerReferencePrice(request.Symbol, request.Side);
        return reference is > 0m
            ? new PriceLimb(
                PriceLimbState.Measured,
                request.StopPrice.Value,
                reference.Value,
                Mirror(request.Side),
                "the stop trigger")
            : PriceLimb.Unmeasurable("the stop trigger");
    }

    /// <summary>
    /// The operator-entered limit and the price it is measured against.
    /// <para>
    /// A stop-limit's limit is priced off its own trigger rather than off today's market, so that is
    /// what it is measured against: falling back to the touch would reject the ordinary protective
    /// orders these controls exist to preserve — a sell stop at 90 with an 89 limit is 1.1% from its
    /// trigger though 11% from a 100 market. An auction limit prices against a future cross that no
    /// continuous reference stands in for, so it resolves as unmeasurable rather than being measured
    /// against a touch that does not govern it; the venue enforces that price, so the limb applies
    /// and fails closed rather than being excluded.
    /// </para>
    /// </summary>
    public static PriceLimb ResolveLimit(OrderRequest request, IPortfolioExposureProvider exposureProvider)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exposureProvider);

        if (!AppliesTo(request) || !HasMeasurableLimit(request) || request.LimitPrice is not > 0m)
        {
            return PriceLimb.NotApplicable;
        }

        var isAuctionLimit = request.Type is OrderType.LimitOnOpen or OrderType.LimitOnClose;
        var label = isAuctionLimit ? "the auction limit" : "the order price";
        var reference = request.Type switch
        {
            OrderType.StopLimit => request.StopPrice,
            OrderType.LimitOnOpen or OrderType.LimitOnClose => null,
            _ => exposureProvider.TryGetTouchPrice(request.Symbol, request.Side)
        };

        return reference is > 0m
            ? new PriceLimb(
                PriceLimbState.Measured,
                request.LimitPrice.Value,
                reference.Value,
                request.Side,
                label)
            : PriceLimb.Unmeasurable(label);
    }

    /// <summary>
    /// Signed deviation of the order's price from the reference, oriented so that a positive
    /// value always means "aggressive" — a buy paying above the market, or a sell hitting below
    /// it. Mirrors the F# policy's orientation so the reported number matches the one compared,
    /// including its saturation: a ratio whose scaling by 100 would exceed
    /// <see cref="decimal.MaxValue"/> is capped rather than thrown, so the evidence attached to a
    /// breach can never itself turn a structured rejection into an evaluation failure.
    /// </summary>
    public static decimal? AggressiveDeviationPercent(
        OrderSide side,
        decimal? orderPrice,
        decimal? referencePrice)
    {
        if (orderPrice is not > 0m || referencePrice is not > 0m)
        {
            return null;
        }

        // Mirrors the F# helper exactly, including why the quotient is never formed when it
        // could overflow: MaxValue over a 0.1 reference overflows the division itself, before
        // any scaling. The comparison uses cap x reference, which is representable precisely
        // when the reference is at most 100 (cap x 100 = MaxValue); above that the ratio cannot
        // reach the cap, because the numerator is bounded by MaxValue.
        const decimal hundred = 100m;
        var cap = decimal.MaxValue / hundred;
        var difference = orderPrice.Value - referencePrice.Value;

        decimal signedDeviation;
        if (referencePrice.Value > hundred)
        {
            signedDeviation = difference / referencePrice.Value * hundred;
        }
        else
        {
            var limit = cap * referencePrice.Value;
            signedDeviation = difference switch
            {
                _ when difference > limit => decimal.MaxValue,
                _ when difference < -limit => decimal.MinValue,
                _ => difference / referencePrice.Value * hundred
            };
        }

        return side is OrderSide.Buy ? signedDeviation : -signedDeviation;
    }
}
