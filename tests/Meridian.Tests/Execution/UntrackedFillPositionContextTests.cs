using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Xunit;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

/// <summary>
/// The rule that decides whether a fill for an order this host never tracked can be booked
/// against the live book: an opening or adding buy is established by the fill itself, a
/// reduction is established by the lot it reduces, and anything the book cannot tell from the
/// close of a position it has lost is not bookable.
/// </summary>
public sealed class UntrackedFillPositionContextTests
{
    [Fact]
    public void Buy_IntoNoPosition_IsBookable()
    {
        OrderManagementSystem.TryDescribeMissingPositionContext(
                new StaticPortfolioState(), Fill(OrderSide.Buy), 5m, out var reason)
            .Should().BeFalse("a buy that opens a long is established by its own fill price");
        reason.Should().BeNull();
    }

    [Fact]
    public void Buy_AddingToALong_IsBookable()
    {
        OrderManagementSystem.TryDescribeMissingPositionContext(
                new StaticPortfolioState(new TestPosition("AAPL", 10)), Fill(OrderSide.Buy), 5m, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Buy_CoveringWithinAKnownShort_IsBookable()
    {
        OrderManagementSystem.TryDescribeMissingPositionContext(
                new StaticPortfolioState(new TestPosition("AAPL", -10)), Fill(OrderSide.Buy), 10m, out _)
            .Should().BeFalse("the short's lot supplies the basis for the cover");
    }

    [Fact]
    public void Buy_ReversingThroughAKnownShort_IsNotBookable()
    {
        OrderManagementSystem.TryDescribeMissingPositionContext(
                new StaticPortfolioState(new TestPosition("AAPL", -4)), Fill(OrderSide.Buy), 5m, out var reason)
            .Should().BeTrue();
        reason.Should().Contain("cover the known short");
    }

    [Fact]
    public void Sell_IntoNoPosition_IsNotBookable()
    {
        OrderManagementSystem.TryDescribeMissingPositionContext(
                new StaticPortfolioState(), Fill(OrderSide.Sell), 4m, out var reason)
            .Should().BeTrue("it cannot be told from the close of a position lost with the previous host");
        reason.Should().Contain("holds no long");
    }

    [Fact]
    public void Sell_IntoAKnownShort_IsNotBookable()
    {
        OrderManagementSystem.TryDescribeMissingPositionContext(
                new StaticPortfolioState(new TestPosition("AAPL", -3)), Fill(OrderSide.Sell), 4m, out _)
            .Should().BeTrue();
    }

    [Fact]
    public void Sell_WithinAKnownLong_IsBookable()
    {
        OrderManagementSystem.TryDescribeMissingPositionContext(
                new StaticPortfolioState(new TestPosition("AAPL", 10)), Fill(OrderSide.Sell), 10m, out _)
            .Should().BeFalse("the long's lot supplies the basis for the close");
    }

    [Fact]
    public void Sell_ExceedingTheKnownLong_IsNotBookable()
    {
        OrderManagementSystem.TryDescribeMissingPositionContext(
                new StaticPortfolioState(new TestPosition("AAPL", 3)), Fill(OrderSide.Sell), 4m, out var reason)
            .Should().BeTrue();
        reason.Should().Contain("reverse through zero");
    }

    [Fact]
    public void AnyFill_WithoutAComposedBook_IsNotBookable()
    {
        OrderManagementSystem.TryDescribeMissingPositionContext(null, Fill(OrderSide.Buy), 1m, out var reason)
            .Should().BeTrue();
        reason.Should().Contain("no portfolio state");
    }

    [Fact]
    public void Symbol_IsMatchedCaseInsensitively()
    {
        OrderManagementSystem.TryDescribeMissingPositionContext(
                new StaticPortfolioState(new TestPosition("AAPL", 10)), Fill(OrderSide.Sell) with { Symbol = "aapl" }, 4m, out _)
            .Should().BeFalse();
    }

    private static ExecutionReport Fill(OrderSide side) => new()
    {
        OrderId = "alpaca-1",
        ClientOrderId = "MDN-20260807-000099",
        Symbol = "AAPL",
        Side = side,
        OrderQuantity = 10m,
        FilledQuantity = 10m,
        FillPrice = 100m,
        OrderStatus = OrderStatus.Filled,
        ReportType = ExecutionReportType.Fill,
        Timestamp = DateTimeOffset.Parse("2026-08-07T14:30:05Z")
    };

    private sealed class StaticPortfolioState : IPortfolioState
    {
        public StaticPortfolioState(params IPosition[] positions)
        {
            Positions = positions.ToDictionary(static position => position.Symbol, StringComparer.Ordinal);
        }

        public decimal Cash => 100_000m;
        public decimal PortfolioValue => 100_000m;
        public decimal UnrealisedPnl => 0m;
        public decimal RealisedPnl => 0m;
        public IReadOnlyDictionary<string, IPosition> Positions { get; }
    }

    private sealed record TestPosition(
        string Symbol,
        long Quantity,
        decimal AverageCostBasis = 100m,
        decimal UnrealizedPnl = 0m,
        decimal RealizedPnl = 0m) : IPosition;
}
