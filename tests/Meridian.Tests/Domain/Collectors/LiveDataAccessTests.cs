using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Tests for the live data access methods on collectors (GetRecentTrades,
/// GetOrderFlowSnapshot, GetCurrentSnapshot) used by the live data API endpoints.
/// </summary>
public class LiveDataAccessTests
{
    private readonly TestMarketEventPublisher _publisher;
    private readonly QuoteCollector _quoteCollector;
    private readonly TradeDataCollector _tradeCollector;
    private readonly MarketDepthCollector _depthCollector;

    public LiveDataAccessTests()
    {
        _publisher = new TestMarketEventPublisher();
        _quoteCollector = new QuoteCollector(_publisher);
        _tradeCollector = new TradeDataCollector(_publisher, _quoteCollector);
        _depthCollector = new MarketDepthCollector(_publisher, requireExplicitSubscription: false);
    }

    #region TradeDataCollector.GetRecentTrades

    [Fact]
    public void GetRecentTrades_WithNoTrades_ReturnsEmptyList()
    {
        // Act
        var trades = _tradeCollector.GetRecentTrades("SPY");

        // Assert
        trades.Should().BeEmpty();
    }

    [Fact]
    public void GetRecentTrades_WithNullSymbol_ReturnsEmptyList()
    {
        // Act
        var trades = _tradeCollector.GetRecentTrades(null!);

        // Assert
        trades.Should().BeEmpty();
    }

    [Fact]
    public void GetRecentTrades_AfterSingleTrade_ReturnsThatTrade()
    {
        // Arrange
        _tradeCollector.OnTrade(CreateTrade("AAPL", price: 150.50m, size: 100, seqNum: 1));

        // Act
        var trades = _tradeCollector.GetRecentTrades("AAPL");

        // Assert
        trades.Should().HaveCount(1);
        trades[0].Symbol.Should().Be("AAPL");
        trades[0].Price.Should().Be(150.50m);
        trades[0].Size.Should().Be(100);
    }

    [Fact]
    public void GetRecentTrades_ReturnsNewestFirst()
    {
        // Arrange
        _tradeCollector.OnTrade(CreateTrade("SPY", price: 450.00m, seqNum: 1));
        _tradeCollector.OnTrade(CreateTrade("SPY", price: 451.00m, seqNum: 2));
        _tradeCollector.OnTrade(CreateTrade("SPY", price: 452.00m, seqNum: 3));

        // Act
        var trades = _tradeCollector.GetRecentTrades("SPY");

        // Assert
        trades.Should().HaveCount(3);
        trades[0].Price.Should().Be(452.00m);
        trades[1].Price.Should().Be(451.00m);
        trades[2].Price.Should().Be(450.00m);
    }

    [Fact]
    public void GetRecentTrades_RespectsLimitParameter()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
            _tradeCollector.OnTrade(CreateTrade("SPY", price: 450m + i, seqNum: i));

        // Act
        var trades = _tradeCollector.GetRecentTrades("SPY", limit: 3);

        // Assert
        trades.Should().HaveCount(3);
        trades[0].Price.Should().Be(460m); // Most recent
    }

    [Fact]
    public void GetRecentTrades_IsolatesSymbols()
    {
        // Arrange
        _tradeCollector.OnTrade(CreateTrade("SPY", price: 450m, seqNum: 1));
        _tradeCollector.OnTrade(CreateTrade("AAPL", price: 150m, seqNum: 1));
        _tradeCollector.OnTrade(CreateTrade("SPY", price: 451m, seqNum: 2));

        // Act
        var spyTrades = _tradeCollector.GetRecentTrades("SPY");
        var aaplTrades = _tradeCollector.GetRecentTrades("AAPL");

        // Assert
        spyTrades.Should().HaveCount(2);
        aaplTrades.Should().HaveCount(1);
    }

    #endregion

    #region TradeDataCollector.GetOrderFlowSnapshot

    [Fact]
    public void GetOrderFlowSnapshot_WithNoTrades_ReturnsNull()
    {
        // Act
        var stats = _tradeCollector.GetOrderFlowSnapshot("SPY");

        // Assert
        stats.Should().BeNull();
    }

    [Fact]
    public void GetOrderFlowSnapshot_WithNullSymbol_ReturnsNull()
    {
        // Act
        var stats = _tradeCollector.GetOrderFlowSnapshot(null!);

        // Assert
        stats.Should().BeNull();
    }

    [Fact]
    public void GetOrderFlowSnapshot_AfterTrades_ReturnsAccurateStats()
    {
        // Arrange
        _tradeCollector.OnTrade(CreateTrade("SPY", price: 450m, size: 100, aggressor: AggressorSide.Buy, seqNum: 1));
        _tradeCollector.OnTrade(CreateTrade("SPY", price: 449m, size: 50, aggressor: AggressorSide.Sell, seqNum: 2));

        // Act
        var stats = _tradeCollector.GetOrderFlowSnapshot("SPY");

        // Assert
        stats.Should().NotBeNull();
        stats!.Symbol.Should().Be("SPY");
        stats.BuyVolume.Should().Be(100);
        stats.SellVolume.Should().Be(50);
        stats.TradeCount.Should().Be(2);
        stats.VWAP.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void GetTrackedSymbols_ReturnsAllSymbolsWithTrades()
    {
        // Arrange
        _tradeCollector.OnTrade(CreateTrade("SPY", seqNum: 1));
        _tradeCollector.OnTrade(CreateTrade("AAPL", seqNum: 1));
        _tradeCollector.OnTrade(CreateTrade("MSFT", seqNum: 1));

        // Act
        var symbols = _tradeCollector.GetTrackedSymbols();

        // Assert
        symbols.Should().HaveCount(3);
        symbols.Should().Contain("SPY");
        symbols.Should().Contain("AAPL");
        symbols.Should().Contain("MSFT");
    }

    #endregion

    #region QuoteCollector (existing TryGet/Snapshot methods)

    [Fact]
    public void QuoteCollector_TryGet_ReturnsFalseForUnknownSymbol()
    {
        // Act
        var found = _quoteCollector.TryGet("UNKNOWN", out var quote);

        // Assert
        found.Should().BeFalse();
        quote.Should().BeNull();
    }

    [Fact]
    public void QuoteCollector_TryGet_ReturnsLatestQuote()
    {
        // Arrange
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 450.00m,
            BidSize: 200,
            AskPrice: 450.05m,
            AskSize: 300,
            StreamId: "TEST",
            Venue: "ARCA");
        _quoteCollector.OnQuote(update);

        // Act
        var found = _quoteCollector.TryGet("SPY", out var quote);

        // Assert
        found.Should().BeTrue();
        quote.Should().NotBeNull();
        quote!.BidPrice.Should().Be(450.00m);
        quote.AskPrice.Should().Be(450.05m);
        quote.BidSize.Should().Be(200);
        quote.AskSize.Should().Be(300);
    }

    [Fact]
    public void QuoteCollector_Snapshot_ReturnsAllSymbols()
    {
        // Arrange
        _quoteCollector.OnQuote(new MarketQuoteUpdate(DateTimeOffset.UtcNow, "SPY", 450m, 100, 450.05m, 100));
        _quoteCollector.OnQuote(new MarketQuoteUpdate(DateTimeOffset.UtcNow, "AAPL", 150m, 100, 150.05m, 100));

        // Act
        var snapshot = _quoteCollector.Snapshot();

        // Assert
        snapshot.Should().HaveCount(2);
        snapshot.Should().ContainKey("SPY");
        snapshot.Should().ContainKey("AAPL");
    }

    #endregion

    #region MarketDepthCollector.GetCurrentSnapshot

    [Fact]
    public void GetCurrentSnapshot_WithNoData_ReturnsNull()
    {
        // Act
        var snapshot = _depthCollector.GetCurrentSnapshot("SPY");

        // Assert
        snapshot.Should().BeNull();
    }

    [Fact]
    public void GetCurrentSnapshot_WithNullSymbol_ReturnsNull()
    {
        // Act
        var snapshot = _depthCollector.GetCurrentSnapshot(null!);

        // Assert
        snapshot.Should().BeNull();
    }

    [Fact]
    public void GetCurrentSnapshot_AfterDepthUpdates_ReturnsValidSnapshot()
    {
        // Arrange - Insert bid and ask levels
        _depthCollector.OnDepth(new MarketDepthUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            Position: 0,
            Operation: DepthOperation.Insert,
            Side: OrderBookSide.Bid,
            Price: 450.00m,
            Size: 100));

        _depthCollector.OnDepth(new MarketDepthUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            Position: 0,
            Operation: DepthOperation.Insert,
            Side: OrderBookSide.Ask,
            Price: 450.05m,
            Size: 200));

        // Act
        var snapshot = _depthCollector.GetCurrentSnapshot("SPY");

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.Symbol.Should().Be("SPY");
        snapshot.Bids.Should().HaveCount(1);
        snapshot.Asks.Should().HaveCount(1);
        snapshot.Bids[0].Price.Should().Be(450.00m);
        snapshot.Asks[0].Price.Should().Be(450.05m);
        snapshot.MidPrice.Should().Be(450.025m);
    }

    [Fact]
    public void GetCurrentSnapshot_WithMultipleLevels_ReturnsAllLevels()
    {
        // Arrange - Insert multiple bid levels
        _depthCollector.OnDepth(new MarketDepthUpdate(DateTimeOffset.UtcNow, "SPY", 0, DepthOperation.Insert, OrderBookSide.Bid, 450.00m, 100));
        _depthCollector.OnDepth(new MarketDepthUpdate(DateTimeOffset.UtcNow, "SPY", 1, DepthOperation.Insert, OrderBookSide.Bid, 449.95m, 200));
        _depthCollector.OnDepth(new MarketDepthUpdate(DateTimeOffset.UtcNow, "SPY", 0, DepthOperation.Insert, OrderBookSide.Ask, 450.05m, 150));
        _depthCollector.OnDepth(new MarketDepthUpdate(DateTimeOffset.UtcNow, "SPY", 1, DepthOperation.Insert, OrderBookSide.Ask, 450.10m, 250));

        // Act
        var snapshot = _depthCollector.GetCurrentSnapshot("SPY");

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.Bids.Should().HaveCount(2);
        snapshot.Asks.Should().HaveCount(2);
        snapshot.Bids[0].Price.Should().Be(450.00m); // Best bid
        snapshot.Asks[0].Price.Should().Be(450.05m); // Best ask
    }

    [Fact]
    public void GetCurrentSnapshot_CalculatesImbalance()
    {
        // Arrange - Different bid/ask sizes to create imbalance
        _depthCollector.OnDepth(new MarketDepthUpdate(DateTimeOffset.UtcNow, "SPY", 0, DepthOperation.Insert, OrderBookSide.Bid, 450.00m, 300));
        _depthCollector.OnDepth(new MarketDepthUpdate(DateTimeOffset.UtcNow, "SPY", 0, DepthOperation.Insert, OrderBookSide.Ask, 450.05m, 100));

        // Act
        var snapshot = _depthCollector.GetCurrentSnapshot("SPY");

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.Imbalance.Should().NotBeNull();
        snapshot.Imbalance!.Value.Should().BeGreaterThan(0); // bid-heavy
    }

    [Fact]
    public void GetTrackedSymbols_ReturnsAllSymbolsWithDepth()
    {
        // Arrange
        _depthCollector.OnDepth(new MarketDepthUpdate(DateTimeOffset.UtcNow, "SPY", 0, DepthOperation.Insert, OrderBookSide.Bid, 450m, 100));
        _depthCollector.OnDepth(new MarketDepthUpdate(DateTimeOffset.UtcNow, "AAPL", 0, DepthOperation.Insert, OrderBookSide.Bid, 150m, 100));

        // Act
        var symbols = _depthCollector.GetTrackedSymbols();

        // Assert
        symbols.Should().HaveCount(2);
        symbols.Should().Contain("SPY");
        symbols.Should().Contain("AAPL");
    }

    #endregion

    #region Helper Methods

    private static MarketTradeUpdate CreateTrade(
        string symbol,
        decimal price = 100m,
        long size = 100,
        AggressorSide aggressor = AggressorSide.Buy,
        long seqNum = 1)
    {
        return new MarketTradeUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: price,
            Size: size,
            Aggressor: aggressor,
            SequenceNumber: seqNum,
            StreamId: "TEST",
            Venue: "TEST"
        );
    }

    #endregion
}
