using System.Net.Http;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.NYSE;
using Meridian.Tests.TestHelpers;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// End-to-end pipeline tests for <see cref="NyseMarketDataClient"/>.
/// <para>
/// Uses <see cref="NYSEDataSource.ProcessTestMessage"/> to inject raw WebSocket JSON payloads
/// and verifies that the routing chain — <c>NYSEDataSource → Rx subject → NyseMarketDataClient.OnTrade/OnQuote/OnDepth → collector → publisher</c>
/// — produces correctly populated events in <see cref="TestMarketEventPublisher"/>.
/// These tests cover the gap left by the existing <see cref="NYSEMessageParsingTests"/>, which
/// validates JSON field extraction in isolation but does not exercise end-to-end routing.
/// </para>
/// </summary>
public sealed class NyseMessagePipelineTests : IAsyncDisposable
{
    private readonly TestMarketEventPublisher _publisher;
    private readonly TradeDataCollector _tradeCollector;
    private readonly QuoteCollector _quoteCollector;
    private readonly MarketDepthCollector _depthCollector;
    private readonly NyseMarketDataClient _client;
    private readonly NYSEDataSource _source;

    public NyseMessagePipelineTests()
    {
        _publisher = new TestMarketEventPublisher();
        _tradeCollector = new TradeDataCollector(_publisher, null);
        _depthCollector = new MarketDepthCollector(_publisher, requireExplicitSubscription: false);
        _quoteCollector = new QuoteCollector(_publisher);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        _client = new NyseMarketDataClient(
            _tradeCollector,
            _depthCollector,
            _quoteCollector,
            factory,
            new NYSEOptions());

        // Obtain the inner NYSEDataSource via reflection so we can inject test messages.
        var sourceField = typeof(NyseMarketDataClient)
            .GetField("_source", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        _source = (NYSEDataSource)sourceField.GetValue(_client)!;

        // Subscribe symbols so the collectors accept messages.
        _client.SubscribeTrades(new SymbolConfig("AAPL"));
        _client.SubscribeTrades(new SymbolConfig("MSFT"));
        _client.SubscribeMarketDepth(new SymbolConfig("AAPL"));
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();

    // -------------------------------------------------------------------------
    // Trade pipeline
    // -------------------------------------------------------------------------

    [Fact]
    public void TradeMessage_RoutesToPublisherWithCorrectFields()
    {
        const string json =
            """{"type":"trade","symbol":"AAPL","price":213.45,"size":100,"timestamp":"2025-01-15T14:30:00.123Z","exchange":"NYSE","conditions":"@","sequence":9876,"side":"buy"}""";

        _source.ProcessTestMessage(json);

        var events = _publisher.PublishedEvents.Where(e => e.Type == MarketEventType.Trade).ToArray();
        events.Should().NotBeEmpty(because: "a trade message for a subscribed symbol should produce a Trade event");

        var trade = events[0].Payload as Meridian.Contracts.Domain.Models.Trade;
        trade.Should().NotBeNull();
        trade!.Symbol.Should().Be("AAPL");
        trade.Price.Should().Be(213.45m);
        trade.Size.Should().Be(100);
    }

    [Fact]
    public void TradeMessage_BuySide_AggressorIsCorrect()
    {
        const string json =
            """{"type":"trade","symbol":"AAPL","price":100.00,"size":10,"timestamp":"2025-01-15T14:30:00Z","side":"buy","sequence":1}""";

        _source.ProcessTestMessage(json);

        var events = _publisher.PublishedEvents.Where(e => e.Type == MarketEventType.Trade).ToArray();
        events.Should().NotBeEmpty();

        var trade = events[0].Payload as Meridian.Contracts.Domain.Models.Trade;
        trade!.Aggressor.Should().Be(AggressorSide.Buy);
    }

    [Fact]
    public void TradeMessage_SellSide_AggressorIsCorrect()
    {
        const string json =
            """{"type":"trade","symbol":"AAPL","price":100.00,"size":10,"timestamp":"2025-01-15T14:30:00Z","side":"sell","sequence":1}""";

        _source.ProcessTestMessage(json);

        var events = _publisher.PublishedEvents.Where(e => e.Type == MarketEventType.Trade).ToArray();
        events.Should().NotBeEmpty();

        var trade = events[0].Payload as Meridian.Contracts.Domain.Models.Trade;
        trade!.Aggressor.Should().Be(AggressorSide.Sell);
    }

    [Fact]
    public void TradeMessage_UnsubscribedSymbol_IsProcessedByCollector()
    {
        // TradeDataCollector is stateless with respect to subscriptions: it processes
        // every trade pushed to it via OnTrade.  Subscription filtering is handled at the
        // WebSocket / source level (the source won't subscribe to TSLA, so no server
        // messages for TSLA would arrive).  When injecting test messages directly via
        // ProcessTestMessage, TSLA trades WILL reach the collector.
        // This test documents that understood behaviour.
        const string json =
            """{"type":"trade","symbol":"TSLA","price":250.00,"size":50,"timestamp":"2025-01-15T14:30:00Z","sequence":1}""";

        _source.ProcessTestMessage(json);

        // TSLA flows through the Rx pipeline to the collector and produces a Trade event.
        var tslaEvents = _publisher.PublishedEvents
            .Where(e => e.Type == MarketEventType.Trade && e.Symbol == "TSLA")
            .ToArray();

        tslaEvents.Should().NotBeEmpty(
            because: "the collector processes all messages routed to it regardless of subscription state; " +
                     "subscription-based server-side filtering is the source's responsibility");
    }

    [Fact]
    public void MultipleTradeMessages_AllAccepted()
    {
        // Use distinct sequence numbers per symbol stream to avoid out-of-order integrity events.
        const string trade1 =
            """{"type":"trade","symbol":"AAPL","price":213.45,"size":100,"timestamp":"2025-01-15T14:30:00.100Z","sequence":1}""";
        const string trade2 =
            """{"type":"trade","symbol":"MSFT","price":401.00,"size":200,"timestamp":"2025-01-15T14:30:00.200Z","sequence":1}""";
        const string trade3 =
            """{"type":"trade","symbol":"AAPL","price":213.50,"size":50,"timestamp":"2025-01-15T14:30:00.300Z","sequence":2}""";

        _source.ProcessTestMessage(trade1);
        _source.ProcessTestMessage(trade2);
        _source.ProcessTestMessage(trade3);

        var trades = _publisher.PublishedEvents.Where(e => e.Type == MarketEventType.Trade).ToArray();
        trades.Should().HaveCount(3, because: "each trade message for a subscribed symbol should produce one event");
    }

    // -------------------------------------------------------------------------
    // Quote pipeline
    // -------------------------------------------------------------------------

    [Fact]
    public void QuoteMessage_RoutesToPublisherWithCorrectFields()
    {
        const string json =
            """{"type":"quote","symbol":"AAPL","bidPrice":213.44,"bidSize":300,"askPrice":213.46,"askSize":200,"timestamp":"2025-01-15T14:30:00.456Z","bidExchange":"NYSE","askExchange":"ARCA"}""";

        _source.ProcessTestMessage(json);

        var events = _publisher.PublishedEvents.Where(e => e.Type == MarketEventType.BboQuote).ToArray();
        events.Should().NotBeEmpty(because: "a quote message for a subscribed symbol should produce a BboQuote event");

        var quote = events[0].Payload as BboQuotePayload;
        quote.Should().NotBeNull();
        quote!.Symbol.Should().Be("AAPL");
        quote.BidPrice.Should().Be(213.44m);
        quote.AskPrice.Should().Be(213.46m);
        quote.BidSize.Should().Be(300);
        quote.AskSize.Should().Be(200);
    }

    [Fact]
    public void QuoteMessage_SpreadPreserved()
    {
        const string json =
            """{"type":"quote","symbol":"AAPL","bidPrice":100.10,"bidSize":100,"askPrice":100.12,"askSize":50,"timestamp":"2025-01-15T14:30:00Z"}""";

        _source.ProcessTestMessage(json);

        var events = _publisher.PublishedEvents.Where(e => e.Type == MarketEventType.BboQuote).ToArray();
        events.Should().NotBeEmpty();

        var quote = events[0].Payload as BboQuotePayload;
        (quote!.AskPrice - quote.BidPrice).Should().Be(0.02m,
            because: "ask–bid spread must be preserved through the pipeline");
    }

    // -------------------------------------------------------------------------
    // Depth pipeline
    // -------------------------------------------------------------------------

    [Fact]
    public void DepthMessage_Add_RoutesToPublisher()
    {
        const string json =
            """{"type":"depth","symbol":"AAPL","operation":"add","side":"bid","level":0,"price":213.43,"size":500,"timestamp":"2025-01-15T14:30:00Z"}""";

        _source.ProcessTestMessage(json);

        var events = _publisher.PublishedEvents.Where(e => e.Type == MarketEventType.L2Snapshot).ToArray();
        events.Should().NotBeEmpty(because: "a depth 'add' message for a subscribed symbol should produce an L2Snapshot event");
    }

    [Fact]
    public void DepthMessage_DeleteOnEmptyBook_EmitsDepthIntegrityEvent()
    {
        // Deleting a position from an empty order book is a structural integrity error.
        // The collector should emit a DepthIntegrity event (not crash and not silently drop it).
        const string json =
            """{"type":"depth","symbol":"AAPL","operation":"remove","side":"ask","level":1,"price":213.47,"size":0,"timestamp":"2025-01-15T14:30:00Z"}""";

        _source.ProcessTestMessage(json);

        var events = _publisher.PublishedEvents
            .Where(e => e.Type == MarketEventType.Integrity)
            .ToArray();
        events.Should().NotBeEmpty(because: "removing a non-existent level should be flagged as a depth integrity violation");
    }

    // -------------------------------------------------------------------------
    // Robustness / error handling
    // -------------------------------------------------------------------------

    [Fact]
    public void HeartbeatMessage_DoesNotEmitAnyEvent()
    {
        const string json = """{"type":"heartbeat","timestamp":"2025-01-15T14:30:00Z"}""";

        _source.ProcessTestMessage(json);

        _publisher.PublishedEvents.Should().BeEmpty(
            because: "heartbeat frames carry no market data and must not produce any events");
    }

    [Fact]
    public void ErrorMessage_DoesNotThrow()
    {
        const string json = """{"type":"error","message":"authentication failed"}""";

        var act = () => _source.ProcessTestMessage(json);

        act.Should().NotThrow(because: "error frames should be logged and silently discarded");
    }

    [Fact]
    public void MalformedJson_DoesNotThrow()
    {
        const string json = "{this is not valid JSON}";

        var act = () => _source.ProcessTestMessage(json);

        act.Should().NotThrow(because: "the message processor must swallow parse errors to keep the feed alive");
    }

    [Fact]
    public void UnknownMessageType_DoesNotThrowOrEmitEvent()
    {
        const string json = """{"type":"custom_extension","data":{"foo":42}}""";

        var act = () => _source.ProcessTestMessage(json);

        act.Should().NotThrow();
        _publisher.PublishedEvents.Should().BeEmpty(
            because: "unrecognised message types must be silently ignored");
    }
}
