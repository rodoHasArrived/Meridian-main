using System.Globalization;
using Meridian.Execution;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Risk.Rules;

/// <summary>
/// Price-sanity gate for bracket/OCO child limbs — the take-profit and stop-loss prices an
/// order carries in metadata and the gateway routes as broker-side child legs the moment the
/// parent is accepted. Without this rule those prices reach the broker having passed no risk
/// decision at all: <see cref="FatFingerRule"/> and <see cref="PriceCollarRule"/> measure only
/// the parent's own typed prices.
/// <para>
/// Each limb is measured with exactly the machinery the parent's prices are measured with —
/// <see cref="OrderPriceLimbs"/> resolves what each price is meaningful against, the shared F#
/// band evaluates it, and the operator's one fat-finger deviation tolerance is the band. The
/// limbs are the exit legs of the entry, so each is evaluated as the child order it becomes:
/// the opposite side of the parent, the take-profit as a limit against the touch, the stop-loss
/// trigger against the matcher's trigger reference with the mirrored orientation, and a
/// stop-loss limit against its own trigger. A limb that is individually plausible can still be
/// structurally absurd as a pair — a long exit whose take-profit sits at or below its stop-loss
/// can never bracket the market — so the pair's ordering is checked too, first and without
/// needing market data, for the same reason the fat-finger quantity limb is settled before the
/// price limbs: a definitive breach must not be downgraded to a pricing-data gap.
/// </para>
/// <para>
/// <b>Deliberate boundary: price sanity only — the child limbs reserve no notional or
/// gross-exposure capacity.</b> They are contingent exit legs that close the position the
/// parent opens, and under one-cancels-other semantics at most one of them ever executes, so
/// reserving both against the exposure ceilings would double-count risk the parent's own
/// reservation already carries. Reservation-aware OCO arithmetic — reserve the worse limb,
/// release on the sibling's cancel — remains future work for the pre-trade gate; until it
/// exists this rule keeps a mistyped exit price out of the market without pretending to be an
/// exposure control.
/// </para>
/// <para>
/// Fail-closed to match the parent bands: with the deviation band configured, a bracket whose
/// symbol has no reference price is refused as unmeasurable rather than approved — a band an
/// unpriceable limb sails past is not a band — and the refusal is reported as unmeasurable so
/// a pricing gap cannot trip the circuit breaker. A <see langword="null"/> or non-positive band
/// (the first-run defaults model) disarms the rule entirely, so the limbs pass exactly as the
/// parent's own prices would. Orders carrying no bracket metadata are untouched, and a
/// multi-leg package is excluded for the same reason <see cref="OrderPriceLimbs.AppliesTo"/>
/// excludes it: its prices belong to the combination, not the top-level symbol. A limb price
/// the gateway would route but that is not positive is skipped exactly as the parent's own
/// non-positive prices are — the broker refuses that shape loudly on its own.
/// </para>
/// </summary>
public sealed class BracketChildLimbRule : IRiskRule
{
    /// <summary>
    /// Breach code for a take-profit/stop-loss pair that cannot bracket the market: on a long
    /// exit the take-profit does not sit above the stop-loss trigger, or the mirror on a short
    /// exit. Distinct from the band codes because the pair, not either price alone, is the
    /// mistake.
    /// </summary>
    public const string InvertedCode = "BRACKET_LIMB_INVERTED";

    /// <summary>Breach code for a child limb priced beyond the fat-finger deviation band.</summary>
    public const string PriceDeviationCode = "BRACKET_LIMB_PRICE_DEVIATION_EXCEEDED";

    /// <summary>Breach code for a stop-loss trigger on the wrong side of the market, which
    /// would fire the exit the moment the broker accepts the bracket.</summary>
    public const string StopTriggerCode = "BRACKET_LIMB_STOP_TRIGGER_WRONG_SIDE";

