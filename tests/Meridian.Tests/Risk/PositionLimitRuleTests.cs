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

    // ── Working orders ───────────────────────────────────────────────
    // The position tracker reports filled quantity only. These pin that quantity still working
    // on the order's own side counts toward the limit.

    private static OrderState Working(
        string orderId, OrderSide side, decimal quantity, decimal filled = 0m,
        string symbol = DefaultSymbol, string? parentOrderId = null) => new()
    {
        OrderId = orderId,
        Symbol = symbol,
        Side = side,
        Type = OrderType.Limit,
        Quantity = quantity,
        FilledQuantity = filled,
        Status = filled > 0m ? OrderStatus.PartiallyFilled : OrderStatus.Accepted,
        ParentOrderId = parentOrderId,
    };

    private static PositionLimitRule CreateSutWithWorkingOrders(
        decimal filledPosition, params OrderState[] workingOrders)
        => new(
            TrackerWithPosition(DefaultSymbol, filledPosition),
            DefaultMaxPositionSize,
            NullLogger<PositionLimitRule>.Instance,
            workingOrders: () => workingOrders);

    [Fact]
    public async Task EvaluateAsync_SecondRestingBuy_ThatWouldExceedLimitWithTheFirst_IsRejected()
    {
        // Flat, limit 100, one resting buy of 80: a second buy of 80 would reach 160 once both fill.
        var sut = CreateSutWithWorkingOrders(0m, Working("o-1", OrderSide.Buy, 80m));

        var result = await sut.EvaluateAsync(CreateBuyOrder(quantity: 80m));

        result.IsApproved.Should().BeFalse();
        result.ObservedValue.Should().Be(160m);
        result.RejectReason.Should().Contain("still working");
    }

    [Fact]
    public async Task EvaluateAsync_RestingBuyThatStillFitsWithWorkingOrders_IsApproved()
    {
        var sut = CreateSutWithWorkingOrders(0m, Working("o-1", OrderSide.Buy, 80m));

        (await sut.EvaluateAsync(CreateBuyOrder(quantity: 20m))).IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_CountsOnlyTheUnfilledRemainderOfAWorkingOrder()
    {
        // 60 of the resting 80 already filled and are in the position; only 20 are still working.
        var sut = CreateSutWithWorkingOrders(60m, Working("o-1", OrderSide.Buy, 80m, filled: 60m));

        (await sut.EvaluateAsync(CreateBuyOrder(quantity: 20m))).IsApproved.Should().BeTrue();
        (await sut.EvaluateAsync(CreateBuyOrder(quantity: 21m))).IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WorkingOrdersOnTheOppositeSideOrAnotherSymbol_AreNotNettedOrCounted()
    {
        var sut = CreateSutWithWorkingOrders(
            80m,
            Working("o-sell", OrderSide.Sell, 80m),
            Working("o-other", OrderSide.Buy, 500m, symbol: "MSFT"));

        // A resting sell may never fill, so it does not make room for a buy; MSFT is irrelevant.
        (await sut.EvaluateAsync(CreateBuyOrder(quantity: 20m))).IsApproved.Should().BeTrue();
        (await sut.EvaluateAsync(CreateBuyOrder(quantity: 21m))).IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_AmendmentOfAWorkingOrder_DoesNotCountItsOwnOriginal()
    {
        // An amendment is revalidated under the original order id with the full amended quantity.
        var sut = CreateSutWithWorkingOrders(0m, Working("o-1", OrderSide.Buy, 80m));

        var amendTo100 = CreateBuyOrder(quantity: 100m) with { ClientOrderId = "o-1" };
        var amendTo101 = CreateBuyOrder(quantity: 101m) with { ClientOrderId = "o-1" };

        (await sut.EvaluateAsync(amendTo100)).IsApproved.Should().BeTrue();
        (await sut.EvaluateAsync(amendTo101)).IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_ReducingOrderWhileOverTheLimit_IsStillAllowedWithWorkingOrders()
    {
        // Long 300 against a limit of 100 with one resting sell of 100: unwinding must stay open.
        var sut = CreateSutWithWorkingOrders(300m, Working("o-1", OrderSide.Sell, 100m));

        (await sut.EvaluateAsync(CreateSellOrder(quantity: 100m))).IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WorkingSellsThatWouldTogetherOpenAnOversizedShort_AreRejected()
    {
        // Long 100; resting sells of 100 and 100 already take the worst case to -100.
        var sut = CreateSutWithWorkingOrders(
            100m,
            Working("o-1", OrderSide.Sell, 100m),
            Working("o-2", OrderSide.Sell, 100m));

        (await sut.EvaluateAsync(CreateSellOrder(quantity: 50m))).IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_BracketExitLegsOfOneParent_CountOnceBecauseOnlyOneCanExecute()
    {
        // Long 100 protected by a take-profit and a stop-loss leg of the same entry. Summing the
        // legs would read the worst case as -100 and refuse any manual partial exit.
        var sut = CreateSutWithWorkingOrders(
            100m,
            Working("tp", OrderSide.Sell, 100m, parentOrderId: "entry-1"),
            Working("sl", OrderSide.Sell, 100m, parentOrderId: "entry-1"));

        (await sut.EvaluateAsync(CreateSellOrder(quantity: 50m))).IsApproved.Should().BeTrue();
        // Worst case with one leg filling is flat; a further 101 would open a 101 short.
        (await sut.EvaluateAsync(CreateSellOrder(quantity: 101m))).IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WithoutAWorkingOrderSource_BehavesAsBefore()
    {
        var sut = new PositionLimitRule(
            TrackerWithPosition(DefaultSymbol, 0m),
            DefaultMaxPositionSize,
            NullLogger<PositionLimitRule>.Instance,
            workingOrders: () => null);

        (await sut.EvaluateAsync(CreateBuyOrder(quantity: 100m))).IsApproved.Should().BeTrue();
    }
}
