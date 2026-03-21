using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Unit tests for <see cref="L3OrderBookCollector"/>, validating the L2+L3 dual-write contract:
/// every order-lifecycle call must publish exactly the L3 event followed by a derived L2 snapshot.
/// </summary>
public class L3OrderBookCollectorTests
{
    private static readonly DateTimeOffset Ts = new(2024, 6, 15, 9, 30, 0, TimeSpan.Zero);

    private readonly TestMarketEventPublisher _publisher;
    private readonly L3OrderBookCollector _collector;

    private IReadOnlyList<MarketEvent> Published => _publisher.PublishedEvents;

    public L3OrderBookCollectorTests()
    {
        _publisher = new TestMarketEventPublisher();
        _collector = new L3OrderBookCollector(_publisher, requireExplicitSubscription: false);
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private static OrderAdd MakeAdd(string orderId, string symbol, OrderSide side, decimal price, long size, long seq = 1)
        => new(orderId, symbol, side, price, size, Ts, seq, Venue: "NYSE");

    private static OrderModify MakeModify(string orderId, long seq, decimal? newPrice = null, long? newSize = null)
        => new(orderId, seq, NewPrice: newPrice, NewDisplayedSize: newSize, Venue: "NYSE");

    private static OrderCancel MakeCancel(string orderId, long canceledSize, long seq)
        => new(orderId, canceledSize, seq, Venue: "NYSE");

    private static OrderExecute MakeExecute(string restingId, decimal price, long size, long seq)
        => new(restingId, price, size, AggressorSide.Buy, seq, Venue: "NYSE");

    private static OrderReplace MakeReplace(string oldId, string newId, long seq, decimal? newPrice = null, long? newSize = null)
        => new(oldId, newId, seq, NewPrice: newPrice, NewDisplayedSize: newSize, Venue: "NYSE");

    // ─── dual-write contract ──────────────────────────────────────────────────

    [Fact]
    public void OnOrderAdd_PublishesL3ThenL2()
    {
        _collector.OnOrderAdd(MakeAdd("A1", "AAPL", OrderSide.Buy, 185m, 100, 1));

        Published.Should().HaveCount(2);
        Published[0].Type.Should().Be(MarketEventType.OrderAdd);
        Published[0].Payload.Should().BeOfType<OrderAdd>();
        Published[1].Type.Should().Be(MarketEventType.L2Snapshot);
        Published[1].Payload.Should().BeOfType<LOBSnapshot>();
    }

    [Fact]
    public void OnOrderModify_PublishesL3ThenL2()
    {
        _collector.OnOrderAdd(MakeAdd("A1", "AAPL", OrderSide.Buy, 185m, 100, 1));
        _publisher.Clear();

        _collector.OnOrderModify(Ts, "AAPL", MakeModify("A1", 2, newPrice: 186m));

        Published.Should().HaveCount(2);
        Published[0].Type.Should().Be(MarketEventType.OrderModify);
        Published[1].Type.Should().Be(MarketEventType.L2Snapshot);
    }

    [Fact]
    public void OnOrderCancel_PublishesL3ThenL2()
    {
        _collector.OnOrderAdd(MakeAdd("A1", "AAPL", OrderSide.Buy, 185m, 100, 1));
        _publisher.Clear();

        _collector.OnOrderCancel(Ts, "AAPL", MakeCancel("A1", 100, 2));

        Published.Should().HaveCount(2);
        Published[0].Type.Should().Be(MarketEventType.OrderCancel);
        Published[1].Type.Should().Be(MarketEventType.L2Snapshot);
    }

    [Fact]
    public void OnOrderExecute_PublishesL3ThenL2()
    {
        _collector.OnOrderAdd(MakeAdd("A1", "AAPL", OrderSide.Sell, 186m, 200, 1));
        _publisher.Clear();

        _collector.OnOrderExecute(Ts, "AAPL", MakeExecute("A1", 186m, 100, 2));

        Published.Should().HaveCount(2);
        Published[0].Type.Should().Be(MarketEventType.OrderExecute);
        Published[1].Type.Should().Be(MarketEventType.L2Snapshot);
    }

    [Fact]
    public void OnOrderReplace_PublishesL3ThenL2()
    {
        _collector.OnOrderAdd(MakeAdd("OLD", "AAPL", OrderSide.Buy, 185m, 100, 1));
        _publisher.Clear();

        _collector.OnOrderReplace(Ts, "AAPL", MakeReplace("OLD", "NEW", 2));

        Published.Should().HaveCount(2);
        Published[0].Type.Should().Be(MarketEventType.OrderReplace);
        Published[1].Type.Should().Be(MarketEventType.L2Snapshot);
    }

    // ─── L2 aggregation correctness ──────────────────────────────────────────

    [Fact]
    public void L2Snapshot_AggregatesMultipleBidOrdersAtSamePrice()
    {
        // Two buy orders at the same price should be combined into one bid level.
        _collector.OnOrderAdd(MakeAdd("B1", "SPY", OrderSide.Buy, 450m, 100, 1));
        _collector.OnOrderAdd(MakeAdd("B2", "SPY", OrderSide.Buy, 450m, 200, 2));

        var snap = GetLastSnapshot();
        snap.Bids.Should().HaveCount(1);
        snap.Bids[0].Price.Should().Be(450m);
        snap.Bids[0].Size.Should().Be(300m); // 100 + 200
    }

    [Fact]
    public void L2Snapshot_BidsSortedDescending_AsksSortedAscending()
    {
        _collector.OnOrderAdd(MakeAdd("B1", "SPY", OrderSide.Buy, 449m, 100, 1));
        _collector.OnOrderAdd(MakeAdd("B2", "SPY", OrderSide.Buy, 450m, 100, 2));
        _collector.OnOrderAdd(MakeAdd("A1", "SPY", OrderSide.Sell, 451m, 100, 3));
        _collector.OnOrderAdd(MakeAdd("A2", "SPY", OrderSide.Sell, 452m, 100, 4));

        var snap = GetLastSnapshot();
        snap.Bids[0].Price.Should().Be(450m); // best bid first (descending)
        snap.Bids[1].Price.Should().Be(449m);
        snap.Asks[0].Price.Should().Be(451m); // best ask first (ascending)
        snap.Asks[1].Price.Should().Be(452m);
    }

    [Fact]
    public void L2Snapshot_ReflectsModifyPrice()
    {
        _collector.OnOrderAdd(MakeAdd("B1", "SPY", OrderSide.Buy, 450m, 500, 1));
        _collector.OnOrderModify(Ts, "SPY", MakeModify("B1", 2, newPrice: 451m));

        var snap = GetLastSnapshot();
        snap.Bids.Should().HaveCount(1);
        snap.Bids[0].Price.Should().Be(451m);
        snap.Bids[0].Size.Should().Be(500m);
    }

    [Fact]
    public void L2Snapshot_ReflectsModifySize()
    {
        _collector.OnOrderAdd(MakeAdd("B1", "SPY", OrderSide.Buy, 450m, 500, 1));
        _collector.OnOrderModify(Ts, "SPY", MakeModify("B1", 2, newSize: 300));

        var snap = GetLastSnapshot();
        snap.Bids[0].Size.Should().Be(300m);
    }

    [Fact]
    public void L2Snapshot_PartialCancel_ReducesSize()
    {
        _collector.OnOrderAdd(MakeAdd("A1", "SPY", OrderSide.Buy, 450m, 500, 1));
        _collector.OnOrderCancel(Ts, "SPY", MakeCancel("A1", 200, 2));

        var snap = GetLastSnapshot();
        snap.Bids[0].Size.Should().Be(300m); // 500 - 200
    }

    [Fact]
    public void L2Snapshot_FullCancel_RemovesLevel()
    {
        _collector.OnOrderAdd(MakeAdd("A1", "SPY", OrderSide.Buy, 450m, 500, 1));
        _collector.OnOrderCancel(Ts, "SPY", MakeCancel("A1", 500, 2));

        var snap = GetLastSnapshot();
        snap.Bids.Should().BeEmpty();
        snap.Asks.Should().BeEmpty();
    }

    [Fact]
    public void L2Snapshot_PartialExecute_ReducesSize()
    {
        _collector.OnOrderAdd(MakeAdd("R1", "SPY", OrderSide.Sell, 451m, 300, 1));
        _collector.OnOrderExecute(Ts, "SPY", MakeExecute("R1", 451m, 100, 2));

        var snap = GetLastSnapshot();
        snap.Asks[0].Size.Should().Be(200m); // 300 - 100
    }

    [Fact]
    public void L2Snapshot_FullExecute_RemovesLevel()
    {
        _collector.OnOrderAdd(MakeAdd("R1", "SPY", OrderSide.Sell, 451m, 100, 1));
        _collector.OnOrderExecute(Ts, "SPY", MakeExecute("R1", 451m, 100, 2));

        var snap = GetLastSnapshot();
        snap.Asks.Should().BeEmpty();
    }

    [Fact]
    public void L2Snapshot_Replace_MovesOrderToNewId()
    {
        _collector.OnOrderAdd(MakeAdd("OLD", "AAPL", OrderSide.Buy, 185m, 400, 1));
        _collector.OnOrderReplace(Ts, "AAPL", MakeReplace("OLD", "NEW", 2, newPrice: 186m, newSize: 300));

        var snap = GetLastSnapshot();
        snap.Bids.Should().HaveCount(1);
        snap.Bids[0].Price.Should().Be(186m);
        snap.Bids[0].Size.Should().Be(300m);
    }

    [Fact]
    public void L2Snapshot_MidPrice_ComputedFromBestBidAndAsk()
    {
        _collector.OnOrderAdd(MakeAdd("B1", "SPY", OrderSide.Buy, 450m, 100, 1));
        _collector.OnOrderAdd(MakeAdd("A1", "SPY", OrderSide.Sell, 452m, 100, 2));

        var snap = GetLastSnapshot();
        snap.MidPrice.Should().Be(451m); // (450 + 452) / 2
    }

    // ─── sequence number propagation ─────────────────────────────────────────

    [Fact]
    public void L3Event_SequenceNumber_PropagatesFromPayload()
    {
        _collector.OnOrderAdd(MakeAdd("A1", "AAPL", OrderSide.Buy, 185m, 100, seq: 42));

        Published[0].Sequence.Should().Be(42);
    }

    [Fact]
    public void L2Snapshot_SequenceNumber_MatchesOrderEvent()
    {
        _collector.OnOrderAdd(MakeAdd("A1", "AAPL", OrderSide.Buy, 185m, 100, seq: 77));

        Published[1].Sequence.Should().Be(77); // L2 carries same seq as the triggering order event
    }

    // ─── guard / edge cases ───────────────────────────────────────────────────

    [Fact]
    public void OnOrderAdd_NullOrder_Throws()
    {
        var act = () => _collector.OnOrderAdd(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OnOrderModify_NullModify_Throws()
    {
        var act = () => _collector.OnOrderModify(Ts, "AAPL", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OnOrderCancel_NullCancel_Throws()
    {
        var act = () => _collector.OnOrderCancel(Ts, "AAPL", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OnOrderExecute_NullExecute_Throws()
    {
        var act = () => _collector.OnOrderExecute(Ts, "AAPL", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OnOrderReplace_NullReplace_Throws()
    {
        var act = () => _collector.OnOrderReplace(Ts, "AAPL", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ModifyUnknownOrder_IsSilentlyIgnored()
    {
        // No OrderAdd for "GHOST" — modify is a no-op on the book.
        _collector.OnOrderModify(Ts, "AAPL", MakeModify("GHOST", 1, newPrice: 100m));

        // L3 event + empty-book L2 snapshot are still published.
        Published.Should().HaveCount(2);
        Published[0].Type.Should().Be(MarketEventType.OrderModify);
        var snap = Published[1].Payload as LOBSnapshot;
        snap.Should().NotBeNull();
        snap!.Bids.Should().BeEmpty();
        snap.Asks.Should().BeEmpty();
    }

    [Fact]
    public void GetTrackedSymbols_ReturnsAllSymbolsWithOrders()
    {
        _collector.OnOrderAdd(MakeAdd("A", "SPY", OrderSide.Buy, 450m, 100, 1));
        _collector.OnOrderAdd(MakeAdd("B", "AAPL", OrderSide.Buy, 185m, 100, 1));

        _collector.GetTrackedSymbols().Should().BeEquivalentTo(["SPY", "AAPL"]);
    }

    [Fact]
    public void GetCurrentSnapshot_ReturnsNull_WhenNoOrdersExist()
    {
        _collector.GetCurrentSnapshot("UNKNOWN").Should().BeNull();
    }

    [Fact]
    public void GetCurrentSnapshot_ReturnsCurrentBookState()
    {
        _collector.OnOrderAdd(MakeAdd("B1", "SPY", OrderSide.Buy, 450m, 100, 1));

        var snap = _collector.GetCurrentSnapshot("SPY");
        snap.Should().NotBeNull();
        snap!.Bids.Should().HaveCount(1);
        snap.Bids[0].Price.Should().Be(450m);
    }

    // ─── subscription guard ───────────────────────────────────────────────────

    [Fact]
    public void WithExplicitSubscription_IgnoresUnregisteredSymbol()
    {
        var collector = new L3OrderBookCollector(_publisher, requireExplicitSubscription: true);

        collector.OnOrderAdd(MakeAdd("A1", "AAPL", OrderSide.Buy, 185m, 100, 1));

        Published.Should().BeEmpty();
    }

    [Fact]
    public void WithExplicitSubscription_ProcessesRegisteredSymbol()
    {
        var collector = new L3OrderBookCollector(_publisher, requireExplicitSubscription: true);
        collector.RegisterSubscription("AAPL");

        collector.OnOrderAdd(MakeAdd("A1", "AAPL", OrderSide.Buy, 185m, 100, 1));

        Published.Should().HaveCount(2);
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private LOBSnapshot GetLastSnapshot()
    {
        var snap = Published.LastOrDefault(e => e.Type == MarketEventType.L2Snapshot)?.Payload as LOBSnapshot;
        snap.Should().NotBeNull("an L2 snapshot should have been published");
        return snap!;
    }
}
