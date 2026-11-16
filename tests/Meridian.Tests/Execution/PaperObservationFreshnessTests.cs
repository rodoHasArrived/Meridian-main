using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Adapters;
using Meridian.Execution.PaperMatching;
using Meridian.Execution.Sdk;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using AdapterGateway = Meridian.Execution.Adapters.PaperTradingGateway;
using ExecutionGateway = Meridian.Execution.PaperTradingGateway;
using OrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Tests.Execution;

/// <summary>
/// The opt-in observation freshness window: a stalled tick feed must not price, trigger, or fill
/// a paper order, the default must keep matching the cache as it is, and the pre-trade guard
/// must drop exactly the observations the matcher drops.
/// </summary>
public sealed class PaperObservationFreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private static Trade TradeAt(DateTimeOffset at, decimal price, string symbol = "SPY") =>
        new(at, symbol, price, Size: 100, Aggressor: AggressorSide.Buy, SequenceNumber: 1);

    private static BboQuotePayload QuoteAt(DateTimeOffset at, decimal bid, decimal ask, string symbol = "SPY") =>
        new(at, symbol, bid, 100, ask, 100, (bid + ask) / 2m, ask - bid, 1);

    private static PaperObservationFreshnessPolicy Policy(TimeSpan? maxAge = null) =>
        new(maxAge ?? Window, () => Now);

    [Fact]
    public void Disabled_CapturesTheCacheAsItIs_HoweverOld()
    {
        var cache = new LiveMarketDataCache();
        cache.RecordTrade("SPY", TradeAt(Now.AddHours(-6), 130m));

        var observation = new PaperObservationFreshnessPolicy(null, () => Now).Capture(cache, "SPY");

        observation.IsStale.Should().BeFalse();
        observation.LastTradePrice.Should().Be(130m);
        PaperObservationFreshnessPolicy.From(new PaperTradingGatewayOptions()).MaxAge.Should().BeNull(
            "the freshness window is opt-in; the default preserves paper-match/1 behaviour");
    }

    [Fact]
    public void StalePrintBesideFreshQuote_DropsOnlyThePrint()
    {
        var cache = new LiveMarketDataCache();
        cache.RecordTrade("SPY", TradeAt(Now.AddMinutes(-6), 130m));
        cache.RecordQuote("SPY", QuoteAt(Now.AddSeconds(-1), 99m, 100m));

        var observation = Policy().Capture(cache, "SPY");

        observation.IsStale.Should().BeFalse();
        observation.LastTradePrice.Should().BeNull();
        observation.ResolveStopTriggerPrice(OrderSide.Buy).Should().Be(100m,
            "a buy stop must not fire off a print the market left six minutes ago");
    }

    [Fact]
    public void AllTickDataStale_WithholdsEverything_IncludingACachedBar()
    {
        var cache = new LiveMarketDataCache();
        cache.RecordQuote("SPY", QuoteAt(Now.AddSeconds(-30), 99m, 100m));
        cache.RecordBar("SPY", new HistoricalBar("SPY", new DateOnly(2026, 2, 27), 95m, 97m, 94m, 96m, 1_000));

        var observation = Policy().Capture(cache, "SPY");

        observation.IsStale.Should().BeTrue();
        observation.HasAnyObservation.Should().BeFalse(
            "a stalled tick feed must not fall through to an older bar close");
    }

    [Fact]
    public void BarOnlySymbol_IsUntouched()
    {
        var cache = new LiveMarketDataCache();
        cache.RecordBar("SPY", new HistoricalBar("SPY", new DateOnly(2026, 2, 27), 95m, 97m, 94m, 96m, 1_000));

        var observation = Policy().Capture(cache, "SPY");

        observation.IsStale.Should().BeFalse();
        observation.BarClose.Should().Be(96m);
    }

    [Fact]
    public void FutureDatedTick_BeyondTheWindow_IsNotCurrent()
    {
        var cache = new LiveMarketDataCache();
        cache.RecordTrade("SPY", TradeAt(Now.AddMinutes(10), 130m));

        Policy().Capture(cache, "SPY").IsStale.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveWindow_IsRejectedAtComposition(int seconds)
    {
        var act = () => PaperObservationFreshnessPolicy.From(
            new PaperTradingGatewayOptions { MaxObservationAgeSeconds = seconds });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutionGateway_MarketOrder_StaleFeed_Rejects_EvenWithScaffoldEnabled(bool allowScaffold)
    {
        var cache = new LiveMarketDataCache();
        cache.RecordTrade("SPY", TradeAt(DateTimeOffset.UtcNow.AddMinutes(-10), 100m));
        var gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance,
            securityMaster: null,
            options: new PaperTradingGatewayOptions
            {
                MaxObservationAgeSeconds = 60,
                AllowScaffoldMarketFills = allowScaffold,
                ScaffoldMarketFillPrice = 42m
            },
            liveFeed: cache);

        var report = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 3m
        });

        report.OrderStatus.Should().Be(OrderStatus.Rejected);
        report.FilledQuantity.Should().Be(0);
        report.RejectReason.Should().Contain("SPY").And.Contain("MaxObservationAgeSeconds");
    }

    [Fact]
    public async Task ExecutionGateway_MarketOrder_FreshFeed_StillFillsUnderTheWindow()
    {
        var cache = new LiveMarketDataCache();
        cache.RecordTrade("SPY", TradeAt(DateTimeOffset.UtcNow, 100m));
        var gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance,
            securityMaster: null,
            options: new PaperTradingGatewayOptions { MaxObservationAgeSeconds = 60 },
            liveFeed: cache);

        var report = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 3m
        });

        report.OrderStatus.Should().Be(OrderStatus.Filled);
        report.FillPrice.Should().Be(100m);
    }

    [Fact]
    public async Task ExecutionGateway_MarketableLimit_StaleFeed_RestsInsteadOfFilling()
    {
        var cache = new LiveMarketDataCache();
        cache.RecordTrade("SPY", TradeAt(DateTimeOffset.UtcNow.AddMinutes(-10), 100m));
        var gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance,
            securityMaster: null,
            options: new PaperTradingGatewayOptions { MaxObservationAgeSeconds = 60 },
            liveFeed: cache);

        var report = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 3m,
            LimitPrice = 105m
        });

        report.OrderStatus.Should().Be(OrderStatus.Accepted,
            "a limit that only looks marketable against a stale print must wait for current data");
    }

    [Fact]
    public async Task AdapterGateway_MarketOrder_StaleFeed_Rejects()
    {
        var cache = new LiveMarketDataCache();
        cache.RecordQuote("AAPL", QuoteAt(DateTimeOffset.UtcNow.AddMinutes(-10), 99m, 100m, "AAPL"));
        await using var gateway = new AdapterGateway(
            NullLogger<AdapterGateway>.Instance,
            options: new PaperTradingGatewayOptions
            {
                MaxObservationAgeSeconds = 60,
                AllowScaffoldMarketFills = true
            },
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
            update.Status.Should().Be(Meridian.Execution.Models.OrderStatus.Rejected);
            update.AverageFillPrice.Should().BeNull();
            update.RejectReason.Should().Contain("AAPL").And.Contain("MaxObservationAgeSeconds");
            return;
        }

        Assert.Fail("No order update was received from the paper gateway.");
    }

    [Fact]
    public void PreTradeGuard_SharesTheWindow_SoItDropsThePrintTheMatcherDrops()
    {
        var aggregate = new Mock<Meridian.Strategies.Services.IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns([]);

        var cache = new LiveMarketDataCache();
        cache.RecordTrade("AAPL", TradeAt(Now.AddMinutes(-6), 130m, "AAPL"));
        cache.RecordQuote("AAPL", QuoteAt(Now.AddSeconds(-1), 90m, 100m, "AAPL"));

        Meridian.Risk.IPortfolioExposureProvider Guard(PaperObservationFreshnessPolicy? freshness) =>
            new AggregatePortfolioExposureProvider(
                aggregate.Object,
                clock: () => Now,
                liveFeedAccessor: () => cache,
                paperMatchingIsAuthoritative: () => true,
                paperObservationFreshness: freshness);

        Guard(null).TryGetTriggerReferencePrice("AAPL", OrderSide.Buy)
            .Should().Be(130m, "with no window the matcher still fires from the old print");
        Guard(Policy()).TryGetTriggerReferencePrice("AAPL", OrderSide.Buy)
            .Should().Be(100m, "under the window the matcher resolves the trigger from the fresh ask");
    }
}
