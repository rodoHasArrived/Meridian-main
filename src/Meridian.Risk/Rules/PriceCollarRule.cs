using Meridian.Execution;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Risk.Rules;

/// <summary>Operator-configured collar band, as a percentage through the reference price.</summary>
/// <param name="CollarPercent">
/// How far through the reference an order may be priced before it needs sign-off. Null or
/// non-positive disables the collar entirely, matching every other threshold in this catalogue.
/// </param>
public readonly record struct PriceCollarThresholds(decimal? CollarPercent);

/// <summary>
/// Parks an order priced further through the market than the desk transacts without sign-off, so a
/// human decides rather than the order routing on its own.
/// <para>
/// This is the escalate-severity counterpart to <see cref="FatFingerRule"/>, and the distinction
/// between them is the point. A fat finger is a <em>mistake</em>: nobody meant to send it, so
/// offering an operator a release would be offering to confirm a typo, and the rule rejects at
/// <see cref="RiskRuleSeverity.Error"/>. A collar breach is a <em>decision</em>: the price is
/// aggressive but may be exactly what the desk intends in a fast market, so it is parked at
/// <see cref="RiskRuleSeverity.Escalate"/> for someone to approve or refuse. Rejecting these would
/// stop legitimate trading; approving them silently would remove the judgement the band exists to
/// demand.
/// </para>
/// <para>
/// The collar therefore sits <b>inside</b> the fat-finger band: it should be the tighter of the two,
/// so ordinary aggressive orders escalate while genuinely mistyped ones are refused outright. The
/// two bands are configured independently and this rule does not police that ordering — a collar
/// set wider than the fat-finger band simply never fires, because the harder rule refuses first at
/// <see cref="FatFingerRule.Priority"/>, which runs ahead of this one.
/// </para>
/// <para>
/// Everything about <em>what</em> is measured comes from <see cref="OrderPriceLimbs"/>, shared with
/// the fat-finger band, and the deviation itself is computed by the same F# policy the harder rule
/// calls. The two controls differ by threshold and severity and by nothing else: a collar that
/// disagreed with the fat-finger band about which order shapes carry a measurable price, or about
/// which reference a price is meaningful against, would protect a shape the other refuses.
/// </para>
/// </summary>
public sealed class PriceCollarRule : IRiskRule
{
    /// <summary>Stable code for a limit priced beyond the collar.</summary>
    public const string PriceCollarCode = "PRICE_COLLAR_EXCEEDED";

    /// <summary>Stable code for a stop trigger sitting beyond the collar on the crossing side.</summary>
    public const string StopTriggerCollarCode = "PRICE_COLLAR_STOP_TRIGGER";

    /// <summary>Stable code for a priced order the collar cannot measure.</summary>
    public const string UnmeasurableCode = "PRICE_COLLAR_UNMEASURABLE";

    private readonly IPortfolioExposureProvider _exposureProvider;
    private readonly Func<PriceCollarThresholds> _thresholds;
    private readonly ILogger<PriceCollarRule> _logger;

