using System.Globalization;
using System.Runtime.CompilerServices;
using Meridian.Contracts.Domain.Enums;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Infrastructure.Adapters.NYSE;

/// <summary>
/// Parser for NYSE TAQ (Trade and Quote) National Trades CSV files.
///
/// File format: EQY_US_TAQ_NATIONAL_TRADES_YYYYMMDD.gz
/// Distributed via NYSE FTP: ftp.nyse.com/Historical Data Samples/TAQ NYSE NATIONAL TRADES/
///
/// Message types in the CSV:
///   3   - Symbol summary record (open price, daily statistics)
///   34  - Trading status record (halt, resume, etc.)
///   220 - Trade print record (actual executed trades)
///
/// Type-220 column layout (1-indexed):
///   1  msg_type       : Always 220
///   2  global_seq_num : SIP-assigned sequence number (unique within day)
///   3  time           : HH:MM:SS.nnnnnnnnn (nanosecond precision, Eastern Time)
///   4  symbol         : Ticker symbol (may contain spaces for test symbols)
///   5  exchange_code  : Numeric SIP participant code for reporting exchange
///   6  exch_seq_num   : Exchange-provided trade sequence number
///   7  price          : Trade price (decimal; may omit leading zero, e.g., ".1185")
///   8  volume         : Trade volume (shares)
///   9  sale_condition : Sale condition character(s) (e.g., "@" = regular round-lot)
///   10 correction     : Correction indicator ("F" = final correction, " " = normal)
///   11 tape           : Consolidated tape ("T" = Tape A/NYSE-listed, " " = other)
///   12 stopped_stock  : Stopped stock indicator ("I" = stopped/indicated, " " = normal)
///
/// Exchange codes (field 5) — SIP participant numbering:
///   4 = NYSE Arca (P)     5 = NYSE (N)          6 = CBOE EDGX (K)
///   7 = CBOE EDGA (J)     8 = CBOE BZX (Z)      9 = CBOE BYX (Y)
///  10 = NYSE American (A) 11 = NASDAQ (Q)       12 = NASDAQ BX (B)
///  13 = IEX (V)           14 = MEMX (U)         15 = LTSE (L)
/// </summary>
public static class NyseNationalTradesCsvParser
{
    /// <summary>Trade message type code in NYSE TAQ National Trades CSV files.</summary>
    public const int TradeMessageType = 220;

    /// <summary>Symbol summary message type.</summary>
    public const int SummaryMessageType = 3;

    /// <summary>Trading status message type.</summary>
    public const int StatusMessageType = 34;

