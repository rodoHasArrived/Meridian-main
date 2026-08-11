using Meridian.Execution;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Risk.Rules;

/// <summary>
/// Both fat-finger limbs as one immutable reading, so an evaluation always sees a threshold pair
/// that actually existed. <see langword="null"/> disables the corresponding limb.
/// </summary>
public readonly record struct FatFingerThresholds(
    decimal? MaxOrderQuantity,
    decimal? MaxPriceDeviationPercent);

/// <summary>
/// Blocks the classic order-entry mistakes: a quantity far larger than the desk ever intends to
/// send in one order, a price typed far through the market, and a stop whose trigger is on the
/// wrong side of it.
/// <para>
/// The price limb is deliberately <b>directional</b>. A resting buy far below the market and a
/// resting sell far above it are ordinary working orders, so only the aggressive side is
/// measured — paying above the reference on a buy, or selling below it on a sell. A symmetric
/// band would reject the entire resting book.
/// </para>
/// <para>
/// Every price this rule measures is measured against the reference that price is actually
/// meaningful to, and each order type contributes only the prices it genuinely puts at risk of a
/// typo:
/// </para>
/// <list type="bullet">
///   <item><see cref="OrderType.Limit"/> — its <see cref="OrderRequest.LimitPrice"/> is measured
///     against the current touch. This is the only type whose limit is meaningful against the
///     <i>current continuous</i> market, which is the only market this rule can see.</item>
///   <item><see cref="OrderType.StopMarket"/> — its <see cref="OrderRequest.StopPrice"/> is
///     measured against the touch with the <b>mirrored</b> orientation described below. A stop is
///     not measured for being far from the market — that is what a stop is — but for being on the
///     <i>wrong side</i> of it, which is not a stop at all.</item>
///   <item><see cref="OrderType.StopLimit"/> — both. Its trigger is measured like a stop-market
///     trigger, and its limit is measured against <b>its own trigger</b> rather than the market,
///     because that is what the limit is priced off: a sell stop at $90 with an $89 limit is an
///     ordinary protective order, 1.1% from its trigger, though it is 11% from a $100 market.</item>
///   <item><see cref="OrderType.Market"/> is excluded even when it carries a
///     <see cref="OrderRequest.LimitPrice"/>. The paper gateway lets a caller supply one as its
///     simulated market observation, so on a market order that value is not an operator's typed
///     limit and must not be compared against the live book.</item>
///   <item><see cref="OrderType.LimitOnOpen"/> and <see cref="OrderType.LimitOnClose"/> are
///     excluded: their limit applies to a future auction, not the continuous touch. Measuring one
///     against the present BBO rejects routine auction orders, and pre-open there may be no fresh
///     BBO at all, which would make every auction order unmeasurable.</item>
///   <item><see cref="OrderType.TrailingStop"/> is excluded: its trigger is derived by the broker
///     from <see cref="OrderRequest.TrailPrice"/> or <see cref="OrderRequest.TrailPercent"/> and
///     moves with the market, so comparing a snapshot of it against the current touch measures
///     nothing that stays true.</item>
///   <item>A multi-leg order is excluded entirely: its limit is the net debit or credit for the
///     <i>package</i>, which is not comparable to a quote for the top-level symbol. A $1 credit
///     spread on a $200 underlying would otherwise look 99.5% through the market.</item>
/// </list>
/// <para>
/// A stop trigger's aggressive direction is the exact <b>mirror</b> of a limit's, and conflating
/// the two is what makes a wrong-side stop invisible.
/// <see cref="Meridian.Execution.PaperMatching.PaperOrderMatchingPolicy"/> fires a buy stop once
/// the market reaches or passes <i>above</i> it and a sell stop once the market reaches or falls
/// <i>below</i> it, so a correctly placed buy stop sits above the market. A buy stop typed beneath
/// the market — $1 against a $100 book — is already crossed on arrival, and a stop-market order
/// that triggers on acceptance routes as an unbounded market order with no price protection at
/// all. That is the single most expensive shape a mistyped order can take here, so the trigger is
/// settled before the limit.
/// </para>
/// <para>
/// The quantity limb is likewise skipped for a <b>broker-notional</b> order. Alpaca-style gateways
/// route a metadata dollar amount and discard <see cref="OrderRequest.Quantity"/>, so on those
/// orders the quantity field carries dollars — comparing it to a share ceiling would reject a valid
/// $5,000 order against a 1,000-share limit. Their economic size is gated by
/// <see cref="OrderNotionalRule"/>, which reads the routed notional from the same place the gateway
/// does.
/// </para>
/// <para>
/// Every excluded order still passes through the quantity limb, and its economic size is still
/// gated by <see cref="OrderNotionalRule"/>.
/// </para>
/// <para>
/// With the deviation band configured, a priced order whose symbol has no reference price is
/// REJECTED as unmeasurable rather than approved: a band an unpriceable order sails past is not
/// a band. The rejection is reported as unmeasurable so a pricing gap does not trip the circuit
/// breaker, matching <see cref="OrderNotionalRule"/>. <see langword="null"/> thresholds disable
/// the corresponding limb, and an entirely unconfigured rule approves without measuring.
/// </para>
/// </summary>
public sealed class FatFingerRule : IRiskRule
{
    /// <summary>Breach code for the quantity limb.</summary>
    public const string QuantityCode = "FAT_FINGER_QUANTITY_EXCEEDED";

