using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class SyntheticMarketDataProviderTests
{
    [Fact]
    public async Task HistoricalProvider_ReturnsAdjustedBars_WithCorporateActions()
    {
        var provider = SyntheticProviderTestHarness.CreateHistorical();

        var bars = await provider.GetAdjustedDailyBarsAsync("NVDA", new DateOnly(2024, 6, 3), new DateOnly(2024, 6, 14));
        var dividends = await provider.GetDividendsAsync("NVDA", new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var splits = await provider.GetSplitsAsync("NVDA", new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        bars.Should().NotBeEmpty();
        bars.Should().Contain(b => b.SplitFactor == 10m);
        bars.Should().OnlyContain(b => b.AdjustedClose.HasValue);
        dividends.Should().NotBeEmpty();
        splits.Should().ContainSingle(s => s.SplitRatio == 10m);
    }

    [Fact]
    public async Task HistoricalProvider_ReturnsQuotesTradesAndAuctions()
    {
        var provider = SyntheticProviderTestHarness.CreateHistorical(SyntheticProviderTestHarness.BuildScenarioConfig(replayBarsLimit: 20));
        var start = new DateTimeOffset(2024, 3, 18, 13, 30, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 3, 18, 20, 0, 0, TimeSpan.Zero);

        var quotes = await provider.GetHistoricalQuotesAsync("SPY", start, end, limit: 20);
        var trades = await provider.GetHistoricalTradesAsync("SPY", start, end, limit: 20);
        var auctions = await provider.GetHistoricalAuctionsAsync("SPY", new DateOnly(2024, 3, 18), new DateOnly(2024, 3, 18));

        quotes.Quotes.Should().NotBeEmpty();
        quotes.Quotes.Should().OnlyContain(q => q.AskPrice >= q.BidPrice);
        trades.Trades.Should().NotBeEmpty();
        trades.Trades.Should().OnlyContain(t => t.Price > 0 && t.Size > 0);
        auctions.Auctions.Should().ContainSingle();
        auctions.Auctions[0].OpeningAuctions.Should().NotBeEmpty();
        auctions.Auctions[0].ClosingAuctions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task StreamingClient_PublishesTradesQuotesAndOrderBookSnapshots()
    {
        var publisher = new SignalingMarketEventPublisher();
        var client = new SyntheticMarketDataClient(publisher, new SyntheticMarketDataConfig(Enabled: true, EventsPerSecond: 20));
        await client.ConnectAsync();

        var tradeSub = client.SubscribeTrades(new SymbolConfig("AAPL", SubscribeTrades: true, SubscribeDepth: false));
        var depthSub = client.SubscribeMarketDepth(new SymbolConfig("AAPL", SubscribeTrades: false, SubscribeDepth: true, DepthLevels: 5));
        await publisher.WaitUntilAsync(
            events =>
                events.Any(e => e.Type == MarketEventType.Trade) &&
                events.Any(e => e.Type == MarketEventType.BboQuote) &&
                events.Any(e => e.Type == MarketEventType.L2Snapshot),
            TimeSpan.FromSeconds(2));
        client.UnsubscribeTrades(tradeSub);
        client.UnsubscribeMarketDepth(depthSub);
        await client.DisconnectAsync();

        var publishedEvents = publisher.PublishedEvents;
        publishedEvents.Should().Contain(e => e.Type == MarketEventType.Trade);
        publishedEvents.Should().Contain(e => e.Type == MarketEventType.BboQuote);
        publishedEvents.Should().Contain(e => e.Type == MarketEventType.L2Snapshot);
        publishedEvents.Should().Contain(e => e.Symbol == "AAPL");
        publishedEvents.Where(e => e.Payload is LOBSnapshot).Should().OnlyContain(e => ((LOBSnapshot)e.Payload).Bids.Count == 5);
    }

    [Fact]
    public async Task StreamingClient_SessionClose_DisconnectStopsSyntheticPublishLoops()
    {
        var publisher = new SignalingMarketEventPublisher();
        await using var client = new SyntheticMarketDataClient(
            publisher,
            new SyntheticMarketDataConfig(Enabled: true, EventsPerSecond: 50));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await client.ConnectAsync(timeout.Token);
        client.SubscribeTrades(new SymbolConfig("MSFT", SubscribeTrades: true, SubscribeDepth: false));

        await publisher.WaitUntilAsync(events => events.Count > 0, TimeSpan.FromSeconds(2));

        await client.DisconnectAsync(timeout.Token);
        var countAfterDisconnect = publisher.PublishedEvents.Count;

        var receivedAfterDisconnect = await publisher.HasAdditionalEventWithinAsync(
            countAfterDisconnect,
            TimeSpan.FromMilliseconds(100));

        receivedAfterDisconnect.Should().BeFalse();
    }

    [Fact]
    public void SubscribeTrades_NullConfig_ThrowsArgumentNullException()
    {
        var publisher = new SignalingMarketEventPublisher();
        var client = new SyntheticMarketDataClient(publisher, new SyntheticMarketDataConfig(Enabled: true));

        var act = () => client.SubscribeTrades(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cfg");
    }

    [Fact]
    public void SubscribeMarketDepth_NullConfig_ThrowsArgumentNullException()
    {
        var publisher = new SignalingMarketEventPublisher();
        var client = new SyntheticMarketDataClient(publisher, new SyntheticMarketDataConfig(Enabled: true));

        var act = () => client.SubscribeMarketDepth(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cfg");
    }


    [Fact]
    public async Task HistoricalProvider_ScenarioTimeout_IsRepeatable()
    {
        var provider = SyntheticProviderTestHarness.CreateHistorical(
            SyntheticProviderTestHarness.BuildScenarioConfig(timeoutEveryNCalls: 2));

        var start = new DateOnly(2024, 4, 1);
        var end = new DateOnly(2024, 4, 5);

        await provider.GetAdjustedDailyBarsAsync("AAPL", start, end);
        var act = async () => await provider.GetAdjustedDailyBarsAsync("AAPL", start, end);

        await act.Should().ThrowAsync<TimeoutException>();
    }

}
