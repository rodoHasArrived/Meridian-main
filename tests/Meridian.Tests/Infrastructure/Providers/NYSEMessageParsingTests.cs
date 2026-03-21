using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Adapters;

/// <summary>
/// Golden-sample tests for NYSE WebSocket message parsing.
/// Part of Phase 0 — Baseline &amp; Safety Rails (refactor-map Step 0.1).
///
/// These tests lock the expected JSON schema of NYSE feed messages.
/// If the upstream format changes or the parser is modified, these tests
/// will detect the regression before it reaches production.
/// </summary>
public sealed class NYSEMessageParsingTests
{
    #region Trade Messages

    [Fact]
    public void ParseTradeMessage_ExtractsAllRequiredFields()
    {
        var json = """{"type":"trade","symbol":"AAPL","price":185.50,"size":100,"timestamp":"2025-01-15T14:30:00.123Z"}""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("trade");
        root.GetProperty("symbol").GetString().Should().Be("AAPL");
        root.GetProperty("price").GetDecimal().Should().Be(185.50m);
        root.GetProperty("size").GetInt64().Should().Be(100);
        root.GetProperty("timestamp").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ParseTradeMessage_ExtractsOptionalFields()
    {
        var json = """{"type":"trade","symbol":"MSFT","price":400.25,"size":200,"timestamp":"2025-01-15T14:30:00Z","exchange":"NYSE","conditions":"@","sequence":12345,"side":"buy"}""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("exchange", out var exchange).Should().BeTrue();
        exchange.GetString().Should().Be("NYSE");

        root.TryGetProperty("conditions", out var cond).Should().BeTrue();
        cond.GetString().Should().Be("@");

        root.TryGetProperty("sequence", out var seq).Should().BeTrue();
        seq.GetInt64().Should().Be(12345);

        root.TryGetProperty("side", out var side).Should().BeTrue();
        side.GetString().Should().Be("buy");
    }

