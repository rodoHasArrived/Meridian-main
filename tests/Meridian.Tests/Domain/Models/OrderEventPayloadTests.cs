using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Events;
using Meridian.Contracts.Domain.Models;
using Xunit;
using MarketEvent = Meridian.Contracts.Domain.Events.MarketEventDto;

namespace Meridian.Tests;

/// <summary>
/// Unit tests for the order-event payload records:
/// OrderAdd, OrderModify, OrderCancel, OrderExecute, OrderReplace.
/// </summary>
public class OrderEventPayloadTests
{
    private static readonly DateTimeOffset Ts = new(2024, 6, 15, 9, 30, 0, TimeSpan.Zero);

    #region OrderAdd

    [Fact]
    public void OrderAdd_WithRequiredFields_CreatesSuccessfully()
    {
        var order = new OrderAdd(
            OrderId: "ORD-001",
            Symbol: "AAPL",
            Side: OrderSide.Buy,
            Price: 185.50m,
            DisplayedSize: 500,
            PriorityTimestamp: Ts,
            SequenceNumber: 1
        );

        order.OrderId.Should().Be("ORD-001");
        order.Symbol.Should().Be("AAPL");
        order.Side.Should().Be(OrderSide.Buy);
        order.Price.Should().Be(185.50m);
        order.DisplayedSize.Should().Be(500);
        order.PriorityTimestamp.Should().Be(Ts);
        order.SequenceNumber.Should().Be(1);
        order.HiddenSize.Should().BeNull();
        order.ParticipantId.Should().BeNull();
        order.MarketMaker.Should().BeNull();
        order.StreamId.Should().BeNull();
        order.Venue.Should().BeNull();
    }

    [Fact]
    public void OrderAdd_WithAllFields_CreatesSuccessfully()
    {
        var order = new OrderAdd(
            OrderId: "ORD-002",
            Symbol: "SPY",
            Side: OrderSide.Sell,
            Price: 450.25m,
            DisplayedSize: 100,
            PriorityTimestamp: Ts,
            SequenceNumber: 42,
            HiddenSize: 900,
            ParticipantId: "FIRM-A",
            MarketMaker: "MM-XYZ",
            StreamId: "NASDAQ-ITCH",
            Venue: "NASDAQ"
        );

        order.HiddenSize.Should().Be(900);
        order.ParticipantId.Should().Be("FIRM-A");
        order.MarketMaker.Should().Be("MM-XYZ");
        order.StreamId.Should().Be("NASDAQ-ITCH");
        order.Venue.Should().Be("NASDAQ");
    }

    [Theory]
    [InlineData(OrderSide.Buy)]
    [InlineData(OrderSide.Sell)]
    [InlineData(OrderSide.Unknown)]
    public void OrderAdd_WithAllSides_CreatesSuccessfully(OrderSide side)
    {
        var order = new OrderAdd("ORD-1", "AAPL", side, 100m, 100, Ts, 1);
        order.Side.Should().Be(side);
    }

    [Fact]
    public void OrderAdd_IsImmutable()
    {
        var original = new OrderAdd("ORD-1", "AAPL", OrderSide.Buy, 100m, 100, Ts, 1);
        var modified = original with { Price = 101m };

        original.Price.Should().Be(100m);
        modified.Price.Should().Be(101m);
    }

