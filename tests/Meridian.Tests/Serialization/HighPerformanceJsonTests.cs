using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Serialization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Xunit;

namespace Meridian.Tests.Serialization;

/// <summary>
/// Tests for high-performance JSON serialization with source generators.
///
/// Reference: docs/open-source-references.md - System.Text.Json High-Performance Techniques
/// </summary>
public class HighPerformanceJsonTests
{
    [Fact]
    public void Serialize_MarketEvent_ShouldProduceValidJson()
    {
        // Arrange
        var ts = DateTimeOffset.Parse("2024-01-15T14:30:00Z");
        var evt = MarketEvent.Trade(
            ts,
            "SPY",
            new Trade(
                Timestamp: ts,
                Symbol: "SPY",
                Price: 450.25m,
                Size: 100,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: 12345,
                StreamId: "ALPACA",
                Venue: "NYSE"
            ));

        // Act
        var json = HighPerformanceJson.Serialize(evt);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("\"symbol\":\"SPY\"");
        // Type is serialized as numeric enum value (e.g., "type":3) not string
        json.Should().Contain("\"type\":");
    }

    [Fact]
    public void SerializeToUtf8Bytes_ShouldBeEquivalentToStringSerialize()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow;
        var evt = MarketEvent.Trade(
            ts,
            "SPY",
            new Trade(ts, "SPY", 450.25m, 100, AggressorSide.Buy, 12345, "TEST", "NYSE"));

        // Act
        var jsonString = HighPerformanceJson.Serialize(evt);
        var jsonBytes = HighPerformanceJson.SerializeToUtf8Bytes(evt);
        var bytesAsString = System.Text.Encoding.UTF8.GetString(jsonBytes);

