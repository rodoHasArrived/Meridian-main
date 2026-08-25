using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Provenance regression tests for the ingress seam: collectors are shared singletons that
/// serve every active adapter, so every published MarketEvent must carry the real per-event
/// provider identity stamped at the adapter origin — never a hardcoded vendor default —
/// and sourceless updates must fail loudly instead of being silently misattributed.
/// </summary>
public class CollectorSourceProvenanceTests
{
    private readonly TestMarketEventPublisher _publisher = new();
    private IReadOnlyList<MarketEvent> _publishedEvents => _publisher.PublishedEvents;

    // ------------------------------------------------------------------ //
    //  MarketEvent no-default enforcement                                 //
    // ------------------------------------------------------------------ //

    [Fact]
    public void MarketEvent_DirectConstructionWithoutSource_IsUnknownNotIb()
    {
        // The record's positional default must be the honest UNKNOWN sentinel — the old
        // "IB" default silently misattributed every provider's data to Interactive Brokers.
        var evt = new MarketEvent(
            DateTimeOffset.UtcNow,
            "SPY",
            MarketEventType.Trade,
            new Trade(DateTimeOffset.UtcNow, "SPY", 100m, 10, AggressorSide.Buy, 1));

        evt.Source.Should().Be(MarketDataSources.Unknown);
        evt.Source.Should().NotBe("IB");
    }

    // ------------------------------------------------------------------ //
    //  TradeDataCollector                                                 //
    // ------------------------------------------------------------------ //

    [Fact]
    public void TradeCollector_PolygonFedUpdate_PublishesPolygonSourcedEvents()
    {
        var collector = new TradeDataCollector(_publisher);

        collector.OnTrade(CreateTrade("AAPL", seq: 101, source: MarketDataSources.Polygon));

        _publishedEvents.Should().HaveCount(2);
        _publishedEvents[0].Type.Should().Be(MarketEventType.Trade);
        _publishedEvents[0].Source.Should().Be("POLYGON");
        _publishedEvents[1].Type.Should().Be(MarketEventType.OrderFlow);
        _publishedEvents[1].Source.Should().Be("POLYGON");
    }

    [Fact]
    public void TradeCollector_PerEventSource_AttributesEachProviderCorrectly()
    {
        // One shared collector instance serving two adapters concurrently — the exact
        // topology that made a per-collector default source impossible.
        var collector = new TradeDataCollector(_publisher);

        collector.OnTrade(CreateTrade("AAPL", seq: 1, source: MarketDataSources.Polygon, streamId: "POLYGON"));
        collector.OnTrade(CreateTrade("AAPL", seq: 1, source: MarketDataSources.Ib, streamId: "IB-TBT"));

        var trades = _publishedEvents.Where(e => e.Type == MarketEventType.Trade).ToList();
        trades.Should().HaveCount(2);
        trades[0].Source.Should().Be("POLYGON");
        trades[1].Source.Should().Be("IB");
    }

    [Fact]
    public void TradeCollector_MissingSource_RejectsWithMissingSourceIntegrityEvent()
    {
        var collector = new TradeDataCollector(_publisher);

        collector.OnTrade(CreateTrade("SPY", seq: 1, source: null));

        _publishedEvents.Should().HaveCount(1);
        _publishedEvents[0].Type.Should().Be(MarketEventType.Integrity);
        _publishedEvents[0].Source.Should().Be(MarketDataSources.Unknown);
        var integrity = _publishedEvents[0].Payload.Should().BeOfType<IntegrityEvent>().Subject;
        integrity.ErrorCode.Should().Be(1008);
        integrity.Description.Should().Contain("without a provider source");
    }

    [Fact]
    public void TradeCollector_UnsequencedStream_AcceptsEveryTradeWithoutFalseIntegrityEvents()
    {
        // Sequence 0 means "the provider does not sequence this stream" (e.g. IB tick-by-tick).
        // Continuity checks must be skipped rather than rejecting everything after the first
        // trade as out-of-order — and no sequence may be fabricated to paper over it.
        var collector = new TradeDataCollector(_publisher);

        collector.OnTrade(CreateTrade("SPY", seq: 0, source: MarketDataSources.Ib));
        collector.OnTrade(CreateTrade("SPY", seq: 0, source: MarketDataSources.Ib));
        collector.OnTrade(CreateTrade("SPY", seq: 0, source: MarketDataSources.Ib));

        _publishedEvents.Where(e => e.Type == MarketEventType.Integrity).Should().BeEmpty();
        _publishedEvents.Where(e => e.Type == MarketEventType.Trade).Should().HaveCount(3);
    }