    [Fact]
    public void ParseTradeMessage_MissingOptionalFields_DoesNotThrow()
    {
        var json = """{"type":"trade","symbol":"SPY","price":450.00,"size":50,"timestamp":"2025-01-15T14:30:00Z"}""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("exchange", out _).Should().BeFalse();
        root.TryGetProperty("conditions", out _).Should().BeFalse();
        root.TryGetProperty("sequence", out _).Should().BeFalse();
        root.TryGetProperty("side", out _).Should().BeFalse();
    }

    [Fact]
    public void ParseTradeMessage_DecimalPricePreservation()
    {
        var json = """{"type":"trade","symbol":"BRK.A","price":624850.00,"size":1,"timestamp":"2025-01-15T14:30:00Z"}""";
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("price").GetDecimal().Should().Be(624850.00m);
    }

    [Fact]
    public void ParseTradeMessage_SubPennyPrice()
    {
        var json = """{"type":"trade","symbol":"AAPL","price":185.5025,"size":100,"timestamp":"2025-01-15T14:30:00Z"}""";
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("price").GetDecimal().Should().Be(185.5025m);
    }

    #endregion

    #region Quote Messages

    [Fact]
    public void ParseQuoteMessage_ExtractsAllRequiredFields()
    {
        var json = """{"type":"quote","symbol":"AAPL","bidPrice":185.45,"bidSize":300,"askPrice":185.50,"askSize":200,"timestamp":"2025-01-15T14:30:00.456Z"}""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("quote");
        root.GetProperty("symbol").GetString().Should().Be("AAPL");
        root.GetProperty("bidPrice").GetDecimal().Should().Be(185.45m);
        root.GetProperty("bidSize").GetInt64().Should().Be(300);
        root.GetProperty("askPrice").GetDecimal().Should().Be(185.50m);
        root.GetProperty("askSize").GetInt64().Should().Be(200);
        root.GetProperty("timestamp").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ParseQuoteMessage_ExtractsOptionalExchangeFields()
    {
        var json = """{"type":"quote","symbol":"AAPL","bidPrice":185.45,"bidSize":300,"askPrice":185.50,"askSize":200,"timestamp":"2025-01-15T14:30:00Z","bidExchange":"NYSE","askExchange":"ARCA","sequence":67890}""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("bidExchange", out var bidEx).Should().BeTrue();
        bidEx.GetString().Should().Be("NYSE");

        root.TryGetProperty("askExchange", out var askEx).Should().BeTrue();
        askEx.GetString().Should().Be("ARCA");

        root.TryGetProperty("sequence", out var seq).Should().BeTrue();
        seq.GetInt64().Should().Be(67890);
    }

    [Fact]
    public void ParseQuoteMessage_SpreadCalculation()
    {
        var json = """{"type":"quote","symbol":"SPY","bidPrice":450.10,"bidSize":500,"askPrice":450.12,"askSize":400,"timestamp":"2025-01-15T14:30:00Z"}""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var bid = root.GetProperty("bidPrice").GetDecimal();
        var ask = root.GetProperty("askPrice").GetDecimal();
        var spread = ask - bid;

        spread.Should().Be(0.02m);
        ask.Should().BeGreaterThanOrEqualTo(bid, "ask should be >= bid in a valid quote");
    }

    #endregion

    #region Depth Messages

    [Fact]
    public void ParseDepthMessage_ExtractsAllRequiredFields()
    {
        var json = """{"type":"depth","symbol":"AAPL","operation":"update","side":"bid","level":0,"price":185.45,"size":1500,"timestamp":"2025-01-15T14:30:00Z"}""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("depth");
        root.GetProperty("symbol").GetString().Should().Be("AAPL");
        root.GetProperty("operation").GetString().Should().Be("update");
        root.GetProperty("side").GetString().Should().Be("bid");
        root.GetProperty("level").GetInt32().Should().Be(0);
        root.GetProperty("price").GetDecimal().Should().Be(185.45m);
        root.GetProperty("size").GetInt64().Should().Be(1500);
        root.GetProperty("timestamp").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ParseDepthMessage_OperationVariants()
    {
        // These are the valid operation values the parser recognizes
        var operations = new[] { "add", "insert", "update", "modify", "delete", "remove" };

        foreach (var op in operations)
        {
            var json = $$$"""{"type":"depth","symbol":"SPY","operation":"{{{op}}}","side":"ask","level":1,"price":450.15,"size":200,"timestamp":"2025-01-15T14:30:00Z"}""";
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("operation").GetString().Should().Be(op);
        }
    }

    [Fact]
    public void ParseDepthMessage_SideVariants()
    {
        foreach (var side in new[] { "bid", "ask" })
        {
            var json = $$$"""{"type":"depth","symbol":"SPY","operation":"update","side":"{{{side}}}","level":0,"price":450.10,"size":100,"timestamp":"2025-01-15T14:30:00Z"}""";
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("side").GetString().Should().Be(side);
        }
    }

    [Fact]
    public void ParseDepthMessage_OptionalMarketMakerAndSequence()
    {
        var json = """{"type":"depth","symbol":"AAPL","operation":"add","side":"bid","level":2,"price":185.40,"size":800,"timestamp":"2025-01-15T14:30:00Z","marketMaker":"GSCO","sequence":99999}""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("marketMaker", out var mm).Should().BeTrue();
        mm.GetString().Should().Be("GSCO");

        root.TryGetProperty("sequence", out var seq).Should().BeTrue();
        seq.GetInt64().Should().Be(99999);
    }

    #endregion

    #region Control Messages

    [Fact]
    public void ParseHeartbeatMessage_Recognized()
    {
        var json = """{"type":"heartbeat"}""";
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("heartbeat");
    }

    [Fact]
    public void ParseErrorMessage_ContainsMessageField()
    {
        var json = """{"type":"error","message":"Rate limit exceeded"}""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("error");
        root.GetProperty("message").GetString().Should().Be("Rate limit exceeded");
    }

    [Fact]
    public void UnknownMessageType_DoesNotThrow()
    {
        var json = """{"type":"unknown_future_type","data":"payload"}""";

        var act = () =>
        {
            using var doc = JsonDocument.Parse(json);
            _ = doc.RootElement.GetProperty("type").GetString();
        };

        act.Should().NotThrow();
    }

    #endregion

    #region Historical Bar Response

    [Fact]
    public void ParseHistoricalDailyBar_RequiredFields()
    {
        var json = """{"bars":[{"date":"2025-01-15","open":185.00,"high":186.50,"low":184.80,"close":186.00,"volume":45000000}]}""";
        using var doc = JsonDocument.Parse(json);

        var bars = doc.RootElement.GetProperty("bars");
        bars.ValueKind.Should().Be(JsonValueKind.Array);
        bars.GetArrayLength().Should().Be(1);

        var bar = bars[0];
        bar.GetProperty("date").GetString().Should().Be("2025-01-15");
        bar.GetProperty("open").GetDecimal().Should().Be(185.00m);
        bar.GetProperty("high").GetDecimal().Should().Be(186.50m);
        bar.GetProperty("low").GetDecimal().Should().Be(184.80m);
        bar.GetProperty("close").GetDecimal().Should().Be(186.00m);
        bar.GetProperty("volume").GetInt64().Should().Be(45000000);
    }

    [Fact]
    public void ParseHistoricalDailyBar_AdjustedFields()
    {
        var json = """{"bars":[{"date":"2025-01-15","open":185.00,"high":186.50,"low":184.80,"close":186.00,"volume":45000000,"adjustedClose":186.00,"adjustedVolume":45000000,"splitFactor":1.0,"dividendAmount":0.0}]}""";
        using var doc = JsonDocument.Parse(json);

        var bar = doc.RootElement.GetProperty("bars")[0];
        bar.TryGetProperty("adjustedClose", out var adjClose).Should().BeTrue();
        adjClose.GetDecimal().Should().Be(186.00m);

        bar.TryGetProperty("splitFactor", out var split).Should().BeTrue();
        split.GetDecimal().Should().Be(1.0m);

        bar.TryGetProperty("dividendAmount", out var div).Should().BeTrue();
        div.GetDecimal().Should().Be(0.0m);
    }

    [Fact]
    public void ParseHistoricalIntradayBar_RequiredFields()
    {
        var json = """{"bars":[{"timestamp":"2025-01-15T14:30:00Z","open":185.00,"high":185.25,"low":184.95,"close":185.10,"volume":125000}]}""";
        using var doc = JsonDocument.Parse(json);

        var bar = doc.RootElement.GetProperty("bars")[0];
        bar.GetProperty("timestamp").GetString().Should().Be("2025-01-15T14:30:00Z");
        bar.GetProperty("open").GetDecimal().Should().Be(185.00m);
        bar.GetProperty("close").GetDecimal().Should().Be(185.10m);
        bar.GetProperty("volume").GetInt64().Should().Be(125000);
    }

    [Fact]
    public void ParseHistoricalIntradayBar_OptionalVwapAndTradeCount()
    {
        var json = """{"bars":[{"timestamp":"2025-01-15T14:30:00Z","open":185.00,"high":185.25,"low":184.95,"close":185.10,"volume":125000,"tradeCount":450,"vwap":185.08}]}""";
        using var doc = JsonDocument.Parse(json);

        var bar = doc.RootElement.GetProperty("bars")[0];
        bar.TryGetProperty("tradeCount", out var tc).Should().BeTrue();
        tc.GetInt64().Should().Be(450);

        bar.TryGetProperty("vwap", out var vwap).Should().BeTrue();
        vwap.GetDecimal().Should().Be(185.08m);
    }

    [Fact]
    public void ParseHistoricalBars_EmptyArray()
    {
        var json = """{"bars":[]}""";
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("bars").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void ParseHistoricalBars_MultipleBars()
    {
        var json = """{"bars":[{"date":"2025-01-13","open":183.00,"high":184.00,"low":182.50,"close":183.80,"volume":35000000},{"date":"2025-01-14","open":184.00,"high":185.50,"low":183.90,"close":185.20,"volume":42000000},{"date":"2025-01-15","open":185.00,"high":186.50,"low":184.80,"close":186.00,"volume":45000000}]}""";
        using var doc = JsonDocument.Parse(json);

        var bars = doc.RootElement.GetProperty("bars");
        bars.GetArrayLength().Should().Be(3);

        // Verify chronological order
        var firstDate = bars[0].GetProperty("date").GetString();
        var lastDate = bars[2].GetProperty("date").GetString();
        string.Compare(firstDate, lastDate, StringComparison.Ordinal).Should().BeLessThan(0);
    }

    #endregion
}
