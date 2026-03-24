using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.NYSE;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Integration tests that feed parsed NYSE TAQ golden-sample records through the
/// <see cref="NyseNationalTradesCsvParser"/> → <see cref="TradeDataCollector"/> →
/// <see cref="TestMarketEventPublisher"/> pipeline.
///
/// These tests verify that field values present in the official NYSE TAQ file
/// (extracted from EQY_US_TAQ_NATIONAL_TRADES_20231002.gz) survive the full
/// parsing and collection path without distortion.
///
/// The test data strings are identical to the golden samples in
/// <see cref="NyseNationalTradesCsvParserTests"/>; they are repeated here for
/// test-isolation so this file can be read and maintained independently.
/// </summary>
public sealed class NyseTaqCollectorIntegrationTests
{
    // -------------------------------------------------------------------------
    // Golden-sample CSV lines — verbatim from NYSE TAQ (2023-10-02)
    // -------------------------------------------------------------------------

    private const string AaplRegularMarket =
        "220,79081,09:31:56.104292761,AAPL,10,4694,172.33,100,@, , , ";

    private const string AaplPreMarketFinalCorrection =
        "220,64802,09:28:00.060475084,AAPL,5,239,171.08,3,@,F,T,I";

    private const string AaplEarlyPreMarket =
        "220,64834,09:28:06.132261636,AAPL,6,338,171.04,2,@,F,T,I";

    private const string SpyPreMarket =
        "220,62239,07:36:47.502276617,SPY,4,695,427.61,100,@,F,T, ";

    private const string SpyAfterHours =
        "220,759005,18:14:58.433196581,SPY,742,1171667,427.56,300,@, ,T, ";

    private const string TslaEarlyPreMarket =
        "220,61928,07:00:18.892107762,TSLA,5,40,251.2,100,@,F,T, ";

    private const string AvtxFractionalPenny =
        "220,95185,09:44:21.542666242,AVTX,374,68755,.1185,15703,@,F, , ";

    private static readonly DateOnly SessionDate = new(2023, 10, 2);

    // -------------------------------------------------------------------------
    // Infrastructure
    // -------------------------------------------------------------------------

    private readonly TestMarketEventPublisher _publisher;
    private readonly TradeDataCollector _collector;

    public NyseTaqCollectorIntegrationTests()
    {
        _publisher = new TestMarketEventPublisher();
        _collector = new TradeDataCollector(_publisher);
    }

    // -------------------------------------------------------------------------
    // Helper: convert a parsed TAQ record to a MarketTradeUpdate, mirroring the
    // mapping in NyseMarketDataClient.OnTrade (trade.SourceId → StreamId,
    // trade.Exchange → Venue).
    // -------------------------------------------------------------------------

    private static MarketTradeUpdate ToUpdate(NyseTaqTradeRecord record)
    {
        var trade = NyseNationalTradesCsvParser.ToRealtimeTrade(record);
        return new MarketTradeUpdate(
            Timestamp: trade.Timestamp,
            Symbol: trade.Symbol,
            Price: trade.Price,
            Size: trade.Size,
            Aggressor: trade.Side,
            SequenceNumber: trade.SequenceNumber ?? 0,
            StreamId: trade.SourceId,
            Venue: trade.Exchange,
            RawConditions: !string.IsNullOrWhiteSpace(trade.Conditions)
                ? trade.Conditions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null);
    }

    // -------------------------------------------------------------------------
    // Test 1 – Regular-market AAPL: all key fields survive to the Trade event
    // -------------------------------------------------------------------------