    [Fact]
    public void TradeCollector_NonContiguousProviderSequence_DoesNotReportGapsButStillRejectsDuplicates()
    {
        // Polygon's per-ticker "q" is unique and increasing but explicitly non-dense:
        // jumps are normal interleaving, not data loss — but a replayed duplicate is real.
        var collector = new TradeDataCollector(_publisher);

        collector.OnTrade(CreateTrade("AAPL", seq: 10, source: MarketDataSources.Polygon, contiguous: false));
        collector.OnTrade(CreateTrade("AAPL", seq: 25, source: MarketDataSources.Polygon, contiguous: false));

        _publishedEvents.Where(e => e.Type == MarketEventType.Integrity).Should().BeEmpty();
        _publishedEvents.Where(e => e.Type == MarketEventType.Trade).Should().HaveCount(2);

        _publisher.Clear();
        collector.OnTrade(CreateTrade("AAPL", seq: 25, source: MarketDataSources.Polygon, contiguous: false));

        _publishedEvents.Should().HaveCount(1);
        _publishedEvents[0].Type.Should().Be(MarketEventType.Integrity);
    }

    [Fact]
    public void TradeCollector_ContiguousSequenceGap_StillReportsGapIntegrity()
    {
        // Dense-sequence feeds keep full gap detection.
        var collector = new TradeDataCollector(_publisher);

        collector.OnTrade(CreateTrade("SPY", seq: 1, source: MarketDataSources.Nyse));
        _publisher.Clear();
        collector.OnTrade(CreateTrade("SPY", seq: 5, source: MarketDataSources.Nyse));

        _publishedEvents.Should().HaveCount(3);
        _publishedEvents[0].Type.Should().Be(MarketEventType.Integrity);
        _publishedEvents[0].Source.Should().Be("NYSE");
    }

    // ------------------------------------------------------------------ //
    //  QuoteCollector                                                     //
    // ------------------------------------------------------------------ //

    [Fact]
    public void QuoteCollector_StampsRealSourceOnPublishedQuote()
    {
        var collector = new QuoteCollector(_publisher);

        collector.OnQuote(CreateQuote("AAPL", source: MarketDataSources.Polygon));

        _publishedEvents.Should().HaveCount(1);
        _publishedEvents[0].Type.Should().Be(MarketEventType.BboQuote);
        _publishedEvents[0].Source.Should().Be("POLYGON");
    }

    [Fact]
    public void QuoteCollector_MissingSource_RejectsWithoutUpsertingState()
    {
        var collector = new QuoteCollector(_publisher);

        collector.OnQuote(CreateQuote("SPY", source: null));

        _publishedEvents.Should().HaveCount(1);
        _publishedEvents[0].Type.Should().Be(MarketEventType.Integrity);
        _publishedEvents[0].Source.Should().Be(MarketDataSources.Unknown);
        var integrity = _publishedEvents[0].Payload.Should().BeOfType<IntegrityEvent>().Subject;
        integrity.ErrorCode.Should().Be(1008);

        collector.TryGet("SPY", out _).Should().BeFalse("a rejected quote must not enter BBO state");
    }

    [Fact]
    public void QuoteCollector_ProviderSuppliedSequence_IsPreservedNotOverwritten()
    {
        var collector = new QuoteCollector(_publisher);

        collector.OnQuote(CreateQuote("AAPL", source: MarketDataSources.Polygon, sequence: 4242));

        var payload = _publishedEvents.Single().Payload.Should().BeOfType<BboQuotePayload>().Subject;
        payload.SequenceNumber.Should().Be(4242, "provider quote sequences must survive to the payload so gap detection stays meaningful");
        payload.IsProviderSequence.Should().BeTrue();
        _publishedEvents.Single().Sequence.Should().Be(4242);
    }

    [Fact]
    public void QuoteCollector_NoProviderSequence_FallsBackToLocalCounterAndSaysSo()
    {
        var collector = new QuoteCollector(_publisher);

        collector.OnQuote(CreateQuote("AAPL", source: MarketDataSources.Robinhood, sequence: null));
        collector.OnQuote(CreateQuote("AAPL", source: MarketDataSources.Robinhood, sequence: null));

        var payloads = _publishedEvents.Select(e => e.Payload).OfType<BboQuotePayload>().ToList();
        payloads.Should().HaveCount(2);
        payloads[0].SequenceNumber.Should().Be(1);
        payloads[1].SequenceNumber.Should().Be(2);
        payloads.Should().OnlyContain(p => !p.IsProviderSequence, "locally assigned sequences must not masquerade as provider sequences");
    }

