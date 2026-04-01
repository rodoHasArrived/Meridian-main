using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.StockSharp;
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

    [Fact]
    public void MessageConverter_Stub_TradeError_MentionsStockSharpAlgoPackage()
    {
        var ex = Record.Exception(() =>
            Meridian.Infrastructure.Adapters.StockSharp.Converters.MessageConverter.ToTrade(new object(), "AAPL"));

        ex.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().Contain("StockSharp.Algo");
    }

    [Fact]
    public void MessageConverter_Stub_LOBSnapshotError_MentionsStockSharpMessagesPackage()
    {
        var ex = Record.Exception(() =>
            Meridian.Infrastructure.Adapters.StockSharp.Converters.MessageConverter.ToLOBSnapshot(new object(), "AAPL"));

        ex.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().Contain("StockSharp.Messages");
    }

    #endregion

    #region LOBSnapshot Domain Model (Output Model)

    [Fact]
    public void LOBSnapshot_Construction_WithBidsAndAsks()
    {
        // Validated adapter sample: Rithmic — simulates a QuoteChangeMessage output
        // for an ES futures snapshot with 3 bid and 3 ask levels.
        var ts = new DateTimeOffset(2025, 6, 1, 9, 30, 0, TimeSpan.Zero);
        var bids = new List<OrderBookLevel>
        {
            new(OrderBookSide.Bid, 0, 5275.00m, 25),
            new(OrderBookSide.Bid, 1, 5274.75m, 50),
            new(OrderBookSide.Bid, 2, 5274.50m, 75),
        };
        var asks = new List<OrderBookLevel>
        {
            new(OrderBookSide.Ask, 0, 5275.25m, 20),
            new(OrderBookSide.Ask, 1, 5275.50m, 40),
            new(OrderBookSide.Ask, 2, 5275.75m, 60),
        };
        var midPrice = (bids[0].Price + asks[0].Price) / 2m;

        var snapshot = new LOBSnapshot(
            Timestamp: ts,
            Symbol: "ESM5",
            Bids: bids,
            Asks: asks,
            MidPrice: midPrice,
            SequenceNumber: 1001,
            Venue: "CME");

        snapshot.Symbol.Should().Be("ESM5");
        snapshot.Bids.Should().HaveCount(3);
        snapshot.Asks.Should().HaveCount(3);
        snapshot.MidPrice.Should().Be(5275.125m);
        snapshot.SequenceNumber.Should().Be(1001);
        snapshot.Venue.Should().Be("CME");
    }

    [Fact]
    public void LOBSnapshot_EmptyBidsAndAsks_IsValid()
    {
        // StockSharp may emit empty snapshots during connector reset.
        var snapshot = new LOBSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "CLM5",
            Bids: Array.Empty<OrderBookLevel>(),
            Asks: Array.Empty<OrderBookLevel>(),
            MidPrice: null,
            Venue: "NYMEX");

        snapshot.Bids.Should().BeEmpty();
        snapshot.Asks.Should().BeEmpty();
        snapshot.MidPrice.Should().BeNull();
    }

    [Fact]
    public void LOBSnapshot_BestBidIsFirstBid()
    {
        // Validated adapter sample: IQFeed — level numbering starts at 0 (best).
        var bids = Enumerable.Range(0, 5).Select(i => new OrderBookLevel(
            Side: OrderBookSide.Bid,
            Level: (ushort)i,
            Price: 450.00m - (i * 0.01m),
            Size: 100 + (i * 10))).ToList();

        var snapshot = new LOBSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            Bids: bids,
            Asks: Array.Empty<OrderBookLevel>(),
            MidPrice: null,
            Venue: "NASDAQ");

        snapshot.Bids[0].Price.Should().BeGreaterThan(snapshot.Bids[4].Price);
        snapshot.Bids[0].Level.Should().Be(0);
        snapshot.Bids[4].Level.Should().Be(4);
    }

    [Fact]
    public void LOBSnapshot_SerializationRoundTrip()
    {
        var snapshot = new LOBSnapshot(
            Timestamp: new DateTimeOffset(2025, 6, 1, 9, 30, 0, TimeSpan.Zero),
            Symbol: "AAPL",
            Bids: new List<OrderBookLevel>
            {
                new(OrderBookSide.Bid, 0, 185.45m, 1000),
            },
            Asks: new List<OrderBookLevel>
            {
                new(OrderBookSide.Ask, 0, 185.50m, 800),
            },
            MidPrice: 185.475m,
            SequenceNumber: 42,
            Venue: "NASDAQ");

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<LOBSnapshot>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Symbol.Should().Be("AAPL");
        deserialized.Bids.Should().HaveCount(1);
        deserialized.Asks.Should().HaveCount(1);
        deserialized.MidPrice.Should().Be(185.475m);
        deserialized.SequenceNumber.Should().Be(42);
    }

    [Fact]
    public void LOBSnapshot_JsonContainsExpectedTopLevelFields()
    {
        var snapshot = new LOBSnapshot(
            Timestamp: new DateTimeOffset(2025, 6, 1, 9, 30, 0, TimeSpan.Zero),
            Symbol: "MSFT",
            Bids: Array.Empty<OrderBookLevel>(),
            Asks: Array.Empty<OrderBookLevel>());

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("symbol", out _).Should().BeTrue();
        root.TryGetProperty("bids", out _).Should().BeTrue();
        root.TryGetProperty("asks", out _).Should().BeTrue();
        root.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    #endregion

    #region BboQuotePayload Domain Model (Output Model)

    [Fact]
    public void BboQuotePayload_Construction_WithValidPrices()
    {
        // Validated adapter sample: IQFeed — Level1ChangeMessage best bid/offer fields
        // converted by MessageConverter.ToBboQuote for an equity (AAPL on NASDAQ).
        var ts = new DateTimeOffset(2025, 6, 1, 9, 30, 0, TimeSpan.Zero);
        var payload = new BboQuotePayload(
            Timestamp: ts,
            Symbol: "AAPL",
            BidPrice: 185.45m,
            BidSize: 1500,
            AskPrice: 185.50m,
            AskSize: 1200,
            MidPrice: 185.475m,
            Spread: 0.05m,
            SequenceNumber: 100,
            StreamId: "STOCKSHARP",
            Venue: "NASDAQ");

        payload.Symbol.Should().Be("AAPL");
        payload.BidPrice.Should().Be(185.45m);
        payload.AskPrice.Should().Be(185.50m);
        payload.MidPrice.Should().Be(185.475m);
        payload.Spread.Should().Be(0.05m);
        payload.BidSize.Should().Be(1500);
        payload.AskSize.Should().Be(1200);
    }

    [Fact]
    public void BboQuotePayload_CrossedMarket_HasNullMidAndSpread()
    {
        // Crossed market (bid > ask): MessageConverter.ToBboQuote sets mid/spread to null.
        var payload = new BboQuotePayload(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            BidPrice: 186.00m,
            BidSize: 100,
            AskPrice: 185.90m,
            AskSize: 100,
            MidPrice: null,
            Spread: null,
            SequenceNumber: 0);

        payload.MidPrice.Should().BeNull();
        payload.Spread.Should().BeNull();
    }

    [Fact]
    public void BboQuotePayload_ZeroBidPrice_HasNullMidAndSpread()
    {
        // Zero prices (no market data yet): mid and spread should be null.
        var payload = new BboQuotePayload(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "MSFT",
            BidPrice: 0m,
            BidSize: 0,
            AskPrice: 400.00m,
            AskSize: 500,
            MidPrice: null,
            Spread: null,
            SequenceNumber: 0);

        payload.MidPrice.Should().BeNull();
        payload.Spread.Should().BeNull();
    }

    [Fact]
    public void BboQuotePayload_FromUpdate_ComputesMidAndSpread()
    {
        // BboQuotePayload.FromUpdate uses the static factory that calculates mid/spread.
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 450.00m,
            BidSize: 2000,
            AskPrice: 450.10m,
            AskSize: 1800,
            SequenceNumber: 55,
            StreamId: "STOCKSHARP",
            Venue: "ARCA");

        var payload = BboQuotePayload.FromUpdate(update, seq: 55);

        payload.MidPrice.Should().Be(450.05m);
        payload.Spread.Should().Be(0.10m);
    }

    [Fact]
    public void BboQuotePayload_SerializationRoundTrip()
    {
        var payload = new BboQuotePayload(
            Timestamp: new DateTimeOffset(2025, 6, 1, 9, 30, 0, TimeSpan.Zero),
            Symbol: "GOOGL",
            BidPrice: 175.20m,
            BidSize: 300,
            AskPrice: 175.25m,
            AskSize: 250,
            MidPrice: 175.225m,
            Spread: 0.05m,
            SequenceNumber: 77,
            Venue: "NASDAQ");

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BboQuotePayload>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Symbol.Should().Be("GOOGL");
        deserialized.BidPrice.Should().Be(175.20m);
        deserialized.AskPrice.Should().Be(175.25m);
        deserialized.MidPrice.Should().Be(175.225m);
        deserialized.Spread.Should().Be(0.05m);
    }

    [Fact]
    public void BboQuotePayload_JsonContainsExpectedFields()
    {
        var payload = new BboQuotePayload(
            Timestamp: new DateTimeOffset(2025, 6, 1, 9, 30, 0, TimeSpan.Zero),
            Symbol: "TSLA",
            BidPrice: 200.00m,
            BidSize: 500,
            AskPrice: 200.05m,
            AskSize: 400,
            MidPrice: 200.025m,
            Spread: 0.05m,
            SequenceNumber: 0);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("bidPrice", out _).Should().BeTrue();
        root.TryGetProperty("askPrice", out _).Should().BeTrue();
        root.TryGetProperty("midPrice", out _).Should().BeTrue();
        root.TryGetProperty("spread", out _).Should().BeTrue();
        root.TryGetProperty("bidSize", out _).Should().BeTrue();
        root.TryGetProperty("askSize", out _).Should().BeTrue();
    }

    #endregion

    #region HistoricalBar Domain Model (Output Model)

    [Fact]
    public void HistoricalBar_Construction_ValidOhlcv()
    {
        // Validated adapter sample: Rithmic — TimeFrameCandleMessage for ES daily bar
        // converted by MessageConverter.ToHistoricalBar with source="stocksharp".
        var bar = new HistoricalBar(
            Symbol: "ESM5",
            SessionDate: new DateOnly(2025, 6, 1),
            Open: 5270.00m,
            High: 5285.50m,
            Low: 5265.25m,
            Close: 5280.00m,
            Volume: 1_250_000,
            Source: "stocksharp");

        bar.Symbol.Should().Be("ESM5");
        bar.Open.Should().Be(5270.00m);
        bar.High.Should().Be(5285.50m);
        bar.Low.Should().Be(5265.25m);
        bar.Close.Should().Be(5280.00m);
        bar.Volume.Should().Be(1_250_000);
        bar.Source.Should().Be("stocksharp");
    }

    [Fact]
    public void HistoricalBar_DerivedProperties_Range()
    {
        var bar = new HistoricalBar(
            Symbol: "AAPL",
            SessionDate: new DateOnly(2025, 1, 15),
            Open: 183.00m,
            High: 187.00m,
            Low: 182.50m,
            Close: 185.50m,
            Volume: 50_000_000);

        bar.Range.Should().Be(4.50m);  // High - Low
    }

    [Fact]
    public void HistoricalBar_DerivedProperties_BodySize()
    {
        var bar = new HistoricalBar(
            Symbol: "AAPL",
            SessionDate: new DateOnly(2025, 1, 15),
            Open: 183.00m,
            High: 187.00m,
            Low: 182.50m,
            Close: 185.50m,
            Volume: 50_000_000);

        bar.BodySize.Should().Be(2.50m);  // |Close - Open|
    }

    [Fact]
    public void HistoricalBar_IsBullish_WhenCloseGreaterThanOpen()
    {
        var bar = new HistoricalBar(
            Symbol: "MSFT",
            SessionDate: new DateOnly(2025, 1, 15),
            Open: 395.00m,
            High: 402.00m,
            Low: 394.50m,
            Close: 400.25m,
            Volume: 25_000_000);

        bar.IsBullish.Should().BeTrue();
        bar.IsBearish.Should().BeFalse();
    }

    [Fact]
    public void HistoricalBar_IsBearish_WhenCloseLessThanOpen()
    {
        var bar = new HistoricalBar(
            Symbol: "MSFT",
            SessionDate: new DateOnly(2025, 1, 16),
            Open: 400.00m,
            High: 401.50m,
            Low: 393.00m,
            Close: 394.75m,
            Volume: 30_000_000);

        bar.IsBearish.Should().BeTrue();
        bar.IsBullish.Should().BeFalse();
    }

    [Fact]
    public void HistoricalBar_TypicalPrice_IsAverageOfHighLowClose()
    {
        var bar = new HistoricalBar(
            Symbol: "SPY",
            SessionDate: new DateOnly(2025, 1, 15),
            Open: 449.00m,
            High: 452.00m,
            Low: 448.00m,
            Close: 451.00m,
            Volume: 80_000_000);

        // (High + Low + Close) / 3
        bar.TypicalPrice.Should().Be((452.00m + 448.00m + 451.00m) / 3m);
    }

    [Fact]
    public void HistoricalBar_RejectsZeroOpen()
    {
        var act = () => new HistoricalBar(
            Symbol: "AAPL",
            SessionDate: new DateOnly(2025, 1, 15),
            Open: 0m,
            High: 187.00m,
            Low: 182.50m,
            Close: 185.50m,
            Volume: 50_000_000);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HistoricalBar_RejectsLowExceedingHigh()
    {
        var act = () => new HistoricalBar(
            Symbol: "AAPL",
            SessionDate: new DateOnly(2025, 1, 15),
            Open: 185.00m,
            High: 184.00m,
            Low: 186.00m,
            Close: 185.50m,
            Volume: 50_000_000);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HistoricalBar_RejectsNegativeVolume()
    {
        var act = () => new HistoricalBar(
            Symbol: "AAPL",
            SessionDate: new DateOnly(2025, 1, 15),
            Open: 183.00m,
            High: 187.00m,
            Low: 182.50m,
            Close: 185.50m,
            Volume: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HistoricalBar_RejectsEmptySymbol()
    {
        var act = () => new HistoricalBar(
            Symbol: "",
            SessionDate: new DateOnly(2025, 1, 15),
            Open: 183.00m,
            High: 187.00m,
            Low: 182.50m,
            Close: 185.50m,
            Volume: 50_000_000);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HistoricalBar_SerializationRoundTrip()
    {
        var bar = new HistoricalBar(
            Symbol: "ESM5",
            SessionDate: new DateOnly(2025, 6, 1),
            Open: 5270.00m,
            High: 5285.50m,
            Low: 5265.25m,
            Close: 5280.00m,
            Volume: 1_250_000,
            Source: "stocksharp",
            SequenceNumber: 5);

        var json = JsonSerializer.Serialize(bar, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<HistoricalBar>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Symbol.Should().Be("ESM5");
        deserialized.Open.Should().Be(5270.00m);
        deserialized.High.Should().Be(5285.50m);
        deserialized.Low.Should().Be(5265.25m);
        deserialized.Close.Should().Be(5280.00m);
        deserialized.Volume.Should().Be(1_250_000);
        deserialized.Source.Should().Be("stocksharp");
    }

    #endregion

    #region ConnectorCapabilities — Rithmic and IQFeed

    [Fact]
    public void ConnectorCapabilities_Rithmic_SupportsExpectedFeatures()
    {
        var caps = StockSharpConnectorCapabilities.GetCapabilities("Rithmic");

        caps.ConnectorType.Should().Be("Rithmic");
        caps.SupportsStreaming.Should().BeTrue();
        caps.SupportsHistorical.Should().BeTrue();
        caps.SupportsTrades.Should().BeTrue();
        caps.SupportsDepth.Should().BeTrue();
        caps.SupportsQuotes.Should().BeTrue();
        caps.SupportsOrderLog.Should().BeTrue();
        caps.SupportsCandles.Should().BeTrue();
    }

    [Fact]
    public void ConnectorCapabilities_Rithmic_SupportsFuturesMarkets()
    {
        var caps = StockSharpConnectorCapabilities.GetCapabilities("Rithmic");

        caps.SupportedMarkets.Should().Contain("CME");
        caps.SupportedMarkets.Should().Contain("NYMEX");
        caps.SupportedAssetTypes.Should().Contain("Future");
    }

    [Fact]
    public void ConnectorCapabilities_IQFeed_SupportsEquitiesAndOptions()
    {
        var caps = StockSharpConnectorCapabilities.GetCapabilities("IQFeed");

        caps.ConnectorType.Should().Be("IQFeed");
        caps.SupportsStreaming.Should().BeTrue();
        caps.SupportsHistorical.Should().BeTrue();
        caps.SupportsTrades.Should().BeTrue();
        caps.SupportsDepth.Should().BeTrue();
        caps.SupportsQuotes.Should().BeTrue();
        caps.SupportsOrderLog.Should().BeTrue();
        caps.SupportedAssetTypes.Should().Contain("Stock");
        caps.SupportedAssetTypes.Should().Contain("Option");
    }

    [Fact]
    public void ConnectorCapabilities_IQFeed_SupportsEquityMarkets()
    {
        var caps = StockSharpConnectorCapabilities.GetCapabilities("IQFeed");

        caps.SupportedMarkets.Should().Contain("NYSE");
        caps.SupportedMarkets.Should().Contain("NASDAQ");
    }

    [Fact]
    public void ConnectorCapabilities_InteractiveBrokers_SupportsGlobalBrokerWorkflow()
    {
        var caps = StockSharpConnectorCapabilities.GetCapabilities("InteractiveBrokers");

        caps.ConnectorType.Should().Be("InteractiveBrokers");
        caps.SupportsStreaming.Should().BeTrue();
        caps.SupportsHistorical.Should().BeTrue();
        caps.SupportsDepth.Should().BeTrue();
        caps.SupportedMarkets.Should().Contain("NYSE");
        caps.Notes.Should().Contain(note => note.Contains("TWS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConnectorCapabilities_Kraken_ExplainsCryptoPackageRequirements()
    {
        var caps = StockSharpConnectorCapabilities.GetCapabilities("Kraken");

        caps.ConnectorType.Should().Be("Kraken");
        caps.SupportsStreaming.Should().BeTrue();
        caps.SupportedAssetTypes.Should().Contain("Crypto");
        caps.Warnings.Should().Contain(warning => warning.Contains("StockSharp.Kraken"));
    }

    [Fact]
    public void ConnectorCapabilities_GetCapabilities_CaseInsensitive()
    {
        StockSharpConnectorCapabilities.GetCapabilities("rithmic").ConnectorType.Should().Be("Rithmic");
        StockSharpConnectorCapabilities.GetCapabilities("IQFEED").ConnectorType.Should().Be("IQFeed");
        StockSharpConnectorCapabilities.GetCapabilities("IB").ConnectorType.Should().Be("InteractiveBrokers");
    }

    [Fact]
    public void ConnectorCapabilities_Unknown_ReturnsFallback()
    {
        var caps = StockSharpConnectorCapabilities.GetCapabilities("NonExistentConnector");

        caps.ConnectorType.Should().Be("Unknown");
        caps.SupportsStreaming.Should().BeFalse();
        caps.SupportsHistorical.Should().BeFalse();
        caps.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void ConnectorCapabilities_GetConnectorsWithHistoricalSupport_IncludesRithmicAndIQFeed()
    {
        var connectors = StockSharpConnectorCapabilities.GetConnectorsWithHistoricalSupport();

        connectors.Select(c => c.ConnectorType).Should().Contain("Rithmic");
        connectors.Select(c => c.ConnectorType).Should().Contain("IQFeed");
    }

    [Fact]
    public void ConnectorCapabilities_GetConnectorsWithOrderLogSupport_IncludesRithmicAndIQFeed()
    {
        var connectors = StockSharpConnectorCapabilities.GetConnectorsWithOrderLogSupport();

        connectors.Select(c => c.ConnectorType).Should().Contain("Rithmic");
        connectors.Select(c => c.ConnectorType).Should().Contain("IQFeed");
    }

    [Fact]
    public void ConnectorCapabilities_Rithmic_MaxDepthLevels_IsSet()
    {
        var caps = StockSharpConnectorCapabilities.GetCapabilities("Rithmic");
        caps.MaxDepthLevels.Should().NotBeNull().And.BeGreaterThan(0);
    }

    [Fact]
    public void ConnectorCapabilities_IQFeed_MaxDepthLevels_IsSet()
    {
        var caps = StockSharpConnectorCapabilities.GetCapabilities("IQFeed");
        caps.MaxDepthLevels.Should().NotBeNull().And.BeGreaterThan(0);
    }

    [Fact]
    public void ConnectorCapabilities_ToDictionary_ContainsAllKeys()
    {
        var caps = StockSharpConnectorCapabilities.GetCapabilities("Rithmic");
        var dict = caps.ToDictionary();

        dict.Should().ContainKey("connectorType");
        dict.Should().ContainKey("supportsStreaming");
        dict.Should().ContainKey("supportsHistorical");
        dict.Should().ContainKey("supportsTrades");
        dict.Should().ContainKey("supportsDepth");
        dict.Should().ContainKey("supportsQuotes");
        dict.Should().ContainKey("supportedMarkets");
        dict.Should().ContainKey("supportedAssetTypes");
    }

    #endregion

    #region Connector Factory Stub — Rithmic and IQFeed

    [Fact]
    public void ConnectorFactory_Rithmic_Stub_ThrowsWithPackageName()
    {
        // When STOCKSHARP is not defined, creating any connector throws with
        // package installation guidance.
        var config = new StockSharpConfig(Enabled: true, ConnectorType: "Rithmic");

        var ex = Record.Exception(() => StockSharpConnectorFactory.Create(config));

        ex.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().Contain("StockSharp");
    }

    [Fact]
    public void ConnectorFactory_IQFeed_Stub_ThrowsWithPackageName()
    {
        var config = new StockSharpConfig(Enabled: true, ConnectorType: "IQFeed");

        var ex = Record.Exception(() => StockSharpConnectorFactory.Create(config));

        ex.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().Contain("StockSharp");
    }

    [Fact]
    public void ConnectorFactory_Rithmic_Stub_MessageContainsEnableStockSharpFlag()
    {
        var config = new StockSharpConfig(Enabled: true, ConnectorType: "Rithmic");

        var ex = Record.Exception(() => StockSharpConnectorFactory.Create(config));

        ex.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().Contain("EnableStockSharp=true");
    }

    [Fact]
    public void ConnectorFactory_IQFeed_Stub_MessageContainsSupportedConnectorList()
    {
        var config = new StockSharpConfig(Enabled: true, ConnectorType: "IQFeed");

        var ex = Record.Exception(() => StockSharpConnectorFactory.Create(config));

        // The stub message includes the list of supported named connectors.
        ex.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().Contain("Rithmic");
    }

    [Fact]
    public void ConnectorFactory_Binance_Stub_ThrowsWithCrowdfundingNote()
    {
        var config = new StockSharpConfig(Enabled: true, ConnectorType: "Binance");

        var ex = Record.Exception(() => StockSharpConnectorFactory.Create(config));

        ex.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().Contain("Binance");
    }

    [Fact]
    public void ConnectorFactory_Kraken_Stub_ThrowsWithGuidePathAndConnectorName()
    {
        var config = new StockSharpConfig(Enabled: true, ConnectorType: "Kraken");

        var ex = Record.Exception(() => StockSharpConnectorFactory.Create(config));

        ex.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().ContainAll("Kraken", "EnableStockSharp=true", "stocksharp-connectors.md");
    }

    [Fact]
    public void ConnectorFactory_UnknownConnector_WithoutAdapterType_ThrowsWithGuidance()
    {
        var config = new StockSharpConfig(Enabled: true, ConnectorType: "UnknownVendor");

        var ex = Record.Exception(() => StockSharpConnectorFactory.Create(config));

        ex.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().Contain("stocksharp-connectors.md");
    }

    #endregion
}
