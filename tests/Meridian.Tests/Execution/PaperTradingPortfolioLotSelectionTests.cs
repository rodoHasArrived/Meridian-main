using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Tests.Execution;

public sealed class PaperTradingPortfolioLotSelectionTests
{
    [Fact]
    public void ApplyFill_WhenSellingLong_UsesHifoForRealisedPnl()
    {
        var portfolio = new PaperTradingPortfolio(100_000m, lotSelectionMethod: PositionLotSelectionMethod.Hifo);

        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, 10, 100m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, 10, 120m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Sell, 10, 130m));

        portfolio.RealisedPnl.Should().Be(100m);
    }

    [Fact]
    public void ApplyFill_WhenSellingLong_UsesFifoByDefault()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);

        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, 10, 100m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, 10, 120m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Sell, 10, 130m));

        portfolio.RealisedPnl.Should().Be(300m);
    }

    [Fact]
    public void ApplyFill_WhenPartiallySellingLong_RecomputesRemainingCostBasisFromLots()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);

        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, 10, 100m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, 10, 120m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Sell, 10, 130m));

        portfolio.GetPositions().Single().AverageCostBasis.Should().Be(120m);
    }

    [Fact]
    public void ApplyFill_WhenPartiallyCoveringShort_RecomputesRemainingCostBasisFromLots()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);

        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Sell, 10, 100m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Sell, 10, 120m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, 10, 90m));

        portfolio.GetPositions().Single().AverageCostBasis.Should().Be(120m);
    }

    private static ExecutionReport BuildFill(string symbol, OrderSide side, decimal quantity, decimal price) =>
        new()
        {
            OrderId = Guid.NewGuid().ToString("N"),
            ReportType = ExecutionReportType.Fill,
            Symbol = symbol,
            Side = side,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = quantity,
            FilledQuantity = quantity,
            FillPrice = price,
            Timestamp = DateTimeOffset.UtcNow,
        };
}
