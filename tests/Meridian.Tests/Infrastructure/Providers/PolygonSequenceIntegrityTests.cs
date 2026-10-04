using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Providers;

/// <summary>
/// Regression tests for Polygon sequence integrity and provenance: the adapter must pass
/// Polygon's own sequence number ("q") through when the feed supplies one, must never
/// fabricate a client-side sequence, and must stamp POLYGON — not a defaulted vendor —
/// on every event it produces.
/// </summary>
public class PolygonSequenceIntegrityTests
{
    private readonly TestMarketEventPublisher _publisher = new();
    private readonly TradeDataCollector _tradeCollector;
    private readonly QuoteCollector _quoteCollector;
    private IReadOnlyList<MarketEvent> _publishedEvents => _publisher.PublishedEvents;

    public PolygonSequenceIntegrityTests()
    {
        _tradeCollector = new TradeDataCollector(_publisher);
        _quoteCollector = new QuoteCollector(_publisher);
    }

    private PolygonMarketDataClient CreateClient(PolygonOptions? options = null)
        => new(_publisher, _tradeCollector, _quoteCollector, options);

    [Fact]
    public void ProcessTrade_WithProviderSequence_PassesRealSequenceAndPolygonSource()
    {
        var client = CreateClient();
        client.SubscribeTrades(new SymbolConfig("AAPL"));

        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.25,"s":100,"t":1704067200000,"i":"trade-1","x":4,"q":98765}]""");

        var tradeEvt = _publishedEvents.Single(e => e.Type == MarketEventType.Trade);
        tradeEvt.Source.Should().Be(MarketDataSources.Polygon);
        tradeEvt.Sequence.Should().Be(98765, "Polygon's own \"q\" sequence must be preserved, not replaced by a fabricated counter");
        var trade = tradeEvt.Payload.Should().BeOfType<Trade>().Subject;
        trade.SequenceNumber.Should().Be(98765);
    }

    [Fact]
    public void ProcessTrade_WithoutProviderSequence_ReportsUnsequencedInsteadOfFabricating()
    {
        var client = CreateClient();
        client.SubscribeTrades(new SymbolConfig("AAPL"));

        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.25,"s":100,"t":1704067200000,"i":"trade-1","x":4}]""");
        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.30,"s":50,"t":1704067201000,"i":"trade-2","x":4}]""");

        var trades = _publishedEvents.Where(e => e.Type == MarketEventType.Trade).ToList();
        trades.Should().HaveCount(2, "unsequenced trades must flow, not be rejected against fictional continuity");
        trades.Should().OnlyContain(e => e.Sequence == 0, "absent provider sequences must stay 0, never a client-side Interlocked counter");
        _publishedEvents.Where(e => e.Type == MarketEventType.Integrity).Should().BeEmpty();
    }

    [Fact]
    public void ProcessTrade_SparseProviderSequences_DoNotFloodGapIntegrityEvents()
    {
        // Polygon documents "q" as unique and increasing per ticker but NOT sequential —
        // a jump between consecutive trades is interleaving, not data loss.
        var client = CreateClient();
        client.SubscribeTrades(new SymbolConfig("AAPL"));

        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.25,"s":100,"t":1704067200000,"i":"t1","x":4,"q":10}]""");
        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.30,"s":50,"t":1704067201000,"i":"t2","x":4,"q":95}]""");

        _publishedEvents.Where(e => e.Type == MarketEventType.Integrity).Should().BeEmpty();
        _publishedEvents.Where(e => e.Type == MarketEventType.Trade).Should().HaveCount(2);
    }

    [Fact]
    public void ProcessTrade_DuplicateTickerSequenceAcrossTradeIdsAndVenues_IsRejected()
    {
        var client = CreateClient();
        client.SubscribeTrades(new SymbolConfig("AAPL"));

        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.25,"s":100,"t":1704067200000,"i":"trade-1","x":4,"q":100}]""");
        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.30,"s":50,"t":1704067201000,"i":"trade-2","x":11,"q":100}]""");

        _publishedEvents.Where(e => e.Type == MarketEventType.Trade).Should().ContainSingle(
            "Polygon q is unique per ticker, so a duplicate cannot become a fresh stream through i or x");
        var integrity = _publishedEvents.Single(e => e.Type == MarketEventType.Integrity);
        integrity.Source.Should().Be(MarketDataSources.Polygon);
        integrity.Payload.Should().BeOfType<IntegrityEvent>().Which.Should().Match<IntegrityEvent>(evt =>
            evt.ErrorCode == 1002 &&
            evt.SequenceNumber == 100 &&
            evt.StreamId == "trade-2" &&
            evt.Venue == "EDGA");
    }

    [Fact]
    public void ProcessTrade_DecreasingTickerSequenceAcrossTradeIdsAndVenues_IsRejected()
    {
        var client = CreateClient();
        client.SubscribeTrades(new SymbolConfig("AAPL"));

        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.25,"s":100,"t":1704067200000,"i":"trade-1","x":4,"q":101}]""");
        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.30,"s":50,"t":1704067201000,"i":"trade-2","x":11,"q":99}]""");

        _publishedEvents.Where(e => e.Type == MarketEventType.Trade).Should().ContainSingle(
            "Polygon q must be compared across changing trade ids and execution venues for one ticker");
        var integrity = _publishedEvents.Single(e => e.Type == MarketEventType.Integrity);
        integrity.Source.Should().Be(MarketDataSources.Polygon);
        integrity.Payload.Should().BeOfType<IntegrityEvent>().Which.Should().Match<IntegrityEvent>(evt =>
            evt.ErrorCode == 1002 &&
            evt.Description.Contains("last 101, received 99", StringComparison.Ordinal) &&
            evt.StreamId == "trade-2" &&
            evt.Venue == "EDGA");
    }

    [Fact]
    public void ProcessTrade_SequenceResetsOnlyOnNextEasternTradingDate()
    {
        var client = CreateClient();
        client.SubscribeTrades(new SymbolConfig("AAPL"));

        // Both timestamps straddle midnight UTC but remain Jan 15 in New York (18:59/19:01 EST).
        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.25,"s":100,"t":1768521540000,"i":"trade-1","x":4,"q":100}]""");
        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.30,"s":50,"t":1768521660000,"i":"trade-2","x":4,"q":1}]""");

        // 14:30 UTC is 09:30 EST on Jan 16: q may restart for the new U.S. equities session.
        client.ProcessTestMessage(
            """[{"ev":"T","sym":"AAPL","p":150.35,"s":75,"t":1768573800000,"i":"trade-3","x":4,"q":1}]""");

        _publishedEvents.Where(e => e.Type == MarketEventType.Trade).Should().HaveCount(2,
            "UTC midnight is not a Polygon session boundary, but the next Eastern date is");
        var integrity = _publishedEvents.Where(e => e.Type == MarketEventType.Integrity)
            .Should().ContainSingle().Subject;
        integrity.Payload.Should().BeOfType<IntegrityEvent>().Which.Should().Match<IntegrityEvent>(evt =>
            evt.SequenceNumber == 1
            && evt.StreamId == "trade-2"
            && evt.Description.Contains("last 100, received 1", StringComparison.Ordinal));
    }

    [Fact]
    public void ProcessQuote_WithProviderSequence_PreservesItOnThePayload()
    {
        var client = CreateClient(new PolygonOptions(SubscribeQuotes: true));
        client.SubscribeMarketDepth(new SymbolConfig("AAPL")); // registers the "quotes" subscription (Polygon BBO)

        client.ProcessTestMessage(
            """[{"ev":"Q","sym":"AAPL","bp":150.20,"bs":100,"ap":150.25,"as":200,"t":1704067200000,"x":4,"q":31337}]""");

        var quoteEvt = _publishedEvents.Single(e => e.Type == MarketEventType.BboQuote);
        quoteEvt.Source.Should().Be(MarketDataSources.Polygon);
        var payload = quoteEvt.Payload.Should().BeOfType<BboQuotePayload>().Subject;
        payload.SequenceNumber.Should().Be(31337, "the QuoteCollector must not overwrite a provider-supplied quote sequence");
        payload.IsProviderSequence.Should().BeTrue();
    }

    [Fact]
    public void ProcessQuote_WithoutProviderSequence_FallsBackToLocalCounterMarkedAsLocal()
    {
        var client = CreateClient(new PolygonOptions(SubscribeQuotes: true));
        client.SubscribeMarketDepth(new SymbolConfig("AAPL")); // registers the "quotes" subscription (Polygon BBO)

        client.ProcessTestMessage(
            """[{"ev":"Q","sym":"AAPL","bp":150.20,"bs":100,"ap":150.25,"as":200,"t":1704067200000,"x":4}]""");

        var payload = _publishedEvents.Single(e => e.Type == MarketEventType.BboQuote)
            .Payload.Should().BeOfType<BboQuotePayload>().Subject;
        payload.SequenceNumber.Should().Be(1);
        payload.IsProviderSequence.Should().BeFalse();
    }

    [Fact]
    public void ProcessAggregate_DoesNotFabricateASequence_AndStampsPolygon()
    {
        var client = CreateClient(new PolygonOptions(SubscribeAggregates: true));
        client.SubscribeAggregates(new SymbolConfig("AAPL"));

        client.ProcessTestMessage(
            """[{"ev":"AM","sym":"AAPL","o":150.0,"h":150.5,"l":149.8,"c":150.4,"v":1000,"vw":150.3,"s":1704067200000,"e":1704067260000,"n":50}]""");
        client.ProcessTestMessage(
            """[{"ev":"AM","sym":"AAPL","o":150.4,"h":150.9,"l":150.2,"c":150.8,"v":900,"vw":150.6,"s":1704067260000,"e":1704067320000,"n":42}]""");

        var bars = _publishedEvents.Where(e => e.Type == MarketEventType.AggregateBar).ToList();
        bars.Should().HaveCount(2);
        bars.Should().OnlyContain(e => e.Source == MarketDataSources.Polygon);
        bars.Should().OnlyContain(e => e.Sequence == 0,
            "aggregate identity is the bar window; a fabricated Interlocked counter would defeat replay-stable dedup");
    }
}
