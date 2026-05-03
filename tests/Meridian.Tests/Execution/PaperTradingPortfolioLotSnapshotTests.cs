using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Tests.Execution;

public sealed class PaperTradingPortfolioLotSnapshotTests
{
    [Fact]
    public void GetPositionLots_ReturnsTrackedLotsForDefaultAccount()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);

        portfolio.ApplyFill(BuildFill("MSFT", 5, 100m));
        portfolio.ApplyFill(BuildFill("MSFT", 5, 105m));

        var lots = portfolio.GetPositionLots("MSFT");

        lots.Should().HaveCount(2);
        lots[0].OpenQuantity.Should().Be(5);
        lots[1].OpenQuantity.Should().Be(5);
    }

    private static ExecutionReport BuildFill(string symbol, decimal quantity, decimal price) =>
        new()
        {
            OrderId = Guid.NewGuid().ToString("N"),
            ReportType = ExecutionReportType.Fill,
            Symbol = symbol,
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = quantity,
            FilledQuantity = quantity,
            FillPrice = price,
            Timestamp = DateTimeOffset.UtcNow,
        };
}
