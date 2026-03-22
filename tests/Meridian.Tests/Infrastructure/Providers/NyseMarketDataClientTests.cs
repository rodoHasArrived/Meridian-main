using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.NYSE;
using Meridian.Infrastructure.DataSources;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class NyseMarketDataClientTests : IAsyncDisposable
{
    private readonly TestMarketEventPublisher _publisher;
    private readonly TradeDataCollector _tradeCollector;
    private readonly MarketDepthCollector _depthCollector;
    private readonly QuoteCollector _quoteCollector;
    private readonly NyseMarketDataClient _client;

    public NyseMarketDataClientTests()
    {
        _publisher = new TestMarketEventPublisher();
        _tradeCollector = new TradeDataCollector(_publisher, null);
        _depthCollector = new MarketDepthCollector(_publisher, requireExplicitSubscription: false);
        _quoteCollector = new QuoteCollector(_publisher);
        _client = new NyseMarketDataClient(
            _tradeCollector,
            _depthCollector,
            _quoteCollector,
            new NYSEOptions());
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    [Fact]
    public void SubscribeTrades_AddsTradeAndCompanionQuoteSubscriptions()
    {
        var tradeSubscriptionId = _client.SubscribeTrades(new SymbolConfig("AAPL"));
        var source = GetSource();

        tradeSubscriptionId.Should().BeGreaterThan(0);
        source.ActiveSubscriptions.Should().HaveCount(2);
        source.SubscribedSymbols.Should().ContainSingle("AAPL");
    }

    [Fact]
    public void UnsubscribeTrades_RemovesCompanionQuoteSubscription()
    {
        var tradeSubscriptionId = _client.SubscribeTrades(new SymbolConfig("AAPL"));
        var source = GetSource();

        _client.UnsubscribeTrades(tradeSubscriptionId);

        source.ActiveSubscriptions.Should().BeEmpty(
            because: "trade subscriptions auto-subscribe quotes and must tear both legs down together");
        source.SubscribedSymbols.Should().BeEmpty();
    }

    [Fact]
    public void InjectedMessages_ArePublishedThroughCollectors()
    {
        _client.SubscribeTrades(new SymbolConfig("AAPL"));
        _client.SubscribeMarketDepth(new SymbolConfig("AAPL"));

        InvokeSourceMessage("""{"type":"trade","symbol":"AAPL","price":185.50,"size":100,"timestamp":"2025-01-15T14:30:00.123Z","exchange":"NYSE","conditions":"@","sequence":12345,"side":"buy"}""");
        InvokeSourceMessage("""{"type":"quote","symbol":"AAPL","bidPrice":185.45,"bidSize":300,"askPrice":185.50,"askSize":200,"timestamp":"2025-01-15T14:30:00.456Z","bidExchange":"NYSE","askExchange":"ARCA","sequence":67890}""");
        InvokeSourceMessage("""{"type":"depth","symbol":"AAPL","operation":"add","side":"bid","level":0,"price":185.45,"size":1500,"timestamp":"2025-01-15T14:30:00.789Z","marketMaker":"GSCO","sequence":99999}""");

        var tradeEvent = _publisher.PublishedEvents.Single(evt => evt.Type == MarketEventType.Trade);
        var quoteEvent = _publisher.PublishedEvents.Single(evt => evt.Type == MarketEventType.BboQuote);
        var snapshotEvent = _publisher.PublishedEvents.Single(evt => evt.Type == MarketEventType.L2Snapshot);

        var trade = tradeEvent.Payload.Should().BeOfType<Trade>().Subject;
        trade.Symbol.Should().Be("AAPL");
        trade.Price.Should().Be(185.50m);
        trade.Size.Should().Be(100);
        trade.SequenceNumber.Should().Be(12345);
        trade.Venue.Should().Be("NYSE");
        trade.Aggressor.Should().Be(AggressorSide.Buy);
        trade.RawConditions.Should().Equal("@");

        var quote = quoteEvent.Payload.Should().BeOfType<BboQuotePayload>().Subject;
        quote.Symbol.Should().Be("AAPL");
        quote.BidPrice.Should().Be(185.45m);
        quote.AskPrice.Should().Be(185.50m);
        quote.BidSize.Should().Be(300);
        quote.AskSize.Should().Be(200);
        quote.Venue.Should().Be("NYSE");

        var snapshot = snapshotEvent.Payload.Should().BeOfType<LOBSnapshot>().Subject;
        snapshot.Symbol.Should().Be("AAPL");
        snapshot.Bids.Should().ContainSingle();
        snapshot.Bids[0].Price.Should().Be(185.45m);
        snapshot.Bids[0].Size.Should().Be(1500);
        snapshot.Bids[0].MarketMaker.Should().Be("GSCO");
    }

    [Fact]
    public void UnsubscribeAll_ClearsTrackedSubscriptions()
    {
        _client.SubscribeTrades(new SymbolConfig("AAPL"));
        _client.SubscribeMarketDepth(new SymbolConfig("MSFT"));
        var source = GetSource();

        source.UnsubscribeAll();

        source.ActiveSubscriptions.Should().BeEmpty();
        source.SubscribedSymbols.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectionLoss_WithNoReconnectAttempts_SetsUnavailableAndKeepsSubscriptionsTracked()
    {
        await using var reconnectClient = new NyseMarketDataClient(
            _tradeCollector,
            _depthCollector,
            _quoteCollector,
            new NYSEOptions { MaxReconnectAttempts = 0 });

        reconnectClient.SubscribeTrades(new SymbolConfig("AAPL"));
        var source = GetSource(reconnectClient);

        await InvokeConnectionLostAsync(source);

        source.Status.Should().Be(DataSourceStatus.Unavailable);
        source.ActiveSubscriptions.Should().HaveCount(2,
            because: "subscription intent should be preserved for later recovery");
        source.SubscribedSymbols.Should().ContainSingle("AAPL");
    }

    [Fact]
    public async Task ConnectionLoss_ReplacesReconnectCancellationSource()
    {
        await using var reconnectClient = new NyseMarketDataClient(
            _tradeCollector,
            _depthCollector,
            _quoteCollector,
            new NYSEOptions { MaxReconnectAttempts = 0 });

        var source = GetSource(reconnectClient);
        var before = GetReconnectCts(source);

        await InvokeConnectionLostAsync(source);

        var after = GetReconnectCts(source);
        after.Should().NotBeSameAs(before,
            because: "each reconnect session should get a fresh cancellation source");
    }

    private NYSEDataSource GetSource()
    {
        return GetSource(_client);
    }

    private static NYSEDataSource GetSource(NyseMarketDataClient client)
    {
        var field = typeof(NyseMarketDataClient).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(client).Should().BeOfType<NYSEDataSource>().Subject;
    }

    private void InvokeSourceMessage(string message)
    {
        var source = GetSource();
        var method = typeof(NYSEDataSource).GetMethod("ProcessWebSocketMessage", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(source, new object[] { message });
    }

    private static async Task InvokeConnectionLostAsync(NYSEDataSource source)
    {
        var method = typeof(NYSEDataSource).GetMethod("OnWsConnectionLostAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = method!.Invoke(source, Array.Empty<object>()).Should().BeAssignableTo<Task>().Subject;
        await task;
    }

    private static CancellationTokenSource GetReconnectCts(NYSEDataSource source)
    {
        var field = typeof(NYSEDataSource).GetField("_reconnectCts", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(source).Should().BeOfType<CancellationTokenSource>().Subject;
    }
}
