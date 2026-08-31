using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Adapters;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using AdapterGateway = Meridian.Execution.Adapters.PaperTradingGateway;
using ExecutionGateway = Meridian.Execution.PaperTradingGateway;
using OrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Tests.Execution;

/// <summary>
/// Paper gateways must price market fills from the live feed when one is wired, instead of the
/// fixed scaffold notional price.
/// </summary>
public sealed class PaperGatewayLiveFeedPricingTests
{
    private static LiveMarketDataCache CreateCacheWithTrade(string symbol, decimal price)
    {
        var cache = new LiveMarketDataCache();
        cache.RecordTrade(symbol, new Trade(
            DateTimeOffset.UtcNow,
            symbol,
            price,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1));
        return cache;
    }

    [Fact]
    public async Task AdapterGateway_MarketOrder_FillsAtLastTradePriceFromFeed()
    {
        var cache = CreateCacheWithTrade("AAPL", 123.45m);
        await using var gateway = new AdapterGateway(
            NullLogger<AdapterGateway>.Instance,
            securityMaster: null,
            options: null,
            liveFeed: cache);

        await gateway.SubmitAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var update in gateway.StreamOrderUpdatesAsync(cts.Token))
        {
            update.Status.Should().Be(Meridian.Execution.Models.OrderStatus.Filled);
            update.AverageFillPrice.Should().Be(123.45m, "market fills must use the live feed price, not the scaffold");
            return;
        }

        Assert.Fail("No order update was received from the paper gateway.");
    }

    [Fact]
    public async Task AdapterGateway_MarketBuy_PaysTheObservedAskWhenOnlyQuotesSeen()
    {
        var cache = new LiveMarketDataCache();
        cache.RecordQuote("MSFT", new BboQuotePayload(
            DateTimeOffset.UtcNow,
            "MSFT",
            BidPrice: 100m,
            BidSize: 5,
            AskPrice: 102m,
            AskSize: 5,
            MidPrice: null,
            Spread: null,
            SequenceNumber: 1));
        await using var gateway = new AdapterGateway(
            NullLogger<AdapterGateway>.Instance,
            securityMaster: null,
            options: null,
            liveFeed: cache);

        await gateway.SubmitAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var update in gateway.StreamOrderUpdatesAsync(cts.Token))
        {
            update.AverageFillPrice.Should().Be(102m,
                "a market buy transacts against the observed ask — the spread is a real cost, not a free midpoint");
            return;
        }

        Assert.Fail("No order update was received from the paper gateway.");
    }

    [Fact]
    public async Task ExecutionGateway_MarketOrder_FillsAtLastTradePriceFromFeed()
    {
        var cache = CreateCacheWithTrade("SPY", 512.25m);
        var gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance,
            securityMaster: null,
            options: null,
            liveFeed: cache);

        var report = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 3m
        });

        report.OrderStatus.Should().Be(OrderStatus.Filled);
        report.FillPrice.Should().Be(512.25m);
    }

    [Fact]
    public async Task ExecutionGateway_MarketOrder_WithoutFeed_RejectsByDefault()
    {
        var gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance,
            securityMaster: null,
            options: new PaperTradingGatewayOptions { ScaffoldMarketFillPrice = 42m });

        var report = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 3m
        });

        report.OrderStatus.Should().Be(OrderStatus.Rejected,
            "a market order with no live reference price must fail closed instead of filling at a fabricated price");
        report.FilledQuantity.Should().Be(0);
        report.RejectReason.Should().Contain("SPY").And.Contain("AllowScaffoldMarketFills");
    }

    [Fact]
    public async Task ExecutionGateway_MarketOrder_WithoutFeed_FillsAtScaffoldPriceWhenOptedIn()
    {
        var gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance,
            securityMaster: null,
            options: new PaperTradingGatewayOptions
            {
                AllowScaffoldMarketFills = true,
                ScaffoldMarketFillPrice = 42m
            });

        var report = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 3m
        });

        report.OrderStatus.Should().Be(OrderStatus.Filled);
        report.FillPrice.Should().Be(42m);
    }

    [Fact]
    public async Task AdapterGateway_MarketOrder_WithoutFeed_RejectsByDefault()
    {
        await using var gateway = new AdapterGateway(NullLogger<AdapterGateway>.Instance);

        await gateway.SubmitAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var update in gateway.StreamOrderUpdatesAsync(cts.Token))
        {
            update.Status.Should().Be(Meridian.Execution.Models.OrderStatus.Rejected,
                "a market order with no live reference price must fail closed instead of filling at a fabricated price");
            update.FilledQuantity.Should().Be(0);
            update.AverageFillPrice.Should().BeNull();
            update.RejectReason.Should().Contain("AAPL").And.Contain("AllowScaffoldMarketFills");
            return;
        }

        Assert.Fail("No order update was received from the paper gateway.");
    }

    [Fact]
    public async Task ExecutionGateway_MarketOrder_CallerProvidedSimulatedPriceStillWins()
    {
        var cache = CreateCacheWithTrade("SPY", 512.25m);
        var gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance,
            securityMaster: null,
            options: null,
            liveFeed: cache);

        var report = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 3m,
            LimitPrice = 500m
        });

        report.FillPrice.Should().Be(500m, "an explicit caller-simulated price must take precedence over the feed");
    }
}