    /// <summary>Breach code for the price-deviation limb.</summary>
    public const string PriceDeviationCode = "FAT_FINGER_PRICE_DEVIATION_EXCEEDED";

    /// <summary>
    /// Breach code for a stop whose trigger sits on the wrong side of the market. Distinct from
    /// <see cref="PriceDeviationCode"/> because the two describe different mistakes and an
    /// operator reading the audit needs to know which: one order was priced through the market,
    /// the other was never really a stop.
    /// </summary>
    public const string StopTriggerCode = "FAT_FINGER_STOP_TRIGGER_WRONG_SIDE";

    /// <summary>Rejection code for a priced order with no reference price to measure against.</summary>
    public const string UnmeasurableCode = "FAT_FINGER_UNMEASURABLE";

    private readonly IPortfolioExposureProvider _exposureProvider;
    private readonly Func<FatFingerThresholds> _thresholds;
    private readonly ILogger<FatFingerRule> _logger;

    /// <param name="thresholds">
    /// Reads <b>both</b> limbs as one value. Two independently locked accessors would let an
    /// evaluation straddle a configuration update and observe a pair that never existed: replacing
    /// (quantity null, band 50) with (quantity 100, band null) atomically can still be read as the
    /// old null quantity and the new null band, leaving the rule apparently unconfigured and
    /// approving an oversized order that both the old and the new configuration would have caught.
    /// </param>
    public FatFingerRule(
        IPortfolioExposureProvider exposureProvider,
        Func<FatFingerThresholds> thresholds,
        ILogger<FatFingerRule> logger)
    {
        _exposureProvider = exposureProvider ?? throw new ArgumentNullException(nameof(exposureProvider));
        _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string RuleName => "FatFinger";

    /// <summary>
    /// Rejects. A fat finger is a mistake, not a decision awaiting sign-off, so parking it for
    /// governed approval would offer an operator a release for an order they never meant to send.
    /// </summary>
    public RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <summary>
    /// Runs ahead of the portfolio-aware rules. A mistyped order should be attributed to the
    /// mistake rather than to whichever exposure ceiling its inflated size happened to breach.
    /// </summary>
    public int Priority => -10;

    /// <inheritdoc />
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        var (maxQuantity, maxDeviationPercent) = _thresholds();
        if (maxQuantity is null or <= 0m && maxDeviationPercent is null or <= 0m)
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        // The quantity limb needs no market data, so it is settled first and on its own. An
        // oversized order is a definitive breach; reporting it as a pricing-data gap because its
        // quote happened to be missing would lose the stable code and the observed-vs-limit
        // evidence for a mistake that is not in doubt.
        // A dollar-sized order's Quantity is not a share count, so the ceiling does not apply to
        // it. BrokerNotionalMetadata is the same seam the gateway reads, so the two cannot
        // disagree about whether this order routes dollars.
        var routesDollars = BrokerNotionalMetadata.TryRead(request.Metadata, request.Quantity) is not null;

        // For a package the gateway routes each leg at Quantity x RatioQuantity, so the top-level
        // count understates what actually reaches the venue: 100 packages with a mistyped ratio of
        // 100 route 10,000 contracts on that leg while passing a 1,000 ceiling. Measure the largest
        // effective leg instead, which reduces to the plain quantity for a single-symbol order.
        // Only evaluated when the limb is actually active: the effective-quantity calculation is
        // meaningless for a dollar-sized order and must not run for a package the price limb
        // already excludes, or an arithmetic edge there would refuse an order this rule does not
        // even gate.
        var quantityMagnitude = !routesDollars && maxQuantity is > 0m
            ? ResolveEffectiveQuantity(request)
            : 0m;
        if (!routesDollars && maxQuantity is > 0m && quantityMagnitude > maxQuantity.Value)
        {
            var quantityDecision = Interop.RiskInterop.EvaluateFatFinger(
                Interop.RiskInterop.CreateFatFingerContext(
                    request,
                    referencePrice: default(decimal?),
                    orderPrice: default(decimal?),
                    maxOrderQuantity: maxQuantity,
                    maxPriceDeviationPercent: default(decimal?)));

            _logger.LogWarning(
                "Fat-finger rule rejected an order on {Symbol}; the quantity limb breached its configured ceiling",
                LogSanitizer.Sanitize(request.Symbol));

            return Task.FromResult(RiskValidationResult.Rejected(
                quantityDecision.Reasons.FirstOrDefault() ?? "Fat-finger quantity ceiling breached.") with
            {
                Code = QuantityCode,
                ObservedValue = quantityMagnitude,
                LimitValue = maxQuantity
            });
        }

        if (maxDeviationPercent is not > 0m || !IsPriceLimbApplicable(request))
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        // The trigger is settled before the limit. A wrong-side trigger is the more dangerous of
        // the two mistakes, because a stop-market order that fires on acceptance routes with no
        // price protection at all — so when an order carries both mistakes, the rejection names
        // the one that would have cost more.
        var stopPrice = HasMeasurableTrigger(request) ? request.StopPrice : null;
        if (stopPrice is > 0m)
        {
            // A trigger is measured against the side-neutral market observation, NOT the crossing
            // touch the limit limb uses. The matcher fires a stop off the traded price — last trade
            // or bar close, with the touch only as a fallback — so measuring the trigger against the
            // side the order would cross at reads a wide book as crossed when the matcher does not.
            // On a 90/110 book with the last trade at 100, a buy stop at 105 is still waiting, but
            // against the 110 ask it looks 4.5% through. The valuation mark resolves the same
            // trade-preferred way, so it is what the trigger is compared with.
            var triggerReference = _exposureProvider.TryGetReferencePrice(request.Symbol);
            if (triggerReference is null or <= 0m)
            {
                return Task.FromResult(Unmeasurable(request, "the stop trigger"));
            }

            var triggerDecision = Interop.RiskInterop.EvaluateFatFingerStopTrigger(
                Interop.RiskInterop.CreateFatFingerContext(
                    request,
                    referencePrice: triggerReference,
                    orderPrice: stopPrice,
                    maxOrderQuantity: default(decimal?),
                    maxPriceDeviationPercent: maxDeviationPercent));

            if (!triggerDecision.Approved)
            {
                _logger.LogWarning(
                    "Fat-finger rule rejected an order on {Symbol}; its stop trigger sits on the wrong side of the market and would fire immediately",
                    LogSanitizer.Sanitize(request.Symbol));

                return Task.FromResult(RiskValidationResult.Rejected(
                    triggerDecision.Reasons.FirstOrDefault() ?? "Fat-finger stop trigger is on the wrong side of the market.") with
                {
                    Code = StopTriggerCode,
                    // Mirrored orientation: for a trigger the wrong side is the opposite of a
                    // limit's, so the reported number is taken as though the sides were swapped.
                    ObservedValue = ResolveAggressiveDeviationPercent(Mirror(request.Side), stopPrice, triggerReference),
                    LimitValue = maxDeviationPercent
                });
            }
        }

        var orderPrice = HasMeasurableLimit(request) ? request.LimitPrice : null;
        if (orderPrice is not > 0m)
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        // A stop-limit's limit is priced off its own trigger rather than off today's market, so
        // that is what it is measured against. Falling back to the touch instead would reject the
        // ordinary protective orders this rule exists to preserve. A stop-limit that reaches here
        // with no usable trigger is malformed, and its null reference takes the unmeasurable path.
        var referencePrice = request.Type is OrderType.StopLimit
            ? stopPrice
            : _exposureProvider.TryGetTouchPrice(request.Symbol, request.Side);
        if (referencePrice is null or <= 0m)
        {
            return Task.FromResult(Unmeasurable(request, "the order price"));
        }

        var decision = Interop.RiskInterop.EvaluateFatFinger(
            Interop.RiskInterop.CreateFatFingerContext(
                request,
                referencePrice: referencePrice,
                orderPrice: orderPrice,
                maxOrderQuantity: default(decimal?),
                maxPriceDeviationPercent: maxDeviationPercent));

        if (decision.Approved)
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        _logger.LogWarning(
            "Fat-finger rule rejected an order on {Symbol}; the price-deviation limb breached its configured band",
            LogSanitizer.Sanitize(request.Symbol));

        return Task.FromResult(RiskValidationResult.Rejected(
            decision.Reasons.FirstOrDefault() ?? "Fat-finger price band breached.") with
        {
            Code = PriceDeviationCode,
            ObservedValue = ResolveAggressiveDeviationPercent(request.Side, orderPrice, referencePrice),
            LimitValue = maxDeviationPercent
        });
    }

