using FluentAssertions;
using AccountKind = Meridian.Execution.Models.AccountKind;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// The aggregated <see cref="PaperTradingPortfolio.Positions"/> view must report each symbol's own
/// realised P&amp;L and a quantity-weighted cost basis. It used to stamp the whole account's
/// realised P&amp;L on every held symbol, so a run summary that summed positions counted it once
/// per symbol, and it kept the first account's basis for a symbol held in several accounts.
/// </summary>
public sealed class PaperTradingPortfolioPositionPnlTests
{
    [Fact]
    public void Positions_ReportPerSymbolRealisedPnl_NotTheAccountTotal()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(Fill("AAPL", OrderSide.Buy, 20m, 100m));
        portfolio.ApplyFill(Fill("AAPL", OrderSide.Sell, 10m, 110m)); // +100 realised on AAPL
        portfolio.ApplyFill(Fill("MSFT", OrderSide.Buy, 10m, 200m));
        portfolio.ApplyFill(Fill("NVDA", OrderSide.Buy, 10m, 300m));

        var positions = portfolio.Positions;

        portfolio.RealisedPnl.Should().Be(100m);
        positions["AAPL"].RealizedPnl.Should().Be(100m);
        positions["MSFT"].RealizedPnl.Should().Be(0m);
        positions["NVDA"].RealizedPnl.Should().Be(0m);
        positions.Values.Sum(static p => p.RealizedPnl).Should().Be(portfolio.RealisedPnl,
            "summing open positions must not repeat the account total once per symbol");
    }

    [Fact]
    public void Positions_AcrossAccounts_WeightCostBasisByQuantityAndSumOwnRealisedPnl()
    {
        var portfolio = new PaperTradingPortfolio(
        [
            new AccountDefinition("acct-a", "Account A", AccountKind.Brokerage, 100_000m),
            new AccountDefinition("acct-b", "Account B", AccountKind.Brokerage, 100_000m),
        ]);
        portfolio.ApplyFill("acct-a", Fill("AAPL", OrderSide.Buy, 10m, 100m));
        portfolio.ApplyFill("acct-b", Fill("AAPL", OrderSide.Buy, 40m, 125m));
        portfolio.ApplyFill("acct-b", Fill("AAPL", OrderSide.Sell, 10m, 135m)); // +100 realised in B
        portfolio.ApplyFill("acct-a", Fill("MSFT", OrderSide.Buy, 5m, 200m));
        portfolio.ApplyFill("acct-a", Fill("MSFT", OrderSide.Sell, 5m, 180m)); // -100 realised in A, MSFT flat

        var aapl = portfolio.Positions["AAPL"];

        aapl.Quantity.Should().Be(40);
        aapl.AverageCostBasis.Should().Be(118.75m, "(10 × 100 + 30 × 125) / 40, not account A's 100 alone");
        aapl.RealizedPnl.Should().Be(100m, "only AAPL's realised P&L, not account A's -100 from MSFT");
    }

    private static ExecutionReport Fill(string symbol, OrderSide side, decimal quantity, decimal price) => new()
    {
        OrderId = $"EQ-{Guid.NewGuid():N}",
        Symbol = symbol,
        ReportType = ExecutionReportType.Fill,
        OrderStatus = OrderStatus.Filled,
        Side = side,
        FilledQuantity = quantity,
        FillPrice = price
    };
}
