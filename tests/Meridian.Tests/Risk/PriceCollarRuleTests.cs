using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Risk;

/// <summary>
/// Covers the price collar: the escalate-severity counterpart to the fat-finger band.
/// <para>
/// The distinction under test is not the arithmetic — that is the same F# policy the fat-finger
/// band calls, over the same shared limbs — but the <em>posture</em>. A collar breach is a decision
/// awaiting sign-off, so it parks the order for a human rather than refusing it; refusing would
/// stop legitimate aggressive trading, and approving silently would remove the judgement the band
/// exists to demand.
/// </para>
/// </summary>
public sealed class PriceCollarRuleTests
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

    private static PriceCollarRule Rule(decimal? collarPercent, decimal? referencePrice = Reference) => new(
        new StubExposureProvider(referencePrice),
        () => new PriceCollarThresholds(collarPercent),
        NullLogger<PriceCollarRule>.Instance);

    [Fact]
    public void Rule_EscalatesRatherThanRejects_AndRunsBehindTheFatFingerBand()
    {
        var rule = Rule(collarPercent: 5m);

        // Escalate is what makes the outcome releasable: a collar breach is a decision, not a typo.
        rule.Severity.Should().Be(RiskRuleSeverity.Escalate);
        rule.RuleName.Should().Be("PriceCollar");

        // Behind the fat-finger band but still ahead of the portfolio ceilings, so an order that is
        // both mistyped and beyond the collar is reported as the typo rather than offered a release.
        rule.Priority.Should().BeGreaterThan(new FatFingerRule(
            new StubExposureProvider(Reference),
            () => new FatFingerThresholds(null, null),
            NullLogger<FatFingerRule>.Instance).Priority);
        rule.Priority.Should().BeLessThan(0);
    }

    [Fact]
    public async Task Unconfigured_ApprovesEvenAnAggressiveOrder()
    {
        var rule = Rule(collarPercent: null);

        var result = await rule.EvaluateAsync(Order(limitPrice: 10_000m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Price_BeyondTheCollar_EscalatesForApprovalRatherThanRejecting()
    {
        var rule = Rule(collarPercent: 5m);

        // 10% through a 100 reference, against a 5% collar.
        var result = await rule.EvaluateAsync(Order(limitPrice: 110m, side: OrderSide.Buy));

        result.IsApproved.Should().BeFalse();
        result.RequiresApproval.Should().BeTrue("a collar breach is a decision awaiting sign-off");
        result.Code.Should().Be(PriceCollarRule.PriceCollarCode);
        result.ObservedValue.Should().Be(10m);
        result.LimitValue.Should().Be(5m);
    }

    [Fact]
    public async Task Price_InsideTheCollar_Approves()
    {
        var rule = Rule(collarPercent: 5m);

        var result = await rule.EvaluateAsync(Order(limitPrice: 104m, side: OrderSide.Buy));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Price_RestingOrders_AreNeverCollared()
    {
        var rule = Rule(collarPercent: 5m);

        // The collar inherits the fat-finger band's directionality through the shared limbs: a
        // resting buy far below the market and a resting sell far above it are ordinary working
        // orders, and a collar that parked them would park the desk's entire book for approval.
        (await rule.EvaluateAsync(Order(limitPrice: 50m, side: OrderSide.Buy))).IsApproved.Should().BeTrue();
        (await rule.EvaluateAsync(Order(limitPrice: 200m, side: OrderSide.Sell))).IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task StopTrigger_OnTheWrongSide_EscalatesUnderItsOwnCode()
    {
        var rule = Rule(collarPercent: 5m);

        // A buy stop beneath the market is already crossed; the collar parks it rather than
        // refusing, and names the trigger rather than the limit.
        var result = await rule.EvaluateAsync(
            Order(limitPrice: null, stopPrice: 50m, side: OrderSide.Buy, type: OrderType.StopMarket));

        result.IsApproved.Should().BeFalse();
        result.RequiresApproval.Should().BeTrue();
        result.Code.Should().Be(PriceCollarRule.StopTriggerCollarCode);
    }

    [Fact]
    public async Task StopTrigger_CorrectlyPlaced_IsNotCollaredForDistance()
    {
        var rule = Rule(collarPercent: 5m);

        // A protective stop sits far from the market by design. Measuring its distance would park
        // every stop-loss for approval, which is the failure the mirrored orientation prevents.
        var result = await rule.EvaluateAsync(
            Order(limitPrice: null, stopPrice: 40m, side: OrderSide.Sell, type: OrderType.StopMarket));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Price_WithNoReference_IsUnmeasurableRatherThanApproved()
    {
        var rule = Rule(collarPercent: 5m, referencePrice: null);

        var result = await rule.EvaluateAsync(Order(limitPrice: 110m));

        result.IsApproved.Should().BeFalse();
        result.IsUnmeasurable.Should().BeTrue("a band an unpriceable order sails past is not a band");
        result.Code.Should().Be(PriceCollarRule.UnmeasurableCode);
    }

    [Fact]
    public async Task Package_IsNotCollared_ForTheSameReasonTheFatFingerBandExcludesIt()
    {
        var rule = Rule(collarPercent: 5m);

        // A package's top-level price is a net debit or credit belonging to no single symbol. This
        // comes from the shared limbs, so the collar and the fat-finger band cannot disagree about
        // which shapes carry a measurable price.
        var order = Order(limitPrice: 1m, side: OrderSide.Buy) with
        {
            Legs = [new OrderLeg { Symbol = "AAPL_C1", Side = OrderSide.Buy, RatioQuantity = 1m }]
        };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeTrue();
    }

    /// <summary>
    /// The two controls must agree about what they are looking at. Every order shape the collar
    /// declines to measure is one the fat-finger band also declines, because both ask the same
    /// <see cref="OrderPriceLimbs"/> — this pins that agreement rather than trusting it.
    /// </summary>
    [Theory]
    [InlineData(OrderType.Market)]
    [InlineData(OrderType.TrailingStop)]
    [InlineData(OrderType.MarketOnOpen)]
    public async Task ExcludedShapes_AreExcludedFromBothControlsAlike(OrderType type)
    {
        var collar = Rule(collarPercent: 5m);
        var fatFinger = new FatFingerRule(
            new StubExposureProvider(Reference),
            () => new FatFingerThresholds(null, 5m),
            NullLogger<FatFingerRule>.Instance);

        var order = Order(limitPrice: 10_000m, side: OrderSide.Buy, type: type);

        (await collar.EvaluateAsync(order)).IsApproved.Should().BeTrue();
        (await fatFinger.EvaluateAsync(order)).IsApproved.Should().BeTrue();
    }

    /// <summary>
    /// One reference for every seam, so the collar measures the same market the fat-finger band
    /// does. The default interface methods resolve the touch and trigger references from this.
    /// </summary>
    private sealed class StubExposureProvider(decimal? referencePrice) : IPortfolioExposureProvider
    {
        public PortfolioExposureSnapshot GetSnapshot() => PortfolioExposureSnapshot.Empty;

        public decimal? TryGetReferencePrice(string symbol) => referencePrice;
    }
}
