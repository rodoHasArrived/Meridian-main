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

        result.RejectReason.Should().Contain(DefaultSymbol);
    }
}