    [Fact]
    public void QuoteCollector_MixedSequenceRegimes_PreservesProviderAndFallsBackHonestly()
    {
        var collector = new QuoteCollector(_publisher);

        collector.OnQuote(CreateQuote("AAPL", source: MarketDataSources.Polygon, sequence: 900));
        collector.OnQuote(CreateQuote("AAPL", source: MarketDataSources.Robinhood, sequence: null));

        var payloads = _publishedEvents.Select(e => e.Payload).OfType<BboQuotePayload>().ToList();
        payloads[0].SequenceNumber.Should().Be(900);
        payloads[0].IsProviderSequence.Should().BeTrue();
        payloads[1].IsProviderSequence.Should().BeFalse();
    }

    // ------------------------------------------------------------------ //
    //  MarketDepthCollector                                               //
    // ------------------------------------------------------------------ //

    [Fact]
    public void DepthCollector_StampsRealSourceOnPublishedSnapshot()
    {
        var collector = new MarketDepthCollector(_publisher, requireExplicitSubscription: false);

        collector.OnDepth(CreateDepth("AAPL", source: MarketDataSources.Nyse));

        _publishedEvents.Should().HaveCount(1);
        _publishedEvents[0].Type.Should().Be(MarketEventType.L2Snapshot);
        _publishedEvents[0].Source.Should().Be("NYSE");
    }

    [Fact]
    public void DepthCollector_MissingSource_RejectsWithMissingSourceIntegrityEvent()
    {
        var collector = new MarketDepthCollector(_publisher, requireExplicitSubscription: false);

        collector.OnDepth(CreateDepth("AAPL", source: null));

        _publishedEvents.Should().HaveCount(1);
        _publishedEvents[0].Type.Should().Be(MarketEventType.Integrity);
        _publishedEvents[0].Source.Should().Be(MarketDataSources.Unknown);
        var integrity = _publishedEvents[0].Payload.Should().BeOfType<IntegrityEvent>().Subject;
        integrity.ErrorCode.Should().Be(1008);
    }

    // ------------------------------------------------------------------ //
    //  L3OrderBookCollector                                               //
    // ------------------------------------------------------------------ //

    [Fact]
    public void L3Collector_RequiresARealSourceAtConstruction()
    {
        var actBlank = () => new L3OrderBookCollector(_publisher, "  ");
        actBlank.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void L3Collector_DerivedL2Snapshot_CarriesTheProviderSourceNotADefault()
    {
        var collector = new L3OrderBookCollector(_publisher, MarketDataSources.Nyse, requireExplicitSubscription: false);

        collector.OnOrderAdd(new OrderAdd(
            PriorityTimestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            OrderId: "o-1",
            Side: OrderSide.Buy,
            Price: 100m,
            DisplayedSize: 10,
            SequenceNumber: 1));

        _publishedEvents.Should().HaveCount(2);
        _publishedEvents[0].Type.Should().Be(MarketEventType.OrderAdd);
        _publishedEvents[0].Source.Should().Be("NYSE");
        _publishedEvents[1].Type.Should().Be(MarketEventType.L2Snapshot);
        _publishedEvents[1].Source.Should().Be("NYSE", "the derived L2 must carry the same provider identity as the L3 events it came from");
    }

    // ------------------------------------------------------------------ //
    //  Helpers                                                            //
    // ------------------------------------------------------------------ //

    private static MarketTradeUpdate CreateTrade(
        string symbol,
        long seq,
        string? source,
        string? streamId = "STREAM",
        bool contiguous = true)
        => new(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: seq,
            StreamId: streamId,
            Venue: "XNAS",
            Source: source,
            SequenceIsContiguous: contiguous);

    private static MarketQuoteUpdate CreateQuote(
        string symbol,
        string? source,
        long? sequence = null)
        => new(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            BidPrice: 100m,
            BidSize: 100,
            AskPrice: 100.05m,
            AskSize: 200,
            SequenceNumber: sequence,
            StreamId: "STREAM",
            Venue: "XNAS",
            Source: source);

    private static MarketDepthUpdate CreateDepth(string symbol, string? source)
        => new(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Position: 0,
            Operation: DepthOperation.Insert,
            Side: OrderBookSide.Bid,
            Price: 100m,
            Size: 10,
            Source: source);
}
