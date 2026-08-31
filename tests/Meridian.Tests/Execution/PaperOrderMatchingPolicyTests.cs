using FluentAssertions;
using Meridian.Execution.PaperMatching;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Order-type semantics of the shared paper matching policy: limit orders fill only at or
/// better than the limit, stops trigger per the documented trade-preferred policy, market
/// orders fill from observed data only, and every fill stays inside the observed envelope.
/// </summary>
public sealed class PaperOrderMatchingPolicyTests
{
    private static PaperMarketObservation Quote(decimal bid, decimal ask, decimal? lastTrade = null) =>
        new() { BidPrice = bid, AskPrice = ask, LastTradePrice = lastTrade };

    private static PaperMatchResult Evaluate(
        OrderSide side,
        OrderType type,
        PaperMarketObservation observation,
        decimal? limit = null,
        decimal? stop = null,
        bool stopTriggered = false) =>
        PaperOrderMatchingPolicy.Evaluate(side, type, limit, stop, stopTriggered, observation);

    [Fact]
    public void MarketBuy_FillsAtObservedAsk()
    {
        var result = Evaluate(OrderSide.Buy, OrderType.Market, Quote(100m, 102m, lastTrade: 101m));

        result.Outcome.Should().Be(PaperMatchOutcome.Filled);
        result.FillPrice.Should().Be(102m, "a market buy pays the observed ask, not the last trade");
    }

    [Fact]
    public void MarketSell_FillsAtObservedBid()
    {
        var result = Evaluate(OrderSide.Sell, OrderType.Market, Quote(100m, 102m, lastTrade: 101m));

        result.Outcome.Should().Be(PaperMatchOutcome.Filled);
        result.FillPrice.Should().Be(100m, "a market sell hits the observed bid");
    }

    [Fact]
    public void MarketOrder_WithNoObservation_ReportsNoMarketData()
    {
        var result = Evaluate(OrderSide.Buy, OrderType.Market, default);

        result.Outcome.Should().Be(PaperMatchOutcome.NoMarketData,
            "a market order must never fill at a fabricated price");
        result.FillPrice.Should().BeNull();
    }

    [Fact]
    public void MarketOrder_WithOnlyTrade_FillsExactlyAtTradePrice()
    {
        var observation = new PaperMarketObservation { LastTradePrice = 55.5m };

        var result = Evaluate(OrderSide.Buy, OrderType.Market, observation);

        result.Outcome.Should().Be(PaperMatchOutcome.Filled);
        result.FillPrice.Should().Be(55.5m,
            "with only a trade print observed, the envelope collapses to that price");
    }

    [Fact]
    public void MarketOrder_WithOnlyBar_FillsAtBarCloseInsideBarRange()
    {
        var observation = new PaperMarketObservation { BarLow = 98m, BarHigh = 104m, BarClose = 103m };

        var result = Evaluate(OrderSide.Buy, OrderType.Market, observation);

        result.Outcome.Should().Be(PaperMatchOutcome.Filled);
        result.FillPrice.Should().Be(103m);
        result.FillPrice!.Value.Should().BeInRange(98m, 104m);
    }

    [Theory]
    [InlineData(103, 102)]  // limit above ask: marketable, fills at the ask (better than limit)
    [InlineData(102, 102)]  // limit exactly at ask: fills at the limit
    public void LimitBuy_Marketable_FillsAtObservedPriceNeverAboveLimit(decimal limit, decimal expectedFill)
    {
        var result = Evaluate(OrderSide.Buy, OrderType.Limit, Quote(100m, 102m), limit: limit);

        result.Outcome.Should().Be(PaperMatchOutcome.Filled);
        result.FillPrice.Should().Be(expectedFill);
        result.FillPrice!.Value.Should().BeLessThanOrEqualTo(limit,
            "a limit buy may only fill at or better than its limit price");
    }

    [Fact]
    public void LimitBuy_NotMarketable_Rests()
    {
        var result = Evaluate(OrderSide.Buy, OrderType.Limit, Quote(100m, 102m), limit: 101m);

        result.Outcome.Should().Be(PaperMatchOutcome.Resting,
            "the ask is above the limit, so the order cannot fill and must rest — never fill at its own limit price");
        result.FillPrice.Should().BeNull();
    }

