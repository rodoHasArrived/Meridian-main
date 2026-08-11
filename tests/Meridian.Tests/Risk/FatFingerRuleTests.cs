using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Risk;

/// <summary>
/// Covers the fat-finger gate: an absolute per-order quantity ceiling and a directional
/// price-deviation band measured against the executable side of the book.
/// <para>
/// The directional cases are the ones that matter most. A desk's resting book is full of buys
/// below the market and sells above it; a symmetric band would reject all of them, so those
/// cases are asserted explicitly rather than left implied.
/// </para>
/// </summary>
public sealed class FatFingerRuleTests
{
    private const decimal Reference = 100m;

    private static OrderRequest Order(
        decimal quantity = 10m,
        decimal? limitPrice = null,
        decimal? stopPrice = null,
        OrderSide side = OrderSide.Buy,
        OrderType? type = null,
        string symbol = "AAPL") => new()
        {
            Symbol = symbol,
            Side = side,
            Type = type ?? (limitPrice.HasValue ? OrderType.Limit : OrderType.Market),
            Quantity = quantity,
            LimitPrice = limitPrice,
            StopPrice = stopPrice
        };

    private static FatFingerRule Rule(
        decimal? maxQuantity = null,
        decimal? maxDeviationPercent = null,
        decimal? referencePrice = Reference) => new(
            new StubExposureProvider(referencePrice),
            () => new FatFingerThresholds(maxQuantity, maxDeviationPercent),
            NullLogger<FatFingerRule>.Instance);

    [Fact]
    public void Rule_RejectsRatherThanEscalates_AndRunsAheadOfPortfolioRules()
    {
        var rule = Rule(maxQuantity: 1_000m);

        // A fat finger is a mistake, not a decision awaiting sign-off.
        rule.Severity.Should().Be(RiskRuleSeverity.Error);
        // Negative priority puts it ahead of the portfolio-aware ceilings so an inflated order
        // is attributed to the typo rather than to the exposure limit it happened to breach.
        rule.Priority.Should().BeLessThan(0);
        rule.RuleName.Should().Be("FatFinger");
    }

    [Fact]
    public async Task Unconfigured_ApprovesEvenAnAbsurdOrder()
    {
        var rule = Rule(maxQuantity: null, maxDeviationPercent: null);

        var result = await rule.EvaluateAsync(Order(quantity: 10_000_000m, limitPrice: 50_000m));

        result.IsApproved.Should().BeTrue();
    }

    // --- quantity limb ---

