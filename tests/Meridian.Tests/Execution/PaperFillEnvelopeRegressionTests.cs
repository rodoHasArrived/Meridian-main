using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Adapters;
using Meridian.Execution.PaperMatching;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using AdapterGateway = Meridian.Execution.Adapters.PaperTradingGateway;
using ExecutionGateway = Meridian.Execution.PaperTradingGateway;
using OrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Tests.Execution;

/// <summary>
/// W9-PAPER-003 envelope regression suite: no paper fill can occur at a price outside the
/// observed market-data envelope for the bar or tick in effect. Covers the matching policy
/// property-wise across randomized observations and order shapes, and both gateways
/// end-to-end including resting-order re-evaluation.
/// </summary>
public sealed class PaperFillEnvelopeRegressionTests
{
    // ---------------------------------------------------------------------------
    // Property: the pure matching policy can never produce a fill outside the
    // observed envelope, for any order shape against any observation.
    // ---------------------------------------------------------------------------

    [Property(MaxTest = 500)]
    public bool AnyFill_IsAlwaysInsideTheObservedEnvelope(
        bool isBuy,
        byte typeSelector,
        decimal rawBid,
        decimal rawAsk,
        decimal rawTrade,
        decimal rawBarLow,
        decimal rawBarHigh,
        decimal rawLimit,
        decimal rawStop,
        bool stopTriggered,
        bool includeQuote,
        bool includeTrade,
        bool includeBar)
    {
        var observation = BuildObservation(
            includeQuote, rawBid, rawAsk, includeTrade, rawTrade, includeBar, rawBarLow, rawBarHigh);

        var type = (typeSelector % 4) switch
        {
            0 => OrderType.Market,
            1 => OrderType.Limit,
            2 => OrderType.StopMarket,
            _ => OrderType.StopLimit
        };

        var result = PaperOrderMatchingPolicy.Evaluate(
            isBuy ? OrderSide.Buy : OrderSide.Sell,
            type,
            limitPrice: NormalizePrice(rawLimit),
            stopPrice: NormalizePrice(rawStop),
            stopTriggered,
            observation);

        if (result.Outcome is not PaperMatchOutcome.Filled)
        {
            return true;
        }

        var price = result.FillPrice!.Value;
        return observation.EnvelopeLow is { } low
            && observation.EnvelopeHigh is { } high
            && price >= low
            && price <= high;
    }

    [Property(MaxTest = 500)]
    public bool LimitFills_NeverViolateTheLimitPrice(
        bool isBuy,
        decimal rawBid,
        decimal rawAsk,
        decimal rawTrade,
        decimal rawLimit,
        bool includeQuote,
        bool includeTrade)
    {
        var observation = BuildObservation(
            includeQuote, rawBid, rawAsk, includeTrade, rawTrade, includeBar: false, 0m, 0m);
        var limit = NormalizePrice(rawLimit);

        var result = PaperOrderMatchingPolicy.Evaluate(
            isBuy ? OrderSide.Buy : OrderSide.Sell,
            OrderType.Limit,
            limit,
            stopPrice: null,
            stopAlreadyTriggered: false,
            observation);

        if (result.Outcome is not PaperMatchOutcome.Filled || limit is not { } limitPrice)
        {
            return true;
        }

        return isBuy
            ? result.FillPrice!.Value <= limitPrice
            : result.FillPrice!.Value >= limitPrice;
    }