    /// <summary>Rejection code for a bracket whose limbs have no reference price to be
    /// measured against.</summary>
    public const string UnmeasurableCode = "BRACKET_LIMB_UNMEASURABLE";

    private readonly IPortfolioExposureProvider _exposureProvider;
    private readonly Func<FatFingerThresholds> _thresholds;
    private readonly ILogger<BracketChildLimbRule> _logger;

    /// <param name="thresholds">
    /// The fat-finger thresholds, shared deliberately: an operator who says "no price more than
    /// 10% through the market" is stating one tolerance, and a child limb 10% through is the
    /// same class of slip as a parent limit 10% through. Only the deviation limb is read — the
    /// quantity limb already gated the parent, whose size the children inherit.
    /// </param>
    public BracketChildLimbRule(
        IPortfolioExposureProvider exposureProvider,
        Func<FatFingerThresholds> thresholds,
        ILogger<BracketChildLimbRule> logger)
    {
        _exposureProvider = exposureProvider ?? throw new ArgumentNullException(nameof(exposureProvider));
        _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string RuleName => "BracketChildLimbs";

    /// <summary>
    /// Rejects, exactly as <see cref="FatFingerRule"/> does: an absurd exit price is a mistake,
    /// not a decision awaiting sign-off, and offering an operator a release would offer to
    /// confirm a typo.
    /// </summary>
    public RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <summary>
    /// Directly behind the parent price bands (fat-finger at -10, collar at -9) and still ahead
    /// of the portfolio-aware ceilings: a parent-price mistake is attributed to the parent
    /// first, and a child-limb mistake to the limb rather than to whichever exposure ceiling
    /// the order happened to breach.
    /// </summary>
    public int Priority => -8;

    /// <inheritdoc />
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var maxDeviationPercent = _thresholds().MaxPriceDeviationPercent;
        if (maxDeviationPercent is not > 0m)
        {
            // Disarmed band (first-run defaults): the limbs pass exactly as the parent's own
            // prices would under the disarmed fat-finger rule.
            return Task.FromResult(RiskValidationResult.Approved());
        }

        // The same seam the gateway routes the child legs from, so the gate and the broker
        // cannot disagree about which limbs exist or what they are priced at.
        var takeProfitLimit = BracketOrderMetadata.TryReadTakeProfitLimit(request.Metadata);
        var stopLossStop = BracketOrderMetadata.TryReadStopLossStop(request.Metadata);
        var stopLossLimit = BracketOrderMetadata.TryReadStopLossLimit(request.Metadata);
        if (takeProfitLimit is null && stopLossStop is null && stopLossLimit is null)
        {
            // Parent-only order: nothing here to measure, and nothing to fail closed over.
            return Task.FromResult(RiskValidationResult.Approved());
        }

        if (request.Legs is { Count: > 0 })
        {
            // Same exclusion as OrderPriceLimbs.AppliesTo: a package's prices belong to the
            // combination, not the top-level symbol, so there is no reference its child limbs
            // are meaningful against — and the broker refuses bracket metadata on a package.
            return Task.FromResult(RiskValidationResult.Approved());
        }

        // Boundary (deliberate): everything below is price sanity only. The child limbs
        // reserve NO notional or gross-exposure capacity — they are contingent exit legs
        // closing the parent's position, and under one-executes (OCO) semantics reserving
        // both would double-count exposure the parent's reservation already carries.
        // Reservation-aware OCO arithmetic remains future work for the pre-trade gate.

        // Exit legs take the opposite side of the entry. Mirror maps every non-Buy parent to
        // Buy children, matching how the gateway routes an undefined side as a sell.
        var childSide = OrderPriceLimbs.Mirror(request.Side);

        // The pair's ordering is settled first and on its own, like the fat-finger quantity
        // limb: it needs no market data, so a definitive structural breach must not be
        // downgraded to a pricing-data gap when the reference happens to be missing.
        if (takeProfitLimit is > 0m && stopLossStop is > 0m)
        {
            var inverted = childSide is OrderSide.Sell
                ? takeProfitLimit.Value <= stopLossStop.Value
                : takeProfitLimit.Value >= stopLossStop.Value;
            if (inverted)
            {
                _logger.LogWarning(
                    "Bracket child-limb rule rejected an order on {Symbol}; its take-profit and stop-loss cannot bracket the market",
                    LogSanitizer.Sanitize(request.Symbol));

                var orientation = childSide is OrderSide.Sell ? "above" : "below";
                var exit = childSide is OrderSide.Sell ? "long" : "short";
                var takeProfitText = takeProfitLimit.Value.ToString("0.####", CultureInfo.InvariantCulture);
                var stopLossText = stopLossStop.Value.ToString("0.####", CultureInfo.InvariantCulture);
                return Task.FromResult(RiskValidationResult.Rejected(
                    $"Bracket limbs inverted: {request.Symbol} take-profit at {takeProfitText} "
                        + $"must sit {orientation} the stop-loss trigger at {stopLossText} "
                        + $"for a {exit} exit; this pair can never bracket the market.") with
                {
                    Code = InvertedCode,
                    ObservedValue = takeProfitLimit,
                    LimitValue = stopLossStop
                });
            }
        }

        // The stop-loss trigger is settled before the priced limbs, for the fat-finger rule's
        // reason: a trigger on the wrong side fires the exit the moment the broker accepts the
        // bracket, which is the mistake that would have cost the most.
        if (stopLossStop is not null || stopLossLimit is not null)
        {
            // The stop-loss leg as the child order it becomes: a stop-market exit, or a
            // stop-limit exit when a limit rides along. A limit with no trigger resolves
            // unmeasurable through the shared limbs — there is nothing the limit is priced
            // off — which is the fail-closed posture, and the broker refuses that shape too.
            var stopLossChild = request with
            {
                Side = childSide,
                Type = stopLossLimit is not null ? OrderType.StopLimit : OrderType.StopMarket,
                StopPrice = stopLossStop,
                LimitPrice = stopLossLimit,
                TrailPrice = null,
                TrailPercent = null,
                Legs = null
            };

            var trigger = OrderPriceLimbs.ResolveTrigger(stopLossChild, _exposureProvider);
            if (trigger.State is PriceLimbState.Unmeasurable)
            {
                return Task.FromResult(Unmeasurable(request, "the stop-loss trigger"));
            }

            if (trigger.State is PriceLimbState.Measured)
            {
                var triggerDecision = Interop.RiskInterop.EvaluateFatFingerStopTrigger(
                    Interop.RiskInterop.CreateFatFingerContext(
                        stopLossChild,
                        referencePrice: trigger.Reference,
                        orderPrice: trigger.Price,
                        maxOrderQuantity: default(decimal?),
                        maxPriceDeviationPercent: maxDeviationPercent));

                if (!triggerDecision.Approved)
                {
                    _logger.LogWarning(
                        "Bracket child-limb rule rejected an order on {Symbol}; its stop-loss trigger sits on the wrong side of the market and would fire immediately",
                        LogSanitizer.Sanitize(request.Symbol));

                    return Task.FromResult(RiskValidationResult.Rejected(
                        "Bracket stop-loss leg: " + (triggerDecision.Reasons.FirstOrDefault()
                            ?? "the stop-loss trigger is on the wrong side of the market.")) with
                    {
                        Code = StopTriggerCode,
                        ObservedValue = OrderPriceLimbs.AggressiveDeviationPercent(
                            trigger.Orientation, trigger.Price, trigger.Reference),
                        LimitValue = maxDeviationPercent
                    });
                }
            }

            var stopLossLimitLimb = OrderPriceLimbs.ResolveLimit(stopLossChild, _exposureProvider);
            if (stopLossLimitLimb.State is PriceLimbState.Unmeasurable)
            {
                return Task.FromResult(Unmeasurable(request, "the stop-loss limit"));
            }

            if (stopLossLimitLimb.State is PriceLimbState.Measured
                && EvaluateBand(stopLossChild, stopLossLimitLimb, maxDeviationPercent, "stop-loss")
                    is { } stopLossRefusal)
            {
                return Task.FromResult(stopLossRefusal);
            }
        }

        if (takeProfitLimit is not null)
        {
            // The take-profit leg is a plain limit exit, measured against the touch its side
            // would cross at, exactly as a parent limit is. The band stays directional: a
            // distant take-profit on the passive side is the point of a take-profit and never
            // breaches, while one through the market is the classic inverted typo.
            var takeProfitChild = request with
            {
                Side = childSide,
                Type = OrderType.Limit,
                LimitPrice = takeProfitLimit,
                StopPrice = null,
                TrailPrice = null,
                TrailPercent = null,
                Legs = null
            };

            var limb = OrderPriceLimbs.ResolveLimit(takeProfitChild, _exposureProvider);
            if (limb.State is PriceLimbState.Unmeasurable)
            {
                return Task.FromResult(Unmeasurable(request, "the take-profit limit"));
            }

            if (limb.State is PriceLimbState.Measured
                && EvaluateBand(takeProfitChild, limb, maxDeviationPercent, "take-profit")
                    is { } takeProfitRefusal)
            {
                return Task.FromResult(takeProfitRefusal);
            }
        }

        return Task.FromResult(RiskValidationResult.Approved());
    }

