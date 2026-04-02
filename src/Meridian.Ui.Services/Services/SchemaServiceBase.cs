using System.Text;
using System.Text.Json;
using Meridian.Contracts.Schema;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Ui.Services;

/// <summary>
/// Base class containing shared schema creation and export logic.
/// Platform-specific projects (WPF) extend this class with their own
/// storage and caching implementations.
/// </summary>
public abstract class SchemaServiceBase : ISchemaService
{
    /// <summary>
    /// Gets a JSON schema for the specified event type.
    /// Implements ISchemaService for use by shared UI services.
    /// </summary>
    public virtual string? GetJsonSchema(string eventType)
    {
        var schemas = GetSchemaDefinitions();

        // Find the canonical key (respecting OrdinalIgnoreCase comparison)
        if (!schemas.TryGetValue(eventType, out var schema))
            return null;

        // Use the canonical casing from the dictionary key, not the raw caller input.
        // Single() is safe here because TryGetValue already confirmed exactly one match.
        var canonicalKey = schemas.Keys.Single(k => string.Equals(k, eventType, StringComparison.OrdinalIgnoreCase));

        return JsonSerializer.Serialize(
            new { schema = canonicalKey, @namespace = "Meridian.Domain.Events", definition = schema },
            DesktopJsonOptions.PrettyPrint);
    }

    /// <summary>
    /// Creates a complete data dictionary with all known event schemas.
    /// </summary>
    protected virtual DataDictionary CreateDataDictionary()
    {
        return new DataDictionary
        {
            Version = "2.0",
            GeneratedAt = DateTime.UtcNow,
            Schemas = new Dictionary<string, EventSchema>
            {
                ["Trade"] = CreateTradeSchema(),
                ["Quote"] = CreateQuoteSchema(),
                ["BboQuote"] = CreateBboQuoteSchema(),
                ["L2Depth"] = CreateL2DepthSchema(),
                ["Bar"] = CreateBarSchema()
            },
            ExchangeCodes = GetExchangeCodes(),
            TradeConditions = GetTradeConditions(),
            QuoteConditions = GetQuoteConditions()
        };
    }

