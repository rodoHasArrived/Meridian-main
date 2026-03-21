using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Events;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Synthetic;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class SyntheticMarketDataProviderTests
{
    [Fact]
    public async Task HistoricalProvider_ReturnsAdjustedBars_WithCorporateActions()
    {
        var provider = new SyntheticHistoricalDataProvider(new SyntheticMarketDataConfig(Enabled: true));

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
        var provider = new SyntheticHistoricalDataProvider(new SyntheticMarketDataConfig(Enabled: true, HistoricalTradeDensityPerDay: 12, HistoricalQuoteDensityPerDay: 16));
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
        var publisher = new RecordingPublisher();
        var client = new SyntheticMarketDataClient(publisher, new SyntheticMarketDataConfig(Enabled: true, EventsPerSecond: 20));
        await client.ConnectAsync();

        var tradeSub = client.SubscribeTrades(new SymbolConfig("AAPL", SubscribeTrades: true, SubscribeDepth: false));
        var depthSub = client.SubscribeMarketDepth(new SymbolConfig("AAPL", SubscribeTrades: false, SubscribeDepth: true, DepthLevels: 5));
        await Task.Delay(250);
        client.UnsubscribeTrades(tradeSub);
        client.UnsubscribeMarketDepth(depthSub);
        await client.DisconnectAsync();

        publisher.Events.Should().Contain(e => e.Type == Meridian.Contracts.Domain.Enums.MarketEventType.Trade);
        publisher.Events.Should().Contain(e => e.Type == Meridian.Contracts.Domain.Enums.MarketEventType.BboQuote);
        publisher.Events.Should().Contain(e => e.Type == Meridian.Contracts.Domain.Enums.MarketEventType.L2Snapshot);
        publisher.Events.Should().Contain(e => e.Symbol == "AAPL");
        publisher.Events.OfType<MarketEvent>().Where(e => e.Payload is LOBSnapshot).Should().OnlyContain(e => ((LOBSnapshot)e.Payload).Bids.Count == 5);
    }

    private sealed class RecordingPublisher : IMarketEventPublisher
    {
        public ConcurrentBag<MarketEvent> Events { get; } = new();

        public bool TryPublish(in MarketEvent evt)
        {
            Events.Add(evt);
            return true;
        }
    }
}