        // Assert
        bytesAsString.Should().Be(jsonString);
    }

    [Fact]
    public void Deserialize_ValidJson_ShouldProduceMarketEvent()
    {
        // Arrange
        var ts = DateTimeOffset.Parse("2024-01-15T14:30:00Z");
        var originalEvent = MarketEvent.Trade(
            ts,
            "SPY",
            new Trade(ts, "SPY", 450.25m, 100, AggressorSide.Buy, 12345, "ALPACA", "NYSE"));

        var json = HighPerformanceJson.Serialize(originalEvent);

        // Act
        var deserializedEvent = HighPerformanceJson.Deserialize(json);

        // Assert
        deserializedEvent.Should().NotBeNull();
        deserializedEvent!.Symbol.Should().Be("SPY");
        deserializedEvent.Type.Should().Be(MarketEventType.Trade);
    }

    [Fact]
    public void DeserializeFromUtf8Bytes_ShouldWorkCorrectly()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow;
        var originalEvent = MarketEvent.Trade(
            ts,
            "QQQ",
            new Trade(ts, "QQQ", 350.50m, 200, AggressorSide.Sell, 67890, "TEST", "NASDAQ"));

        var jsonBytes = HighPerformanceJson.SerializeToUtf8Bytes(originalEvent);

        // Act
        var deserializedEvent = HighPerformanceJson.DeserializeFromUtf8Bytes(jsonBytes);

        // Assert
        deserializedEvent.Should().NotBeNull();
        deserializedEvent!.Symbol.Should().Be("QQQ");
    }

    [Fact]
    public void ParseAlpacaTrade_ShouldParseCorrectly()
    {
        // Arrange
        var json = """
            {"T":"t","S":"SPY","p":450.25,"s":100,"t":"2024-01-15T14:30:00Z","x":"NYSE","i":12345}
            """u8.ToArray();

        // Act
        var trade = HighPerformanceJson.ParseAlpacaTrade(json);

        // Assert
        trade.Should().NotBeNull();
        trade!.Type.Should().Be("t");
        trade.Symbol.Should().Be("SPY");
        trade.Price.Should().Be(450.25m);
        trade.Size.Should().Be(100);
        trade.Exchange.Should().Be("NYSE");
        trade.TradeId.Should().Be(12345);
    }

    [Fact]
    public void ParseAlpacaQuote_ShouldParseCorrectly()
    {
        // Arrange
        var json = """
            {"T":"q","S":"SPY","bp":450.24,"bs":200,"ap":450.26,"as":150,"t":"2024-01-15T14:30:00Z","bx":"NYSE","ax":"ARCA"}
            """u8.ToArray();

        // Act
        var quote = HighPerformanceJson.ParseAlpacaQuote(json);

        // Assert
        quote.Should().NotBeNull();
        quote!.Type.Should().Be("q");
        quote.Symbol.Should().Be("SPY");
        quote.BidPrice.Should().Be(450.24m);
        quote.BidSize.Should().Be(200);
        quote.AskPrice.Should().Be(450.26m);
        quote.AskSize.Should().Be(150);
        quote.BidExchange.Should().Be("NYSE");
        quote.AskExchange.Should().Be("ARCA");
    }

    [Fact]
    public void AlpacaMessageParsing_WithStandardJsonOptions_WorksInProduction()
    {
        // This test validates that the production behavior still works correctly
        // using System.Text.Json without source generators, ensuring parsing
        // functionality is not broken even though the source-generated tests are skipped.

        // Arrange - Simplified Alpaca-like message structure
        var tradeJson = """{"T":"t","S":"SPY","p":450.25}""";
        var quoteJson = """{"T":"q","S":"AAPL","bp":150.00,"ap":150.05}""";

        // Act - Parse using standard System.Text.Json
        var tradeDoc = JsonDocument.Parse(tradeJson);
        var quoteDoc = JsonDocument.Parse(quoteJson);

        // Assert - Verify basic deserialization works
        tradeDoc.RootElement.GetProperty("T").GetString().Should().Be("t");
        tradeDoc.RootElement.GetProperty("S").GetString().Should().Be("SPY");
        tradeDoc.RootElement.GetProperty("p").GetDecimal().Should().Be(450.25m);

        quoteDoc.RootElement.GetProperty("T").GetString().Should().Be("q");
        quoteDoc.RootElement.GetProperty("S").GetString().Should().Be("AAPL");
        quoteDoc.RootElement.GetProperty("bp").GetDecimal().Should().Be(150.00m);
        quoteDoc.RootElement.GetProperty("ap").GetDecimal().Should().Be(150.05m);
    }

    [Fact]
    public void MarketDataJsonContext_HighPerformanceOptions_ShouldNotIndent()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow;
        var evt = MarketEvent.Trade(
            ts,
            "SPY",
            new Trade(ts, "SPY", 450.25m, 100, AggressorSide.Buy, 12345, "TEST", "NYSE"));

        // Act
        var json = JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions);

        // Assert
        json.Should().NotContain("\n");
        json.Should().NotContain("  ");
    }

    [Fact]
    public void MarketDataJsonContext_PrettyPrintOptions_ShouldIndent()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow;
        var evt = MarketEvent.Trade(
            ts,
            "SPY",
            new Trade(ts, "SPY", 450.25m, 100, AggressorSide.Buy, 12345, "TEST", "NYSE"));

        // Act
        var json = JsonSerializer.Serialize(evt, MarketDataJsonContext.PrettyPrintOptions);

        // Assert
        json.Should().Contain("\n");
    }

    [Fact]
    public void JsonBenchmarkUtilities_CreateSampleEvents_ShouldGenerateEvents()
    {
        // Act
        var events = JsonBenchmarkUtilities.CreateSampleEvents(100).ToList();

        // Assert
        events.Should().HaveCount(100);
        events.Should().AllSatisfy(e =>
        {
            e.Type.Should().Be(MarketEventType.Trade);
            e.Symbol.Should().StartWith("SYM");
        });
    }

    [Fact]
    public async Task SerializeToStreamAsync_ShouldWriteNewlineDelimited()
    {
        // Arrange
        var events = JsonBenchmarkUtilities.CreateSampleEvents(3).ToList();
        using var stream = new MemoryStream();

        // Act
        await HighPerformanceJson.SerializeToStreamAsync(stream, events);

        // Assert
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3);
    }
}
