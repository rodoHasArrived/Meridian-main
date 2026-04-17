using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Meridian.Application.Config;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using ContractEvents = Meridian.Contracts.Domain.Events;

namespace Meridian.Application.Serialization;

/// <summary>
/// High-performance JSON serialization using source generators.
/// Eliminates reflection overhead for faster serialization/deserialization.
///
/// Based on: System.Text.Json source generators (.NET 7+)
/// Reference: docs/open-source-references.md - System.Text.Json High-Performance Techniques
///
/// USAGE: All JSON serialization in this codebase should use one of the following:
/// - MarketDataJsonContext.HighPerformanceOptions for compact output (storage, wire protocol)
/// - MarketDataJsonContext.PrettyPrintOptions for human-readable output (debugging, config files)
/// - HighPerformanceJson static methods for MarketEvent serialization
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(MarketEvent))]
[JsonSerializable(typeof(MarketEvent[]))]
[JsonSerializable(typeof(List<MarketEvent>))]
[JsonSerializable(typeof(ContractEvents.MarketEventPayload.HeartbeatPayload))]
[JsonSerializable(typeof(Trade))]
[JsonSerializable(typeof(BboQuotePayload))]
[JsonSerializable(typeof(L2SnapshotPayload))]
[JsonSerializable(typeof(LOBSnapshot))]
[JsonSerializable(typeof(HistoricalBar))]
[JsonSerializable(typeof(IntegrityEvent))]
[JsonSerializable(typeof(DepthIntegrityEvent))]
[JsonSerializable(typeof(OrderFlowStatistics))]
[JsonSerializable(typeof(OrderBookLevel))]
[JsonSerializable(typeof(OrderBookLevel[]))]
[JsonSerializable(typeof(List<OrderBookLevel>))]
[JsonSerializable(typeof(MarketTradeUpdate))]
[JsonSerializable(typeof(MarketQuoteUpdate))]
[JsonSerializable(typeof(MarketDepthUpdate))]
[JsonSerializable(typeof(SymbolConfig))]
[JsonSerializable(typeof(SymbolConfig[]))]
[JsonSerializable(typeof(List<SymbolConfig>))]
[JsonSerializable(typeof(AggregateBarPayload))]
// Option types
[JsonSerializable(typeof(OptionQuote))]
[JsonSerializable(typeof(OptionTrade))]
[JsonSerializable(typeof(GreeksSnapshot))]
[JsonSerializable(typeof(OptionChainSnapshot))]
[JsonSerializable(typeof(OptionContractSpec))]
[JsonSerializable(typeof(OpenInterestUpdate))]
[JsonSerializable(typeof(List<OptionQuote>))]
[JsonSerializable(typeof(List<OptionTrade>))]
// Order book event types
[JsonSerializable(typeof(OrderAdd))]
[JsonSerializable(typeof(OrderModify))]
[JsonSerializable(typeof(OrderCancel))]
[JsonSerializable(typeof(OrderExecute))]
[JsonSerializable(typeof(OrderReplace))]
// Canonicalization types
[JsonSerializable(typeof(CanonicalTradeCondition))]
[JsonSerializable(typeof(CanonicalTradeCondition[]))]
[JsonSerializable(typeof(CanonicalizationConfig))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
// Configuration types
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(StorageConfig))]
[JsonSerializable(typeof(SourceRegistryConfig))]
[JsonSerializable(typeof(AlpacaOptions))]
[JsonSerializable(typeof(PolygonOptions))]
[JsonSerializable(typeof(IBOptions))]
[JsonSerializable(typeof(DataSourceConfig))]
[JsonSerializable(typeof(DataSourceConfig[]))]
[JsonSerializable(typeof(List<DataSourceConfig>))]
[JsonSerializable(typeof(DataSourcesConfig))]
[JsonSerializable(typeof(FailoverRuleConfig))]
[JsonSerializable(typeof(FailoverRuleConfig[]))]
[JsonSerializable(typeof(List<FailoverRuleConfig>))]
[JsonSerializable(typeof(SymbolRegistry))]
[JsonSerializable(typeof(SymbolRegistryEntry))]
[JsonSerializable(typeof(List<SymbolRegistryEntry>))]
[JsonSerializable(typeof(SymbolAlias))]
[JsonSerializable(typeof(List<SymbolAlias>))]
[JsonSerializable(typeof(SymbolIdentifiers))]
[JsonSerializable(typeof(SymbolClassification))]
[JsonSerializable(typeof(List<CorporateActionRef>))]
[JsonSerializable(typeof(CorporateActionRef))]
[JsonSerializable(typeof(IdentifierIndex))]
[JsonSerializable(typeof(SymbolRegistryStatistics))]
[JsonSerializable(typeof(SymbolLookupResult))]
[JsonSerializable(typeof(SymbolMappingsConfig))]
[JsonSerializable(typeof(SymbolMappingConfig))]
[JsonSerializable(typeof(SymbolMappingConfig[]))]
[JsonSerializable(typeof(List<SymbolMappingConfig>))]
[JsonSerializable(typeof(BackfillConfig))]
[JsonSerializable(typeof(BackfillJobsConfig))]
[JsonSerializable(typeof(ScheduledBackfillConfig))]
[JsonSerializable(typeof(DefaultScheduleConfig))]
[JsonSerializable(typeof(DefaultScheduleConfig[]))]
[JsonSerializable(typeof(List<DefaultScheduleConfig>))]
[JsonSerializable(typeof(BackfillProvidersConfig))]
[JsonSerializable(typeof(YahooFinanceConfig))]
[JsonSerializable(typeof(NasdaqDataLinkConfig))]
[JsonSerializable(typeof(StooqConfig))]
[JsonSerializable(typeof(OpenFigiConfig))]
[JsonSerializable(typeof(AlpacaBackfillConfig))]
[JsonSerializable(typeof(TiingoConfig))]
[JsonSerializable(typeof(PolygonConfig))]
[JsonSerializable(typeof(AlphaVantageConfig))]
[JsonSerializable(typeof(FinnhubConfig))]
[JsonSerializable(typeof(FredConfig))]
[JsonSerializable(typeof(RobinhoodConfig))]
[JsonSerializable(typeof(DerivativesConfig))]
[JsonSerializable(typeof(IndexOptionsConfig))]
// Canonicalization enums
[JsonSerializable(typeof(CanonicalTradeCondition))]
[JsonSerializable(typeof(CanonicalTradeCondition[]))]
[JsonSerializable(typeof(MarketEventTier))]
public partial class MarketDataJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Pre-configured options for high-performance serialization.
    /// Use for storage, wire protocols, and any performance-critical path.
    /// - Compact output (no indentation)
    /// - CamelCase property naming
    /// - Null values omitted
    /// - Case-insensitive property matching on read
    /// - Source-generated serializers (no reflection)
    /// </summary>
    public static readonly JsonSerializerOptions HighPerformanceOptions = new()
    {
        TypeInfoResolver = Default,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Pre-configured options for pretty-printed output (debugging, config files).
    /// - Indented output for readability
    /// - CamelCase property naming
    /// - Null values omitted
    /// - Case-insensitive property matching on read
    /// - Source-generated serializers (no reflection)
    /// </summary>
    public static readonly JsonSerializerOptions PrettyPrintOptions = new()
    {
        TypeInfoResolver = Default,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

/// <summary>
/// Dedicated JSON context for Alpaca wire messages.
///
/// NOTE: Alpaca payloads use both "T" and "t" keys in the same object.
/// They must be parsed with case-sensitive matching to avoid source-generation
/// property collisions.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNameCaseInsensitive = false,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(AlpacaTradeMessage))]
[JsonSerializable(typeof(AlpacaQuoteMessage))]
[JsonSerializable(typeof(AlpacaMessage[]))]
public partial class AlpacaJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Alpaca trade message structure for source-generated parsing.
/// </summary>
public sealed record AlpacaTradeMessage
{
    [JsonPropertyName("T")]
    public string? Type { get; init; }

    [JsonPropertyName("S")]
    public string? Symbol { get; init; }

    [JsonPropertyName("p")]
    public decimal Price { get; init; }

    [JsonPropertyName("s")]
    public long Size { get; init; }

    [JsonPropertyName("t")]
    public string? Timestamp { get; init; }

    [JsonPropertyName("x")]
    public string? Exchange { get; init; }

    [JsonPropertyName("i")]
    public long TradeId { get; init; }

    [JsonPropertyName("z")]
    public string? Tape { get; init; }

    [JsonPropertyName("c")]
    public string[]? Conditions { get; init; }
}

/// <summary>
/// Alpaca quote message structure for source-generated parsing.
/// </summary>
public sealed record AlpacaQuoteMessage
{
    [JsonPropertyName("T")]
    public string? Type { get; init; }

    [JsonPropertyName("S")]
    public string? Symbol { get; init; }

    [JsonPropertyName("bp")]
    public decimal BidPrice { get; init; }

    [JsonPropertyName("bs")]
    public long BidSize { get; init; }

    [JsonPropertyName("ap")]
    public decimal AskPrice { get; init; }

    [JsonPropertyName("as")]
    public long AskSize { get; init; }

    [JsonPropertyName("t")]
    public string? Timestamp { get; init; }

    [JsonPropertyName("bx")]
    public string? BidExchange { get; init; }

    [JsonPropertyName("ax")]
    public string? AskExchange { get; init; }

    [JsonPropertyName("z")]
    public string? Tape { get; init; }

    [JsonPropertyName("c")]
    public string[]? Conditions { get; init; }
}

/// <summary>
/// Generic Alpaca message for initial type detection.
/// </summary>
public sealed record AlpacaMessage
{
    [JsonPropertyName("T")]
    public string? Type { get; init; }

    [JsonPropertyName("msg")]
    public string? Message { get; init; }

    [JsonPropertyName("code")]
    public int? Code { get; init; }
}

/// <summary>
/// High-performance JSON utilities using source-generated serializers.
/// </summary>
public static class HighPerformanceJson
{
    /// <summary>
    /// Serialize a market event to JSON bytes without reflection.
    /// </summary>
    public static byte[] SerializeToUtf8Bytes(MarketEvent evt)
    {
        return JsonSerializer.SerializeToUtf8Bytes(evt, MarketDataJsonContext.Default.MarketEvent);
    }

    /// <summary>
    /// Serialize a market event to a string without reflection.
    /// </summary>
    public static string Serialize(MarketEvent evt)
    {
        return JsonSerializer.Serialize(evt, MarketDataJsonContext.Default.MarketEvent);
    }

    /// <summary>
    /// Deserialize a market event from JSON bytes without reflection.
    /// </summary>
    public static MarketEvent? DeserializeFromUtf8Bytes(ReadOnlySpan<byte> utf8Json)
    {
        return JsonSerializer.Deserialize(utf8Json, MarketDataJsonContext.Default.MarketEvent);
    }

    /// <summary>
    /// Deserialize a market event from a string without reflection.
    /// </summary>
    public static MarketEvent? Deserialize(string json)
    {
        return JsonSerializer.Deserialize(json, MarketDataJsonContext.Default.MarketEvent);
    }

    /// <summary>
    /// Parse an Alpaca trade message without reflection.
    /// </summary>
    public static AlpacaTradeMessage? ParseAlpacaTrade(ReadOnlySpan<byte> utf8Json)
    {
        return JsonSerializer.Deserialize(utf8Json, AlpacaJsonContext.Default.AlpacaTradeMessage);
    }

    /// <summary>
    /// Parse an Alpaca quote message without reflection.
    /// </summary>
    public static AlpacaQuoteMessage? ParseAlpacaQuote(ReadOnlySpan<byte> utf8Json)
    {
        return JsonSerializer.Deserialize(utf8Json, AlpacaJsonContext.Default.AlpacaQuoteMessage);
    }

    /// <summary>
    /// Parse Alpaca messages array without reflection.
    /// </summary>
    public static AlpacaMessage[]? ParseAlpacaMessages(ReadOnlySpan<byte> utf8Json)
    {
        return JsonSerializer.Deserialize(utf8Json, AlpacaJsonContext.Default.AlpacaMessageArray);
    }

    /// <summary>
    /// Write a market event to a Utf8JsonWriter without reflection.
    /// </summary>
    public static void WriteTo(Utf8JsonWriter writer, MarketEvent evt)
    {
        JsonSerializer.Serialize(writer, evt, MarketDataJsonContext.Default.MarketEvent);
    }

    /// <summary>
    /// Serialize multiple events efficiently to a stream.
    /// </summary>
    public static async Task SerializeToStreamAsync(
        Stream stream,
        IEnumerable<MarketEvent> events,
        CancellationToken ct = default)
    {
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = true
        });

        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            JsonSerializer.Serialize(writer, evt, MarketDataJsonContext.Default.MarketEvent);
            await stream.WriteAsync(NewlineBytes, ct);
            await writer.FlushAsync(ct);
            writer.Reset();
        }
    }

    private static readonly byte[] NewlineBytes = "\n"u8.ToArray();
}

/// <summary>
/// Benchmark comparison utilities for JSON performance testing.
/// </summary>
public static class JsonBenchmarkUtilities
{
    /// <summary>
    /// Create a sample trade event for benchmarking.
    /// </summary>
    public static MarketEvent CreateSampleTradeEvent()
    {
        return MarketEvent.Trade(
            DateTimeOffset.UtcNow,
            "SPY",
            new Trade(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "SPY",
                Price: 450.25m,
                Size: 100,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: 12345,
                StreamId: "ALPACA",
                Venue: "NYSE"
            ));
    }

    /// <summary>
    /// Create sample events for benchmarking.
    /// </summary>
    public static IEnumerable<MarketEvent> CreateSampleEvents(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var symbol = $"SYM{i % 100}";
            yield return MarketEvent.Trade(
                DateTimeOffset.UtcNow,
                symbol,
                new Trade(
                    Timestamp: DateTimeOffset.UtcNow,
                    Symbol: symbol,
                    Price: 100m + (i % 100) * 0.01m,
                    Size: 100 + i % 1000,
                    Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                    SequenceNumber: i,
                    StreamId: "BENCH",
                    Venue: "TEST"
                ));
        }
    }
}
