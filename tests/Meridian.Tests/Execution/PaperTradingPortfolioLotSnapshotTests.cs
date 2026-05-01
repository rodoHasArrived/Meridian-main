using Meridian.Execution.Models;
using Meridian.Execution.Services;

namespace Meridian.Tests.Execution;

public sealed class PaperTradingPortfolioLotSnapshotTests
{
    [Fact]
    public void GetPositionLots_ReturnsTrackedLotsForDefaultAccount()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);

        portfolio.ApplyFill(new ExecutionReport { Symbol = "MSFT", Side = OrderSide.Buy, FilledQuantity = 5, FillPrice = 100m });
        portfolio.ApplyFill(new ExecutionReport { Symbol = "MSFT", Side = OrderSide.Buy, FilledQuantity = 5, FillPrice = 105m });

        var lots = portfolio.GetPositionLots("MSFT");

        lots.Should().HaveCount(2);
        lots[0].OpenQuantity.Should().Be(5);
        lots[1].OpenQuantity.Should().Be(5);
    }
}
