using System.Text.Json;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Prometheus;
using QuantConnect;
using QuantConnect.Data;
using Serilog;

namespace Meridian.Integrations.Lean;

/// <summary>
/// Custom Lean BaseData implementation for Meridian trade events.
/// Allows Lean algorithms to consume tick-by-tick trade data collected by Meridian.
/// </summary>
public sealed class MeridianTradeData : BaseData
{
    private static readonly ILogger _log = Log.ForContext<MeridianTradeData>();

    /// <summary>Counter for JSON parse failures during trade data reading</summary>
    private static readonly Counter JsonParseFailures = Metrics.CreateCounter(
        "mdc_lean_trade_json_parse_failures_total",
        "Total number of JSON parse failures when reading trade data in Lean integration");

    /// <summary>Counter for unexpected parse errors during trade data reading</summary>
    private static readonly Counter UnexpectedParseErrors = Metrics.CreateCounter(
        "mdc_lean_trade_unexpected_parse_errors_total",
        "Total number of unexpected errors when parsing trade data in Lean integration");

    /// <summary>Trade price</summary>
    public decimal TradePrice { get; set; }

    /// <summary>Trade volume/size</summary>
    public decimal TradeSize { get; set; }

    /// <summary>Exchange where trade occurred</summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>Trade conditions/flags</summary>
    public List<string> Conditions { get; set; } = new();

    /// <summary>Sequence number for ordering</summary>
    public long SequenceNumber { get; set; }

    /// <summary>Aggressor side (buy/sell)</summary>
    public string AggressorSide { get; set; } = string.Empty;

    /// <summary>
    /// Return the URL string source of the file. This will be converted to a stream.
    /// </summary>
    public override SubscriptionDataSource GetSource(SubscriptionDataConfig config, DateTime date, bool isLiveMode)
    {
        // In live mode, data would come from the real-time collector
        if (isLiveMode)
        {
            return new SubscriptionDataSource(string.Empty, SubscriptionTransportMedium.LocalFile);
        }

        // For backtesting, construct the path to the JSONL file in the Meridian data directory
        // Assuming data is organized as: {DataRoot}/{Symbol}/trade/{date}.jsonl
        var dataRoot = Environment.GetEnvironmentVariable("MDC_DATA_ROOT") ?? "./data";
        var symbol = config.Symbol.Value.ToUpperInvariant();
        var dateStr = date.ToString("yyyy-MM-dd");
        var filePath = Path.Combine(dataRoot, "marketdatacollector", symbol, "trade", $"{dateStr}.jsonl");

        return new SubscriptionDataSource(filePath, SubscriptionTransportMedium.LocalFile);
    }

    /// <summary>
    /// Reader converts each line of the data source into a BaseData object.
    /// </summary>
    public override BaseData Reader(SubscriptionDataConfig config, string line, DateTime date, bool isLiveMode)
    {
        try
        {
            // Parse the JSONL line as a MarketEvent
            var marketEvent = JsonSerializer.Deserialize<MarketEvent>(line);

            if (marketEvent == null || marketEvent.Type != MarketEventType.Trade)
                return null!;

            // Extract trade payload
            var trade = marketEvent.Payload as Trade;
            if (trade == null)
                return null!;

            return new MeridianTradeData
            {
                Symbol = Symbol.Create(marketEvent.Symbol, SecurityType.Equity, Market.USA),
                Time = marketEvent.Timestamp.UtcDateTime,
                Value = trade.Price,
                TradePrice = trade.Price,
                TradeSize = (decimal)trade.Size,
                Exchange = trade.Venue ?? string.Empty,
                Conditions = new List<string>(),
                SequenceNumber = trade.SequenceNumber,
                AggressorSide = trade.Aggressor.ToString()
            };
        }
        catch (JsonException ex)
        {
            JsonParseFailures.Inc();
            var linePreview = line.Length > 100 ? line[..100] + "..." : line;
            _log.Warning(ex,
                "Failed to parse trade data JSON for {Symbol} on {Date}: {LinePreview}",
                config.Symbol.Value,
                date.ToString("yyyy-MM-dd"),
                linePreview);
            return null!;
        }
        catch (Exception ex)
        {
            UnexpectedParseErrors.Inc();
            var linePreview = line.Length > 100 ? line[..100] + "..." : line;
            _log.Warning(ex,
                "Unexpected error parsing trade data for {Symbol} on {Date}: {LinePreview}",
                config.Symbol.Value,
                date.ToString("yyyy-MM-dd"),
                linePreview);
            return null!;
        }
    }

    /// <summary>
    /// Clone implementation required by Lean
    /// </summary>
    public override BaseData Clone()
    {
        return new MeridianTradeData
        {
            Symbol = Symbol,
            Time = Time,
            Value = Value,
            TradePrice = TradePrice,
            TradeSize = TradeSize,
            Exchange = Exchange,
            Conditions = new List<string>(Conditions),
            SequenceNumber = SequenceNumber,
            AggressorSide = AggressorSide
        };
    }
}