    /// <summary>Creates the rule over a live threshold accessor, so a reconfiguration takes effect
    /// without recomposing the validator.</summary>
    public PriceCollarRule(
        IPortfolioExposureProvider exposureProvider,
        Func<PriceCollarThresholds> thresholds,
        ILogger<PriceCollarRule> logger)
    {
        _exposureProvider = exposureProvider ?? throw new ArgumentNullException(nameof(exposureProvider));
        _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string RuleName => "PriceCollar";

    /// <summary>
    /// Escalate, not Error: a collar breach is a decision awaiting sign-off rather than a mistake.
    /// The severity is what makes the outcome releasable — the validator admits an escalated order
    /// only against an approval naming this rule.
    /// </summary>
    public RiskRuleSeverity Severity => RiskRuleSeverity.Escalate;

    /// <summary>
    /// Runs immediately after <see cref="FatFingerRule"/> and still ahead of the portfolio-aware
    /// ceilings. An order that is both mistyped and beyond the collar is a typo, and reporting it as
    /// a decision awaiting approval would offer a release for an order nobody meant to send.
    /// </summary>
    public int Priority => -9;

    /// <inheritdoc />
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var collarPercent = _thresholds().CollarPercent;
        if (collarPercent is not > 0m || !OrderPriceLimbs.AppliesTo(request))
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        // Trigger before limit, for the same reason the fat-finger band settles them in that order:
        // a stop that fires on acceptance routes with no price protection at all, so when an order
        // carries both, the escalation names the one that would have cost more.
        var trigger = OrderPriceLimbs.ResolveTrigger(request, _exposureProvider);
        if (trigger.State is PriceLimbState.Unmeasurable)
        {
            return Task.FromResult(Unmeasurable(request, trigger.Label));
        }

        if (trigger.State is PriceLimbState.Measured)
        {
            var decision = Interop.RiskInterop.EvaluateFatFingerStopTrigger(
                Interop.RiskInterop.CreateFatFingerContext(
                    request,
                    referencePrice: trigger.Reference,
                    orderPrice: trigger.Price,
                    maxOrderQuantity: default(decimal?),
                    maxPriceDeviationPercent: collarPercent));

            if (!decision.Approved)
            {
                _logger.LogWarning(
                    "Price collar parked an order on {Symbol}; its stop trigger sits beyond the collar on the crossing side",
                    LogSanitizer.Sanitize(request.Symbol));

                return Task.FromResult(Escalated(
                    decision.Reasons.FirstOrDefault()
                        ?? "Stop trigger is beyond the price collar and needs approval.",
                    StopTriggerCollarCode,
                    OrderPriceLimbs.AggressiveDeviationPercent(
                        trigger.Orientation, trigger.Price, trigger.Reference),
                    collarPercent));
            }
        }

        var limit = OrderPriceLimbs.ResolveLimit(request, _exposureProvider);
        if (limit.State is PriceLimbState.NotApplicable)
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        if (limit.State is PriceLimbState.Unmeasurable)
        {
            return Task.FromResult(Unmeasurable(request, limit.Label));
        }

        var limitDecision = Interop.RiskInterop.EvaluateFatFinger(
            Interop.RiskInterop.CreateFatFingerContext(
                request,
                referencePrice: limit.Reference,
                orderPrice: limit.Price,
                maxOrderQuantity: default(decimal?),
                maxPriceDeviationPercent: collarPercent));

        if (limitDecision.Approved)
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        _logger.LogWarning(
            "Price collar parked an order on {Symbol}; its limit is priced beyond the collar band",
            LogSanitizer.Sanitize(request.Symbol));

        return Task.FromResult(Escalated(
            limitDecision.Reasons.FirstOrDefault() ?? "Order price is beyond the collar and needs approval.",
            PriceCollarCode,
            OrderPriceLimbs.AggressiveDeviationPercent(limit.Orientation, limit.Price, limit.Reference),
            collarPercent));
    }

    /// <summary>
    /// Parks a priced order the collar cannot measure. Approving what it could not price would
    /// leave a band an unpriceable order sails past, which is not a band — the same reasoning
    /// <see cref="FatFingerRule"/> applies, but landing on an escalation rather than a refusal
    /// because this rule's whole posture is that a human decides.
    /// </summary>
    private RiskValidationResult Unmeasurable(OrderRequest request, string measuredPrice)
    {
        _logger.LogWarning(
            "Price collar parked a priced order it cannot measure: no reference price for {Symbol}",
            LogSanitizer.Sanitize(request.Symbol));

        return RiskValidationResult.Unmeasurable(
            $"Price collar: {request.Symbol} has no reference price to measure {measuredPrice} against.") with
        {
            Code = UnmeasurableCode
        };
    }

    private static RiskValidationResult Escalated(
        string reason,
        string code,
        decimal? observed,
        decimal? limit) =>
        RiskValidationResult.Escalated(reason) with
        {
            Code = code,
            ObservedValue = observed,
            LimitValue = limit
        };
}