    [Theory]
    [InlineData(101, 102)]  // limit below bid: marketable, fills at the bid (better than limit)
    [InlineData(102, 102)]  // limit exactly at bid: fills at the limit
    public void LimitSell_Marketable_FillsAtObservedPriceNeverBelowLimit(decimal limit, decimal expectedFill)
    {
        var result = Evaluate(OrderSide.Sell, OrderType.Limit, Quote(102m, 104m), limit: limit);

        result.Outcome.Should().Be(PaperMatchOutcome.Filled);
        result.FillPrice.Should().Be(expectedFill);
        result.FillPrice!.Value.Should().BeGreaterThanOrEqualTo(limit,
            "a limit sell may only fill at or better than its limit price");
    }

    [Fact]
    public void LimitSell_NotMarketable_Rests()
    {
        var result = Evaluate(OrderSide.Sell, OrderType.Limit, Quote(100m, 102m), limit: 103m);

        result.Outcome.Should().Be(PaperMatchOutcome.Resting);
    }

    [Fact]
    public void BuyStop_TriggersWhenLastTradeReachesStop_ThenFillsAsMarket()
    {
        var result = Evaluate(
            OrderSide.Buy, OrderType.StopMarket, Quote(104m, 106m, lastTrade: 105m), stop: 105m);

        result.Outcome.Should().Be(PaperMatchOutcome.Filled,
            "the last trade reached the stop price, so the stop triggers and fills as a market order");
        result.FillPrice.Should().Be(106m, "the triggered stop pays the observed ask");
        result.StopTriggered.Should().BeTrue();
    }

    [Fact]
    public void BuyStop_DoesNotTriggerBelowStopPrice()
    {
        var result = Evaluate(
            OrderSide.Buy, OrderType.StopMarket, Quote(100m, 102m, lastTrade: 101m), stop: 105m);

        result.Outcome.Should().Be(PaperMatchOutcome.Resting);
        result.StopTriggered.Should().BeFalse();
    }

    [Fact]
    public void SellStop_TriggersWhenLastTradeFallsToStop()
    {
        var result = Evaluate(
            OrderSide.Sell, OrderType.StopMarket, Quote(94m, 96m, lastTrade: 95m), stop: 95m);

        result.Outcome.Should().Be(PaperMatchOutcome.Filled);
        result.FillPrice.Should().Be(94m, "the triggered sell stop hits the observed bid");
    }

    [Fact]
    public void BuyStop_WithNoTradeObserved_UsesQuoteFallbackTrigger()
    {
        var result = Evaluate(
            OrderSide.Buy, OrderType.StopMarket, Quote(104m, 106m), stop: 105m);

        result.Outcome.Should().Be(PaperMatchOutcome.Filled,
            "with no trade observed, a buy stop triggers on the ask per the documented quote fallback");
    }

    [Fact]
    public void StopLimit_TriggeredButNotMarketable_RestsWithTriggerArmed()
    {
        var result = Evaluate(
            OrderSide.Buy, OrderType.StopLimit, Quote(104m, 106m, lastTrade: 105m),
            limit: 105m, stop: 105m);

        result.Outcome.Should().Be(PaperMatchOutcome.Resting,
            "the stop triggered but the ask is above the limit, so the order rests as a limit order");
        result.StopTriggered.Should().BeTrue("the trigger must stay armed and never re-arm");
    }

    [Fact]
    public void StopLimit_AlreadyTriggered_FillsWhenLimitBecomesMarketable()
    {
        var result = Evaluate(
            OrderSide.Buy, OrderType.StopLimit, Quote(103m, 104.5m, lastTrade: 104m),
            limit: 105m, stop: 110m, stopTriggered: true);

        result.Outcome.Should().Be(PaperMatchOutcome.Filled,
            "an armed stop-limit fills with limit semantics regardless of the current stop condition");
        result.FillPrice.Should().Be(104.5m);
    }

    [Fact]
    public void CrossedObservation_FillStaysInsideEnvelope()
    {
        // Degenerate/crossed data: bid above ask. The envelope still bounds the fill.
        var observation = new PaperMarketObservation { BidPrice = 103m, AskPrice = 101m };

        var result = Evaluate(OrderSide.Buy, OrderType.Market, observation);

        result.Outcome.Should().Be(PaperMatchOutcome.Filled);
        result.FillPrice!.Value.Should().BeInRange(
            observation.EnvelopeLow!.Value, observation.EnvelopeHigh!.Value);
    }
}
