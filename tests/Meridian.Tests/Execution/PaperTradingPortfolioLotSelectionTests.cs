using Meridian.Contracts.Api;
using Meridian.Execution.Models;
using Meridian.Execution.Services;

namespace Meridian.Tests.Execution;

public sealed class PaperTradingPortfolioLotSelectionTests
{
    [Fact]
    public void ApplyFill_WhenSellingLong_UsesHifoForRealisedPnl()
    {
        var portfolio = new PaperTradingPortfolio(100_000m, lotSelectionMethod: PositionLotSelectionMethod.Hifo);

        portfolio.ApplyFill(new ExecutionReport { Symbol = "AAPL", Side = OrderSide.Buy, FilledQuantity = 10, FillPrice = 100m });
        portfolio.ApplyFill(new ExecutionReport { Symbol = "AAPL", Side = OrderSide.Buy, FilledQuantity = 10, FillPrice = 120m });
        portfolio.ApplyFill(new ExecutionReport { Symbol = "AAPL", Side = OrderSide.Sell, FilledQuantity = 10, FillPrice = 130m });

        portfolio.RealisedPnl.Should().Be(100m);
    }

    [Fact]
    public void ApplyFill_WhenSellingLong_UsesFifoByDefault()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);

        portfolio.ApplyFill(new ExecutionReport { Symbol = "AAPL", Side = OrderSide.Buy, FilledQuantity = 10, FillPrice = 100m });
        portfolio.ApplyFill(new ExecutionReport { Symbol = "AAPL", Side = OrderSide.Buy, FilledQuantity = 10, FillPrice = 120m });
        portfolio.ApplyFill(new ExecutionReport { Symbol = "AAPL", Side = OrderSide.Sell, FilledQuantity = 10, FillPrice = 130m });

        portfolio.RealisedPnl.Should().Be(300m);
    }
}
