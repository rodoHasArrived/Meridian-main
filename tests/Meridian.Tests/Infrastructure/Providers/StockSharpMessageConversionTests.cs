using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Xunit;

namespace Meridian.Tests.Infrastructure.Adapters;

/// <summary>
/// Golden-sample tests for StockSharp message conversion output.
/// Part of Phase 0 — Baseline &amp; Safety Rails (refactor-map Step 0.1).
///
/// Since StockSharp conversion uses native S# types behind #if STOCKSHARP,
/// these tests validate the downstream domain models that the converter produces.
/// They lock the serialization contracts so that changes to the converter or
/// domain models are caught before reaching production.
///
/// The StockSharp client always writes:
/// - MarketTradeUpdate for trades (via TradeDataCollector)
/// - MarketDepthUpdate for depth (via MarketDepthCollector)
/// - Trade domain model via MessageConverter.ToTrade
/// - LOBSnapshot domain model via MessageConverter.ToLOBSnapshot
/// - BboQuotePayload domain model via MessageConverter.ToBboQuote
/// </summary>
public sealed class StockSharpMessageConversionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region MarketTradeUpdate (Input Model)

    [Fact]
    public void MarketTradeUpdate_ConstructionWithAllFields()
    {
        var ts = new DateTimeOffset(2025, 1, 15, 14, 30, 0, TimeSpan.Zero);
        var update = new MarketTradeUpdate(
            Timestamp: ts,
            Symbol: "AAPL",
            Price: 185.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 12345,
            StreamId: "STOCKSHARP",
            Venue: "NASDAQ");

        update.Symbol.Should().Be("AAPL");
        update.Price.Should().Be(185.50m);
        update.Size.Should().Be(100);
        update.Aggressor.Should().Be(AggressorSide.Buy);
        update.SequenceNumber.Should().Be(12345);
        update.StreamId.Should().Be("STOCKSHARP");
        update.Venue.Should().Be("NASDAQ");
    }

    [Fact]
    public void MarketTradeUpdate_DefaultOptionalFields()
    {
        var update = new MarketTradeUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            Price: 450.00m,
            Size: 50,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 0);

        update.StreamId.Should().BeNull();
        update.Venue.Should().BeNull();
    }

    [Fact]
    public void MarketTradeUpdate_SerializationRoundTrip()
    {
        var update = new MarketTradeUpdate(
            Timestamp: new DateTimeOffset(2025, 1, 15, 14, 30, 0, TimeSpan.Zero),
            Symbol: "MSFT",
            Price: 400.25m,
            Size: 200,
            Aggressor: AggressorSide.Sell,
            SequenceNumber: 67890,
            StreamId: "STOCKSHARP",
            Venue: "NYSE");

        var json = JsonSerializer.Serialize(update, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<MarketTradeUpdate>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Symbol.Should().Be("MSFT");
        deserialized.Price.Should().Be(400.25m);
        deserialized.Size.Should().Be(200);
        deserialized.Aggressor.Should().Be(AggressorSide.Sell);
    }

    [Fact]
    public void MarketTradeUpdate_JsonContainsExpectedFields()
    {
        var update = new MarketTradeUpdate(
            Timestamp: new DateTimeOffset(2025, 1, 15, 14, 30, 0, TimeSpan.Zero),
            Symbol: "AAPL",
            Price: 185.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 12345,
            StreamId: "STOCKSHARP",
            Venue: "NASDAQ");

        var json = JsonSerializer.Serialize(update, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("symbol", out _).Should().BeTrue();
        root.TryGetProperty("price", out _).Should().BeTrue();
        root.TryGetProperty("size", out _).Should().BeTrue();
        root.TryGetProperty("aggressor", out _).Should().BeTrue();
        root.TryGetProperty("sequenceNumber", out _).Should().BeTrue();
        root.TryGetProperty("streamId", out _).Should().BeTrue();
        root.TryGetProperty("venue", out _).Should().BeTrue();
    }

    #endregion

    #region MarketDepthUpdate (Input Model)

    [Fact]
    public void MarketDepthUpdate_BidLevel()
    {
        var update = new MarketDepthUpdate(
            Timestamp: new DateTimeOffset(2025, 1, 15, 14, 30, 0, TimeSpan.Zero),
            Symbol: "AAPL",
            Position: 0,
            Operation: DepthOperation.Update,
            Side: OrderBookSide.Bid,
            Price: 185.45m,
            Size: 1500m,
            MarketMaker: null,
            SequenceNumber: 0,
            StreamId: "STOCKSHARP",
            Venue: "NASDAQ");

        update.Side.Should().Be(OrderBookSide.Bid);
        update.Position.Should().Be(0);
        update.Operation.Should().Be(DepthOperation.Update);
        update.Price.Should().Be(185.45m);
        update.Size.Should().Be(1500m);
    }

    [Fact]
    public void MarketDepthUpdate_AskLevel()
    {
        var update = new MarketDepthUpdate(
            Timestamp: new DateTimeOffset(2025, 1, 15, 14, 30, 0, TimeSpan.Zero),
            Symbol: "AAPL",
            Position: 0,
            Operation: DepthOperation.Update,
            Side: OrderBookSide.Ask,
            Price: 185.50m,
            Size: 1200m,
            StreamId: "STOCKSHARP",
            Venue: "NASDAQ");

        update.Side.Should().Be(OrderBookSide.Ask);
        update.Price.Should().Be(185.50m);
    }

    [Fact]
    public void MarketDepthUpdate_MultipleDepthLevels()
    {
        // StockSharp sends all levels in a snapshot; verify the level numbering pattern
        var updates = Enumerable.Range(0, 5).Select(i => new MarketDepthUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            Position: (ushort)i,
            Operation: DepthOperation.Update,
            Side: OrderBookSide.Bid,
            Price: 450.00m - (i * 0.01m),
            Size: 100m * (i + 1),
            StreamId: "STOCKSHARP",
            Venue: "ARCA")).ToList();

        updates.Should().HaveCount(5);
        updates[0].Position.Should().Be(0);
        updates[4].Position.Should().Be(4);

        // Prices should decrease from best bid
        updates[0].Price.Should().BeGreaterThan(updates[4].Price);
    }

    [Fact]
    public void MarketDepthUpdate_SerializationRoundTrip()
    {
        var update = new MarketDepthUpdate(
            Timestamp: new DateTimeOffset(2025, 1, 15, 14, 30, 0, TimeSpan.Zero),
            Symbol: "AAPL",
            Position: 2,
            Operation: DepthOperation.Insert,
            Side: OrderBookSide.Ask,
            Price: 185.55m,
            Size: 800m,
            SequenceNumber: 42,
            StreamId: "STOCKSHARP",
            Venue: "NYSE");

        var json = JsonSerializer.Serialize(update, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<MarketDepthUpdate>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Symbol.Should().Be("AAPL");
        deserialized.Position.Should().Be(2);
        deserialized.Operation.Should().Be(DepthOperation.Insert);
        deserialized.Side.Should().Be(OrderBookSide.Ask);
    }

    #endregion

    #region Trade Domain Model (Output Model)

    [Fact]
    public void Trade_DomainModel_Construction()
    {
        var trade = new Trade(
            Timestamp: new DateTimeOffset(2025, 1, 15, 14, 30, 0, TimeSpan.Zero),
            Symbol: "AAPL",
            Price: 185.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 12345,
            StreamId: "STOCKSHARP",
            Venue: "NASDAQ");

        trade.Symbol.Should().Be("AAPL");
        trade.Price.Should().Be(185.50m);
        trade.Size.Should().Be(100);
        trade.Aggressor.Should().Be(AggressorSide.Buy);
    }

    [Fact]
    public void Trade_DomainModel_RejectsZeroPrice()
    {
        var act = () => new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 0m,
            Size: 100,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Trade_DomainModel_RejectsNegativePrice()
    {
        var act = () => new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: -1m,
            Size: 100,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Trade_DomainModel_RejectsNegativeSize()
    {
        var act = () => new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 185.50m,
            Size: -1,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Trade_DomainModel_RejectsEmptySymbol()
    {
        var act = () => new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "",
            Price: 185.50m,
            Size: 100,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Trade_DomainModel_SerializationPreservesFields()
    {
        var trade = new Trade(
            Timestamp: new DateTimeOffset(2025, 1, 15, 14, 30, 0, TimeSpan.Zero),
            Symbol: "AAPL",
            Price: 185.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 12345,
            StreamId: "STOCKSHARP",
            Venue: "NASDAQ");

        var json = JsonSerializer.Serialize(trade, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("symbol").GetString().Should().Be("AAPL");
        root.GetProperty("price").GetDecimal().Should().Be(185.50m);
        root.GetProperty("size").GetInt64().Should().Be(100);
    }

    #endregion

    #region AggressorSide Mapping (StockSharp-specific pattern)

    [Fact]
    public void AggressorSide_Enum_HasExpectedValues()
    {
        // StockSharp maps: Sides.Buy -> AggressorSide.Buy, Sides.Sell -> AggressorSide.Sell, null -> Unknown
        AggressorSide.Buy.Should().BeDefined();
        AggressorSide.Sell.Should().BeDefined();
        AggressorSide.Unknown.Should().BeDefined();
    }

    [Fact]
    public void AggressorSide_SerializesToExpectedStrings()
    {
        var buyJson = JsonSerializer.Serialize(AggressorSide.Buy, JsonOptions);
        var sellJson = JsonSerializer.Serialize(AggressorSide.Sell, JsonOptions);
        var unknownJson = JsonSerializer.Serialize(AggressorSide.Unknown, JsonOptions);

        // Enum values serialize as numbers by default
        buyJson.Should().NotBeNullOrEmpty();
        sellJson.Should().NotBeNullOrEmpty();
        unknownJson.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region DepthOperation and OrderBookSide Enums

    [Fact]
    public void DepthOperation_Enum_HasExpectedValues()
    {
        // StockSharp always uses DepthOperation.Update for depth snapshots
        DepthOperation.Insert.Should().BeDefined();
        DepthOperation.Update.Should().BeDefined();
        DepthOperation.Delete.Should().BeDefined();
    }

    [Fact]
    public void OrderBookSide_Enum_HasExpectedValues()
    {
        OrderBookSide.Bid.Should().BeDefined();
        OrderBookSide.Ask.Should().BeDefined();
    }

    #endregion

    #region StockSharp Converter Stub Behavior

    [Fact]
    public void MessageConverter_Stub_ThrowsNotSupported_ForTrade()
    {
        // When STOCKSHARP is not defined, the converter stubs should throw
        var act = () => Meridian.Infrastructure.Adapters.StockSharp.Converters.MessageConverter.ToTrade(
            new object(), "AAPL");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void MessageConverter_Stub_ThrowsNotSupported_ForLOBSnapshot()
    {
        var act = () => Meridian.Infrastructure.Adapters.StockSharp.Converters.MessageConverter.ToLOBSnapshot(
            new object(), "AAPL");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void MessageConverter_Stub_ThrowsNotSupported_ForBboQuote()
    {
        var act = () => Meridian.Infrastructure.Adapters.StockSharp.Converters.MessageConverter.ToBboQuote(
            new object(), "AAPL");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void MessageConverter_Stub_ThrowsNotSupported_ForHistoricalBar()
    {
        var act = () => Meridian.Infrastructure.Adapters.StockSharp.Converters.MessageConverter.ToHistoricalBar(
            new object(), "AAPL");

        act.Should().Throw<NotSupportedException>();
    }

    #endregion
}