    /// <summary>
    /// Parses a single type-220 (trade) CSV line into a <see cref="NyseTaqTradeRecord"/>.
    /// Returns <c>null</c> for non-trade records or lines that fail to parse.
    /// </summary>
    public static NyseTaqTradeRecord? ParseTradeLine(string line, DateOnly sessionDate)
    {
        if (string.IsNullOrEmpty(line))
            return null;

        // Split on comma; field count for type-220 is exactly 12
        var fields = line.Split(',');
        if (fields.Length < 12)
            return null;

        if (!int.TryParse(fields[0], out var msgType) || msgType != TradeMessageType)
            return null;

        if (!long.TryParse(fields[1], out var globalSeq))
            return null;

        if (!TryParseNanosecondTime(fields[2], sessionDate, out var timestamp))
            return null;

        var symbol = fields[3].Trim();
        if (string.IsNullOrEmpty(symbol))
            return null;

        if (!int.TryParse(fields[4], out var exchangeCode))
            return null;

        if (!long.TryParse(fields[5], out var exchSeq))
            return null;

        // Price field may omit leading zero (e.g., ".1185")
        if (!decimal.TryParse(fields[6], NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price <= 0)
            return null;

        if (!long.TryParse(fields[7], out var volume) || volume <= 0)
            return null;

        var saleCondition = fields[8].Trim();
        var correction = fields[9].Trim();
        var tape = fields[10].Trim();
        var stoppedStock = fields[11].Trim();

        return new NyseTaqTradeRecord(
            GlobalSequenceNumber: globalSeq,
            Timestamp: timestamp,
            Symbol: symbol,
            ExchangeCode: exchangeCode,
            ExchangeSequenceNumber: exchSeq,
            Price: price,
            Volume: volume,
            SaleCondition: saleCondition,
            CorrectionIndicator: correction,
            Tape: tape,
            StoppedStock: stoppedStock);
    }

    /// <summary>
    /// Converts a <see cref="NyseTaqTradeRecord"/> to a <see cref="RealtimeTrade"/>
    /// suitable for ingestion into the Meridian event pipeline.
    /// </summary>
    public static RealtimeTrade ToRealtimeTrade(NyseTaqTradeRecord record)
    {
        return new RealtimeTrade(
            Symbol: record.Symbol,
            Price: record.Price,
            Size: record.Volume,
            Timestamp: record.Timestamp,
            SourceId: "nyse-taq",
            Exchange: MapExchangeCode(record.ExchangeCode),
            Conditions: string.IsNullOrEmpty(record.SaleCondition) ? null : record.SaleCondition,
            SequenceNumber: record.GlobalSequenceNumber,
            Side: AggressorSide.Unknown);
    }

    /// <summary>
    /// Lazily parses all type-220 trade records from a sequence of CSV lines.
    /// Non-trade lines and unparseable rows are silently skipped.
    /// </summary>
    public static IEnumerable<NyseTaqTradeRecord> ParseAllTrades(IEnumerable<string> lines, DateOnly sessionDate)
    {
        foreach (var line in lines)
        {
            var record = ParseTradeLine(line, sessionDate);
            if (record is not null)
                yield return record;
        }
    }

    /// <summary>
    /// Returns the message type code of a CSV line (field 1), or -1 if unparseable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMessageType(string line)
    {
        if (string.IsNullOrEmpty(line))
            return -1;

        var comma = line.IndexOf(',');
        if (comma <= 0)
            return -1;

        return int.TryParse(line.AsSpan(0, comma), out var t) ? t : -1;
    }

    // -------------------------------------------------------------------------
    // Exchange code → mnemonic mapping (SIP participant codes)
    // -------------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<int, string> ExchangeCodeMap =
        new Dictionary<int, string>
        {
            [4] = "ARCA",    // NYSE Arca
            [5] = "NYSE",    // New York Stock Exchange
            [6] = "EDGX",   // CBOE EDGX
            [7] = "EDGA",   // CBOE EDGA
            [8] = "BZX",    // CBOE BZX (formerly BATS)
            [9] = "BYX",    // CBOE BYX
            [10] = "AMEX",   // NYSE American (formerly AMEX)
            [11] = "NSDQ",   // NASDAQ
            [12] = "BX",     // NASDAQ BX
            [13] = "IEX",    // Investors Exchange
            [14] = "MEMX",   // Members Exchange
            [15] = "LTSE",   // Long-Term Stock Exchange
        };

    /// <summary>Maps a numeric SIP exchange code to its mnemonic string.</summary>
    public static string MapExchangeCode(int code)
        => ExchangeCodeMap.TryGetValue(code, out var name) ? name : $"XCHG{code}";

    // -------------------------------------------------------------------------
    // Time parsing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses a NYSE TAQ time string with nanosecond precision.
    /// Format: HH:MM:SS.nnnnnnnnn
    /// Clips nanoseconds to .NET's 100-nanosecond tick resolution.
    /// </summary>
    public static bool TryParseNanosecondTime(string timeStr, DateOnly sessionDate, out DateTimeOffset result)
    {
        result = default;

        // Expected format: HH:MM:SS.nnnnnnnnn (19 chars minimum)
        if (timeStr.Length < 8)
            return false;

        if (!int.TryParse(timeStr.AsSpan(0, 2), out var hours) ||
            !int.TryParse(timeStr.AsSpan(3, 2), out var minutes) ||
            !int.TryParse(timeStr.AsSpan(6, 2), out var seconds))
            return false;

        long nanoFraction = 0;
        if (timeStr.Length > 9 && timeStr[8] == '.')
        {
            var fracStr = timeStr.AsSpan(9);
            // Parse up to 9 nanosecond digits
            var digits = Math.Min(fracStr.Length, 9);
            if (!long.TryParse(fracStr[..digits], out nanoFraction))
                nanoFraction = 0;

            // Pad to 9 digits if shorter
            for (int i = fracStr.Length; i < 9; i++)
                nanoFraction *= 10;
        }

        // Convert nanoseconds to ticks (1 tick = 100 ns)
        var ticks = nanoFraction / 100;

        try
        {
            // NYSE TAQ timestamps are Eastern Time (ET); use UTC offset of -5 (EST) or -4 (EDT)
            // For simplicity, record as unspecified local time. Callers must apply timezone.
            var dt = new DateTime(
                sessionDate.Year, sessionDate.Month, sessionDate.Day,
                hours, minutes, seconds,
                DateTimeKind.Unspecified).AddTicks(ticks);

            result = new DateTimeOffset(dt, TimeSpan.Zero);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

/// <summary>
/// A parsed NYSE TAQ type-220 (National Trades) trade record.
/// All fields are mapped directly from the CSV columns; no interpretation is applied.
/// </summary>
public sealed record NyseTaqTradeRecord(
    long GlobalSequenceNumber,
    DateTimeOffset Timestamp,
    string Symbol,
    int ExchangeCode,
    long ExchangeSequenceNumber,
    decimal Price,
    long Volume,
    string SaleCondition,
    string CorrectionIndicator,
    string Tape,
    string StoppedStock)
{
    /// <summary>True when this is a regular round-lot trade (sale condition "@").</summary>
    public bool IsRegularTrade => SaleCondition == "@";

    /// <summary>True when the correction indicator is "F" (final/corrected print).</summary>
    public bool IsFinalCorrection => CorrectionIndicator == "F";

    /// <summary>True when this record is on Consolidated Tape A (NYSE-listed securities).</summary>
    public bool IsTapeA => Tape == "T";

    /// <summary>True when the stopped-stock indicator is set.</summary>
    public bool IsStoppedStock => StoppedStock == "I";

    /// <summary>Determines whether this trade occurred during regular market hours (09:30–16:00 ET).</summary>
    public bool IsRegularHours
    {
        get
        {
            var t = Timestamp.TimeOfDay;
            var open = new TimeSpan(9, 30, 0);
            var close = new TimeSpan(16, 0, 0);
            return t >= open && t < close;
        }
    }
}
