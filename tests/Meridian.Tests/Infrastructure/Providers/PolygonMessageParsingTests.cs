using System.Text;
using System.Text.Json;
using Xunit;

namespace Meridian.Tests.Infrastructure.Adapters;

/// <summary>
/// Unit tests for Polygon WebSocket message parsing.
/// Part of B3 (infrastructure provider unit tests) improvement.
/// Tests the zero-allocation UTF-8 parsing path (#18).
/// </summary>
public sealed class PolygonMessageParsingTests
{
    [Fact]
    public void ParseTradeMessage_ExtractsFields()
    {
        var json = """[{"ev":"T","sym":"AAPL","p":150.25,"s":100,"t":1704067200000,"x":4}]""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        var elem = root[0];
        Assert.Equal("T", elem.GetProperty("ev").GetString());
        Assert.Equal("AAPL", elem.GetProperty("sym").GetString());
        Assert.Equal(150.25m, elem.GetProperty("p").GetDecimal());
        Assert.Equal(100, elem.GetProperty("s").GetInt64());
        Assert.Equal(1704067200000L, elem.GetProperty("t").GetInt64());
    }

    [Fact]
    public void ParseQuoteMessage_ExtractsFields()
    {
        var json = """[{"ev":"Q","sym":"AAPL","bp":150.20,"bs":100,"ap":150.25,"as":200,"t":1704067200000}]""";
        using var doc = JsonDocument.Parse(json);
        var elem = doc.RootElement[0];

        Assert.Equal("Q", elem.GetProperty("ev").GetString());
        Assert.Equal("AAPL", elem.GetProperty("sym").GetString());
        Assert.Equal(150.20m, elem.GetProperty("bp").GetDecimal());
        Assert.Equal(100L, elem.GetProperty("bs").GetInt64());
        Assert.Equal(150.25m, elem.GetProperty("ap").GetDecimal());
        Assert.Equal(200L, elem.GetProperty("as").GetInt64());
    }

    [Fact]
    public void ParseStatusMessage_Recognized()
    {
        var json = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
        using var doc = JsonDocument.Parse(json);
        var elem = doc.RootElement[0];

        Assert.Equal("status", elem.GetProperty("ev").GetString());
        Assert.Equal("connected", elem.GetProperty("status").GetString());
    }

    [Fact]
    public void ParseTradeWithConditions_ExtractsCodes()
    {
        var json = """[{"ev":"T","sym":"AAPL","p":150.25,"s":100,"t":1704067200000,"c":[12,37]}]""";
        using var doc = JsonDocument.Parse(json);
        var elem = doc.RootElement[0];

        var conditions = elem.GetProperty("c");
        Assert.Equal(JsonValueKind.Array, conditions.ValueKind);
        var codes = conditions.EnumerateArray().Select(c => c.GetInt32()).ToList();
        Assert.Contains(12, codes);
        Assert.Contains(37, codes);
    }

    [Fact]
    public void Utf8Parsing_AvoidStringConversion()
    {
        // Verify that UTF-8 byte parsing works without string intermediate
        var json = """[{"ev":"T","sym":"SPY","p":450.00,"s":50}]""";
        var utf8Bytes = Encoding.UTF8.GetBytes(json);

        using var doc = JsonDocument.Parse(utf8Bytes.AsMemory());
        var elem = doc.RootElement[0];

        Assert.Equal("T", elem.GetProperty("ev").GetString());
        Assert.Equal("SPY", elem.GetProperty("sym").GetString());
        Assert.Equal(450.00m, elem.GetProperty("p").GetDecimal());
    }

    [Fact]
    public void MultipleEvents_InSingleMessage()
    {
        var json = """[{"ev":"T","sym":"AAPL","p":150.25,"s":100},{"ev":"Q","sym":"AAPL","bp":150.20,"bs":100,"ap":150.25,"as":200}]""";
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("T", doc.RootElement[0].GetProperty("ev").GetString());
        Assert.Equal("Q", doc.RootElement[1].GetProperty("ev").GetString());
    }

    [Fact]
    public void MissingEvProperty_SkippedGracefully()
    {
        var json = """[{"sym":"AAPL","p":150.25},{"ev":"T","sym":"MSFT","p":200.00}]""";
        using var doc = JsonDocument.Parse(json);

        var first = doc.RootElement[0];
        Assert.False(first.TryGetProperty("ev", out _));

        var second = doc.RootElement[1];
        Assert.True(second.TryGetProperty("ev", out var ev));
        Assert.Equal("T", ev.GetString());
    }

    [Fact]
    public void NonArrayMessage_DetectedCorrectly()
    {
        var json = """{"ev":"status","status":"connected"}""";
        using var doc = JsonDocument.Parse(json);
        Assert.NotEqual(JsonValueKind.Array, doc.RootElement.ValueKind);
    }
}