    /// <summary>
    /// Refuses a priced order the rule cannot measure. With a band configured, approving what it
    /// could not price would leave a band an unpriceable order sails past, which is not a band;
    /// reporting it as unmeasurable rather than a breach keeps a pricing gap out of the circuit
    /// breaker, matching <see cref="OrderNotionalRule"/>.
    /// </summary>
    private RiskValidationResult Unmeasurable(OrderRequest request, string measuredPrice)
    {
        _logger.LogWarning(
            "Fat-finger rule rejected a priced order it cannot measure: no reference price for {Symbol}",
            LogSanitizer.Sanitize(request.Symbol));

        return RiskValidationResult.Unmeasurable(
            $"Fat-finger band: {request.Symbol} has no reference price to measure {measuredPrice} against.") with
        {
            Code = UnmeasurableCode
        };
    }

    /// <summary>
    /// The largest quantity any single leg of this order actually routes. A package's legs each
    /// route <c>Quantity × RatioQuantity</c>, so the top-level count alone understates the order —
    /// and the gateway only checks that ratios are positive whole numbers, so a mistyped ratio is
    /// exactly the kind of slip this rule exists to catch. Reduces to <see cref="OrderRequest.Quantity"/>
    /// for a single-symbol order.
    /// </summary>
    private static decimal ResolveEffectiveQuantity(OrderRequest request)
    {
        var quantity = Math.Abs(request.Quantity);
        if (request.Legs is not { Count: > 0 } legs)
        {
            return quantity;
        }

        var largestRatio = legs.Max(leg => Math.Abs(leg.RatioQuantity));
        if (largestRatio <= 0m)
        {
            return quantity;
        }

        // Saturate rather than multiply blind. The gateway requires ratios to be positive whole
        // numbers but sets no upper bound, so a product of two individually valid decimals can
        // exceed decimal.MaxValue and throw — which the composite validator would turn into a
        // generic evaluation failure instead of the structured quantity breach this is. Anything
        // that would overflow is astronomically past any ceiling, so the cap reports the same
        // verdict without the exception.
        return quantity > decimal.MaxValue / largestRatio
            ? decimal.MaxValue
            : quantity * largestRatio;
    }