    private static PaperMarketObservation BuildObservation(
        bool includeQuote,
        decimal rawBid,
        decimal rawAsk,
        bool includeTrade,
        decimal rawTrade,
        bool includeBar,
        decimal rawBarLow,
        decimal rawBarHigh)
    {
        decimal? bid = includeQuote ? NormalizePrice(rawBid) : null;
        decimal? ask = includeQuote ? NormalizePrice(rawAsk) : null;
        decimal? trade = includeTrade ? NormalizePrice(rawTrade) : null;
        decimal? barLow = includeBar ? NormalizePrice(rawBarLow) : null;
        decimal? barHigh = includeBar ? NormalizePrice(rawBarHigh) : null;
        if (barLow is { } low && barHigh is { } high && low > high)
        {
            (barLow, barHigh) = (high, low);
        }

        return new PaperMarketObservation
        {
            BidPrice = bid,
            AskPrice = ask,
            LastTradePrice = trade,
            BarLow = barLow,
            BarHigh = barHigh,
            BarClose = barHigh
        };
    }

    /// <summary>Maps an arbitrary decimal onto a positive, bounded price grid (or null).</summary>
    private static decimal? NormalizePrice(decimal raw)
    {
        var abs = Math.Abs(raw);
        if (abs == 0m)
        {
            return null;
        }

        var bounded = (abs % 10_000m) + 0.01m;
        return decimal.Round(bounded, 2);
    }

    // ---------------------------------------------------------------------------
    // Gateway end-to-end: resting orders fill only when observed data crosses them,
    // at a price inside the envelope in effect, with costs applied.
    // ---------------------------------------------------------------------------

    private static LiveMarketDataCache CreateCacheWithQuote(string symbol, decimal bid, decimal ask)
    {
        var cache = new LiveMarketDataCache();
        cache.RecordQuote(symbol, new BboQuotePayload(
            DateTimeOffset.UtcNow, symbol, bid, 10, ask, 10,
            MidPrice: null, Spread: null, SequenceNumber: 1));
        return cache;
    }

