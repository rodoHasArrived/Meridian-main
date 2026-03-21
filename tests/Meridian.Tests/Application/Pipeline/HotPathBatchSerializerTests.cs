using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Core.Performance;
using Xunit;

namespace Meridian.Tests.Pipeline;

/// <summary>
/// Tests for the hot-path batch serializer that converts <see cref="RawTradeEvent"/>
/// and <see cref="RawQuoteEvent"/> structs to JSONL-formatted UTF-8 bytes.
/// </summary>
public class HotPathBatchSerializerTests
{
    private readonly SymbolTable _symbolTable;
    private readonly HotPathBatchSerializer _serializer;

    public HotPathBatchSerializerTests()
    {
        _symbolTable = new SymbolTable();
        _symbolTable.GetOrAdd("SPY");   // ID 1
        _symbolTable.GetOrAdd("AAPL");  // ID 2
        _serializer = new HotPathBatchSerializer(_symbolTable);
    }

    #region Constructor validation

    [Fact]
    public void Constructor_NullSymbolTable_Throws()
    {
        var act = () => new HotPathBatchSerializer(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("symbolTable");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(-1)]
    public void Constructor_SmallBufferCapacity_Throws(int capacity)
    {
        var act = () => new HotPathBatchSerializer(_symbolTable, capacity);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("initialBufferCapacity");
    }

    #endregion

    #region Empty batch tests

    [Fact]
    public void SerializeTrades_EmptyBatch_ReturnsEmpty()
    {
        var result = _serializer.SerializeTrades(ReadOnlySpan<RawTradeEvent>.Empty);
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void SerializeQuotes_EmptyBatch_ReturnsEmpty()
    {
        var result = _serializer.SerializeQuotes(ReadOnlySpan<RawQuoteEvent>.Empty);
        result.IsEmpty.Should().BeTrue();
    }

    #endregion

    #region Trade serialization tests

    [Fact]
    public void SerializeTrades_SingleTrade_ProducesValidJsonlLine()
    {
        var ts = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var symbolId = _symbolTable.GetOrAdd("SPY");
        var trade = new RawTradeEvent(
            timestampTicks: ts.UtcTicks,
            symbolHash: symbolId,
            price: 480.25m,
            size: 100L,
            aggressor: 1,
            sequence: 42L);

        var result = _serializer.SerializeTrades(new[] { trade });
        var json = Encoding.UTF8.GetString(result.Span);

        // Should end with newline
        json.Should().EndWith("\n");

        // Should be valid JSON
        var line = json.TrimEnd('\n');
        var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        root.GetProperty("symbol").GetString().Should().Be("SPY");
        root.GetProperty("price").GetDecimal().Should().Be(480.25m);
        root.GetProperty("size").GetInt64().Should().Be(100L);
        root.GetProperty("aggressor").GetByte().Should().Be(1);
        root.GetProperty("sequence").GetInt64().Should().Be(42L);
    }

    [Fact]
    public void SerializeTrades_BatchOfThree_ProducesThreeJsonlLines()
    {
        var ts = DateTimeOffset.UtcNow;
        var symbolId = _symbolTable.GetOrAdd("SPY");
        var trades = new[]
        {
            new RawTradeEvent(ts.UtcTicks, symbolId, 100m, 10L, 1, 1L),
            new RawTradeEvent(ts.UtcTicks, symbolId, 101m, 20L, 2, 2L),
            new RawTradeEvent(ts.UtcTicks, symbolId, 102m, 30L, 1, 3L),
        };

        var result = _serializer.SerializeTrades(trades);
        var json = Encoding.UTF8.GetString(result.Span);
        var lines = json.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(3);
        foreach (var line in lines)
        {
            var doc = JsonDocument.Parse(line);
            doc.RootElement.GetProperty("symbol").GetString().Should().Be("SPY");
        }
    }

    [Fact]
    public void SerializeTrades_UnknownSymbolHash_EmitsEmptyString()
    {
        var trade = new RawTradeEvent(
            DateTimeOffset.UtcNow.UtcTicks, symbolHash: 9999, price: 1m, size: 1L, aggressor: 0, sequence: 0L);

        var result = _serializer.SerializeTrades(new[] { trade });
        var json = Encoding.UTF8.GetString(result.Span);

        var doc = JsonDocument.Parse(json.TrimEnd('\n'));
        doc.RootElement.GetProperty("symbol").GetString().Should().BeEmpty();
    }

    [Fact]
    public void SerializeTrades_TimestampFieldReflectsUtcTicks()
    {
        var ts = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var symbolId = _symbolTable.GetOrAdd("SPY");
        var trade = new RawTradeEvent(ts.UtcTicks, symbolId, 500m, 50L, 0, 1L);

        var result = _serializer.SerializeTrades(new[] { trade });
        var json = Encoding.UTF8.GetString(result.Span).TrimEnd('\n');
        var doc = JsonDocument.Parse(json);

        var tsString = doc.RootElement.GetProperty("timestamp").GetString();
        tsString.Should().NotBeNullOrEmpty();

        var parsed = DateTimeOffset.Parse(tsString!);
        parsed.UtcTicks.Should().Be(ts.UtcTicks);
    }

    [Fact]
    public void SerializeTrades_PricePreservesDecimalPrecision()
    {
        var symbolId = _symbolTable.GetOrAdd("SPY");
        var exactPrice = 1234.5678901234m;
        var trade = new RawTradeEvent(DateTimeOffset.UtcNow.UtcTicks, symbolId, exactPrice, 1L, 0, 1L);

        var result = _serializer.SerializeTrades(new[] { trade });
        var json = Encoding.UTF8.GetString(result.Span).TrimEnd('\n');
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("price").GetDecimal().Should().Be(exactPrice);
    }

    #endregion

    #region Quote serialization tests

    [Fact]
    public void SerializeQuotes_SingleQuote_ProducesValidJsonlLine()
    {
        var ts = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var symbolId = _symbolTable.GetOrAdd("AAPL");
        var quote = new RawQuoteEvent(
            timestampTicks: ts.UtcTicks,
            symbolHash: symbolId,
            bidPrice: 189.90m,
            bidSize: 500L,
            askPrice: 190.10m,
            askSize: 300L,
            sequence: 77L);

        var result = _serializer.SerializeQuotes(new[] { quote });
        var json = Encoding.UTF8.GetString(result.Span);

        json.Should().EndWith("\n");

        var line = json.TrimEnd('\n');
        var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        root.GetProperty("symbol").GetString().Should().Be("AAPL");
        root.GetProperty("bidPrice").GetDecimal().Should().Be(189.90m);
        root.GetProperty("bidSize").GetInt64().Should().Be(500L);
        root.GetProperty("askPrice").GetDecimal().Should().Be(190.10m);
        root.GetProperty("askSize").GetInt64().Should().Be(300L);
        root.GetProperty("sequence").GetInt64().Should().Be(77L);
    }

    [Fact]
    public void SerializeQuotes_BatchOfTwo_ProducesTwoJsonlLines()
    {
        var ts = DateTimeOffset.UtcNow;
        var symbolId = _symbolTable.GetOrAdd("AAPL");
        var quotes = new[]
        {
            new RawQuoteEvent(ts.UtcTicks, symbolId, 189m, 100L, 190m, 200L, 1L),
            new RawQuoteEvent(ts.UtcTicks, symbolId, 188m, 50L, 189m, 75L, 2L),
        };

        var result = _serializer.SerializeQuotes(quotes);
        var json = Encoding.UTF8.GetString(result.Span);
        var lines = json.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(2);
    }

    #endregion

    #region Buffer reuse tests

    [Fact]
    public void SerializeTrades_CalledTwice_BufferIsReused_NotGrown()
    {
        var symbolId = _symbolTable.GetOrAdd("SPY");
        var trade = new RawTradeEvent(DateTimeOffset.UtcNow.UtcTicks, symbolId, 100m, 10L, 1, 1L);

        var first = _serializer.SerializeTrades(new[] { trade });
        var second = _serializer.SerializeTrades(new[] { trade });

        // Both calls should produce the same content
        first.Span.SequenceEqual(second.Span).Should().BeTrue();
    }

    [Fact]
    public void SerializeQuotes_AfterSerializeTrades_DoesNotRetainTradeData()
    {
        var symbolId = _symbolTable.GetOrAdd("SPY");
        var trade = new RawTradeEvent(DateTimeOffset.UtcNow.UtcTicks, symbolId, 999m, 1L, 0, 1L);
        _serializer.SerializeTrades(new[] { trade });

        var quoteId = _symbolTable.GetOrAdd("AAPL");
        var quote = new RawQuoteEvent(DateTimeOffset.UtcNow.UtcTicks, quoteId, 100m, 1L, 101m, 1L, 1L);
        var result = _serializer.SerializeQuotes(new[] { quote });
        var json = Encoding.UTF8.GetString(result.Span);

        // Must not contain trade-only fields from previous call
        json.Should().NotContain("\"price\"");
        json.Should().Contain("\"bidPrice\"");
    }

    #endregion
}
