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

    // --- stop prices are never measured ---

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
    public async Task Price_StopLimitStillMeasuresItsLimitPrice()
    {
        var rule = Rule(maxDeviationPercent: 10m);

        // The trigger is legitimately away from the market; the limit it fires at is not.
        var result = await rule.EvaluateAsync(
            Order(limitPrice: 1_000m, stopPrice: 105m, side: OrderSide.Buy, type: OrderType.StopLimit));

        result.IsApproved.Should().BeFalse();
        result.Code.Should().Be(FatFingerRule.PriceDeviationCode);
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
}
