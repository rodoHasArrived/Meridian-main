using System.Buffers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Meridian.Application.Serialization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;

namespace Meridian.Benchmarks;

/// <summary>
/// Benchmarks comparing JSON serialization approaches.
/// Compares source-generated vs reflection-based serialization.
///
/// Based on: https://github.com/dotnet/BenchmarkDotNet (MIT)
/// Reference: docs/open-source-references.md #13
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class JsonSerializationBenchmarks
{
    private MarketEvent _tradeEvent = null!;
    private MarketEvent[] _tradeEvents = null!;
    private string _tradeEventJson = null!;
    private byte[] _tradeEventUtf8 = null!;

    private static readonly JsonSerializerOptions ReflectionOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [GlobalSetup]
    public void Setup()
    {
        _tradeEvent = MarketEvent.Trade(
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

        _tradeEvents = Enumerable.Range(0, 1000)
            .Select(i =>
            {
                var symbol = $"SYM{i % 100}";
                return MarketEvent.Trade(
                    DateTimeOffset.UtcNow,
                    symbol,
                    new Trade(
                        Timestamp: DateTimeOffset.UtcNow,
                        Symbol: symbol,
                        Price: 100m + i * 0.01m,
                        Size: 100 + i,
                        Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                        SequenceNumber: i,
                        StreamId: "BENCH",
                        Venue: "TEST"
                    ));
            })
            .ToArray();

        _tradeEventJson = JsonSerializer.Serialize(_tradeEvent, ReflectionOptions);
        _tradeEventUtf8 = JsonSerializer.SerializeToUtf8Bytes(_tradeEvent, ReflectionOptions);
    }

    // Serialization benchmarks

    [Benchmark(Baseline = true)]
    public string Serialize_Reflection()
    {
        return JsonSerializer.Serialize(_tradeEvent, ReflectionOptions);
    }

    [Benchmark]
    public string Serialize_SourceGenerated()
    {
        return HighPerformanceJson.Serialize(_tradeEvent);
    }

    [Benchmark]
    public byte[] SerializeToUtf8_Reflection()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_tradeEvent, ReflectionOptions);
    }

    [Benchmark]
    public byte[] SerializeToUtf8_SourceGenerated()
    {
        return HighPerformanceJson.SerializeToUtf8Bytes(_tradeEvent);
    }

    // Deserialization benchmarks

    [Benchmark]
    public MarketEvent? Deserialize_Reflection()
    {
        return JsonSerializer.Deserialize<MarketEvent>(_tradeEventJson, ReflectionOptions);
    }

    [Benchmark]
    public MarketEvent? Deserialize_SourceGenerated()
    {
        return HighPerformanceJson.Deserialize(_tradeEventJson);
    }

    [Benchmark]
    public MarketEvent? DeserializeUtf8_Reflection()
    {
        return JsonSerializer.Deserialize<MarketEvent>(_tradeEventUtf8, ReflectionOptions);
    }

    [Benchmark]
    public MarketEvent? DeserializeUtf8_SourceGenerated()
    {
        return HighPerformanceJson.DeserializeFromUtf8Bytes(_tradeEventUtf8);
    }

    // Batch serialization benchmarks

    [Benchmark]
    public string[] SerializeBatch_Reflection()
    {
        return _tradeEvents.Select(e => JsonSerializer.Serialize(e, ReflectionOptions)).ToArray();
    }

    [Benchmark]
    public string[] SerializeBatch_SourceGenerated()
    {
        return _tradeEvents.Select(HighPerformanceJson.Serialize).ToArray();
    }

    /// <summary>
    /// Serializes a single event to a pooled <see cref="ArrayBufferWriter{T}"/> using
    /// <see cref="HighPerformanceJson.WriteTo"/>.  No intermediate <c>string</c> is created;
    /// the result bytes can be written directly to a <see cref="Stream"/> (e.g. GZipStream),
    /// eliminating the UTF-16 → UTF-8 transcoding step that <see cref="Serialize_SourceGenerated"/>
    /// requires when the string is ultimately written to a byte-oriented sink.
    /// </summary>
    [Benchmark]
    public int WriteTo_Utf8JsonWriter_Pooled()
    {
        var bufferWriter = new ArrayBufferWriter<byte>(512);
        using var writer = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions { SkipValidation = true });
        HighPerformanceJson.WriteTo(writer, _tradeEvent);
        writer.Flush();
        return bufferWriter.WrittenCount;
    }
}

/// <summary>
/// Benchmarks for Utf8JsonReader vs JsonDocument parsing.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class JsonParsingBenchmarks
{
    private byte[] _alpacaTradeMessage = null!;
    private byte[] _alpacaQuoteMessage = null!;

    [GlobalSetup]
    public void Setup()
    {
        _alpacaTradeMessage = """
            {"T":"t","S":"SPY","p":450.25,"s":100,"t":"2024-01-15T14:30:00Z","x":"NYSE","i":12345}
            """u8.ToArray();

        _alpacaQuoteMessage = """
            {"T":"q","S":"SPY","bp":450.24,"bs":200,"ap":450.26,"as":150,"t":"2024-01-15T14:30:00Z","bx":"NYSE","ax":"ARCA"}
            """u8.ToArray();
    }

    [Benchmark(Baseline = true)]
    public AlpacaTradeMessage? ParseTrade_JsonDocument()
    {
        using var doc = JsonDocument.Parse(_alpacaTradeMessage);
        var root = doc.RootElement;

        return new AlpacaTradeMessage
        {
            Type = root.GetProperty("T").GetString(),
            Symbol = root.GetProperty("S").GetString(),
            Price = root.GetProperty("p").GetDecimal(),
            Size = root.GetProperty("s").GetInt32(),
            Timestamp = root.GetProperty("t").GetString(),
            Exchange = root.GetProperty("x").GetString(),
            TradeId = root.GetProperty("i").GetInt64()
        };
    }

    [Benchmark]
    public AlpacaTradeMessage? ParseTrade_SourceGenerated()
    {
        return HighPerformanceJson.ParseAlpacaTrade(_alpacaTradeMessage);
    }

    [Benchmark]
    public AlpacaQuoteMessage? ParseQuote_JsonDocument()
    {
        using var doc = JsonDocument.Parse(_alpacaQuoteMessage);
        var root = doc.RootElement;

        return new AlpacaQuoteMessage
        {
            Type = root.GetProperty("T").GetString(),
            Symbol = root.GetProperty("S").GetString(),
            BidPrice = root.GetProperty("bp").GetDecimal(),
            BidSize = root.GetProperty("bs").GetInt32(),
            AskPrice = root.GetProperty("ap").GetDecimal(),
            AskSize = root.GetProperty("as").GetInt32(),
            Timestamp = root.GetProperty("t").GetString(),
            BidExchange = root.GetProperty("bx").GetString(),
            AskExchange = root.GetProperty("ax").GetString()
        };
    }

    [Benchmark]
    public AlpacaQuoteMessage? ParseQuote_SourceGenerated()
    {
        return HighPerformanceJson.ParseAlpacaQuote(_alpacaQuoteMessage);
    }
}
