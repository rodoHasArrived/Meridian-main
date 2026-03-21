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
/// Custom Lean BaseData implementation for Meridian BBO quote events.
/// Allows Lean algorithms to consume best bid/offer data collected by Meridian.
/// </summary>
public sealed class MeridianQuoteData : BaseData
{
    private static readonly ILogger _log = Log.ForContext<MeridianQuoteData>();

    /// <summary>Counter for JSON parse failures during quote data reading</summary>
    private static readonly Counter JsonParseFailures = Metrics.CreateCounter(
        "mdc_lean_quote_json_parse_failures_total",
        "Total number of JSON parse failures when reading quote data in Lean integration");

    /// <summary>Counter for unexpected parse errors during quote data reading</summary>
    private static readonly Counter UnexpectedParseErrors = Metrics.CreateCounter(
        "mdc_lean_quote_unexpected_parse_errors_total",
        "Total number of unexpected errors when parsing quote data in Lean integration");

    /// <summary>Best bid price</summary>
    public decimal BidPrice { get; set; }

    /// <summary>Best bid size</summary>
    public decimal BidSize { get; set; }

    /// <summary>Best ask price</summary>
    public decimal AskPrice { get; set; }

    /// <summary>Best ask size</summary>
    public decimal AskSize { get; set; }

    /// <summary>Mid price (average of bid and ask)</summary>
    public decimal MidPrice { get; set; }

    /// <summary>Bid-ask spread</summary>
    public decimal Spread { get; set; }

    /// <summary>Sequence number for ordering</summary>
    public long SequenceNumber { get; set; }

    /// <summary>Bid exchange</summary>
    public string BidExchange { get; set; } = string.Empty;

    /// <summary>Ask exchange</summary>
    public string AskExchange { get; set; } = string.Empty;

    /// <summary>
    /// Return the URL string source of the file.
    /// </summary>
    public override SubscriptionDataSource GetSource(SubscriptionDataConfig config, DateTime date, bool isLiveMode)
    {
        if (isLiveMode)
        {
            return new SubscriptionDataSource(string.Empty, SubscriptionTransportMedium.LocalFile);
        }

        // For backtesting, construct the path to the JSONL file
        // Assuming data is organized as: {DataRoot}/{Symbol}/bboquote/{date}.jsonl
        var dataRoot = Environment.GetEnvironmentVariable("MDC_DATA_ROOT") ?? "./data";
        var symbol = config.Symbol.Value.ToUpperInvariant();
        var dateStr = date.ToString("yyyy-MM-dd");
        var filePath = Path.Combine(dataRoot, "marketdatacollector", symbol, "bboquote", $"{dateStr}.jsonl");

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

            if (marketEvent == null || marketEvent.Type != MarketEventType.BboQuote)
                return null!;

            // Extract BBO quote payload
            var quote = marketEvent.Payload as BboQuotePayload;
            if (quote == null)
                return null!;

            return new MeridianQuoteData
            {
                Symbol = Symbol.Create(marketEvent.Symbol, SecurityType.Equity, Market.USA),
                Time = marketEvent.Timestamp.UtcDateTime,
                Value = quote.MidPrice.GetValueOrDefault(),  // Use mid price as the primary value
                BidPrice = quote.BidPrice,
                BidSize = (decimal)quote.BidSize,
                AskPrice = quote.AskPrice,
                AskSize = (decimal)quote.AskSize,
                MidPrice = quote.MidPrice.GetValueOrDefault(),
                Spread = quote.Spread.GetValueOrDefault(),
                SequenceNumber = quote.SequenceNumber,
                BidExchange = quote.Venue ?? string.Empty,
                AskExchange = quote.Venue ?? string.Empty
            };
        }
        catch (JsonException ex)
        {
            JsonParseFailures.Inc();
            var linePreview = line.Length > 100 ? line[..100] + "..." : line;
            _log.Warning(ex,
                "Failed to parse quote data JSON for {Symbol} on {Date}: {LinePreview}",
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
                "Unexpected error parsing quote data for {Symbol} on {Date}: {LinePreview}",
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
        return new MeridianQuoteData
        {
            Symbol = Symbol,
            Time = Time,
            Value = Value,
            BidPrice = BidPrice,
            BidSize = BidSize,
            AskPrice = AskPrice,
            AskSize = AskSize,
            MidPrice = MidPrice,
            Spread = Spread,
            SequenceNumber = SequenceNumber,
            BidExchange = BidExchange,
            AskExchange = AskExchange
        };
    }
}
