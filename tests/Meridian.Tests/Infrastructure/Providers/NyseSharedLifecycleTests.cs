using System.Net.Http;
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
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class NyseSharedLifecycleTests : IAsyncDisposable
{
    private readonly TestMarketEventPublisher _publisher;
    private readonly TradeDataCollector _tradeCollector;
    private readonly MarketDepthCollector _depthCollector;
    private readonly QuoteCollector _quoteCollector;
    private readonly NyseMarketDataClient _client;

    public NyseSharedLifecycleTests()
    {
        _publisher = new TestMarketEventPublisher();
        _tradeCollector = new TradeDataCollector(_publisher, null);
        _depthCollector = new MarketDepthCollector(_publisher, requireExplicitSubscription: false);
        _quoteCollector = new QuoteCollector(_publisher);
        _client = new NyseMarketDataClient(
            _tradeCollector,
            _depthCollector,
            _quoteCollector,
            CreateMockHttpClientFactory(),
            new NYSEOptions());
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Test 1: MultiSymbol_SubscribeTwo_BothTracked
    // -------------------------------------------------------------------------

    [Fact]
    public void MultiSymbol_SubscribeTwo_BothTracked()
    {
        _client.SubscribeTrades(new SymbolConfig("AAPL"));
        _client.SubscribeTrades(new SymbolConfig("MSFT"));

        var source = GetSource();

        source.ActiveSubscriptions.Should().HaveCount(4,
            because: "each SubscribeTrades call registers a trade sub plus a companion quote sub");
        source.SubscribedSymbols.Should().Contain("AAPL").And.Contain("MSFT");
    }

    // -------------------------------------------------------------------------
    // Test 2: MultiSymbol_UnsubscribeOne_OtherRemains
    // -------------------------------------------------------------------------

    [Fact]
    public void MultiSymbol_UnsubscribeOne_OtherRemains()
    {
        var aaplSubId = _client.SubscribeTrades(new SymbolConfig("AAPL"));
        _client.SubscribeTrades(new SymbolConfig("MSFT"));

        _client.UnsubscribeTrades(aaplSubId);

        var source = GetSource();

        source.ActiveSubscriptions.Should().HaveCount(2,
            because: "only the MSFT trade and companion quote subscriptions should remain");
        source.SubscribedSymbols.Should().ContainSingle("MSFT");
        source.SubscribedSymbols.Should().NotContain("AAPL");
    }

    // -------------------------------------------------------------------------
    // Test 3: DepthOnlySubscription_DoesNotAddCompanionQuote
    // -------------------------------------------------------------------------

    [Fact]
    public void DepthOnlySubscription_DoesNotAddCompanionQuote()
    {
        _client.SubscribeMarketDepth(new SymbolConfig("AAPL"));

        var source = GetSource();

        source.ActiveSubscriptions.Should().HaveCount(1,
            because: "a depth-only subscription creates no companion quote leg");
    }

    // -------------------------------------------------------------------------
    // Test 4: InjectTrade_PublishesOrderFlowStatisticsAlongsideTrade
    // The TradeDataCollector emits both a Trade event AND an OrderFlowStatistics
    // event for each accepted trade — verify both are present after a single injection.
    // -------------------------------------------------------------------------

    [Fact]
    public void InjectTrade_PublishesOrderFlowStatisticsAlongsideTrade()
    {
        _client.SubscribeTrades(new SymbolConfig("AAPL"));
        _publisher.Clear();

        InvokeSourceMessage("""{"type":"trade","symbol":"AAPL","price":185.50,"size":100,"timestamp":"2025-01-15T14:30:00.123Z","exchange":"NYSE","conditions":"@","sequence":50001,"side":"buy"}""");

        var tradeEvents = _publisher.PublishedEvents
            .Where(e => e.Type == MarketEventType.Trade)
            .ToList();
        var orderFlowEvents = _publisher.PublishedEvents
            .Where(e => e.Type == MarketEventType.OrderFlow)
            .ToList();

        tradeEvents.Should().ContainSingle(
            because: "a single injected trade must produce exactly one Trade event");
        orderFlowEvents.Should().ContainSingle(
            because: "the TradeDataCollector emits rolling OrderFlowStatistics alongside each accepted trade");
    }

    // -------------------------------------------------------------------------
    // Test 5: MalformedJson_DoesNotThrow
    // -------------------------------------------------------------------------

    [Fact]
    public void MalformedJson_DoesNotThrow()
    {
        _client.SubscribeTrades(new SymbolConfig("AAPL"));

        var act = () => InvokeSourceMessage("{ not json }}}");

        act.Should().NotThrow(
            because: "ProcessWebSocketMessage must swallow parse errors to keep the connection alive");
        _publisher.PublishedEvents.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Test 6: HeartbeatMessage_ProducesNoEvent
    // -------------------------------------------------------------------------

    [Fact]
    public void HeartbeatMessage_ProducesNoEvent()
    {
        _client.SubscribeTrades(new SymbolConfig("AAPL"));

        InvokeSourceMessage("""{"type":"heartbeat","ts":12345}""");

        _publisher.PublishedEvents.Should().BeEmpty(
            because: "heartbeat messages carry no market data and must not trigger any event");
    }

    // -------------------------------------------------------------------------
    // Test 7: UnknownMessageType_ProducesNoEvent
    // -------------------------------------------------------------------------

    [Fact]
    public void UnknownMessageType_ProducesNoEvent()
    {
        _client.SubscribeTrades(new SymbolConfig("AAPL"));

        var act = () => InvokeSourceMessage("""{"type":"unknown_feed_type","data":{}}""");

        act.Should().NotThrow();
        _publisher.PublishedEvents.Should().BeEmpty(
            because: "unrecognised message types must be ignored silently");
    }

    // -------------------------------------------------------------------------
    // Test 8: DepthUpdate_Operation_ChangesBook
    // -------------------------------------------------------------------------

    [Fact]
    public void DepthUpdate_Operation_ChangesBook()
    {
        _client.SubscribeMarketDepth(new SymbolConfig("AAPL"));

        InvokeSourceMessage("""{"type":"depth","symbol":"AAPL","operation":"add","side":"ask","level":0,"price":186.00,"size":500,"timestamp":"2025-01-15T14:30:00.000Z","marketMaker":"GSCO","sequence":20000}""");
        InvokeSourceMessage("""{"type":"depth","symbol":"AAPL","operation":"update","side":"ask","level":0,"price":186.10,"size":800,"timestamp":"2025-01-15T14:30:01.000Z","marketMaker":"MLCO","sequence":20001}""");

        var snapshotEvents = _publisher.PublishedEvents
            .Where(e => e.Type == MarketEventType.L2Snapshot)
            .ToList();

        snapshotEvents.Should().HaveCount(2,
            because: "both the 'add' and the 'update' depth operations must each produce an L2Snapshot event");

        var firstSnapshot = snapshotEvents[0].Payload.Should().BeOfType<LOBSnapshot>().Subject;
        firstSnapshot.Symbol.Should().Be("AAPL");

        var secondSnapshot = snapshotEvents[1].Payload.Should().BeOfType<LOBSnapshot>().Subject;
        secondSnapshot.Symbol.Should().Be("AAPL");
    }

    // -------------------------------------------------------------------------
    // Test 9: MultipleTradesForSameSymbol_BothPublished
    // -------------------------------------------------------------------------

    [Fact]
    public void MultipleTradesForSameSymbol_BothPublished()
    {
        _client.SubscribeTrades(new SymbolConfig("AAPL"));
        _publisher.Clear();

        InvokeSourceMessage("""{"type":"trade","symbol":"AAPL","price":185.00,"size":100,"timestamp":"2025-01-15T14:30:00.000Z","exchange":"NYSE","conditions":"@","sequence":10001,"side":"buy"}""");
        InvokeSourceMessage("""{"type":"trade","symbol":"AAPL","price":185.10,"size":200,"timestamp":"2025-01-15T14:30:01.000Z","exchange":"NYSE","conditions":"@","sequence":10002,"side":"sell"}""");

        // Each trade message is accompanied by a synthesised BBO quote from the quote collector
        // via the companion subscription. However, in this scenario we only inject trade messages,
        // so only trade events are expected.
        var tradeEvents = _publisher.PublishedEvents
            .Where(e => e.Type == MarketEventType.Trade)
            .ToList();

        tradeEvents.Should().HaveCount(2,
            because: "two distinct trade messages for the same subscribed symbol must each produce a Trade event");

        var first = tradeEvents[0].Payload.Should().BeOfType<Trade>().Subject;
        first.Symbol.Should().Be("AAPL");
        first.Price.Should().Be(185.00m);

        var second = tradeEvents[1].Payload.Should().BeOfType<Trade>().Subject;
        second.Symbol.Should().Be("AAPL");
        second.Price.Should().Be(185.10m);
    }

    // -------------------------------------------------------------------------
    // Test 10: SubscribeUnsubscribeResubscribe_WorksCleanly
    // -------------------------------------------------------------------------

    [Fact]
    public void SubscribeUnsubscribeResubscribe_WorksCleanly()
    {
        var source = GetSource();

        // Step 1: subscribe — expect trade + companion quote (2 subs)
        var firstSubId = _client.SubscribeTrades(new SymbolConfig("AAPL"));
        source.ActiveSubscriptions.Should().HaveCount(2);

        // Step 2: unsubscribe — source must be empty
        _client.UnsubscribeTrades(firstSubId);
        source.ActiveSubscriptions.Should().BeEmpty(
            because: "unsubscribing the trade sub must also remove the companion quote sub");

        // Step 3: re-subscribe the same symbol — must arrive back at 2 subs
        _client.SubscribeTrades(new SymbolConfig("AAPL"));
        source.ActiveSubscriptions.Should().HaveCount(2,
            because: "re-subscribing after a full teardown must register fresh trade and quote subscriptions");
        source.SubscribedSymbols.Should().ContainSingle("AAPL");
    }

    // -------------------------------------------------------------------------
    // Private helpers (mirroring NyseMarketDataClientTests)
    // -------------------------------------------------------------------------

    private static IHttpClientFactory CreateMockHttpClientFactory()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        return factory;
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
}
