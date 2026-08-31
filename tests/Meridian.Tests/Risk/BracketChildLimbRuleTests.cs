using System.Globalization;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Risk;

/// <summary>
/// Covers the bracket child-limb gate: take-profit and stop-loss prices ride in order metadata
/// and route as broker-side child legs, so each limb must pass the same directional fat-finger
/// band the parent's own prices pass — evaluated as the exit order it becomes, on the opposite
/// side of the entry.
/// <para>
/// The passes matter as much as the refusals. A take-profit far on the passive side and a
/// stop-loss far on the protective side are the whole point of a bracket, so those cases are
/// asserted explicitly: a gate that rejected ordinary exits would take every bracket strategy
/// with it.
/// </para>
/// </summary>
public sealed class BracketChildLimbRuleTests
{
    private const decimal Reference = 100m;

    private static OrderRequest Bracket(
        OrderSide side = OrderSide.Buy,
        decimal? takeProfit = null,
        decimal? stopLoss = null,
        decimal? stopLossLimit = null,
        decimal quantity = 10m,
        string symbol = "AAPL")
    {
        var metadata = new Dictionary<string, string>();
        if (takeProfit is not null)
        {
            metadata["take_profit.limit_price"] = takeProfit.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (stopLoss is not null)
        {
            metadata["stop_loss.stop_price"] = stopLoss.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (stopLossLimit is not null)
        {
            metadata["stop_loss.limit_price"] = stopLossLimit.Value.ToString(CultureInfo.InvariantCulture);
        }

        return new OrderRequest
        {
            Symbol = symbol,
            Side = side,
            Type = OrderType.Market,
            Quantity = quantity,
            Metadata = metadata.Count > 0 ? metadata : null
        };
    }

    private static BracketChildLimbRule Rule(
        decimal? maxDeviationPercent = 10m,
        decimal? maxQuantity = null,
        decimal? referencePrice = Reference) => new(
            new StubExposureProvider(referencePrice),
            () => new FatFingerThresholds(maxQuantity, maxDeviationPercent),
            NullLogger<BracketChildLimbRule>.Instance);

    [Fact]
    public void Rule_RejectsRatherThanEscalates_AndRunsWithTheParentPriceBands()
    {
        var rule = Rule();

        // An absurd exit price is a mistake, not a decision awaiting sign-off — the same
        // posture as the fat-finger band the limbs share.
        rule.Severity.Should().Be(RiskRuleSeverity.Error);
        // Behind fat-finger (-10) and the collar (-9), still ahead of the portfolio ceilings,
        // so a limb mistake is attributed to the limb rather than to an exposure ceiling.
        rule.Priority.Should().BeLessThan(0);
        rule.RuleName.Should().Be("BracketChildLimbs");
    }

    // --- sane brackets route ---

    [Fact]
    public async Task SaneLongBracket_Approves()
    {
        // Long entry at a 100 market: take profit above at 110, stop out below at 95. The
        // ordinary shape every bracket strategy submits.
        var result = await Rule().EvaluateAsync(Bracket(takeProfit: 110m, stopLoss: 95m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task SaneShortBracket_Approves()
    {
        // Short entry: the exits are buys, so the take-profit sits below the market and the
        // stop-loss above it.
        var result = await Rule().EvaluateAsync(
            Bracket(side: OrderSide.Sell, takeProfit: 90m, stopLoss: 105m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task DistantTakeProfit_OnThePassiveSide_Approves()
    {
        // A sell take-profit 100% above the market is ambitious, not mistyped — the band is
        // directional exactly as it is for the parent, or every far target would be refused.
        var result = await Rule().EvaluateAsync(Bracket(takeProfit: 200m, stopLoss: 95m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task DistantStopLoss_OnTheProtectiveSide_Approves()
    {
        // A sell stop 60% below the market waits for the market to come to it, which is what a
        // stop is; only the wrong side is measured, exactly as for a parent stop.
        var result = await Rule().EvaluateAsync(Bracket(takeProfit: 110m, stopLoss: 40m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task StopLossLimit_IsMeasuredAgainstItsOwnTrigger_NotTheMarket()
    {
        // A stop-limit exit: sell stop at 90 with an 89 limit is 1.1% from its trigger though
        // 11% from the 100 market — measured against the market it would be refused, which is
        // exactly the protective order this control must preserve.
        var result = await Rule().EvaluateAsync(
            Bracket(takeProfit: 110m, stopLoss: 90m, stopLossLimit: 89m));

        result.IsApproved.Should().BeTrue();
    }

    // --- absurd limbs refuse the whole bracket ---

    [Fact]
    public async Task TakeProfitThroughTheMarket_RejectsWithTheBandReason()
    {
        // A long's take-profit is a sell: 50 against a 100 market is 50% through the touch.
        // Carried alone (a take-profit-only child is a routable shape), so the band refusal
        // is isolated from the pair check — and it reads exactly as a fat-finger parent
        // refusal does.
        var result = await Rule().EvaluateAsync(Bracket(takeProfit: 50m));

        result.IsApproved.Should().BeFalse();
        result.RequiresApproval.Should().BeFalse();
        result.IsUnmeasurable.Should().BeFalse();
        result.Code.Should().Be(BracketChildLimbRule.PriceDeviationCode);
        result.RejectReason.Should().Contain("take-profit");
        result.ObservedValue.Should().Be(50m);
        result.LimitValue.Should().Be(10m);
    }

    [Fact]
    public async Task WrongSideStopLoss_OnALong_Rejects_BecauseItFiresImmediately()
    {
        // A long's stop-loss is a sell stop and belongs BELOW the market. At 120 against a 100
        // market it is already crossed: the broker would fire the exit the moment it accepts
        // the bracket, unwinding the position the entry just opened.
        var result = await Rule().EvaluateAsync(Bracket(takeProfit: 130m, stopLoss: 120m));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(BracketChildLimbRule.StopTriggerCode);
        result.RejectReason.Should().Contain("stop-loss");
        result.ObservedValue.Should().Be(20m);
        result.LimitValue.Should().Be(10m);
    }

    [Fact]
    public async Task WrongSideStopLoss_OnAShort_Rejects()
    {
        // The mirror: a short's stop-loss is a buy stop and belongs ABOVE the market. Carried
        // alone (a stop-loss-only child is a routable shape) so the wrong-side refusal is
        // isolated from the pair check.
        var result = await Rule().EvaluateAsync(
            Bracket(side: OrderSide.Sell, stopLoss: 50m));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(BracketChildLimbRule.StopTriggerCode);
    }

    [Fact]
    public async Task MarginallyCrossedStopLoss_InsideTheBand_Approves()
    {
        // 105 is on the wrong side for a long's sell stop, but only 5% wrong — inside the
        // operator's stated tolerance, exactly as a parent stop would be treated.
        var result = await Rule().EvaluateAsync(Bracket(takeProfit: 110m, stopLoss: 105m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task StopLossLimit_FarFromItsOwnTrigger_Rejects()
    {
        // Trigger at 90, limit at 50: once the stop fires, the exit stands 44% below its own
        // trigger — the stop-limit shape of a mistyped bracket.
        var result = await Rule().EvaluateAsync(
            Bracket(takeProfit: 110m, stopLoss: 90m, stopLossLimit: 50m));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(BracketChildLimbRule.PriceDeviationCode);
        result.RejectReason.Should().Contain("stop-loss");
    }

    // --- the pair itself can be absurd ---

    [Fact]
    public async Task InvertedLongBracket_TakeProfitAtOrBelowTheStop_Rejects_EvenWithBothLimbsInBand()
    {
        // Each price alone is inside a 10% band: 96 is 4% below the touch, 98 is a correctly
        // placed sell stop. But a long exit whose take-profit sits below its stop-loss can
        // never bracket the market — one of the two was mistyped.
        var result = await Rule().EvaluateAsync(Bracket(takeProfit: 96m, stopLoss: 98m));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(BracketChildLimbRule.InvertedCode);
        result.ObservedValue.Should().Be(96m);
        result.LimitValue.Should().Be(98m);
    }

    [Fact]
    public async Task InvertedShortBracket_TakeProfitAtOrAboveTheStop_Rejects()
    {
        // The mirror for a short: the buy take-profit must sit below the buy stop-loss.
        var result = await Rule().EvaluateAsync(
            Bracket(side: OrderSide.Sell, takeProfit: 104m, stopLoss: 102m));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(BracketChildLimbRule.InvertedCode);
    }

    [Fact]
    public async Task InvertedBracket_IsReportedAsInverted_EvenWithNoReferencePrice()
    {
        // The pair needs no market data to be wrong, so the definitive structural breach must
        // not be downgraded to a pricing-data gap — the same reasoning that settles the
        // fat-finger quantity limb before its price limbs.
        var rule = Rule(referencePrice: null);

        var result = await rule.EvaluateAsync(Bracket(takeProfit: 90m, stopLoss: 95m));

        result.IsApproved.Should().BeFalse();
        result.IsUnmeasurable.Should().BeFalse();
        result.Code.Should().Be(BracketChildLimbRule.InvertedCode);
    }

    // --- fail closed, disarm open ---

    [Fact]
    public async Task BracketWithNoReferencePrice_RejectsAsUnmeasurable_WithoutClaimingABreach()
    {
        var rule = Rule(referencePrice: null);

        var result = await rule.EvaluateAsync(Bracket(takeProfit: 110m, stopLoss: 95m));

        result.IsApproved.Should().BeFalse();
        // Unmeasurable rather than breached, so a pricing gap cannot trip the circuit breaker —
        // the same posture as the parent fat-finger band's missing-reference refusal.
        result.IsUnmeasurable.Should().BeTrue();
        result.Code.Should().Be(BracketChildLimbRule.UnmeasurableCode);
    }

    [Fact]
    public async Task StopLossLimitWithoutATrigger_RejectsAsUnmeasurable()
    {
        // A stop-loss limit with no stop price has nothing it is priced off; the shared limb
        // resolution fails closed rather than waving through a price nothing can measure.
        var result = await Rule().EvaluateAsync(Bracket(stopLossLimit: 95m));

        result.IsApproved.Should().BeFalse();
        result.IsUnmeasurable.Should().BeTrue();
        result.Code.Should().Be(BracketChildLimbRule.UnmeasurableCode);
    }

    [Fact]
    public async Task DisarmedBand_ApprovesEvenAnAbsurdBracket()
    {
        // First-run defaults: a null band disarms the parent price limbs, and the child limbs
        // pass exactly as the parent would — never one gate stricter than the operator armed.
        var rule = Rule(maxDeviationPercent: null, referencePrice: null);

        var result = await rule.EvaluateAsync(Bracket(takeProfit: 1m, stopLoss: 1_000m));

        result.IsApproved.Should().BeTrue();
    }

    // --- scope: only bracket limbs, only single-symbol parents ---

    [Fact]
    public async Task ParentOnlyOrder_IsUntouched_EvenWithNoReferencePrice()
    {
        // No bracket metadata means nothing to measure and nothing to fail closed over: an
        // unpriceable symbol must not become unroutable because this rule is armed.
        var rule = Rule(referencePrice: null);

        var result = await rule.EvaluateAsync(Bracket());

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task ParentQuantity_IsNotReGated_BecauseTheFatFingerRuleOwnsIt()
    {
        // The quantity limb already gated the parent, whose size the children inherit;
        // re-applying it here would report one mistake twice under two rule names.
        var rule = Rule(maxDeviationPercent: 10m, maxQuantity: 100m);

        var result = await rule.EvaluateAsync(
            Bracket(takeProfit: 110m, stopLoss: 95m, quantity: 100_000m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task MultiLegPackage_IsExcluded_AsTheParentPriceLimbsExcludeIt()
    {
        // A package's prices belong to the combination, not the top-level symbol, so there is
        // no reference its child limbs are meaningful against — the same exclusion
        // OrderPriceLimbs applies to the parent's own prices.
        var order = Bracket(takeProfit: 1m, stopLoss: 1_000m) with
        {
            Legs = [new OrderLeg { Symbol = "AAPL_C1", Side = OrderSide.Sell, RatioQuantity = 1m }]
        };

        var result = await Rule().EvaluateAsync(order);

        result.IsApproved.Should().BeTrue();
    }

    // --- the metadata seam matches the gateway ---

    [Fact]
    public async Task GatewayAliasKeys_AreReadExactlyAsTheGatewayReadsThem()
    {
        // The Alpaca-prefixed alias routes a child leg, so it must be validated too — the rule
        // and the gateway read the same seam or a limb routes unmeasured under a spelling the
        // gate does not know.
        var order = Bracket() with
        {
            Metadata = new Dictionary<string, string>
            {
                ["alpaca:take_profit_limit_price"] = "50"
            }
        };

        var result = await Rule().EvaluateAsync(order);

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(BracketChildLimbRule.PriceDeviationCode);
    }

    [Fact]
    public async Task UnparseableLimbValue_RoutesNoLegAtTheGateway_SoItIsNotMeasuredHere()
    {
        // The gateway parses the first non-blank alias with invariant decimal rules and routes
        // no child leg when it cannot; reading the same way keeps the gate and the broker in
        // agreement about which legs exist.
        var order = Bracket() with
        {
            Metadata = new Dictionary<string, string> { ["take_profit.limit_price"] = "not-a-price" }
        };

        var result = await Rule(referencePrice: null).EvaluateAsync(order);

        result.IsApproved.Should().BeTrue();
    }

    private sealed class StubExposureProvider(decimal? referencePrice) : IPortfolioExposureProvider
    {
        public PortfolioExposureSnapshot GetSnapshot() => PortfolioExposureSnapshot.Empty;

        public decimal? TryGetReferencePrice(string symbol) => referencePrice;
    }
}