    [Fact]
    public void TaqRegularMarket_TradeEventContainsCorrectPriceSizeAndExchange()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AaplRegularMarket, SessionDate)!;
        _collector.OnTrade(ToUpdate(record));

        var tradeEvent = _publisher.PublishedEvents.Single(e => e.Type == MarketEventType.Trade);
        var trade = tradeEvent.Payload.Should().BeOfType<Trade>().Subject;

        trade.Symbol.Should().Be("AAPL");
        trade.Price.Should().Be(172.33m, because: "regular-market AAPL price must be preserved exactly");
        trade.Size.Should().Be(100);
        trade.Venue.Should().Be("AMEX",
            because: "exchange code 10 maps to AMEX via NyseNationalTradesCsvParser.MapExchangeCode");
        trade.SequenceNumber.Should().Be(79081,
            because: "the TAQ global sequence number must propagate through the collector");
    }

    // -------------------------------------------------------------------------
    // Test 2 – Source ID "nyse-taq" must appear as StreamId in the Trade event
    // -------------------------------------------------------------------------

    [Fact]
    public void TaqTrade_SourceIdIsNyseTaq_AppearsAsStreamIdOnPublishedTrade()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AaplRegularMarket, SessionDate)!;
        _collector.OnTrade(ToUpdate(record));

        var trade = _publisher.PublishedEvents
            .Single(e => e.Type == MarketEventType.Trade)
            .Payload.Should().BeOfType<Trade>().Subject;

        trade.StreamId.Should().Be("nyse-taq",
            because: "ToRealtimeTrade sets SourceId=\"nyse-taq\" which the client maps to StreamId");
    }

    // -------------------------------------------------------------------------
    // Test 3 – Pre-market timestamp does not cause the collector to reject the trade
    // -------------------------------------------------------------------------

    [Fact]
    public void TaqPreMarketTrade_CollectorAcceptsAndPublishesTradeEvent()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AaplPreMarketFinalCorrection, SessionDate)!;
        record.IsRegularHours.Should().BeFalse("09:28:00 is before the 09:30 open — sanity check");

        _collector.OnTrade(ToUpdate(record));

        _publisher.PublishedEvents
            .Where(e => e.Type == MarketEventType.Trade)
            .Should().ContainSingle(
                because: "the TradeDataCollector does not filter by session time; pre-market trades must be accepted");
    }

    // -------------------------------------------------------------------------
    // Test 4 – After-hours timestamp does not cause the collector to reject the trade
    // -------------------------------------------------------------------------

    [Fact]
    public void TaqAfterHoursTrade_CollectorAcceptsAndPublishesTradeEvent()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(SpyAfterHours, SessionDate)!;
        record.IsRegularHours.Should().BeFalse("18:14 is after the 16:00 close — sanity check");

        _collector.OnTrade(ToUpdate(record));

        _publisher.PublishedEvents
            .Where(e => e.Type == MarketEventType.Trade)
            .Should().ContainSingle(
                because: "after-hours trades must be accepted; the collector has no session-time filter");
    }

    // -------------------------------------------------------------------------
    // Test 5 – Fractional-penny price (AVTX 0.1185) survives without precision loss
    // -------------------------------------------------------------------------

    [Fact]
    public void TaqFractionalPenny_PriceAndVwapPreservedThroughCollector()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AvtxFractionalPenny, SessionDate)!;
        _collector.OnTrade(ToUpdate(record));

        var trade = _publisher.PublishedEvents
            .Single(e => e.Type == MarketEventType.Trade)
            .Payload.Should().BeOfType<Trade>().Subject;

        trade.Price.Should().Be(0.1185m,
            because: "the fractional-penny price '.1185' must survive parsing AND the collector path without rounding");
        trade.Size.Should().Be(15703);

        var stats = _publisher.PublishedEvents
            .Single(e => e.Type == MarketEventType.OrderFlow)
            .Payload.Should().BeOfType<OrderFlowStatistics>().Subject;

        stats.VWAP.Should().Be(0.1185m,
            because: "with a single trade the rolling-window VWAP must equal the trade price");
    }

    // -------------------------------------------------------------------------
    // Test 6 – Single trade: VWAP equals the trade price
    // -------------------------------------------------------------------------

    [Fact]
    public void TaqSingleTrade_VwapEqualsTradePrice()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AaplRegularMarket, SessionDate)!;
        _collector.OnTrade(ToUpdate(record));

        var stats = _publisher.PublishedEvents
            .Single(e => e.Type == MarketEventType.OrderFlow)
            .Payload.Should().BeOfType<OrderFlowStatistics>().Subject;

        stats.VWAP.Should().Be(172.33m,
            because: "VWAP for a single-trade window equals the trade price");
    }

    // -------------------------------------------------------------------------
    // Test 7 – Two AAPL pre-market trades within 6 seconds on the same stream:
    //          VWAP must be the price-volume-weighted average of both trades.
    //
    // Data (from golden samples):
    //   AAPL @ 09:28:00 — 3 shares @ 171.08  (AaplPreMarketFinalCorrection)
    //   AAPL @ 09:28:06 — 2 shares @ 171.04  (AaplEarlyPreMarket)
    //
    // ToRealtimeTrade maps these to different Venues (NYSE vs EDGX) because the
    // exchange codes differ (5 vs 6).  To exercise a multi-trade VWAP within the
    // same rolling window, both updates are explicitly wired to the same stream
    // (StreamId="nyse-taq", Venue="NYSE") using the TAQ-derived prices.
    //
    // Expected VWAP = (171.08 × 3 + 171.04 × 2) / (3 + 2)
    //              = (513.24 + 342.08) / 5
    //              = 855.32 / 5
    //              = 171.064
    // -------------------------------------------------------------------------

    [Fact]
    public void TaqTwoPreMarketTrades_WithinRollingWindow_VwapIsWeightedAverage()
    {
        var record1 = NyseNationalTradesCsvParser.ParseTradeLine(AaplPreMarketFinalCorrection, SessionDate)!;
        var record2 = NyseNationalTradesCsvParser.ParseTradeLine(AaplEarlyPreMarket, SessionDate)!;

        // Both trades on the same logical stream so their state is accumulated together.
        var update1 = new MarketTradeUpdate(
            Timestamp: record1.Timestamp,
            Symbol: record1.Symbol,
            Price: record1.Price,
            Size: record1.Volume,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: record1.GlobalSequenceNumber,
            StreamId: "nyse-taq",
            Venue: "NYSE");

        var update2 = new MarketTradeUpdate(
            Timestamp: record2.Timestamp,
            Symbol: record2.Symbol,
            Price: record2.Price,
            Size: record2.Volume,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: record2.GlobalSequenceNumber,
            StreamId: "nyse-taq",
            Venue: "NYSE");

        _collector.OnTrade(update1);
        _collector.OnTrade(update2);

        // Second update triggers a sequence gap (64802 → expected 64803, received 64834).
        // The gap emits an IntegrityEvent but the trade is still accepted.
        var orderFlowEvents = _publisher.PublishedEvents
            .Where(e => e.Type == MarketEventType.OrderFlow)
            .ToList();
        orderFlowEvents.Should().HaveCount(2,
            because: "each accepted trade emits one OrderFlowStatistics event");

        var stats = orderFlowEvents.Last().Payload.Should().BeOfType<OrderFlowStatistics>().Subject;

        const decimal expectedVwap = (171.08m * 3 + 171.04m * 2) / (3 + 2); // 171.064
        stats.VWAP.Should().Be(expectedVwap,
            because: "both trades fall within the 10-second rolling window and must be included in VWAP");
        stats.UnknownVolume.Should().Be(5, because: "both trades have AggressorSide.Unknown");
    }

    // -------------------------------------------------------------------------
    // Test 8 – Multi-symbol batch: sequence tracking is isolated per symbol,
    //          so TAQ global sequence numbers that are non-monotonic across symbols
    //          must not trigger cross-symbol IntegrityEvents.
    //
    //  File order (by TAQ global seq):
    //    SPY   seq 62239  (SpyPreMarket)
    //    TSLA  seq 61928  (TslaEarlyPreMarket — LOWER than SPY's; different symbol)
    //    AAPL  seq 64802  (AaplPreMarketFinalCorrection)
    // -------------------------------------------------------------------------

    [Fact]
    public void TaqMultiSymbol_PerSymbolSequenceIsolation_NoCrossSymbolIntegrityEvents()
    {
        var spyRecord = NyseNationalTradesCsvParser.ParseTradeLine(SpyPreMarket, SessionDate)!;
        var tslaRecord = NyseNationalTradesCsvParser.ParseTradeLine(TslaEarlyPreMarket, SessionDate)!;
        var aaplRecord = NyseNationalTradesCsvParser.ParseTradeLine(AaplPreMarketFinalCorrection, SessionDate)!;

        _collector.OnTrade(ToUpdate(spyRecord));
        _collector.OnTrade(ToUpdate(tslaRecord));
        _collector.OnTrade(ToUpdate(aaplRecord));

        var integrityEvents = _publisher.PublishedEvents
            .Where(e => e.Type == MarketEventType.Integrity)
            .ToList();

        integrityEvents.Should().BeEmpty(
            because: "sequence continuity is tracked per (symbol, streamId, venue) tuple; "
                   + "TSLA's lower global sequence number (61928 < SPY's 62239) is on a completely "
                   + "separate sequence track and must not produce a cross-symbol IntegrityEvent");

        // All three trades must be accepted
        _publisher.PublishedEvents
            .Count(e => e.Type == MarketEventType.Trade)
            .Should().Be(3);
    }

    // -------------------------------------------------------------------------
    // Test 9 – Exchange mnemonics are preserved through the full path
    // -------------------------------------------------------------------------

    [Fact]
    public void TaqExchangeCodes_MappedMnemonicsReachPublishedTrade()
    {
        // SPY pre-market is on exchange code 4 = "ARCA"
        var spyRecord = NyseNationalTradesCsvParser.ParseTradeLine(SpyPreMarket, SessionDate)!;
        _collector.OnTrade(ToUpdate(spyRecord));

        var spyTrade = _publisher.PublishedEvents
            .Single(e => e.Type == MarketEventType.Trade)
            .Payload.Should().BeOfType<Trade>().Subject;

        spyTrade.Venue.Should().Be("ARCA",
            because: "SPY pre-market record has exchange code 4, which maps to NYSE Arca (\"ARCA\")");
        _publisher.Clear();

        // TSLA early pre-market is on exchange code 5 = "NYSE"
        var tslaRecord = NyseNationalTradesCsvParser.ParseTradeLine(TslaEarlyPreMarket, SessionDate)!;
        _collector.OnTrade(ToUpdate(tslaRecord));

        var tslaTrade = _publisher.PublishedEvents
            .Single(e => e.Type == MarketEventType.Trade)
            .Payload.Should().BeOfType<Trade>().Subject;

        tslaTrade.Venue.Should().Be("NYSE",
            because: "TSLA early pre-market record has exchange code 5, which maps to NYSE");
    }

    // -------------------------------------------------------------------------
    // Test 10 – All seven golden samples pass through the collector without exceptions
    // -------------------------------------------------------------------------

    [Fact]
    public void TaqAllGoldenSamples_FeedThroughCollectorWithoutException()
    {
        var csvLines = new[]
        {
            AaplRegularMarket,
            AaplPreMarketFinalCorrection,
            AaplEarlyPreMarket,
            SpyPreMarket,
            SpyAfterHours,
            TslaEarlyPreMarket,
            AvtxFractionalPenny,
        };

        var act = () =>
        {
            foreach (var line in csvLines)
            {
                var record = NyseNationalTradesCsvParser.ParseTradeLine(line, SessionDate)!;
                _collector.OnTrade(ToUpdate(record));
            }
        };

        act.Should().NotThrow(
            because: "all seven golden-sample TAQ records are valid and must be accepted by the collector");

        _publisher.PublishedEvents
            .Count(e => e.Type == MarketEventType.Trade)
            .Should().Be(7,
                because: "each of the seven TAQ records should produce exactly one Trade event");
    }
}