    [Fact]
    public async Task AdapterGateway_RestingLimitBuy_FillsWhenLaterQuoteCrosses_InsideEnvelope()
    {
        var cache = CreateCacheWithQuote("AAPL", 100m, 102m);
        await using var gateway = new AdapterGateway(
            NullLogger<AdapterGateway>.Instance,
            securityMaster: null,
            options: null,
            liveFeed: cache,
            costOptions: new PaperTradingCostOptions());

        await gateway.SubmitAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = 101m,
            Quantity = 10m
        });

        // Unmarketable at submit (ask 102 > limit 101): the order rests. A later quote
        // through the limit makes it marketable.
        cache.RecordQuote("AAPL", new BboQuotePayload(
            DateTimeOffset.UtcNow, "AAPL", 99.5m, 10, 100.5m, 10,
            MidPrice: null, Spread: null, SequenceNumber: 2));
        gateway.EvaluateSymbol("AAPL");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var update in gateway.StreamOrderUpdatesAsync(cts.Token))
        {
            update.Status.Should().Be(Meridian.Execution.Models.OrderStatus.Filled);
            update.AverageFillPrice.Should().Be(100.5m,
                "the resting limit buy fills at the observed ask once it crosses the limit");
            update.AverageFillPrice!.Value.Should().BeInRange(99.5m, 100.5m);
            update.Commission.Should().Be(1.00m, "default per-share schedule with 1.00 minimum");
            return;
        }

        Assert.Fail("No fill update was received for the resting limit order.");
    }

    [Fact]
    public async Task AdapterGateway_RestingStop_TriggersOnLaterTrade_NeverBefore()
    {
        var cache = CreateCacheWithQuote("SPY", 500m, 500.5m);
        cache.RecordTrade("SPY", new Trade(
            DateTimeOffset.UtcNow, "SPY", 500.25m, Size: 10, Aggressor: AggressorSide.Buy, SequenceNumber: 1));
        await using var gateway = new AdapterGateway(
            NullLogger<AdapterGateway>.Instance,
            securityMaster: null,
            options: null,
            liveFeed: cache,
            costOptions: new PaperTradingCostOptions { CommissionRate = 0m });

        await gateway.SubmitAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.StopMarket,
            StopPrice = 505m,
            Quantity = 1m
        });

        // Trade below the stop: must not trigger.
        cache.RecordTrade("SPY", new Trade(
            DateTimeOffset.UtcNow, "SPY", 504.99m, Size: 10, Aggressor: AggressorSide.Buy, SequenceNumber: 2));
        gateway.EvaluateSymbol("SPY");

        // Trade through the stop: triggers and fills as a market order.
        cache.RecordQuote("SPY", new BboQuotePayload(
            DateTimeOffset.UtcNow, "SPY", 505m, 10, 505.5m, 10,
            MidPrice: null, Spread: null, SequenceNumber: 3));
        cache.RecordTrade("SPY", new Trade(
            DateTimeOffset.UtcNow, "SPY", 505.25m, Size: 10, Aggressor: AggressorSide.Buy, SequenceNumber: 4));
        gateway.EvaluateSymbol("SPY");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var update in gateway.StreamOrderUpdatesAsync(cts.Token))
        {
            update.Status.Should().Be(Meridian.Execution.Models.OrderStatus.Filled);
            update.AverageFillPrice!.Value.Should().BeInRange(505m, 505.5m,
                "the triggered stop fills inside the quote envelope in effect at trigger time");
            return;
        }

        Assert.Fail("No fill update was received for the stop order.");
    }

    [Fact]
    public async Task ExecutionGateway_RestingLimit_FillsThroughReportStream_InsideEnvelope()
    {
        var cache = CreateCacheWithQuote("MSFT", 100m, 102m);
        await using var gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance,
            securityMaster: null,
            options: null,
            liveFeed: cache,
            costOptions: new PaperTradingCostOptions());

        var accepted = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = 99m,
            Quantity = 5m
        });
        accepted.OrderStatus.Should().Be(OrderStatus.Accepted, "the limit is below the ask, so the order rests");

        cache.RecordQuote("MSFT", new BboQuotePayload(
            DateTimeOffset.UtcNow, "MSFT", 98m, 10, 98.5m, 10,
            MidPrice: null, Spread: null, SequenceNumber: 2));
        gateway.EvaluateSymbol("MSFT");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var report in gateway.StreamExecutionReportsAsync(cts.Token))
        {
            report.OrderStatus.Should().Be(OrderStatus.Filled);
            report.FillPrice!.Value.Should().Be(98.5m);
            report.FillPrice!.Value.Should().BeInRange(98m, 98.5m);
            report.Commission.Should().Be(1.00m);
            return;
        }

        Assert.Fail("No execution report was received for the resting limit order.");
    }

    [Fact]
    public async Task ExecutionGateway_LimitOrder_NeverFillsAtItsOwnPriceWithoutMarketData()
    {
        await using var gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance,
            securityMaster: null,
            options: null,
            liveFeed: new LiveMarketDataCache());

        var report = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "TSLA",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = 250m,
            Quantity = 1m
        });

        report.OrderStatus.Should().Be(OrderStatus.Accepted,
            "with no observed market data a limit order rests — it must never fill at its own limit price");
        report.FillPrice.Should().BeNull();
    }

    [Fact]
    public async Task AdapterGateway_ImmediateOrCancel_CancelsWhenNotImmediatelyFillable()
    {
        var cache = CreateCacheWithQuote("QQQ", 400m, 401m);
        await using var gateway = new AdapterGateway(
            NullLogger<AdapterGateway>.Instance,
            securityMaster: null,
            options: null,
            liveFeed: cache);

        await gateway.SubmitAsync(new OrderRequest
        {
            Symbol = "QQQ",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = 399m,
            TimeInForce = TimeInForce.ImmediateOrCancel,
            Quantity = 1m
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var update in gateway.StreamOrderUpdatesAsync(cts.Token))
        {
            update.Status.Should().Be(Meridian.Execution.Models.OrderStatus.Cancelled,
                "an IOC order that is not immediately fillable cancels instead of resting");
            return;
        }

        Assert.Fail("No update was received for the IOC order.");
    }
}
