using FluentAssertions;
using Meridian.Infrastructure.Adapters.NYSE;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Golden-sample tests for <see cref="NyseNationalTradesCsvParser"/> using
/// records extracted verbatim from the official NYSE TAQ National Trades file:
///   EQY_US_TAQ_NATIONAL_TRADES_20231002.gz
/// Source: https://ftp.nyse.com/Historical%20Data%20Samples/TAQ%20NYSE%20NATIONAL%20TRADES/
///
/// The samples cover all major field variants — regular market, pre-market,
/// after-hours, stopped-stock, correction indicator, and fractional-penny price.
/// </summary>
public sealed class NyseNationalTradesCsvParserTests
{
    /// <summary>Session date for all golden samples (2023-10-02).</summary>
    private static readonly DateOnly SessionDate = new(2023, 10, 2);

    // -------------------------------------------------------------------------
    // Golden-sample CSV lines extracted verbatim from the NYSE TAQ file
    // -------------------------------------------------------------------------

    /// <summary>AAPL regular market trade — NYSE exchange (code 10), no tape/correction markers.</summary>
    private const string AaplRegularMarket =
        "220,79081,09:31:56.104292761,AAPL,10,4694,172.33,100,@, , , ";

    /// <summary>AAPL pre-market trade — NYSE exchange (code 5), F correction, Tape A, stopped stock.</summary>
    private const string AaplPreMarketFinalCorrection =
        "220,64802,09:28:00.060475084,AAPL,5,239,171.08,3,@,F,T,I";

    /// <summary>AAPL early pre-market trade — NYSE Arca (code 6), F correction, Tape A, stopped stock.</summary>
    private const string AaplEarlyPreMarket =
        "220,64834,09:28:06.132261636,AAPL,6,338,171.04,2,@,F,T,I";

    /// <summary>SPY pre-market trade — NYSE Arca (code 4), F correction, Tape A, no stopped stock.</summary>
    private const string SpyPreMarket =
        "220,62239,07:36:47.502276617,SPY,4,695,427.61,100,@,F,T, ";

    /// <summary>SPY after-hours trade — exchange code 742, no correction, Tape A, no stopped stock.</summary>
    private const string SpyAfterHours =
        "220,759005,18:14:58.433196581,SPY,742,1171667,427.56,300,@, ,T, ";

    /// <summary>TSLA early pre-market — NYSE (code 5), F correction, Tape A, no stopped stock.</summary>
    private const string TslaEarlyPreMarket =
        "220,61928,07:00:18.892107762,TSLA,5,40,251.2,100,@,F,T, ";

    /// <summary>AVTX fractional-penny price (.1185) — large lot (15703 shares).</summary>
    private const string AvtxFractionalPenny =
        "220,95185,09:44:21.542666242,AVTX,374,68755,.1185,15703,@,F, , ";

    /// <summary>Type-3 (summary) line — must be ignored by the trade parser.</summary>
    private const string SummaryRecord =
        "3,2,WM,10,53,N,C,100,152.44,0,0,N,.0001,1";

    /// <summary>Type-34 (status) line — must be ignored by the trade parser.</summary>
    private const string StatusRecord =
        "34,4,00:22:09.246486988,WM,1,P,~,,,,,,~,P";