    /// <summary>
    /// Whether this order shape carries any price this rule can measure at all. A package never
    /// does — its prices belong to the combination, not to the top-level symbol — and the types
    /// listed here are the only ones whose prices mean something against the current continuous
    /// market. See the type remarks for the reasoning behind each exclusion.
    /// </summary>
    private static bool IsPriceLimbApplicable(OrderRequest request) =>
        request.Legs is null or { Count: 0 }
        && request.Type is OrderType.Limit or OrderType.StopMarket or OrderType.StopLimit;

    /// <summary>Order types whose <see cref="OrderRequest.StopPrice"/> is a fixed trigger.</summary>
    private static bool HasMeasurableTrigger(OrderRequest request) =>
        request.Type is OrderType.StopMarket or OrderType.StopLimit;

    /// <summary>Order types whose <see cref="OrderRequest.LimitPrice"/> is an operator-entered limit.</summary>
    private static bool HasMeasurableLimit(OrderRequest request) =>
        request.Type is OrderType.Limit or OrderType.StopLimit;

    /// <summary>
    /// The side whose aggressive direction is the mirror of this one's. A stop trigger is
    /// wrong-side in exactly the direction a limit of the same side would be passive, so the
    /// mirrored side lets both limbs share one orientation calculation.
    /// </summary>
    private static OrderSide Mirror(OrderSide side) =>
        side is OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

    /// <summary>
    /// Signed deviation of the order's price from the reference, oriented so that a positive
    /// value always means "aggressive" — a buy paying above the market, or a sell hitting below
    /// it. Mirrors the F# policy's orientation so the reported number matches the one compared,
    /// including its saturation: a ratio whose scaling by 100 would exceed
    /// <see cref="decimal.MaxValue"/> is capped rather than thrown, so the evidence attached to a
    /// breach can never itself turn a structured rejection into an evaluation failure.
    /// </summary>
    private static decimal? ResolveAggressiveDeviationPercent(
        OrderSide side,
        decimal? orderPrice,
        decimal? referencePrice)
    {
        if (orderPrice is not > 0m || referencePrice is not > 0m)
        {
            return null;
        }

        var ratio = (orderPrice.Value - referencePrice.Value) / referencePrice.Value;
        var signedDeviation = ratio switch
        {
            _ when ratio > decimal.MaxValue / 100m => decimal.MaxValue,
            _ when ratio < decimal.MinValue / 100m => decimal.MinValue,
            _ => ratio * 100m
        };

        return side is OrderSide.Buy ? signedDeviation : -signedDeviation;
    }
}
