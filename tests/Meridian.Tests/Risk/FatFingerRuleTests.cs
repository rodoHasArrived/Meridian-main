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
            () => maxQuantity,
            () => maxDeviationPercent,
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
    public async Task Price_StopPriceIsNeverMeasured_SoStopLossesSurvive()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // A protective stop sits away from the market by design. Measuring it would reject
        // every stop-loss on the book.
        var result = await rule.EvaluateAsync(
            Order(limitPrice: null, stopPrice: 40m, side: OrderSide.Sell, type: OrderType.StopMarket));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Price_StopLimitIsExcluded_BecauseItsLimitIsPricedOffTheTrigger()
    {
        var rule = Rule(maxDeviationPercent: 5m);

        // Market at 100: a sell stop at 90 with an 89 limit is an ordinary protective order.
        // The 89 only becomes relevant once the market reaches 90, so measuring it against
        // today's 100 would reject exactly the orders the stop exclusion exists to protect.
        var result = await rule.EvaluateAsync(
            Order(limitPrice: 89m, stopPrice: 90m, side: OrderSide.Sell, type: OrderType.StopLimit));

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
            () => null,
            () => 10m,
            NullLogger<FatFingerRule>.Instance);

        var result = await rule.EvaluateAsync(Order(limitPrice: 85m, side: OrderSide.Sell));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task BuyLimit_IsMeasuredAgainstTheAsk()
    {
        var rule = new FatFingerRule(
            new TwoSidedBookProvider(bid: 90m, ask: 110m),
            () => null,
            () => 10m,
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
    private sealed class TwoSidedBookProvider(decimal bid, decimal ask) : IPortfolioExposureProvider
    {
        public PortfolioExposureSnapshot GetSnapshot() => PortfolioExposureSnapshot.Empty;

        public decimal? TryGetReferencePrice(string symbol) => (bid + ask) / 2m;

        public decimal? TryGetExecutablePrice(string symbol, OrderSide side) =>
            Math.Max((bid + ask) / 2m, side is OrderSide.Buy ? ask : bid);

        public decimal? TryGetTouchPrice(string symbol, OrderSide side) =>
            side is OrderSide.Buy ? ask : bid;
    }
}