    [Fact]
    public void OrderAdd_Equality_WorksCorrectly()
    {
        var a = new OrderAdd("ORD-1", "AAPL", OrderSide.Buy, 100m, 100, Ts, 1);
        var b = new OrderAdd("ORD-1", "AAPL", OrderSide.Buy, 100m, 100, Ts, 1);
        var c = new OrderAdd("ORD-2", "AAPL", OrderSide.Buy, 100m, 100, Ts, 1);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void OrderAdd_InheritsMarketEventPayload()
    {
        var order = new OrderAdd("ORD-1", "AAPL", OrderSide.Buy, 100m, 100, Ts, 1);
        order.Should().BeAssignableTo<MarketEventPayload>();
    }

    #endregion

    #region OrderModify

    [Fact]
    public void OrderModify_WithRequiredFieldsOnly_CreatesSuccessfully()
    {
        var modify = new OrderModify(OrderId: "ORD-001", SequenceNumber: 10);

        modify.OrderId.Should().Be("ORD-001");
        modify.SequenceNumber.Should().Be(10);
        modify.NewPrice.Should().BeNull();
        modify.NewDisplayedSize.Should().BeNull();
        modify.NewHiddenSize.Should().BeNull();
        modify.LosesPriority.Should().BeFalse();
        modify.StreamId.Should().BeNull();
        modify.Venue.Should().BeNull();
    }

    [Fact]
    public void OrderModify_WithPriceChange_LosesPriorityTrue()
    {
        var modify = new OrderModify(
            OrderId: "ORD-001",
            SequenceNumber: 11,
            NewPrice: 186.00m,
            LosesPriority: true
        );

        modify.NewPrice.Should().Be(186.00m);
        modify.LosesPriority.Should().BeTrue();
    }

    [Fact]
    public void OrderModify_WithSizeChange_LosesPriorityFalse()
    {
        // Downward size adjustment typically preserves priority.
        var modify = new OrderModify(
            OrderId: "ORD-001",
            SequenceNumber: 12,
            NewDisplayedSize: 200,
            LosesPriority: false
        );

        modify.NewDisplayedSize.Should().Be(200);
        modify.LosesPriority.Should().BeFalse();
    }

    [Fact]
    public void OrderModify_WithAllFields_CreatesSuccessfully()
    {
        var modify = new OrderModify(
            OrderId: "ORD-003",
            SequenceNumber: 99,
            NewPrice: 190.00m,
            NewDisplayedSize: 300,
            NewHiddenSize: 700,
            LosesPriority: true,
            StreamId: "STREAM-1",
            Venue: "NYSE"
        );

        modify.NewPrice.Should().Be(190.00m);
        modify.NewDisplayedSize.Should().Be(300);
        modify.NewHiddenSize.Should().Be(700);
        modify.LosesPriority.Should().BeTrue();
        modify.StreamId.Should().Be("STREAM-1");
        modify.Venue.Should().Be("NYSE");
    }

    [Fact]
    public void OrderModify_IsImmutable()
    {
        var original = new OrderModify("ORD-1", 1);
        var modified = original with { LosesPriority = true };

        original.LosesPriority.Should().BeFalse();
        modified.LosesPriority.Should().BeTrue();
    }

    [Fact]
    public void OrderModify_InheritsMarketEventPayload()
    {
        var modify = new OrderModify("ORD-1", 1);
        modify.Should().BeAssignableTo<MarketEventPayload>();
    }

    #endregion

    #region OrderCancel

    [Fact]
    public void OrderCancel_WithRequiredFields_CreatesSuccessfully()
    {
        var cancel = new OrderCancel(
            OrderId: "ORD-005",
            CanceledSize: 500,
            SequenceNumber: 20
        );

        cancel.OrderId.Should().Be("ORD-005");
        cancel.CanceledSize.Should().Be(500);
        cancel.SequenceNumber.Should().Be(20);
        cancel.StreamId.Should().BeNull();
        cancel.Venue.Should().BeNull();
    }

    [Fact]
    public void OrderCancel_WithOptionalFields_CreatesSuccessfully()
    {
        var cancel = new OrderCancel(
            OrderId: "ORD-006",
            CanceledSize: 250,
            SequenceNumber: 21,
            StreamId: "FEED-1",
            Venue: "BATS"
        );

        cancel.StreamId.Should().Be("FEED-1");
        cancel.Venue.Should().Be("BATS");
    }

    [Fact]
    public void OrderCancel_PartialCancel_AcceptsAnyPositiveSize()
    {
        // Partial cancel: only 100 of 500 total shares removed.
        var cancel = new OrderCancel("ORD-007", CanceledSize: 100, SequenceNumber: 30);
        cancel.CanceledSize.Should().Be(100);
    }

    [Fact]
    public void OrderCancel_IsImmutable()
    {
        var original = new OrderCancel("ORD-1", 100, 1);
        var modified = original with { CanceledSize = 50 };

        original.CanceledSize.Should().Be(100);
        modified.CanceledSize.Should().Be(50);
    }

    [Fact]
    public void OrderCancel_InheritsMarketEventPayload()
    {
        var cancel = new OrderCancel("ORD-1", 100, 1);
        cancel.Should().BeAssignableTo<MarketEventPayload>();
    }

    #endregion

    #region OrderExecute

    [Fact]
    public void OrderExecute_WithRequiredFields_CreatesSuccessfully()
    {
        var exec = new OrderExecute(
            RestingOrderId: "REST-001",
            ExecPrice: 450.00m,
            ExecSize: 100,
            AggressorSide: AggressorSide.Buy,
            SequenceNumber: 50
        );

        exec.RestingOrderId.Should().Be("REST-001");
        exec.ExecPrice.Should().Be(450.00m);
        exec.ExecSize.Should().Be(100);
        exec.AggressorSide.Should().Be(AggressorSide.Buy);
        exec.SequenceNumber.Should().Be(50);
        exec.TakerOrderId.Should().BeNull();
        exec.TradeId.Should().BeNull();
        exec.StreamId.Should().BeNull();
        exec.Venue.Should().BeNull();
    }

    [Fact]
    public void OrderExecute_WithAllFields_CreatesSuccessfully()
    {
        var exec = new OrderExecute(
            RestingOrderId: "REST-002",
            ExecPrice: 185.75m,
            ExecSize: 200,
            AggressorSide: AggressorSide.Sell,
            SequenceNumber: 51,
            TakerOrderId: "AGG-002",
            TradeId: "TRD-9999",
            StreamId: "ITCH-STREAM",
            Venue: "NYSE"
        );

        exec.TakerOrderId.Should().Be("AGG-002");
        exec.TradeId.Should().Be("TRD-9999");
        exec.StreamId.Should().Be("ITCH-STREAM");
        exec.Venue.Should().Be("NYSE");
    }

    [Theory]
    [InlineData(AggressorSide.Buy)]
    [InlineData(AggressorSide.Sell)]
    [InlineData(AggressorSide.Unknown)]
    public void OrderExecute_WithAllAggressorSides_CreatesSuccessfully(AggressorSide side)
    {
        var exec = new OrderExecute("REST-1", 100m, 100, side, 1);
        exec.AggressorSide.Should().Be(side);
    }

    [Fact]
    public void OrderExecute_IsImmutable()
    {
        var original = new OrderExecute("REST-1", 100m, 100, AggressorSide.Buy, 1);
        var modified = original with { ExecPrice = 101m };

        original.ExecPrice.Should().Be(100m);
        modified.ExecPrice.Should().Be(101m);
    }

    [Fact]
    public void OrderExecute_InheritsMarketEventPayload()
    {
        var exec = new OrderExecute("REST-1", 100m, 100, AggressorSide.Buy, 1);
        exec.Should().BeAssignableTo<MarketEventPayload>();
    }

    #endregion

    #region OrderReplace

    [Fact]
    public void OrderReplace_WithRequiredFields_CreatesSuccessfully()
    {
        var replace = new OrderReplace(
            OldOrderId: "OLD-001",
            NewOrderId: "NEW-001",
            SequenceNumber: 70
        );

        replace.OldOrderId.Should().Be("OLD-001");
        replace.NewOrderId.Should().Be("NEW-001");
        replace.SequenceNumber.Should().Be(70);
        replace.NewPrice.Should().BeNull();
        replace.NewDisplayedSize.Should().BeNull();
        replace.NewHiddenSize.Should().BeNull();
        replace.LosesPriority.Should().BeTrue();   // default is true
        replace.StreamId.Should().BeNull();
        replace.Venue.Should().BeNull();
    }

    [Fact]
    public void OrderReplace_DefaultLosesPriority_IsTrue()
    {
        // Venue ID reassignment always resets queue position by default.
        var replace = new OrderReplace("OLD-1", "NEW-1", 1);
        replace.LosesPriority.Should().BeTrue();
    }

    [Fact]
    public void OrderReplace_WithAllFields_CreatesSuccessfully()
    {
        var replace = new OrderReplace(
            OldOrderId: "OLD-002",
            NewOrderId: "NEW-002",
            SequenceNumber: 71,
            NewPrice: 186.00m,
            NewDisplayedSize: 400,
            NewHiddenSize: 600,
            LosesPriority: false,
            StreamId: "REPLACE-STREAM",
            Venue: "NASDAQ"
        );

        replace.NewPrice.Should().Be(186.00m);
        replace.NewDisplayedSize.Should().Be(400);
        replace.NewHiddenSize.Should().Be(600);
        replace.LosesPriority.Should().BeFalse();
        replace.StreamId.Should().Be("REPLACE-STREAM");
        replace.Venue.Should().Be("NASDAQ");
    }

    [Fact]
    public void OrderReplace_IsImmutable()
    {
        var original = new OrderReplace("OLD-1", "NEW-1", 1);
        var modified = original with { LosesPriority = false };

        original.LosesPriority.Should().BeTrue();
        modified.LosesPriority.Should().BeFalse();
    }

    [Fact]
    public void OrderReplace_InheritsMarketEventPayload()
    {
        var replace = new OrderReplace("OLD-1", "NEW-1", 1);
        replace.Should().BeAssignableTo<MarketEventPayload>();
    }

    #endregion

    #region MarketEvent factory methods

    [Fact]
    public void CreateOrderAdd_WithExplicitSeq_UsesProvidedSeq()
    {
        var order = new OrderAdd("ORD-1", "AAPL", OrderSide.Buy, 100m, 100, Ts, SequenceNumber: 5);
        var evt = MarketEvent.CreateOrderAdd(Ts, "AAPL", order, seq: 99);

        evt.Type.Should().Be(MarketEventType.OrderAdd);
        evt.Sequence.Should().Be(99);
        evt.Symbol.Should().Be("AAPL");
    }

    [Fact]
    public void CreateOrderAdd_WithZeroSeq_FallsBackToPayloadSequence()
    {
        var order = new OrderAdd("ORD-1", "AAPL", OrderSide.Buy, 100m, 100, Ts, SequenceNumber: 42);
        var evt = MarketEvent.CreateOrderAdd(Ts, "AAPL", order);

        evt.Sequence.Should().Be(42);
    }

    [Fact]
    public void CreateOrderModify_CreatesCorrectEventType()
    {
        var modify = new OrderModify("ORD-1", SequenceNumber: 7);
        var evt = MarketEvent.CreateOrderModify(Ts, "AAPL", modify);

        evt.Type.Should().Be(MarketEventType.OrderModify);
        evt.Sequence.Should().Be(7);
    }

    [Fact]
    public void CreateOrderCancel_CreatesCorrectEventType()
    {
        var cancel = new OrderCancel("ORD-1", CanceledSize: 100, SequenceNumber: 8);
        var evt = MarketEvent.CreateOrderCancel(Ts, "AAPL", cancel);

        evt.Type.Should().Be(MarketEventType.OrderCancel);
        evt.Sequence.Should().Be(8);
    }

    [Fact]
    public void CreateOrderExecute_CreatesCorrectEventType()
    {
        var exec = new OrderExecute("REST-1", 100m, 100, AggressorSide.Buy, SequenceNumber: 9);
        var evt = MarketEvent.CreateOrderExecute(Ts, "AAPL", exec);

        evt.Type.Should().Be(MarketEventType.OrderExecute);
        evt.Sequence.Should().Be(9);
    }

    [Fact]
    public void CreateOrderReplace_CreatesCorrectEventType()
    {
        var replace = new OrderReplace("OLD-1", "NEW-1", SequenceNumber: 10);
        var evt = MarketEvent.CreateOrderReplace(Ts, "AAPL", replace);

        evt.Type.Should().Be(MarketEventType.OrderReplace);
        evt.Sequence.Should().Be(10);
    }

    #endregion

    #region OrderSide enum

    [Fact]
    public void OrderSide_HasExpectedValues()
    {
        ((byte)OrderSide.Unknown).Should().Be(0);
        ((byte)OrderSide.Buy).Should().Be(1);
        ((byte)OrderSide.Sell).Should().Be(2);
    }

    #endregion

    #region MarketEventType enum

    [Fact]
    public void MarketEventType_HasOrderEventValues()
    {
        ((byte)MarketEventType.OrderAdd).Should().Be(20);
        ((byte)MarketEventType.OrderModify).Should().Be(21);
        ((byte)MarketEventType.OrderCancel).Should().Be(22);
        ((byte)MarketEventType.OrderExecute).Should().Be(23);
        ((byte)MarketEventType.OrderReplace).Should().Be(24);
    }

    #endregion
}
