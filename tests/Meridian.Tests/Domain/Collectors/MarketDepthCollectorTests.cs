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
/// Unit tests for the MarketDepthCollector class.
/// Tests L2 order book maintenance, depth updates, integrity event detection, and auto-reset behavior.
/// </summary>
public class MarketDepthCollectorTests
{
    private readonly TestMarketEventPublisher _publisher;
    private readonly MarketDepthCollector _collector;
    private IReadOnlyList<MarketEvent> _publishedEvents => _publisher.PublishedEvents;

    public MarketDepthCollectorTests()
    {
        _publisher = new TestMarketEventPublisher();

        // Create collector with explicit subscription disabled for simpler testing
        _collector = new MarketDepthCollector(_publisher, requireExplicitSubscription: false);
    }

    #region Basic Depth Update Tests

    [Fact]
    public void OnDepth_WithInsert_AddsLevelToBook()
    {
        // Arrange
        var update = CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100);

        // Act
        _collector.OnDepth(update);

        // Assert
        _publishedEvents.Should().HaveCount(1);
        _publishedEvents[0].Type.Should().Be(MarketEventType.L2Snapshot);

        var snapshot = _publishedEvents[0].Payload as LOBSnapshot;
        snapshot.Should().NotBeNull();
        snapshot!.Symbol.Should().Be("SPY");
        snapshot.Bids.Should().HaveCount(1);
        snapshot.Bids[0].Price.Should().Be(450.00m);
        snapshot.Bids[0].Size.Should().Be(100);
    }

    [Fact]
    public void OnDepth_WithMultipleInserts_BuildsOrderBook()
    {
        // Arrange & Act - Build a book with 3 bid levels
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 1, 449.90m, 150));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 2, 449.80m, 200));

        // Assert
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.Bids.Should().HaveCount(3);
        lastSnapshot.Bids[0].Price.Should().Be(450.00m);
        lastSnapshot.Bids[1].Price.Should().Be(449.90m);
        lastSnapshot.Bids[2].Price.Should().Be(449.80m);
    }

    [Fact]
    public void OnDepth_WithBidsAndAsks_MaintainsBothSides()
    {
        // Arrange & Act
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Ask, 0, 450.10m, 100));

        // Assert
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.Bids.Should().HaveCount(1);
        lastSnapshot.Asks.Should().HaveCount(1);
        lastSnapshot.Bids[0].Price.Should().Be(450.00m);
        lastSnapshot.Asks[0].Price.Should().Be(450.10m);
    }

    #endregion

    #region Update Operation Tests

    [Fact]
    public void OnDepth_WithUpdate_ModifiesExistingLevel()
    {
        // Arrange - Insert a level first
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));

        // Act - Update the level
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Update, OrderBookSide.Bid, 0, 450.00m, 200));

        // Assert
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.Bids.Should().HaveCount(1);
        lastSnapshot.Bids[0].Size.Should().Be(200);
    }

    [Fact]
    public void OnDepth_WithUpdatePriceChange_UpdatesPrice()
    {
        // Arrange
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));

        // Act
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Update, OrderBookSide.Bid, 0, 449.95m, 100));

        // Assert
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.Bids[0].Price.Should().Be(449.95m);
    }

    #endregion

    #region Delete Operation Tests

    [Fact]
    public void OnDepth_WithDelete_RemovesLevel()
    {
        // Arrange - Build book with 2 levels
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 1, 449.90m, 150));

        // Act - Delete first level
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Delete, OrderBookSide.Bid, 0, 0, 0));

        // Assert
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.Bids.Should().HaveCount(1);
        lastSnapshot.Bids[0].Price.Should().Be(449.90m);
        lastSnapshot.Bids[0].Level.Should().Be(0); // Level should be reindexed
    }

    [Fact]
    public void OnDepth_WithDeleteLastLevel_ResultsInEmptySide()
    {
        // Arrange
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));

        // Act
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Delete, OrderBookSide.Bid, 0, 0, 0));

        // Assert
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.Bids.Should().BeEmpty();
    }

    #endregion

    #region Calculated Fields Tests

    [Fact]
    public void OnDepth_WithBidAndAsk_CalculatesMidPrice()
    {
        // Arrange & Act
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Ask, 0, 450.10m, 100));

        // Assert
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.MidPrice.Should().Be(450.05m);
    }

    [Fact]
    public void OnDepth_WithImbalance_CalculatesImbalanceRatio()
    {
        // Arrange & Act - Bid size > Ask size
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 300));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Ask, 0, 450.10m, 100));

        // Assert - Imbalance = (300 - 100) / (300 + 100) = 0.5
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.Imbalance.Should().Be(0.5m);
    }

    [Fact]
    public void OnDepth_WithEqualSizes_HasZeroImbalance()
    {
        // Arrange & Act
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Ask, 0, 450.10m, 100));

        // Assert
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.Imbalance.Should().Be(0m);
    }

    [Fact]
    public void OnDepth_OnlyBids_MidPriceIsNull()
    {
        // Arrange & Act
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));

        // Assert
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.MidPrice.Should().BeNull();
        lastSnapshot.Imbalance.Should().BeNull();
    }

    #endregion

    #region Integrity Event Tests

    [Fact]
    public void OnDepth_InsertAtInvalidPosition_PublishesIntegrityEvent()
    {
        // Arrange - Try to insert at position 5 with empty book
        var update = CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 5, 450.00m, 100);

        // Act
        _collector.OnDepth(update);

        // Assert — DepthIntegrity + ResyncRequested events are published
        _publishedEvents.Should().HaveCount(2);
        _publishedEvents[0].Type.Should().Be(MarketEventType.Integrity);

        var integrity = _publishedEvents[0].Payload as DepthIntegrityEvent;
        integrity.Should().NotBeNull();
        integrity!.Kind.Should().Be(DepthIntegrityKind.Gap);
    }

    [Fact]
    public void OnDepth_UpdateMissingPosition_PublishesIntegrityEvent()
    {
        // Arrange - Try to update position 0 with empty book
        var update = CreateDepthUpdate("SPY", DepthOperation.Update, OrderBookSide.Bid, 0, 450.00m, 100);

        // Act
        _collector.OnDepth(update);

        // Assert — DepthIntegrity + ResyncRequested events are published
        _publishedEvents.Should().HaveCount(2);
        var integrity = _publishedEvents[0].Payload as DepthIntegrityEvent;
        integrity!.Kind.Should().Be(DepthIntegrityKind.OutOfOrder);
    }

    [Fact]
    public void OnDepth_DeleteMissingPosition_PublishesIntegrityEvent()
    {
        // Arrange - Try to delete from empty book
        var update = CreateDepthUpdate("SPY", DepthOperation.Delete, OrderBookSide.Bid, 0, 0, 0);

        // Act
        _collector.OnDepth(update);

        // Assert — DepthIntegrity + ResyncRequested events are published
        _publishedEvents.Should().HaveCount(2);
        var integrity = _publishedEvents[0].Payload as DepthIntegrityEvent;
        integrity!.Kind.Should().Be(DepthIntegrityKind.InvalidPosition);
    }

    [Fact]
    public void OnDepth_AfterIntegrityEvent_StreamIsStale()
    {
        // Arrange - Cause an integrity event
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Update, OrderBookSide.Bid, 0, 450.00m, 100));

        // Act
        var isStale = _collector.IsSymbolStreamStale("SPY");

        // Assert
        isStale.Should().BeTrue();
    }

    [Fact]
    public void OnDepth_WhenStale_PublishesStaleEvent()
    {
        // Arrange - Cause stale state
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Update, OrderBookSide.Bid, 0, 450.00m, 100));
        _publisher.Clear();

        // Act - Try to update again
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));

        // Assert — DepthIntegrity + ResyncRequested events are published
        _publishedEvents.Should().HaveCount(2);
        var integrity = _publishedEvents[0].Payload as DepthIntegrityEvent;
        integrity!.Kind.Should().Be(DepthIntegrityKind.Stale);
    }

    #endregion

    #region Reset Tests

    [Fact]
    public void ResetSymbolStream_ClearsBookAndStaleState()
    {
        // Arrange - Build book and cause stale
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Update, OrderBookSide.Bid, 5, 0, 0)); // Causes stale

        // Act
        _collector.ResetSymbolStream("SPY");
        _publisher.Clear();

        // Insert should work now
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));

        // Assert
        _collector.IsSymbolStreamStale("SPY").Should().BeFalse();
        _publishedEvents.Should().HaveCount(1);
        _publishedEvents[0].Type.Should().Be(MarketEventType.L2Snapshot);
    }

    [Fact]
    public void GetRecentIntegrityEvents_ReturnsIntegrityHistory()
    {
        // Arrange - Cause multiple integrity events
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Update, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.ResetSymbolStream("SPY");
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Delete, OrderBookSide.Ask, 0, 0, 0));

        // Act
        var events = _collector.GetRecentIntegrityEvents(10);

        // Assert
        events.Should().HaveCount(2);
    }

    #endregion

    #region Multi-Symbol Tests

    [Fact]
    public void OnDepth_MultipleSymbols_MaintainsSeparateBooks()
    {
        // Arrange & Act
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.OnDepth(CreateDepthUpdate("AAPL", DepthOperation.Insert, OrderBookSide.Bid, 0, 180.00m, 200));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 1, 449.90m, 150));

        // Assert
        var spySnapshots = _publishedEvents.Where(e => e.Symbol == "SPY").ToList();
        var aaplSnapshots = _publishedEvents.Where(e => e.Symbol == "AAPL").ToList();

        var lastSpySnapshot = spySnapshots.Last().Payload as LOBSnapshot;
        var lastAaplSnapshot = aaplSnapshots.Last().Payload as LOBSnapshot;

        lastSpySnapshot!.Bids.Should().HaveCount(2);
        lastAaplSnapshot!.Bids.Should().HaveCount(1);
        lastSpySnapshot.Bids[0].Price.Should().Be(450.00m);
        lastAaplSnapshot.Bids[0].Price.Should().Be(180.00m);
    }

    [Fact]
    public void IsSymbolStreamStale_IndependentPerSymbol()
    {
        // Arrange - Cause stale for SPY only
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Update, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.OnDepth(CreateDepthUpdate("AAPL", DepthOperation.Insert, OrderBookSide.Bid, 0, 180.00m, 200));

        // Assert
        _collector.IsSymbolStreamStale("SPY").Should().BeTrue();
        _collector.IsSymbolStreamStale("AAPL").Should().BeFalse();
    }

    #endregion

    #region Subscription Tests

    [Fact]
    public void OnDepth_WithExplicitSubscriptionRequired_IgnoresUnsubscribedSymbols()
    {
        // Arrange
        var collector = new MarketDepthCollector(_publisher, requireExplicitSubscription: true);
        _publisher.Clear();

        // Act - No subscription, should be ignored
        collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));

        // Assert
        _publishedEvents.Should().BeEmpty();
    }

    [Fact]
    public void OnDepth_WithExplicitSubscription_ProcessesSubscribedSymbols()
    {
        // Arrange
        var collector = new MarketDepthCollector(_publisher, requireExplicitSubscription: true);
        collector.RegisterSubscription("SPY");
        _publisher.Clear();

        // Act
        collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));

        // Assert
        _publishedEvents.Should().HaveCount(1);
    }

    [Fact]
    public void UnregisterSubscription_IgnoresFutureUpdates()
    {
        // Arrange
        var collector = new MarketDepthCollector(_publisher, requireExplicitSubscription: true);
        collector.RegisterSubscription("SPY");
        collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));

        collector.UnregisterSubscription("SPY");
        _publisher.Clear();

        // Act
        collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 1, 449.90m, 150));

        // Assert
        _publishedEvents.Should().BeEmpty();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void OnDepth_WithNullUpdate_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _collector.OnDepth(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OnDepth_WithEmptySymbol_DoesNotPublish()
    {
        // Arrange
        var update = CreateDepthUpdate("", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100);

        // Act
        _collector.OnDepth(update);

        // Assert
        _publishedEvents.Should().BeEmpty();
    }

    [Fact]
    public void OnDepth_WithWhitespaceSymbol_DoesNotPublish()
    {
        // Arrange
        var update = CreateDepthUpdate("   ", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100);

        // Act
        _collector.OnDepth(update);

        // Assert
        _publishedEvents.Should().BeEmpty();
    }

    [Fact]
    public void OnDepth_SymbolIsCaseInsensitive()
    {
        // Arrange & Act
        _collector.OnDepth(CreateDepthUpdate("spy", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 1, 449.90m, 150));

        // Assert - Should update same book
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.Bids.Should().HaveCount(2);
    }

    [Fact]
    public void IsSymbolStreamStale_WithNonExistentSymbol_ReturnsFalse()
    {
        // Act
        var isStale = _collector.IsSymbolStreamStale("NONEXISTENT");

        // Assert
        isStale.Should().BeFalse();
    }

    [Fact]
    public void ResetSymbolStream_WithEmptySymbol_DoesNotThrow()
    {
        // Act
        var act = () => _collector.ResetSymbolStream("");

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Level Reindexing Tests

    [Fact]
    public void OnDepth_AfterDelete_ReindexesRemainingLevels()
    {
        // Arrange - Build 3-level book
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 1, 449.90m, 150));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 2, 449.80m, 200));

        // Act - Delete middle level
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Delete, OrderBookSide.Bid, 1, 0, 0));

        // Assert
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.Bids.Should().HaveCount(2);
        lastSnapshot.Bids[0].Level.Should().Be(0);
        lastSnapshot.Bids[0].Price.Should().Be(450.00m);
        lastSnapshot.Bids[1].Level.Should().Be(1);
        lastSnapshot.Bids[1].Price.Should().Be(449.80m);
    }

    [Fact]
    public void OnDepth_InsertAtMiddle_ShiftsLevels()
    {
        // Arrange - Build 2-level book
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 0, 450.00m, 100));
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 1, 449.80m, 200));

        // Act - Insert at position 1 (middle)
        _collector.OnDepth(CreateDepthUpdate("SPY", DepthOperation.Insert, OrderBookSide.Bid, 1, 449.90m, 150));

        // Assert
        var lastSnapshot = _publishedEvents.Last().Payload as LOBSnapshot;
        lastSnapshot!.Bids.Should().HaveCount(3);
        lastSnapshot.Bids[0].Price.Should().Be(450.00m);
        lastSnapshot.Bids[1].Price.Should().Be(449.90m);
        lastSnapshot.Bids[2].Price.Should().Be(449.80m);
    }

    #endregion

    #region MarketMaker Tests

    [Fact]
    public void OnDepth_WithMarketMaker_PreservesMarketMakerInfo()
    {
        // Arrange
        var update = new MarketDepthUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            Position: 0,
            Operation: DepthOperation.Insert,
            Side: OrderBookSide.Bid,
            Price: 450.00m,
            Size: 100,
            MarketMaker: "GSCO");

        // Act
        _collector.OnDepth(update);

        // Assert
        var snapshot = _publishedEvents[0].Payload as LOBSnapshot;
        snapshot!.Bids[0].MarketMaker.Should().Be("GSCO");
    }

    #endregion

    #region Helper Methods

    private static MarketDepthUpdate CreateDepthUpdate(
        string symbol,
        DepthOperation operation,
        OrderBookSide side,
        ushort position,
        decimal price,
        decimal size,
        long sequenceNumber = 0)
    {
        return new MarketDepthUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Position: position,
            Operation: operation,
            Side: side,
            Price: price,
            Size: size,
            MarketMaker: null,
            SequenceNumber: sequenceNumber,
            StreamId: "TEST",
            Venue: "NYSE");
    }

    #endregion
}