    /// <summary>
    /// Returns basic schema definitions as anonymous objects keyed by event type.
    /// Used by GetJsonSchema for the ISchemaService contract.
    /// </summary>
    private static Dictionary<string, object> GetSchemaDefinitions()
    {
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Trade"] = new
            {
                type = "object",
                properties = new
                {
                    symbol = new { type = "string" },
                    price = new { type = "number" },
                    size = new { type = "number" },
                    timestamp = new { type = "string", format = "date-time" },
                    exchange = new { type = "string" },
                    condition = new { type = "string" },
                    sequenceNumber = new { type = "integer" },
                    side = new { type = "string", @enum = new[] { "Buy", "Sell", "Unknown" } }
                },
                required = new[] { "symbol", "price", "size", "timestamp" }
            },
            ["BboQuote"] = new
            {
                type = "object",
                properties = new
                {
                    symbol = new { type = "string" },
                    bidPrice = new { type = "number" },
                    bidSize = new { type = "number" },
                    askPrice = new { type = "number" },
                    askSize = new { type = "number" },
                    spread = new { type = "number" },
                    spreadBps = new { type = "number" },
                    midPrice = new { type = "number" },
                    timestamp = new { type = "string", format = "date-time" }
                },
                required = new[] { "symbol", "bidPrice", "bidSize", "askPrice", "askSize", "timestamp" }
            },
            ["LOBSnapshot"] = new
            {
                type = "object",
                properties = new
                {
                    symbol = new { type = "string" },
                    timestamp = new { type = "string", format = "date-time" },
                    bids = new { type = "array", items = new { type = "object" } },
                    asks = new { type = "array", items = new { type = "object" } },
                    bestBid = new { type = "number" },
                    bestAsk = new { type = "number" },
                    spread = new { type = "number" },
                    midPrice = new { type = "number" }
                },
                required = new[] { "symbol", "timestamp", "bids", "asks" }
            },
            ["HistoricalBar"] = new
            {
                type = "object",
                properties = new
                {
                    symbol = new { type = "string" },
                    timestamp = new { type = "string", format = "date-time" },
                    open = new { type = "number" },
                    high = new { type = "number" },
                    low = new { type = "number" },
                    close = new { type = "number" },
                    volume = new { type = "number" },
                    vwap = new { type = "number" },
                    tradeCount = new { type = "integer" }
                },
                required = new[] { "symbol", "timestamp", "open", "high", "low", "close", "volume" }
            }
        };
    }


    protected static EventSchema CreateTradeSchema()
    {
        return new EventSchema
        {
            Name = "Trade",
            Version = "2.0.0",
            Description = "Represents a single trade execution event with price, size, and exchange information.",
            IntroducedAt = new DateTime(2025, 1, 1),
            Fields = new[]
            {
                new SchemaField { Name = "Timestamp", Type = "datetime", Description = "Event timestamp in UTC with nanosecond precision", Format = "ISO8601" },
                new SchemaField { Name = "Symbol", Type = "string", Description = "Ticker symbol" },
                new SchemaField { Name = "Price", Type = "decimal", Description = "Trade price", Format = "decimal(18,8)" },
                new SchemaField { Name = "Size", Type = "int64", Description = "Trade size in shares" },
                new SchemaField { Name = "Side", Type = "enum", Description = "Aggressor side of the trade", EnumValues = new[] { "Buy", "Sell", "Unknown" } },
                new SchemaField { Name = "Exchange", Type = "string", Description = "Exchange code where trade occurred", ExchangeSpecific = true },
                new SchemaField { Name = "TradeId", Type = "string", Description = "Unique trade identifier from exchange", Nullable = true },
                new SchemaField { Name = "Conditions", Type = "string[]", Description = "Trade condition codes", Nullable = true, ExchangeSpecific = true },
                new SchemaField { Name = "SequenceNumber", Type = "int64", Description = "Sequence number for ordering", Nullable = true }
            },
            PrimaryKey = new[] { "Timestamp", "Symbol", "TradeId" }
        };
    }

    protected static EventSchema CreateQuoteSchema()
    {
        return new EventSchema
        {
            Name = "Quote",
            Version = "2.0.0",
            Description = "Represents a quote update with bid/ask prices and sizes.",
            IntroducedAt = new DateTime(2025, 1, 1),
            Fields = new[]
            {
                new SchemaField { Name = "Timestamp", Type = "datetime", Description = "Event timestamp in UTC", Format = "ISO8601" },
                new SchemaField { Name = "Symbol", Type = "string", Description = "Ticker symbol" },
                new SchemaField { Name = "BidPrice", Type = "decimal", Description = "Best bid price", Nullable = true },
                new SchemaField { Name = "BidSize", Type = "int64", Description = "Size at best bid", Nullable = true },
                new SchemaField { Name = "AskPrice", Type = "decimal", Description = "Best ask price", Nullable = true },
                new SchemaField { Name = "AskSize", Type = "int64", Description = "Size at best ask", Nullable = true },
                new SchemaField { Name = "BidExchange", Type = "string", Description = "Exchange code for best bid", Nullable = true, ExchangeSpecific = true },
                new SchemaField { Name = "AskExchange", Type = "string", Description = "Exchange code for best ask", Nullable = true, ExchangeSpecific = true }
            },
            PrimaryKey = new[] { "Timestamp", "Symbol" }
        };
    }

    protected static EventSchema CreateBboQuoteSchema()
    {
        return new EventSchema
        {
            Name = "BboQuote",
            Version = "2.0.0",
            Description = "Best Bid and Offer (NBBO) quote with national market system data.",
            IntroducedAt = new DateTime(2025, 1, 1),
            Fields = new[]
            {
                new SchemaField { Name = "Timestamp", Type = "datetime", Description = "Event timestamp in UTC", Format = "ISO8601" },
                new SchemaField { Name = "Symbol", Type = "string", Description = "Ticker symbol" },
                new SchemaField { Name = "NbboBidPrice", Type = "decimal", Description = "National best bid price" },
                new SchemaField { Name = "NbboBidSize", Type = "int64", Description = "Size at national best bid" },
                new SchemaField { Name = "NbboAskPrice", Type = "decimal", Description = "National best ask price" },
                new SchemaField { Name = "NbboAskSize", Type = "int64", Description = "Size at national best ask" },
                new SchemaField { Name = "MidPrice", Type = "decimal", Description = "Calculated mid price" },
                new SchemaField { Name = "Spread", Type = "decimal", Description = "Bid-ask spread" }
            },
            PrimaryKey = new[] { "Timestamp", "Symbol" }
        };
    }

    protected static EventSchema CreateL2DepthSchema()
    {
        return new EventSchema
        {
            Name = "L2Depth",
            Version = "2.0.0",
            Description = "Level 2 market depth snapshot with multiple price levels.",
            IntroducedAt = new DateTime(2025, 1, 1),
            Fields = new[]
            {
                new SchemaField { Name = "Timestamp", Type = "datetime", Description = "Snapshot timestamp in UTC", Format = "ISO8601" },
                new SchemaField { Name = "Symbol", Type = "string", Description = "Ticker symbol" },
                new SchemaField { Name = "Bids", Type = "array", Description = "Array of bid price levels" },
                new SchemaField { Name = "Asks", Type = "array", Description = "Array of ask price levels" },
                new SchemaField { Name = "Depth", Type = "int32", Description = "Number of levels on each side" },
                new SchemaField { Name = "Exchange", Type = "string", Description = "Exchange code", ExchangeSpecific = true }
            },
            PrimaryKey = new[] { "Timestamp", "Symbol", "Exchange" }
        };
    }

    protected static EventSchema CreateBarSchema()
    {
        return new EventSchema
        {
            Name = "Bar",
            Version = "2.0.0",
            Description = "OHLCV bar/candlestick data for a time interval.",
            IntroducedAt = new DateTime(2025, 1, 1),
            Fields = new[]
            {
                new SchemaField { Name = "Timestamp", Type = "datetime", Description = "Bar start time in UTC", Format = "ISO8601" },
                new SchemaField { Name = "Symbol", Type = "string", Description = "Ticker symbol" },
                new SchemaField { Name = "Open", Type = "decimal", Description = "Opening price" },
                new SchemaField { Name = "High", Type = "decimal", Description = "Highest price during interval" },
                new SchemaField { Name = "Low", Type = "decimal", Description = "Lowest price during interval" },
                new SchemaField { Name = "Close", Type = "decimal", Description = "Closing price" },
                new SchemaField { Name = "Volume", Type = "int64", Description = "Total volume during interval" },
                new SchemaField { Name = "VWAP", Type = "decimal", Description = "Volume-weighted average price", Nullable = true },
                new SchemaField { Name = "TradeCount", Type = "int32", Description = "Number of trades during interval", Nullable = true },
                new SchemaField { Name = "Resolution", Type = "string", Description = "Bar resolution", EnumValues = new[] { "Tick", "Second", "Minute", "Hour", "Daily" } }
            },
            PrimaryKey = new[] { "Timestamp", "Symbol", "Resolution" }
        };
    }



    protected static Dictionary<string, string> GetExchangeCodes()
    {
        return new Dictionary<string, string>
        {
            ["XNAS"] = "NASDAQ Stock Market",
            ["XNYS"] = "New York Stock Exchange",
            ["ARCX"] = "NYSE Arca",
            ["XASE"] = "NYSE American (AMEX)",
            ["BATS"] = "CBOE BZX Exchange",
            ["BATY"] = "CBOE BYX Exchange",
            ["EDGA"] = "CBOE EDGA Exchange",
            ["EDGX"] = "CBOE EDGX Exchange",
            ["IEXG"] = "IEX Exchange",
            ["MEMX"] = "Members Exchange"
        };
    }

    protected static Dictionary<string, string> GetTradeConditions()
    {
        return new Dictionary<string, string>
        {
            ["@"] = "Regular Sale",
            ["A"] = "Acquisition",
            ["B"] = "Bunched Trade",
            ["C"] = "Cash Sale",
            ["E"] = "Automatic Execution",
            ["F"] = "Intermarket Sweep",
            ["T"] = "Form T",
            ["X"] = "Cross Trade"
        };
    }

    protected static Dictionary<string, string> GetQuoteConditions()
    {
        return new Dictionary<string, string>
        {
            ["A"] = "Slow Quote Offer Side",
            ["B"] = "Slow Quote Bid Side",
            ["C"] = "Closing",
            ["O"] = "Opening",
            ["Q"] = "Regular",
            ["X"] = "Closed"
        };
    }



    protected static string ExportAsJson(DataDictionary dictionary)
    {
        return JsonSerializer.Serialize(dictionary, DesktopJsonOptions.PrettyPrint);
    }

    protected static string ExportAsMarkdown(DataDictionary dictionary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Meridian - Data Dictionary");
        sb.AppendLine();
        sb.AppendLine($"**Version:** {dictionary.Version}");
        sb.AppendLine($"**Generated:** {dictionary.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var schema in dictionary.Schemas.Values.OrderBy(s => s.Name))
        {
            sb.AppendLine($"## {schema.Name} Event Schema");
            sb.AppendLine();
            sb.AppendLine($"**Version:** {schema.Version}");
            sb.AppendLine();
            sb.AppendLine(schema.Description);
            sb.AppendLine();
            sb.AppendLine("### Fields");
            sb.AppendLine();
            sb.AppendLine("| Field | Type | Description | Nullable |");
            sb.AppendLine("|-------|------|-------------|----------|");

            foreach (var field in schema.Fields)
            {
                var nullable = field.Nullable ? "Yes" : "No";
                sb.AppendLine($"| {field.Name} | {field.Type} | {field.Description ?? ""} | {nullable} |");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        if (dictionary.ExchangeCodes != null)
        {
            sb.AppendLine("## Exchange Codes");
            sb.AppendLine();
            sb.AppendLine("| Code | Description |");
            sb.AppendLine("|------|-------------|");
            foreach (var (code, description) in dictionary.ExchangeCodes.OrderBy(x => x.Key))
            {
                sb.AppendLine($"| {code} | {description} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    protected static string ExportAsCsv(DataDictionary dictionary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("EventType,FieldName,Type,Description,Nullable,Format");

        foreach (var schema in dictionary.Schemas.Values.OrderBy(s => s.Name))
        {
            foreach (var field in schema.Fields)
            {
                var description = field.Description?.Replace("\"", "\"\"") ?? "";
                sb.AppendLine($"\"{schema.Name}\",\"{field.Name}\",\"{field.Type}\",\"{description}\",\"{field.Nullable}\",\"{field.Format ?? ""}\"");
            }
        }

        return sb.ToString();
    }

}

/// <summary>
/// Event args for data dictionary generation events.
/// </summary>
public sealed class DataDictionaryEventArgs : EventArgs
{
    public DataDictionary? Dictionary { get; set; }
}