    [Fact]
    public async Task Quantity_AboveCeiling_RejectsAndReportsObservedAgainstLimit()
    {
        // The classic slip: 100,000 shares entered where 1,000 was meant.
        var rule = Rule(maxQuantity: 1_000m);

        var result = await rule.EvaluateAsync(Order(quantity: 100_000m, limitPrice: Reference));

        result.IsApproved.Should().BeFalse();
        result.RequiresApproval.Should().BeFalse();
        result.IsUnmeasurable.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.QuantityCode);
        result.ObservedValue.Should().Be(100_000m);
        result.LimitValue.Should().Be(1_000m);
    }

    [Fact]
    public async Task Quantity_ExactlyAtCeiling_Approves()
    {
        var rule = Rule(maxQuantity: 1_000m);

        var result = await rule.EvaluateAsync(Order(quantity: 1_000m, limitPrice: Reference));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Quantity_AppliesToMarketOrdersWhichCarryNoPrice()
    {
        var rule = Rule(maxQuantity: 1_000m);

        var result = await rule.EvaluateAsync(Order(quantity: 5_000m, type: OrderType.Market));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.QuantityCode);
    }

    // --- price limb, directional ---

    [Fact]
    public async Task Price_BuyPayingFarAboveTheMarket_Rejects()
    {
        // 1000.00 typed where 100.00 was meant.
        var rule = Rule(maxDeviationPercent: 10m);

        var result = await rule.EvaluateAsync(Order(limitPrice: 1_000m, side: OrderSide.Buy));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.PriceDeviationCode);
        result.ObservedValue.Should().Be(900m);
        result.LimitValue.Should().Be(10m);
    }

    [Fact]
    public async Task Price_RestingBuyFarBelowTheMarket_Approves()
    {
        // A working bid 50% under the market is an ordinary resting order, not a typo. A
        // symmetric band would reject it and take the desk's whole bid side with it.
        var rule = Rule(maxDeviationPercent: 10m);

        var result = await rule.EvaluateAsync(Order(limitPrice: 50m, side: OrderSide.Buy));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Price_SellHittingFarBelowTheMarket_Rejects()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        var result = await rule.EvaluateAsync(Order(limitPrice: 50m, side: OrderSide.Sell));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.PriceDeviationCode);
        result.ObservedValue.Should().Be(50m);
    }

    [Fact]
    public async Task Price_RestingSellFarAboveTheMarket_Approves()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        var result = await rule.EvaluateAsync(Order(limitPrice: 200m, side: OrderSide.Sell));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Price_ExactlyAtTheBand_Approves()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // 110 is exactly 10% through a 100 reference; the band rejects beyond it, not at it.
        var result = await rule.EvaluateAsync(Order(limitPrice: 110m, side: OrderSide.Buy));

        result.IsApproved.Should().BeTrue();
    }

    // --- order types the price limb must not touch ---

    [Fact]
    public async Task Price_CorrectlyPlacedStop_IsNotMeasuredForDistance_SoStopLossesSurvive()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // A protective stop sits far from the market by design — that is what makes it a stop.
        // 40 against a 100 market is 60% away, six times the band, and must still route: only
        // the wrong side is measured, and for a sell the wrong side is above the market.
        var result = await rule.EvaluateAsync(
            Order(limitPrice: null, stopPrice: 40m, side: OrderSide.Sell, type: OrderType.StopMarket));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Price_StopLimitLimb_IsMeasuredAgainstItsOwnTrigger_NotTheMarket()
    {
        var rule = Rule(maxDeviationPercent: 5m);

        // Market at 100: a sell stop at 90 with an 89 limit is an ordinary protective order.
        // The 89 only becomes relevant once the market reaches 90, so it is measured against
        // that trigger — 1.1% away, inside the band — rather than against today's 100, which
        // would read 11% and reject exactly the order this is meant to preserve.
        var result = await rule.EvaluateAsync(
            Order(limitPrice: 89m, stopPrice: 90m, side: OrderSide.Sell, type: OrderType.StopLimit));

        result.IsApproved.Should().BeTrue();
    }

    // --- wrong-side stop triggers ---

    [Fact]
    public async Task StopTrigger_BuyStopBeneathTheMarket_IsRejected_BecauseItFiresImmediately()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // A buy stop belongs ABOVE the market: PaperOrderMatchingPolicy fires a buy once the
        // price reaches or passes the stop. At 1 against a 100 market the trigger is already
        // crossed, so this stop-market order becomes an unbounded market order the moment it
        // is accepted — the single most expensive shape a mistyped order can take.
        var result = await rule.EvaluateAsync(
            Order(stopPrice: 1m, side: OrderSide.Buy, type: OrderType.StopMarket));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.StopTriggerCode);
        result.ObservedValue.Should().Be(99m);
        result.LimitValue.Should().Be(10m);
    }

    [Fact]
    public async Task StopTrigger_SellStopAboveTheMarket_IsRejected_BecauseItFiresImmediately()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // The mirror case: a sell stop belongs BELOW the market, so 1,000 against a 100 market
        // is already crossed and fires on acceptance.
        var result = await rule.EvaluateAsync(
            Order(stopPrice: 1_000m, side: OrderSide.Sell, type: OrderType.StopMarket));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.StopTriggerCode);
        result.ObservedValue.Should().Be(900m);
    }

    [Theory]
    [InlineData(OrderSide.Buy, 105d)]
    [InlineData(OrderSide.Sell, 95d)]
    public async Task StopTrigger_PlacedOnTheProtectiveSide_Approves(OrderSide side, double stopPrice)
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // Buy above, sell below: both wait for the market to come to them, which is the whole
        // point of a stop. Neither is on the wrong side, so neither is measured for distance.
        var result = await rule.EvaluateAsync(
            Order(stopPrice: (decimal)stopPrice, side: side, type: OrderType.StopMarket));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task StopTrigger_MarginallyCrossed_Approves_BecauseTheBandIsTheTolerance()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // 95 is on the wrong side for a buy, but only 5% wrong — inside the operator's stated
        // tolerance. A desk that deliberately sends a marketable stop is not fat-fingering;
        // the band is what separates the two, exactly as it does for a limit.
        var result = await rule.EvaluateAsync(
            Order(stopPrice: 95m, side: OrderSide.Buy, type: OrderType.StopMarket));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task StopTrigger_OnAStopLimit_IsMeasuredBeforeItsLimit()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // Both limbs breach: the trigger is 99% the wrong way, and the limit is far from that
        // trigger too. The trigger is reported, because a stop-limit whose trigger is already
        // crossed is the more dangerous mistake of the two.
        var result = await rule.EvaluateAsync(
            Order(limitPrice: 0.5m, stopPrice: 1m, side: OrderSide.Buy, type: OrderType.StopLimit));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.StopTriggerCode);
    }

    [Fact]
    public async Task StopTrigger_TrailingStop_IsNotMeasured_BecauseItsTriggerMovesWithTheMarket()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // A trailing stop's trigger is derived by the broker from TrailPrice/TrailPercent and
        // moves as the market does, so a snapshot of it compared against the current touch
        // measures nothing that stays true.
        var result = await rule.EvaluateAsync(
            Order(stopPrice: 1m, side: OrderSide.Buy, type: OrderType.TrailingStop));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task StopTrigger_WithNoReferencePrice_IsUnmeasurableRatherThanApproved()
    {
        var rule = Rule(maxDeviationPercent: 10m, referencePrice: null);

        var result = await rule.EvaluateAsync(
            Order(stopPrice: 1m, side: OrderSide.Buy, type: OrderType.StopMarket));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.UnmeasurableCode);
    }

    [Fact]
    public async Task StopTrigger_MeasuresTheLastTrade_NotTheTouchOrTheMidpoint()
    {
        // A 100/120 book that last printed at 100. The matcher leaves a buy stop at 105 resting,
        // because it fires off the print and 100 has not reached 105. Measuring that trigger
        // against the 120 ask reads it 12.5% crossed, and against the 110 midpoint 4.5% crossed —
        // both refuse an ordinary breakout order on a wide book. Only the print agrees with the
        // engine, and a control that refuses orders the engine would leave working is not
        // measuring the same market.
        var rule = new FatFingerRule(
            new TwoSidedBookProvider(bid: 100m, ask: 120m, lastTrade: 100m),
            () => new FatFingerThresholds(null, 3m),
            NullLogger<FatFingerRule>.Instance);

        var waiting = await rule.EvaluateAsync(
            Order(stopPrice: 105m, side: OrderSide.Buy, type: OrderType.StopMarket));
        waiting.IsApproved.Should().BeTrue();

        // The same book still catches a genuinely crossed trigger.
        var crossed = await rule.EvaluateAsync(
            Order(stopPrice: 1m, side: OrderSide.Buy, type: OrderType.StopMarket));
        crossed.IsApproved.Should().BeFalse();
        crossed.Code.Should().Be(FatFingerRule.StopTriggerCode);
    }

    [Fact]
    public async Task Price_OnABarDrivenSession_MeasuresTheLimitAgainstTheBarClose()
    {
        // No quote and no print, only a bar. The matcher's own reference precedence ends at the
        // bar close, so it would evaluate this limit normally; a guard that stopped at the print
        // refuses an ordinary order as unmeasurable and blocks supported bar-only experiments.
        var rule = new FatFingerRule(
            new BarOnlyProvider(barClose: 100m),
            () => new FatFingerThresholds(null, 10m),
            NullLogger<FatFingerRule>.Instance);

        var inside = await rule.EvaluateAsync(Order(limitPrice: 105m, side: OrderSide.Buy));
        inside.IsApproved.Should().BeTrue();

        var outside = await rule.EvaluateAsync(Order(limitPrice: 1_000m, side: OrderSide.Buy));
        outside.IsApproved.Should().BeFalse();
        outside.Code.Should().Be(FatFingerRule.PriceDeviationCode);
    }

    [Fact]
    public async Task StopTrigger_OnABarDrivenSession_UsesTheBarClose_NotTheQuote()
    {
        // No print at all, which is the ordinary case on a bar-driven session. The matcher falls
        // to the bar close, so a buy stop at 125 against a 130 close is already triggered — and
        // routes unbounded. Skipping to the 100 ask reads it as 25% below the market, comfortably
        // resting, and approves exactly the order this limb exists to catch.
        var rule = new FatFingerRule(
            new TwoSidedBookProvider(bid: 90m, ask: 100m, lastTrade: null, barClose: 130m),
            () => new FatFingerThresholds(null, 3m),
            NullLogger<FatFingerRule>.Instance);

        var result = await rule.EvaluateAsync(
            Order(stopPrice: 125m, side: OrderSide.Buy, type: OrderType.StopMarket));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.StopTriggerCode);
    }

    [Fact]
    public async Task StopTrigger_PrefersThePrintOverTheBarClose()
    {
        // Both present: the print wins, exactly as it does in the matcher. A 100 print leaves a
        // buy stop at 105 resting even though a stale 130 close would have triggered it.
        var rule = new FatFingerRule(
            new TwoSidedBookProvider(bid: 90m, ask: 100m, lastTrade: 100m, barClose: 130m),
            () => new FatFingerThresholds(null, 3m),
            NullLogger<FatFingerRule>.Instance);

        var result = await rule.EvaluateAsync(
            Order(stopPrice: 105m, side: OrderSide.Buy, type: OrderType.StopMarket));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task StopTrigger_WithNoPrintOrBar_FallsBackToTheCrossingSide_AsTheMatcherDoes()
    {
        // Neither a print nor a bar to prefer, so the matcher would compare against the ask for
        // a buy — and so does this. A buy stop at 1 against a 110 ask is plainly crossed.
        var rule = new FatFingerRule(
            new TwoSidedBookProvider(bid: 90m, ask: 110m),
            () => new FatFingerThresholds(null, 10m),
            NullLogger<FatFingerRule>.Instance);

        var result = await rule.EvaluateAsync(
            Order(stopPrice: 1m, side: OrderSide.Buy, type: OrderType.StopMarket));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.StopTriggerCode);
    }

    [Fact]
    public async Task StopTrigger_AboveTheAskButBelowTheLastTrade_IsRejected()
    {
        // The mismatch in the other direction. A 100/120 book that last printed at 130: a buy
        // stop at 125 sits above the ask and looks correctly placed against any quote-derived
        // reference, but the matcher fires off the 130 print and this order becomes an unbounded
        // market order the moment it is accepted.
        var rule = new FatFingerRule(
            new TwoSidedBookProvider(bid: 100m, ask: 120m, lastTrade: 130m),
            () => new FatFingerThresholds(null, 3m),
            NullLogger<FatFingerRule>.Instance);

        var result = await rule.EvaluateAsync(
            Order(stopPrice: 125m, side: OrderSide.Buy, type: OrderType.StopMarket));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.StopTriggerCode);
    }

    [Fact]
    public async Task Thresholds_AreReadAsOnePair_SoAnUpdateCannotBeStraddled()
    {
        // Two independently locked accessors let an evaluation read the old value of one limb
        // and the new value of the other. Replacing (no ceiling, 50% band) with (100 ceiling,
        // no band) atomically could be observed as (none, none) — the rule's "entirely
        // unconfigured" shape — and approve an order both configurations would have rejected.
        var readings = 0;
        var rule = new FatFingerRule(
            new StubExposureProvider(Reference),
            () =>
            {
                readings++;
                return new FatFingerThresholds(1_000m, 10m);
            },
            NullLogger<FatFingerRule>.Instance);

        var result = await rule.EvaluateAsync(Order(quantity: 100_000m, limitPrice: 1_000m));

        result.Code.Should().Be(FatFingerRule.QuantityCode);
        readings.Should().Be(1, "one evaluation takes exactly one threshold reading");
    }

    [Fact]
    public async Task Price_ExtremeLimitAgainstATinyReference_BreachesRatherThanOverflowing()
    {
        var rule = Rule(maxDeviationPercent: 10m, referencePrice: 1m);

        // The ratio here scaled by 100 exceeds decimal.MaxValue. Throwing would surface as a
        // generic RISK_RULE_EVALUATION_FAILED refusal instead of the structured breach this
        // plainly is, so both the F# band and the reported evidence saturate.
        var result = await rule.EvaluateAsync(
            Order(limitPrice: decimal.MaxValue, side: OrderSide.Buy, type: OrderType.Limit));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.PriceDeviationCode);
        result.ObservedValue.Should().Be(decimal.MaxValue);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(1.0)]
    [InlineData(100.0)]
    [InlineData(1000.0)]
    public async Task Price_ExtremeLimitAgainstAnySmallReference_NeverThrows(double reference)
    {
        // A reference below 1 makes the DIVISION overflow, not just the scaling: MaxValue over
        // 0.1 is ten times what a decimal can hold. Guarding only the scaling left the earlier
        // branch throwing, which the composite validator turns into RISK_RULE_EVALUATION_FAILED.
        // The boundaries either side of 100 are covered because that is where the guard switches
        // strategy.
        var rule = Rule(maxDeviationPercent: 10m, referencePrice: (decimal)reference);

        var buy = await rule.EvaluateAsync(
            Order(limitPrice: decimal.MaxValue, side: OrderSide.Buy, type: OrderType.Limit));
        buy.IsApproved.Should().BeFalse();
        buy.Code.Should().Be(FatFingerRule.PriceDeviationCode);

        // The passive side of the same arithmetic still approves — a sell priced absurdly high
        // is a resting order nobody will hit, and saturation must not turn it into a breach.
        var sell = await rule.EvaluateAsync(
            Order(limitPrice: decimal.MaxValue, side: OrderSide.Sell, type: OrderType.Limit));
        sell.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Price_ExtremePassiveSell_StillApproves_DespiteSaturation()
    {
        var rule = Rule(maxDeviationPercent: 10m, referencePrice: 1m);

        // The same arithmetic on the passive side. A sell priced absurdly high is a resting
        // order nobody will hit, not a fat finger, and saturation must not turn it into one.
        var result = await rule.EvaluateAsync(
            Order(limitPrice: decimal.MaxValue, side: OrderSide.Sell, type: OrderType.Limit));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Price_MarketOrderCarryingASimulatedPrice_IsNotMeasured()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // The paper gateway lets a caller pass a simulated market observation through
        // LimitPrice on a Market order. That is not an operator's typed limit, so comparing
        // it against the live book would reject a paper order for a price nobody entered.
        var result = await rule.EvaluateAsync(
            Order(limitPrice: 1_000m, type: OrderType.Market));

        result.IsApproved.Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderType.LimitOnOpen)]
    [InlineData(OrderType.LimitOnClose)]
    public async Task Price_AuctionOnlyLimits_AreNotMeasuredAgainstTheContinuousTouch(OrderType type)
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // An auction limit is priced for the opening or closing cross, not the current
        // continuous market. Measuring it against the present BBO rejects routine auction
        // orders, and pre-open there may be no fresh BBO at all.
        var result = await rule.EvaluateAsync(Order(limitPrice: 1_000m, type: type));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Quantity_MultiLegOrder_MeasuresTheEffectiveLegQuantity()
    {
        var rule = Rule(maxQuantity: 1_000m);

        // 100 packages at a mistyped ratio of 100 routes 10,000 contracts on that leg, even
        // though the top-level count is only 100. The gateway checks only that ratios are
        // positive whole numbers, so nothing else catches this.
        var order = Order(quantity: 100m, limitPrice: 5m, side: OrderSide.Sell) with
        {
            Legs =
            [
                new OrderLeg { Symbol = "AAPL_C1", Side = OrderSide.Sell, RatioQuantity = 100m },
                new OrderLeg { Symbol = "AAPL_C2", Side = OrderSide.Buy, RatioQuantity = 1m }
            ]
        };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.QuantityCode);
        result.ObservedValue.Should().Be(10_000m);
    }

    [Fact]
    public async Task Quantity_MultiLegProductThatWouldOverflow_SaturatesInsteadOfThrowing()
    {
        var rule = Rule(maxQuantity: 1_000m);

        // Individually valid decimals whose product exceeds decimal.MaxValue. The gateway bounds
        // neither, so this must report the structured quantity breach rather than blowing up into
        // a generic evaluation failure.
        var order = Order(quantity: 1e20m, limitPrice: 5m) with
        {
            Legs = [new OrderLeg { Symbol = "AAPL_C1", Side = OrderSide.Buy, RatioQuantity = 1e10m }]
        };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.QuantityCode);
    }

    [Fact]
    public async Task Quantity_LimbDisabled_DoesNotEvaluateEffectiveQuantityForAnExcludedPackage()
    {
        // Only the price band is configured, and the price limb excludes packages. The quantity
        // calculation must not run at all here - an arithmetic edge in it would otherwise refuse
        // an order this rule does not gate on either limb.
        var rule = Rule(maxQuantity: null, maxDeviationPercent: 10m);

        var order = Order(quantity: 1e20m, limitPrice: 5m) with
        {
            Legs = [new OrderLeg { Symbol = "AAPL_C1", Side = OrderSide.Buy, RatioQuantity = 1e10m }]
        };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Quantity_BrokerNotionalOrder_IsNotComparedWithTheShareCeiling()
    {
        var rule = Rule(maxQuantity: 1_000m);

        // Alpaca-style notional sizing routes the metadata dollar amount and discards
        // Quantity, so this order's "5000" is dollars, not shares. Its economic size is
        // OrderNotionalRule's job.
        var order = Order(quantity: 5_000m, type: OrderType.Market) with
        {
            Metadata = new Dictionary<string, string> { ["notional"] = "5000" }
        };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Price_MultiLegPackage_IsNotMeasuredAgainstTheTopLevelSymbol()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // A $1 net credit on a spread is not comparable to a $100 quote for the underlying;
        // measuring it would reject every credit spread as ~99% through the market.
        var order = Order(limitPrice: 1m, side: OrderSide.Sell, type: OrderType.Limit) with
        {
            Legs =
            [
                new OrderLeg { Symbol = "AAPL_C1", Side = OrderSide.Sell, RatioQuantity = 1m },
                new OrderLeg { Symbol = "AAPL_C2", Side = OrderSide.Buy, RatioQuantity = 1m }
            ]
        };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeTrue();
    }

    // --- unmeasurable ---

    [Fact]
    public async Task PricedOrderWithNoReference_RejectsAsUnmeasurable_WithoutClaimingABreach()
    {
        var rule = Rule(maxDeviationPercent: 10m, referencePrice: null);

        var result = await rule.EvaluateAsync(Order(limitPrice: 1_000m));

        result.IsApproved.Should().BeFalse();
        // Unmeasurable rather than breached, so a pricing gap cannot trip the circuit breaker.
        result.IsUnmeasurable.Should().BeTrue();
        result.Code.Should().Be(FatFingerRule.UnmeasurableCode);
    }

    [Fact]
    public async Task MissingReference_DoesNotBlockWhenOnlyTheQuantityLimbIsConfigured()
    {
        // Nothing is unmeasurable here: the quantity band needs no price at all, so an
        // unpriceable symbol must not become unroutable just because this rule is on.
        var rule = Rule(maxQuantity: 1_000m, maxDeviationPercent: null, referencePrice: null);

        var result = await rule.EvaluateAsync(Order(quantity: 10m, limitPrice: 1_000m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task DefiniteQuantityBreach_IsReportedAsSuch_EvenWithNoReferencePrice()
    {
        // Both limbs configured, quote missing. The quantity breach is definitive and needs no
        // market data, so it must not be downgraded to a pricing-data gap — that would lose the
        // stable code and the observed-vs-limit evidence for a mistake that is not in doubt.
        var rule = Rule(maxQuantity: 1_000m, maxDeviationPercent: 10m, referencePrice: null);

        var result = await rule.EvaluateAsync(Order(quantity: 100_000m, limitPrice: 1_000m));

        result.IsApproved.Should().BeFalse();
        result.IsUnmeasurable.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.QuantityCode);
        result.ObservedValue.Should().Be(100_000m);
        result.LimitValue.Should().Be(1_000m);
    }

    [Fact]
    public async Task SellLimit_IsMeasuredAgainstTheBid_NotTheValuationMidpoint()
    {
        // Book 90/110, mid 100. A sell at 85 is 5.56% below the executable bid and must pass a
        // 10% band. Measured against the midpoint it would read 15% and be rejected, which is
        // why the rule takes the touch rather than the conservative valuation price.
        var rule = new FatFingerRule(
            new TwoSidedBookProvider(bid: 90m, ask: 110m),
            () => new FatFingerThresholds(null, 10m),
            NullLogger<FatFingerRule>.Instance);

        var result = await rule.EvaluateAsync(Order(limitPrice: 85m, side: OrderSide.Sell));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task BuyLimit_IsMeasuredAgainstTheAsk()
    {
        var rule = new FatFingerRule(
            new TwoSidedBookProvider(bid: 90m, ask: 110m),
            () => new FatFingerThresholds(null, 10m),
            NullLogger<FatFingerRule>.Instance);

        // 115 is 4.5% through the 110 ask - inside the band - though it is 15% through the bid.
        var inside = await rule.EvaluateAsync(Order(limitPrice: 115m, side: OrderSide.Buy));
        inside.IsApproved.Should().BeTrue();

        var outside = await rule.EvaluateAsync(Order(limitPrice: 200m, side: OrderSide.Buy));
        outside.IsApproved.Should().BeFalse();
        outside.Code.Should().Be(FatFingerRule.PriceDeviationCode);
    }

    [Fact]
    public async Task MarketOrderWithNoReference_Approves_BecauseThereIsNoTypedPriceToMistype()
    {
        var rule = Rule(maxDeviationPercent: 10m, referencePrice: null);

        var result = await rule.EvaluateAsync(Order(quantity: 10m, type: OrderType.Market));

        result.IsApproved.Should().BeTrue();
    }

    // --- limb ordering ---

    [Fact]
    public async Task OrderBreachingBothLimbs_IsAttributedToQuantity()
    {
        var rule = Rule(maxQuantity: 1_000m, maxDeviationPercent: 10m);

        var result = await rule.EvaluateAsync(Order(quantity: 100_000m, limitPrice: 1_000m));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.QuantityCode);
        result.ObservedValue.Should().Be(100_000m);
    }

    private sealed class StubExposureProvider(decimal? referencePrice) : IPortfolioExposureProvider
    {
        public PortfolioExposureSnapshot GetSnapshot() => PortfolioExposureSnapshot.Empty;

        public decimal? TryGetReferencePrice(string symbol) => referencePrice;
    }

    /// <summary>
    /// A real two-sided book, so the sell side can be measured against the bid rather than the
    /// midpoint. Mirrors the production provider: the valuation price takes the larger of mark
    /// and touch, while the touch price is the raw crossing side.
    /// </summary>
    /// <summary>
    /// A bar-driven session: no quote, no print, only a completed bar — the shape the matcher
    /// still evaluates from and the guard must too.
    /// </summary>
    private sealed class BarOnlyProvider(decimal barClose) : IPortfolioExposureProvider
    {
        public PortfolioExposureSnapshot GetSnapshot() => PortfolioExposureSnapshot.Empty;

        public decimal? TryGetReferencePrice(string symbol) => barClose;

        public decimal? TryGetTouchPrice(string symbol, OrderSide side) => barClose;

        public decimal? TryGetBarClosePrice(string symbol) => barClose;
    }

    private sealed class TwoSidedBookProvider(
        decimal bid,
        decimal ask,
        decimal? lastTrade = null,
        decimal? barClose = null) : IPortfolioExposureProvider
    {
        public PortfolioExposureSnapshot GetSnapshot() => PortfolioExposureSnapshot.Empty;

        // The valuation mark takes the midpoint before it ever looks at a print, which is exactly
        // why a trigger must not read it: production resolves marks the same way.
        public decimal? TryGetReferencePrice(string symbol) => (bid + ask) / 2m;

        public decimal? TryGetExecutablePrice(string symbol, OrderSide side) =>
            Math.Max((bid + ask) / 2m, side is OrderSide.Buy ? ask : bid);

        public decimal? TryGetTouchPrice(string symbol, OrderSide side) =>
            side is OrderSide.Buy ? ask : bid;

        public decimal? TryGetLastTradePrice(string symbol) => lastTrade;

        public decimal? TryGetBarClosePrice(string symbol) => barClose;
    }
}
