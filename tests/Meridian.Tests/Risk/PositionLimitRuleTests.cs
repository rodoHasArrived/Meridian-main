using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Risk;

public sealed class PositionLimitRuleTests
{
    private const string DefaultSymbol = "AAPL";
    private const decimal DefaultMaxPositionSize = 100m;

    private static PositionLimitRule CreateSut(
        IPositionTracker? positionTracker = null,
        decimal maxPositionSize = DefaultMaxPositionSize)
    {
        positionTracker ??= new Mock<IPositionTracker>().Object;
        return new PositionLimitRule(
            positionTracker,
            maxPositionSize,
            NullLogger<PositionLimitRule>.Instance);
    }

    private static OrderRequest CreateBuyOrder(decimal quantity, string symbol = DefaultSymbol) => new()
    {
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = quantity,
    };

    private static OrderRequest CreateSellOrder(decimal quantity, string symbol = DefaultSymbol) => new()
    {
        Symbol = symbol,
        Side = OrderSide.Sell,
        Type = OrderType.Market,
        Quantity = quantity,
    };

    private static IPositionTracker TrackerWithPosition(string symbol, decimal quantity)
    {
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPosition(symbol)).Returns(new PositionState
        {
            Symbol = symbol,
            Quantity = quantity,
        });
        return tracker.Object;
    }

    [Fact]
    public void RuleName_ReturnsPositionLimit()
    {
        var sut = CreateSut();

        sut.RuleName.Should().Be("PositionLimit");
    }

    [Fact]
    public void Constructor_WithNullPositionTracker_ThrowsArgumentNullException()
    {
        var act = () => new PositionLimitRule(
            null!,
            DefaultMaxPositionSize,
            NullLogger<PositionLimitRule>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("positionTracker");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new PositionLimitRule(
            new Mock<IPositionTracker>().Object,
            DefaultMaxPositionSize,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task EvaluateAsync_BuyFromFlatPosition_WithinLimit_ReturnsApproved()
    {
        var tracker = TrackerWithPosition(DefaultSymbol, quantity: 0m);
        var sut = CreateSut(tracker, maxPositionSize: 100m);

        var result = await sut.EvaluateAsync(CreateBuyOrder(quantity: 50m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_BuyFromFlatPosition_ExceedsLimit_ReturnsRejected()
    {
        var tracker = TrackerWithPosition(DefaultSymbol, quantity: 0m);
        var sut = CreateSut(tracker, maxPositionSize: 100m);

        var result = await sut.EvaluateAsync(CreateBuyOrder(quantity: 150m));

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateAsync_BuyFromExistingPosition_ProjectedWithinLimit_ReturnsApproved()
    {
        // Current: 60 long, Buy 30 → projected 90, limit 100 → approve
        var tracker = TrackerWithPosition(DefaultSymbol, quantity: 60m);
        var sut = CreateSut(tracker, maxPositionSize: 100m);

        var result = await sut.EvaluateAsync(CreateBuyOrder(quantity: 30m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_BuyFromExistingPosition_ProjectedExceedsLimit_ReturnsRejected()
    {
        // Current: 80 long, Buy 30 → projected 110, limit 100 → reject
        var tracker = TrackerWithPosition(DefaultSymbol, quantity: 80m);
        var sut = CreateSut(tracker, maxPositionSize: 100m);

        var result = await sut.EvaluateAsync(CreateBuyOrder(quantity: 30m));

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_SellFromLongPosition_ProjectedWithinLimit_ReturnsApproved()
    {
        // Current: 80 long, Sell 60 → projected +20, abs(20) = 20 < 100 → approve
        var tracker = TrackerWithPosition(DefaultSymbol, quantity: 80m);
        var sut = CreateSut(tracker, maxPositionSize: 100m);

        var result = await sut.EvaluateAsync(CreateSellOrder(quantity: 60m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_SellFlipsToShortBeyondLimit_ReturnsRejected()
    {
        // Current: 50 long, Sell 200 → projected -150, abs(-150) = 150 > 100 → reject
        var tracker = TrackerWithPosition(DefaultSymbol, quantity: 50m);
        var sut = CreateSut(tracker, maxPositionSize: 100m);

        var result = await sut.EvaluateAsync(CreateSellOrder(quantity: 200m));

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_SellFlipsToShortWithinLimit_ReturnsApproved()
    {
        // Current: 50 long, Sell 120 → projected -70, abs(-70) = 70 < 100 → approve
        var tracker = TrackerWithPosition(DefaultSymbol, quantity: 50m);
        var sut = CreateSut(tracker, maxPositionSize: 100m);

        var result = await sut.EvaluateAsync(CreateSellOrder(quantity: 120m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_BuyExactlyAtLimit_ReturnsApproved()
    {
        // Exactly at limit: projected == maxPositionSize → abs(100) == 100, NOT > 100 → approve
        var tracker = TrackerWithPosition(DefaultSymbol, quantity: 0m);
        var sut = CreateSut(tracker, maxPositionSize: 100m);

        var result = await sut.EvaluateAsync(CreateBuyOrder(quantity: 100m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_UsesCorrectSymbolForPositionLookup()
    {
        const string symbol = "MSFT";
        var trackerMock = new Mock<IPositionTracker>();
        trackerMock.Setup(t => t.GetPosition(symbol)).Returns(new PositionState
        {
            Symbol = symbol,
            Quantity = 0m,
        });
        var sut = CreateSut(trackerMock.Object);

        await sut.EvaluateAsync(CreateBuyOrder(quantity: 10m, symbol: symbol));

        trackerMock.Verify(t => t.GetPosition(symbol), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_RejectedResult_ContainsSymbolInReason()
    {
        var tracker = TrackerWithPosition(DefaultSymbol, quantity: 0m);
        var sut = CreateSut(tracker, maxPositionSize: 50m);

        var result = await sut.EvaluateAsync(CreateBuyOrder(quantity: 200m));

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Contain(DefaultSymbol);
    }

    // ── Working orders ───────────────────────────────────────────────────

    private static OrderState WorkingOrder(string orderId, OrderSide side, decimal quantity, decimal filled = 0m) => new()
    {
        OrderId = orderId,
        Symbol = DefaultSymbol,
        Side = side,
        Type = OrderType.Limit,
        Quantity = quantity,
        FilledQuantity = filled,
        LimitPrice = 100m,
        Status = filled > 0m ? OrderStatus.PartiallyFilled : OrderStatus.Accepted,
    };

    private static PositionLimitRule CreateSutWithBook(params OrderState[] book)
    {
        var orderManager = new Mock<IOrderManager>();
        orderManager.Setup(m => m.GetUnitReservingOrders()).Returns(book);
        return new PositionLimitRule(
            new Mock<IPositionTracker>().Object,
            () => DefaultMaxPositionSize,
            NullLogger<PositionLimitRule>.Instance,
            () => orderManager.Object);
    }

    [Fact]
    public async Task EvaluateAsync_RestingBuysAlreadyAtLimit_RejectsNextBuy()
    {
        // Five 90-share resting buys against a 100-share limit all used to pass, because only
        // filled positions were counted.
        var sut = CreateSutWithBook(WorkingOrder("o-1", OrderSide.Buy, 90m));

        var result = await sut.EvaluateAsync(CreateBuyOrder(90m));

        result.IsApproved.Should().BeFalse();
        result.ObservedValue.Should().Be(180m, "the evidence must record the projection the rule evaluated");
    }

    [Fact]
    public async Task EvaluateAsync_PartiallyFilledWorkingOrder_CountsOnlyRemainingQuantity()
    {
        var sut = CreateSutWithBook(WorkingOrder("o-1", OrderSide.Buy, 90m, filled: 80m));

        var result = await sut.EvaluateAsync(CreateBuyOrder(90m));

        result.IsApproved.Should().BeTrue("10 remaining + 90 new stays within the 100-share limit");
    }

    [Fact]
    public async Task EvaluateAsync_OppositeSideWorkingOrder_DoesNotOffsetNewOrder()
    {
        // A resting sell can be cancelled at any time, so it must not create room for a buy.
        var sut = CreateSutWithBook(WorkingOrder("o-1", OrderSide.Sell, 90m));

        var result = await sut.EvaluateAsync(CreateBuyOrder(150m));

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_BrokerNotionalWorkingOrder_IsNotCountedAsShares()
    {
        // A resting $500 buy is stored with a placeholder Quantity of 500 and RoutedNotional 500.
        // Counting it as 500 shares would reject a 60-share buy against a 100-share limit.
        var notionalOrder = WorkingOrder("o-1", OrderSide.Buy, 500m) with { RoutedNotional = 500m };
        var sut = CreateSutWithBook(notionalOrder);

        var result = await sut.EvaluateAsync(CreateBuyOrder(60m));

        result.IsApproved.Should().BeTrue("a dollar-sized working order has no unit quantity to project");
    }

    [Fact]
    public async Task EvaluateAsync_AmendmentProbe_DoesNotCountTheOrderBeingAmended()
    {
        var sut = CreateSutWithBook(WorkingOrder("o-1", OrderSide.Buy, 60m));
        var amendment = CreateBuyOrder(90m) with { ClientOrderId = "o-1" };

        var result = await sut.EvaluateAsync(amendment);

        result.IsApproved.Should().BeTrue("the amended order replaces the 60-share original rather than adding to it");
    }

    [Fact]
    public async Task EvaluateAsync_BracketExitLegsOfOneParent_CountOnceBecauseOnlyOneCanExecute()
    {
        // Long 100 protected by a take-profit and a stop-loss leg of the same entry. Summing the
        // legs would read the worst case as -100 and refuse any manual partial exit.
        var orderManager = new Mock<IOrderManager>();
        orderManager.Setup(m => m.GetUnitReservingOrders()).Returns(
        [
            WorkingOrder("tp", OrderSide.Sell, 100m) with { ParentOrderId = "entry-1" },
            WorkingOrder("sl", OrderSide.Sell, 100m) with { ParentOrderId = "entry-1" },
        ]);
        var sut = new PositionLimitRule(
            TrackerWithPosition(DefaultSymbol, 100m),
            () => DefaultMaxPositionSize,
            NullLogger<PositionLimitRule>.Instance,
            () => orderManager.Object);

        (await sut.EvaluateAsync(CreateSellOrder(quantity: 50m))).IsApproved.Should().BeTrue();
        // Worst case with one leg filling is flat; a further sell beyond the limit opens a short.
        (await sut.EvaluateAsync(CreateSellOrder(quantity: DefaultMaxPositionSize + 1m))).IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_ExitLegsOfDifferentParents_AreEachCounted()
    {
        var sut = CreateSutWithBook(
            WorkingOrder("a", OrderSide.Buy, 40m) with { ParentOrderId = "entry-1" },
            WorkingOrder("b", OrderSide.Buy, 40m) with { ParentOrderId = "entry-2" });

        // 80 is working across two independent parents. Counting them as one leg would leave
        // room for 60 more; counting both leaves room for exactly 20.
        (await sut.EvaluateAsync(CreateBuyOrder(quantity: 20m))).IsApproved.Should().BeTrue();
        (await sut.EvaluateAsync(CreateBuyOrder(quantity: 21m))).IsApproved.Should().BeFalse();
    }
}