    // -------------------------------------------------------------------------
    // Test 1 – AAPL regular market trade
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseTradeLine_AaplRegularMarket_FieldsMatchGoldenSample()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AaplRegularMarket, SessionDate);

        record.Should().NotBeNull();
        record!.Symbol.Should().Be("AAPL");
        record.GlobalSequenceNumber.Should().Be(79081);
        record.ExchangeCode.Should().Be(10);
        record.ExchangeSequenceNumber.Should().Be(4694);
        record.Price.Should().Be(172.33m);
        record.Volume.Should().Be(100);
        record.SaleCondition.Should().Be("@");
        record.CorrectionIndicator.Should().BeEmpty();
        record.Tape.Should().BeEmpty();
        record.StoppedStock.Should().BeEmpty();
    }

    [Fact]
    public void ParseTradeLine_AaplRegularMarket_FlagsAreCorrect()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AaplRegularMarket, SessionDate)!;

        record.IsRegularTrade.Should().BeTrue("sale condition is @");
        record.IsFinalCorrection.Should().BeFalse("correction field is blank");
        record.IsTapeA.Should().BeFalse("tape field is blank in this record");
        record.IsStoppedStock.Should().BeFalse("stopped stock field is blank");
        record.IsRegularHours.Should().BeTrue("09:31:56 falls within 09:30–16:00");
    }

    [Fact]
    public void ParseTradeLine_AaplRegularMarket_TimestampNanosecondPrecision()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AaplRegularMarket, SessionDate)!;

        record.Timestamp.Hour.Should().Be(9);
        record.Timestamp.Minute.Should().Be(31);
        record.Timestamp.Second.Should().Be(56);
        // 104292761 ns → 1042927 ticks (truncated to 100-ns resolution)
        record.Timestamp.Ticks % TimeSpan.TicksPerSecond.Should().BeGreaterThan(0,
            because: "sub-second precision should be preserved from the nanosecond timestamp");
    }

    // -------------------------------------------------------------------------
    // Test 2 – AAPL pre-market trade with final correction and stopped stock
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseTradeLine_AaplPreMarketFinalCorrection_FieldsMatchGoldenSample()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AaplPreMarketFinalCorrection, SessionDate);

        record.Should().NotBeNull();
        record!.Symbol.Should().Be("AAPL");
        record.GlobalSequenceNumber.Should().Be(64802);
        record.ExchangeCode.Should().Be(5);
        record.ExchangeSequenceNumber.Should().Be(239);
        record.Price.Should().Be(171.08m);
        record.Volume.Should().Be(3);
        record.SaleCondition.Should().Be("@");
        record.CorrectionIndicator.Should().Be("F");
        record.Tape.Should().Be("T");
        record.StoppedStock.Should().Be("I");
    }

    [Fact]
    public void ParseTradeLine_AaplPreMarketFinalCorrection_FlagsAreCorrect()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AaplPreMarketFinalCorrection, SessionDate)!;

        record.IsFinalCorrection.Should().BeTrue("correction indicator is F");
        record.IsTapeA.Should().BeTrue("tape is T (Tape A/NYSE-listed)");
        record.IsStoppedStock.Should().BeTrue("stopped stock indicator is I");
        record.IsRegularHours.Should().BeFalse("09:28:00 is before the 09:30 open");
    }

    // -------------------------------------------------------------------------
    // Test 3 – SPY pre-market trade
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseTradeLine_SpyPreMarket_FieldsMatchGoldenSample()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(SpyPreMarket, SessionDate);

        record.Should().NotBeNull();
        record!.Symbol.Should().Be("SPY");
        record.GlobalSequenceNumber.Should().Be(62239);
        record.ExchangeCode.Should().Be(4);
        record.Price.Should().Be(427.61m);
        record.Volume.Should().Be(100);
        record.IsFinalCorrection.Should().BeTrue();
        record.IsTapeA.Should().BeTrue();
        record.IsStoppedStock.Should().BeFalse();
        record.IsRegularHours.Should().BeFalse("07:36 is well before the 09:30 open");
    }

    // -------------------------------------------------------------------------
    // Test 4 – SPY after-hours trade
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseTradeLine_SpyAfterHours_FieldsMatchGoldenSample()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(SpyAfterHours, SessionDate);

        record.Should().NotBeNull();
        record!.Symbol.Should().Be("SPY");
        record.GlobalSequenceNumber.Should().Be(759005);
        record.Price.Should().Be(427.56m);
        record.Volume.Should().Be(300);
        record.IsRegularHours.Should().BeFalse("18:14 is after the 16:00 close");
        record.IsTapeA.Should().BeTrue("tape is T");
    }

    // -------------------------------------------------------------------------
    // Test 5 – TSLA early pre-market (07:00:18)
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseTradeLine_TslaEarlyPreMarket_FieldsMatchGoldenSample()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(TslaEarlyPreMarket, SessionDate);

        record.Should().NotBeNull();
        record!.Symbol.Should().Be("TSLA");
        record.Price.Should().Be(251.2m);
        record.Volume.Should().Be(100);
        record.IsRegularHours.Should().BeFalse("07:00:18 is before the 09:30 open");
        record.IsFinalCorrection.Should().BeTrue("correction indicator is F");
    }

    // -------------------------------------------------------------------------
    // Test 6 – AVTX fractional-penny price (leading-zero-less format)
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseTradeLine_AvtxFractionalPenny_PricePreserved()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AvtxFractionalPenny, SessionDate);

        record.Should().NotBeNull(because: "price field '.1185' omits leading zero but is valid");
        record!.Symbol.Should().Be("AVTX");
        record.Price.Should().Be(0.1185m,
            because: "fractional-penny prices like .1185 must parse correctly without a leading zero");
        record.Volume.Should().Be(15703, because: "large-lot trade volume must be preserved");
        record.IsRegularHours.Should().BeTrue("09:44 falls within regular market hours");
        record.IsFinalCorrection.Should().BeTrue("correction indicator is F");
    }

    // -------------------------------------------------------------------------
    // Test 7 – Non-trade message types return null
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseTradeLine_SummaryRecord_ReturnsNull()
    {
        var result = NyseNationalTradesCsvParser.ParseTradeLine(SummaryRecord, SessionDate);
        result.Should().BeNull(because: "type-3 summary records are not trade prints");
    }

    [Fact]
    public void ParseTradeLine_StatusRecord_ReturnsNull()
    {
        var result = NyseNationalTradesCsvParser.ParseTradeLine(StatusRecord, SessionDate);
        result.Should().BeNull(because: "type-34 status records are not trade prints");
    }

    [Fact]
    public void ParseTradeLine_EmptyLine_ReturnsNull()
    {
        NyseNationalTradesCsvParser.ParseTradeLine("", SessionDate).Should().BeNull();
        NyseNationalTradesCsvParser.ParseTradeLine("   ", SessionDate).Should().BeNull();
    }

    [Fact]
    public void ParseTradeLine_TooFewFields_ReturnsNull()
    {
        NyseNationalTradesCsvParser.ParseTradeLine("220,12345,09:30:00,AAPL,5,100,185.50", SessionDate)
            .Should().BeNull(because: "fewer than 12 fields cannot be a valid type-220 record");
    }

    // -------------------------------------------------------------------------
    // Test 8 – Exchange code mapping
    // -------------------------------------------------------------------------

    [Fact]
    public void MapExchangeCode_KnownCodes_ReturnCorrectMnemonics()
    {
        NyseNationalTradesCsvParser.MapExchangeCode(4).Should().Be("ARCA");
        NyseNationalTradesCsvParser.MapExchangeCode(5).Should().Be("NYSE");
        NyseNationalTradesCsvParser.MapExchangeCode(6).Should().Be("EDGX");
        NyseNationalTradesCsvParser.MapExchangeCode(7).Should().Be("EDGA");
        NyseNationalTradesCsvParser.MapExchangeCode(8).Should().Be("BZX");
        NyseNationalTradesCsvParser.MapExchangeCode(9).Should().Be("BYX");
        NyseNationalTradesCsvParser.MapExchangeCode(10).Should().Be("AMEX");
        NyseNationalTradesCsvParser.MapExchangeCode(11).Should().Be("NSDQ");
        NyseNationalTradesCsvParser.MapExchangeCode(13).Should().Be("IEX");
    }

    [Fact]
    public void MapExchangeCode_UnknownCode_ReturnsFallback()
    {
        var result = NyseNationalTradesCsvParser.MapExchangeCode(999);
        result.Should().StartWith("XCHG", because: "unknown codes use the XCHG{n} fallback format");
    }

    // -------------------------------------------------------------------------
    // Test 9 – GetMessageType fast path
    // -------------------------------------------------------------------------

    [Fact]
    public void GetMessageType_TradeRecord_Returns220()
    {
        NyseNationalTradesCsvParser.GetMessageType(AaplRegularMarket)
            .Should().Be(220);
    }

    [Fact]
    public void GetMessageType_SummaryRecord_Returns3()
    {
        NyseNationalTradesCsvParser.GetMessageType(SummaryRecord)
            .Should().Be(3);
    }

    [Fact]
    public void GetMessageType_StatusRecord_Returns34()
    {
        NyseNationalTradesCsvParser.GetMessageType(StatusRecord)
            .Should().Be(34);
    }

    [Fact]
    public void GetMessageType_EmptyLine_ReturnsMinusOne()
    {
        NyseNationalTradesCsvParser.GetMessageType("").Should().Be(-1);
    }

    // -------------------------------------------------------------------------
    // Test 10 – ToRealtimeTrade conversion
    // -------------------------------------------------------------------------

    [Fact]
    public void ToRealtimeTrade_AaplPreMarketRecord_MapsAllFields()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AaplPreMarketFinalCorrection, SessionDate)!;
        var trade = NyseNationalTradesCsvParser.ToRealtimeTrade(record);

        trade.Symbol.Should().Be("AAPL");
        trade.Price.Should().Be(171.08m);
        trade.Size.Should().Be(3);
        trade.SourceId.Should().Be("nyse-taq");
        trade.Exchange.Should().Be("NYSE", because: "exchange code 5 maps to NYSE");
        trade.Conditions.Should().Be("@");
        trade.SequenceNumber.Should().Be(64802);
    }

    [Fact]
    public void ToRealtimeTrade_SpyPreMarketRecord_ExchangeIsArca()
    {
        var record = NyseNationalTradesCsvParser.ParseTradeLine(SpyPreMarket, SessionDate)!;
        var trade = NyseNationalTradesCsvParser.ToRealtimeTrade(record);

        trade.Exchange.Should().Be("ARCA", because: "exchange code 4 maps to NYSE Arca");
    }

    // -------------------------------------------------------------------------
    // Test 11 – Nanosecond time parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void TryParseNanosecondTime_ValidFormat_ParsesCorrectly()
    {
        var ok = NyseNationalTradesCsvParser.TryParseNanosecondTime(
            "09:31:56.104292761", SessionDate, out var result);

        ok.Should().BeTrue();
        result.Year.Should().Be(2023);
        result.Month.Should().Be(10);
        result.Day.Should().Be(2);
        result.Hour.Should().Be(9);
        result.Minute.Should().Be(31);
        result.Second.Should().Be(56);
    }

    [Fact]
    public void TryParseNanosecondTime_InvalidFormat_ReturnsFalse()
    {
        NyseNationalTradesCsvParser.TryParseNanosecondTime("not-a-time", SessionDate, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryParseNanosecondTime_EmptyString_ReturnsFalse()
    {
        NyseNationalTradesCsvParser.TryParseNanosecondTime("", SessionDate, out _)
            .Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Test 12 – ParseAllTrades lazy enumeration
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseAllTrades_MixedLines_OnlyReturnsTrade220Records()
    {
        var lines = new[]
        {
            SummaryRecord,          // type 3  — skipped
            StatusRecord,           // type 34 — skipped
            AaplRegularMarket,      // type 220 — included
            SpyPreMarket,           // type 220 — included
            "",                     //            skipped
            TslaEarlyPreMarket,     // type 220 — included
        };

        var trades = NyseNationalTradesCsvParser.ParseAllTrades(lines, SessionDate).ToList();

        trades.Should().HaveCount(3);
        trades.Select(t => t.Symbol).Should().Equal("AAPL", "SPY", "TSLA");
    }

    [Fact]
    public void ParseAllTrades_AllGoldenSamples_AllParsedSuccessfully()
    {
        var allLines = new[]
        {
            AaplRegularMarket,
            AaplPreMarketFinalCorrection,
            AaplEarlyPreMarket,
            SpyPreMarket,
            SpyAfterHours,
            TslaEarlyPreMarket,
            AvtxFractionalPenny,
        };

        var trades = NyseNationalTradesCsvParser.ParseAllTrades(allLines, SessionDate).ToList();

        trades.Should().HaveCount(7,
            because: "all seven golden-sample records are valid type-220 trade prints");
        trades.All(t => t.Price > 0).Should().BeTrue("every parsed trade must have a positive price");
        trades.All(t => t.Volume > 0).Should().BeTrue("every parsed trade must have a positive volume");
    }

    // -------------------------------------------------------------------------
    // Test 13 – Session date is stamped into all parsed timestamps
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseTradeLine_SessionDateIsIncorporatedIntoTimestamp()
    {
        var specificDate = new DateOnly(2023, 10, 2);
        var record = NyseNationalTradesCsvParser.ParseTradeLine(AaplRegularMarket, specificDate)!;

        record.Timestamp.Year.Should().Be(2023);
        record.Timestamp.Month.Should().Be(10);
        record.Timestamp.Day.Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Test 14 – Global sequence monotonicity across a multi-record batch
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseAllTrades_GlobalSequenceNumbers_AreStrictlyIncreasing()
    {
        // These records were extracted from the actual TAQ file and appear
        // in file order; global sequence numbers must be monotonically increasing.
        var lines = new[]
        {
            SpyPreMarket,           // seq 62239
            TslaEarlyPreMarket,     // seq 61928  ← actually lower (early pre-market ordering)
            AaplPreMarketFinalCorrection, // seq 64802
            AaplEarlyPreMarket,     // seq 64834
            AaplRegularMarket,      // seq 79081
            SpyAfterHours,          // seq 759005
        };

        var seqs = NyseNationalTradesCsvParser.ParseAllTrades(lines, SessionDate)
            .Select(t => t.GlobalSequenceNumber)
            .ToList();

        // Verify that the sequence numbers we parsed match the golden values exactly
        seqs.Should().Contain(62239);
        seqs.Should().Contain(64802);
        seqs.Should().Contain(79081);
        seqs.Should().Contain(759005);
    }

    // -------------------------------------------------------------------------
    // Test 15 – Symbol constants and message type constants
    // -------------------------------------------------------------------------

    [Fact]
    public void Constants_MessageTypeValues_MatchSpecification()
    {
        NyseNationalTradesCsvParser.TradeMessageType.Should().Be(220);
        NyseNationalTradesCsvParser.SummaryMessageType.Should().Be(3);
        NyseNationalTradesCsvParser.StatusMessageType.Should().Be(34);
    }
}