    /// <summary>
    /// Runs one measured child limb through the same F# band the fat-finger rule applies to the
    /// parent's limit, returning the refusal when it breaches and <see langword="null"/> when it
    /// passes. Reuses the shared evaluation rather than duplicating band math, so the two gates
    /// cannot drift apart about how far is too far.
    /// </summary>
    private RiskValidationResult? EvaluateBand(
        OrderRequest childRequest,
        PriceLimb limb,
        decimal? maxDeviationPercent,
        string legName)
    {
        var decision = Interop.RiskInterop.EvaluateFatFinger(
            Interop.RiskInterop.CreateFatFingerContext(
                childRequest,
                referencePrice: limb.Reference,
                orderPrice: limb.Price,
                maxOrderQuantity: default(decimal?),
                maxPriceDeviationPercent: maxDeviationPercent));

        if (decision.Approved)
        {
            return null;
        }

        _logger.LogWarning(
            "Bracket child-limb rule rejected an order on {Symbol}; its {LegName} leg is priced beyond the configured band",
            LogSanitizer.Sanitize(childRequest.Symbol),
            legName);

        return RiskValidationResult.Rejected(
            $"Bracket {legName} leg: " + (decision.Reasons.FirstOrDefault()
                ?? "the leg's price is beyond the configured band.")) with
        {
            Code = PriceDeviationCode,
            ObservedValue = OrderPriceLimbs.AggressiveDeviationPercent(
                limb.Orientation, limb.Price, limb.Reference),
            LimitValue = maxDeviationPercent
        };
    }

    /// <summary>
    /// Refuses a bracket whose limbs this rule cannot measure, matching the fat-finger rule's
    /// posture exactly: with a band configured, approving what it could not price would leave a
    /// band an unpriceable limb sails past, and reporting it as unmeasurable rather than a
    /// breach keeps a pricing gap out of the circuit breaker.
    /// </summary>
    private RiskValidationResult Unmeasurable(OrderRequest request, string measuredPrice)
    {
        _logger.LogWarning(
            "Bracket child-limb rule rejected a bracket order it cannot measure: no reference price for {Symbol}",
            LogSanitizer.Sanitize(request.Symbol));

        return RiskValidationResult.Unmeasurable(
            $"Bracket limb band: {request.Symbol} has no reference price to measure {measuredPrice} against.") with
        {
            Code = UnmeasurableCode
        };
    }
}
