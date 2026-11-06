using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Cash and P&amp;L for multiplied instruments (option contracts, futures) must move by the
/// contract multiplier. Risk sizing already used the full multiplier, so a paper book that
/// debited $2.50 for one option contract quoted at 2.50 (multiplier 100) let risk and cash
/// disagree by 100x.
/// </summary>
public sealed class PaperTradingPortfolioContractMultiplierTests
{
    private const string Symbol = "SPY261218C00500000";

    [Fact]
    public void Buy_OneOptionContract_DebitsPriceTimesMultiplier()
    {
        var portfolio = new PaperTradingPortfolio(10_000m);

        portfolio.ApplyFill(Fill(OrderSide.Buy, 1m, 2.50m), ownerAccountId: null, contractMultiplier: 100m);

        portfolio.Cash.Should().Be(9_750m, "1 contract × $2.50 × 100 = $250");
        var account = (AccountState)portfolio.GetAccount(PaperTradingPortfolio.DefaultAccountId)!;
        var position = account.Positions[Symbol];
        position.CostBasis.Should().Be(2.50m, "cost basis stays quoted per contract");
        position.MarketValue.Should().Be(250m);
        portfolio.PortfolioValue.Should().Be(10_000m, "buying at the mark neither creates nor destroys value");
    }

    [Fact]
    public void ReportLotMultiplier_IsAppliedWhenCallerSuppliesNone()
    {
        var portfolio = new PaperTradingPortfolio(10_000m);

        portfolio.ApplyFill(Fill(OrderSide.Buy, 2m, 2.50m) with { LotMultiplier = 100m });

        portfolio.Cash.Should().Be(9_500m);
    }

    [Fact]
    public void RoundTrip_RealisesAndMarksPnlInDollars()
    {
        var portfolio = new PaperTradingPortfolio(10_000m);
        portfolio.ApplyFill(Fill(OrderSide.Buy, 2m, 2.50m), ownerAccountId: null, contractMultiplier: 100m);

        portfolio.UpdateMarketPrice(Symbol, 3.00m);
        portfolio.UnrealisedPnl.Should().Be(100m, "2 contracts × $0.50 × 100");

        portfolio.ApplyFill(Fill(OrderSide.Sell, 2m, 3.00m), ownerAccountId: null, contractMultiplier: 100m);

        portfolio.RealisedPnl.Should().Be(100m);
        portfolio.Cash.Should().Be(10_100m);
        portfolio.UnrealisedPnl.Should().Be(0m);
    }

    [Fact]
    public void ShortSellAndCover_UseMultiplierForProceedsAndCost()
    {
        var portfolio = new PaperTradingPortfolio(10_000m);
        portfolio.ApplyFill(Fill(OrderSide.Sell, 1m, 4.00m), ownerAccountId: null, contractMultiplier: 100m);

        portfolio.Cash.Should().Be(10_400m);

        portfolio.ApplyFill(Fill(OrderSide.Buy, 1m, 3.00m), ownerAccountId: null, contractMultiplier: 100m);

        portfolio.Cash.Should().Be(10_100m);
        portfolio.RealisedPnl.Should().Be(100m);
    }

    [Fact]
    public void FillWithDifferentMultiplier_OnOpenPosition_IsRefused()
    {
        var portfolio = new PaperTradingPortfolio(10_000m);
        portfolio.ApplyFill(Fill(OrderSide.Buy, 1m, 2.50m), ownerAccountId: null, contractMultiplier: 100m);

        var act = () => portfolio.ApplyFill(Fill(OrderSide.Buy, 1m, 2.50m), ownerAccountId: null, contractMultiplier: 10m);

        act.Should().Throw<InvalidOperationException>().WithMessage("*multiplier*");
        portfolio.Cash.Should().Be(9_750m, "the refused fill must not move cash");
    }

    [Fact]
    public void MultiplierBelowOne_CanOpenAndCloseThePosition()
    {
        // AttributeFill only ever raises the multiplier, so a new position must be seeded with the
        // exact value; otherwise a 0.1 multiplier stays at 1 and every later fill is refused.
        var portfolio = new PaperTradingPortfolio(10_000m);
        portfolio.ApplyFill(Fill(OrderSide.Buy, 10m, 50m) with { LotMultiplier = 0.1m });

        portfolio.Cash.Should().Be(9_950m, "10 × $50 × 0.1 = $50");

        portfolio.ApplyFill(Fill(OrderSide.Sell, 10m, 60m) with { LotMultiplier = 0.1m });

        portfolio.Cash.Should().Be(10_010m);
        portfolio.RealisedPnl.Should().Be(10m);
    }

    [Fact]
    public void EquityFill_WithDefaultMultiplier_IsUnchanged()
    {
        var portfolio = new PaperTradingPortfolio(10_000m);

        portfolio.ApplyFill(new ExecutionReport
        {
            OrderId = "EQ-1",
            Symbol = "AAPL",
            ReportType = ExecutionReportType.Fill,
            OrderStatus = OrderStatus.Filled,
            Side = OrderSide.Buy,
            FilledQuantity = 10m,
            FillPrice = 100m
        });

        portfolio.Cash.Should().Be(9_000m);
    }

    private static ExecutionReport Fill(OrderSide side, decimal quantity, decimal price) => new()
    {
        OrderId = $"OPT-{Guid.NewGuid():N}",
        Symbol = Symbol,
        AssetClass = "us_option",
        ReportType = ExecutionReportType.Fill,
        OrderStatus = OrderStatus.Filled,
        Side = side,
        FilledQuantity = quantity,
        FillPrice = price
    };
}
