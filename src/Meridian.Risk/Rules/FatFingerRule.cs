using Meridian.Execution;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Risk.Rules;

/// <summary>
/// Blocks the two classic order-entry mistakes: a quantity far larger than the desk ever
/// intends to send in one order, and a price typed far through the market.
/// <para>
/// The price limb is deliberately <b>directional</b>. A resting buy far below the market and a
/// resting sell far above it are ordinary working orders, so only the aggressive side is
/// measured — paying above the reference on a buy, or selling below it on a sell. A symmetric
/// band would reject the entire resting book.
/// </para>
/// <para>
/// The price limb applies only to a plain <see cref="OrderType.Limit"/> order, because that is the
/// only type whose limit price is meaningful against the <i>current continuous</i> market — which
/// is the only market this rule can see. Everything else is excluded for a specific reason:
/// </para>
/// <list type="bullet">
///   <item><see cref="OrderRequest.StopPrice"/> is never measured at all: a stop sits away from
///     the market by design, so measuring it would reject every stop-loss.</item>
///   <item><see cref="OrderType.StopLimit"/> is excluded too. Its limit is priced relative to the
///     trigger, not to today's market — a sell stop at $90 with an $89 limit is an ordinary
///     protective order, and measuring that $89 against a $100 market would reject it.</item>
///   <item><see cref="OrderType.Market"/> is excluded even when it carries a
///     <see cref="OrderRequest.LimitPrice"/>. The paper gateway lets a caller supply one as its
///     simulated market observation, so on a market order that value is not an operator's typed
///     limit and must not be compared against the live book.</item>
///   <item><see cref="OrderType.LimitOnOpen"/> and <see cref="OrderType.LimitOnClose"/> are
///     excluded: their limit applies to a future auction, not the continuous touch. Measuring one
///     against the present BBO rejects routine auction orders, and pre-open there may be no fresh
///     BBO at all, which would make every auction order unmeasurable.</item>
///   <item>A multi-leg order is excluded: its limit is the net debit or credit for the
///     <i>package</i>, which is not comparable to a quote for the top-level symbol. A $1 credit
///     spread on a $200 underlying would otherwise look 99.5% through the market.</item>
/// </list>
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

    /// <summary>Rejection code for a priced order with no reference price to measure against.</summary>
    public const string UnmeasurableCode = "FAT_FINGER_UNMEASURABLE";

    private readonly IPortfolioExposureProvider _exposureProvider;
    private readonly Func<decimal?> _maxOrderQuantity;
    private readonly Func<decimal?> _maxPriceDeviationPercent;
    private readonly ILogger<FatFingerRule> _logger;

    public FatFingerRule(
        IPortfolioExposureProvider exposureProvider,
        Func<decimal?> maxOrderQuantity,
        Func<decimal?> maxPriceDeviationPercent,
        ILogger<FatFingerRule> logger)
    {
        _exposureProvider = exposureProvider ?? throw new ArgumentNullException(nameof(exposureProvider));
        _maxOrderQuantity = maxOrderQuantity ?? throw new ArgumentNullException(nameof(maxOrderQuantity));
        _maxPriceDeviationPercent = maxPriceDeviationPercent ?? throw new ArgumentNullException(nameof(maxPriceDeviationPercent));
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
        var maxQuantity = _maxOrderQuantity();
        var maxDeviationPercent = _maxPriceDeviationPercent();
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

        var quantityMagnitude = Math.Abs(request.Quantity);
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

        // Only an operator-entered limit on an immediately marketable order type. See the type
        // remarks for why market, stop, stop-limit, and multi-leg orders are all excluded.
        var orderPrice = IsPriceLimbApplicable(request) ? request.LimitPrice : null;
        if (maxDeviationPercent is not > 0m || orderPrice is not > 0m)
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        // A price control compares against what the order can actually trade at, so this takes
        // the touch rather than the deliberately conservative valuation price: measuring a sell
        // against the midpoint would make an ordinary marketable sell at the bid look like it is
        // priced through the market by half the spread.
        var referencePrice = _exposureProvider.TryGetTouchPrice(request.Symbol, request.Side);
        if (referencePrice is null or <= 0m)
        {
            _logger.LogWarning(
                "Fat-finger rule rejected a priced order it cannot measure: no reference price for {Symbol}",
                LogSanitizer.Sanitize(request.Symbol));
            return Task.FromResult(RiskValidationResult.Unmeasurable(
                $"Fat-finger band: {request.Symbol} has no reference price to measure the order price against.") with
            {
                Code = UnmeasurableCode
            });
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
    /// Whether this order's <see cref="OrderRequest.LimitPrice"/> is an operator-entered limit
    /// that is meaningful against the current market. Only immediately marketable limit types
    /// qualify, and never a package order — see the type remarks for the reasoning behind each
    /// exclusion.
    /// </summary>
    private static bool IsPriceLimbApplicable(OrderRequest request) =>
        request.Legs is null or { Count: 0 }
        && request.Type is OrderType.Limit;

    /// <summary>
    /// Signed deviation of the order's price from the reference, oriented so that a positive
    /// value always means "aggressive" — a buy paying above the market, or a sell hitting below
    /// it. Mirrors the F# policy's orientation so the reported number matches the one compared.
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

        var signedDeviation = (orderPrice.Value - referencePrice.Value) / referencePrice.Value * 100m;
        return side is OrderSide.Buy ? signedDeviation : -signedDeviation;
    }
}
