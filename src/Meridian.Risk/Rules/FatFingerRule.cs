using Meridian.Execution;
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
/// Only <see cref="OrderRequest.LimitPrice"/> is measured. <see cref="OrderRequest.StopPrice"/>
/// is deliberately never passed: a stop sits away from the market by design, so measuring it
/// would reject every stop-loss. A stop-limit is still measured on its limit price, because an
/// aggressive limit is a fat finger whether or not a trigger precedes it.
/// </para>
/// <para>
/// A market order carries no typed price, so the price limb does not apply to it — there is
/// nothing to mistype. Its size is still gated by the quantity limb, and its economic size by
/// <see cref="OrderNotionalRule"/>.
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

        // Only the operator-entered limit price. See the type remarks: a stop price is away
        // from the market by design and must never reach the deviation band.
        var orderPrice = request.LimitPrice;

        // An order pays the touch, not the midpoint, so the reference is the side of the book
        // this order would actually cross.
        var referencePrice = _exposureProvider.TryGetExecutablePrice(request.Symbol, request.Side);

        var deviationBandActive = maxDeviationPercent is > 0m;
        if (deviationBandActive && orderPrice is > 0m && referencePrice is null or <= 0m)
        {
            _logger.LogWarning(
                "Fat-finger rule rejected a priced order it cannot measure: no reference price for {Symbol}",
                request.Symbol);
            return Task.FromResult(RiskValidationResult.Unmeasurable(
                $"Fat-finger band: {request.Symbol} has no reference price to measure the order price against.") with
            {
                Code = UnmeasurableCode
            });
        }

        var context = Interop.RiskInterop.CreateFatFingerContext(
            request,
            referencePrice: referencePrice ?? default(decimal?),
            orderPrice: orderPrice ?? default(decimal?),
            maxOrderQuantity: maxQuantity,
            maxPriceDeviationPercent: maxDeviationPercent);
        var decision = Interop.RiskInterop.EvaluateFatFinger(context);

        if (decision.Approved)
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        var reason = decision.Reasons.FirstOrDefault() ?? "Fat-finger check failed.";

        // Attribution only. The F# policy has already decided admission; this picks which limb
        // to report and what numbers to carry, so a mislabel could never change what the order
        // does. Quantity is tested first because the policy tests it first.
        var quantityMagnitude = Math.Abs(request.Quantity);
        var isQuantityBreach = maxQuantity is > 0m && quantityMagnitude > maxQuantity.Value;

        _logger.LogWarning(
            "Fat-finger rule rejected an order on {Symbol}; the {Limb} limb breached its configured band",
            request.Symbol,
            isQuantityBreach ? "quantity" : "price-deviation");

        return Task.FromResult(RiskValidationResult.Rejected(reason) with
        {
            Code = isQuantityBreach ? QuantityCode : PriceDeviationCode,
            ObservedValue = isQuantityBreach
                ? quantityMagnitude
                : ResolveAggressiveDeviationPercent(request.Side, orderPrice, referencePrice),
            LimitValue = isQuantityBreach ? maxQuantity : maxDeviationPercent
        });
    }

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
