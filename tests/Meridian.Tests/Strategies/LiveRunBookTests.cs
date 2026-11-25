using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Strategies.Live;

namespace Meridian.Tests.Strategies;

/// <summary>
/// A live or paper run's metrics used the equity of the portfolio the run trades against,
/// which the host shares with other runs, manual orders and holdings that predate the run.
/// The run book values only what the run itself did.
/// </summary>
public sealed class LiveRunBookTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Value_MarksTheRunsOwnPositionsAtTheFeedPrice()
    {
        var book = new LiveRunBook(10_000m, "acct", "paper");
        book.ApplyFill(Fill("AAPL", 10, 100m, commission: 1m), 1m);

        var value = book.Value(symbol => symbol == "AAPL" ? 110m : null);

        value.Cash.Should().Be(10_000m - 1_000m - 1m);
        value.Equity.Should().Be(10_000m - 1m + 10m * (110m - 100m));
        var position = value.Positions["AAPL"];
        position.Quantity.Should().Be(10);
        position.AverageCostBasis.Should().Be(100m);
        position.UnrealizedPnl.Should().Be(100m);
        value.Accounts["acct"].LongMarketValue.Should().Be(1_100m);
        value.Accounts["acct"].ShortMarketValue.Should().Be(0m);
    }

    [Fact]
    public void Value_WithoutAFeedPrice_MarksAtTheLastFill()
    {
        var book = new LiveRunBook(10_000m, "acct", null);
        book.ApplyFill(Fill("AAPL", 10, 100m), 1m);
        book.ApplyFill(Fill("AAPL", 5, 106m), 1m);

        var value = book.Value(static _ => null);

        value.Positions["AAPL"].AverageCostBasis.Should().Be(102m);
        value.Equity.Should().Be(10_000m - 1_530m + 15m * 106m);
    }

    [Fact]
    public void ReducingAndReversing_RealisesAgainstTheAverageCost()
    {
        var book = new LiveRunBook(10_000m, "acct", null);
        book.ApplyFill(Fill("AAPL", 10, 100m), 1m);
        book.ApplyFill(Fill("AAPL", -4, 110m), 1m);
        book.ApplyFill(Fill("AAPL", -10, 120m), 1m); // closes 6, opens a 4-share short at 120

        var value = book.Value(static _ => 115m);

        var position = value.Positions["AAPL"];
        position.Quantity.Should().Be(-4);
        position.AverageCostBasis.Should().Be(120m);
        position.RealizedPnl.Should().Be(4m * 10m + 6m * 20m);
        position.UnrealizedPnl.Should().Be(-4m * (115m - 120m));
        value.Accounts["acct"].ShortMarketValue.Should().Be(-460m);
        value.Accounts["acct"].Rules.AllowShortSelling.Should().BeTrue();
        value.Equity.Should().Be(10_000m + 160m + 20m, "realised 160 plus 20 unrealised on the short");
    }

    [Fact]
    public void ContractMultiplier_ScalesCashAndValue()
    {
        var book = new LiveRunBook(10_000m, "acct", null);
        book.ApplyFill(Fill("SPY261218C00600000", 2, 2.50m), 100m);

        var value = book.Value(static _ => 3m);

        value.Cash.Should().Be(10_000m - 500m);
        value.Equity.Should().Be(10_000m + 2m * 100m * 0.50m);
    }

    [Fact]
    public void FaceValueFill_UsesPercentOfPar()
    {
        var book = new LiveRunBook(200_000m, "acct", null);
        book.ApplyFill(Fill("UST10Y", 100_000, 101.25m), LiveRunMetricsTracker.ValueFactor(true, 1m));

        book.Value(static _ => null).Cash.Should().Be(200_000m - 101_250m);
    }

    [Fact]
    public void FinancingCharge_IsPaidFromTheRunsCash()
    {
        var book = new LiveRunBook(10_000m, "acct", null);

        book.ApplyFinancingCharge(12.50m);

        book.FinancingCharges.Should().Be(12.50m);
        book.Value(static _ => null).Equity.Should().Be(9_987.50m);
    }

    private static FillEvent Fill(string symbol, long quantity, decimal price, decimal commission = 0m)
        => new(Guid.NewGuid(), Guid.NewGuid(), symbol, quantity, price, commission, At, "acct");
}

/// <summary>A strategy launched fresh trades against its run book, not the shared portfolio.</summary>
public sealed class LiveRunBookContextTests
{
    [Fact]
    public void Context_WithARunBook_ShowsTheStrategyOnlyTheRunsOwnHoldings()
    {
        var shared = new Meridian.Execution.Services.PaperTradingPortfolio(100_000m);
        shared.ApplyFill(new Meridian.Execution.Sdk.ExecutionReport
        {
            OrderId = "pre-run",
            Symbol = "MSFT",
            Side = Meridian.Execution.Sdk.OrderSide.Buy,
            OrderQuantity = 100m,
            FilledQuantity = 100m,
            FillPrice = 50m,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
            ReportType = Meridian.Execution.Sdk.ExecutionReportType.Fill,
            Timestamp = DateTimeOffset.UtcNow
        });
        var cache = new Meridian.Execution.Adapters.LiveMarketDataCache();
        cache.RecordTrade("AAPL", new Meridian.Contracts.Domain.Models.Trade(DateTimeOffset.UtcNow, "AAPL", 110m,
            Size: 100, Aggressor: Meridian.Contracts.Domain.Enums.AggressorSide.Buy, SequenceNumber: 1));
        var gateway = new Moq.Mock<Meridian.Execution.Interfaces.IOrderGateway>();
        gateway.SetupGet(item => item.BrokerName).Returns("Paper");
        var book = new LiveRunBook(10_000m, "acct", "Paper");
        book.ApplyFill(new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "AAPL", 10, 100m, 1m, DateTimeOffset.UtcNow, "acct"), 1m);

        var context = new LiveStrategyExecutionContext(gateway.Object, cache, shared,
            new HashSet<string> { "AAPL" }, "acct", book);

        context.Cash.Should().Be(10_000m - 1_000m - 1m);
        context.PortfolioValue.Should().Be(10_000m - 1m + 10m * 10m);
        context.Positions.Should().ContainSingle().Which.Key.Should().Be("AAPL", "MSFT predates the run");
        context.Positions["AAPL"].UnrealizedPnl.Should().Be(100m);
        context.Portfolio.PortfolioValue.Should().Be(context.PortfolioValue);
        context.Portfolio.Positions.Should().ContainKey("AAPL").And.NotContainKey("MSFT");
        context.Accounts["acct"].LongMarketValue.Should().Be(1_100m);
    }

    [Fact]
    public void Context_WithoutARunBook_StillShowsThePortfolioItWasGiven()
    {
        var shared = new Meridian.Execution.Services.PaperTradingPortfolio(100_000m);
        var gateway = new Moq.Mock<Meridian.Execution.Interfaces.IOrderGateway>();
        gateway.SetupGet(item => item.BrokerName).Returns("Paper");

        var context = new LiveStrategyExecutionContext(gateway.Object,
            new Meridian.Execution.Adapters.LiveMarketDataCache(), shared, new HashSet<string> { "AAPL" });

        context.PortfolioValue.Should().Be(100_000m);
        context.Portfolio.Should().BeSameAs(shared);
    }

    [Fact]
    public void ClosedPosition_LeavesThePositionList_ButKeepsItsRealisedPnlInEquity()
    {
        var book = new LiveRunBook(10_000m, "acct", null);
        book.ApplyFill(new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "AAPL", 10, 100m, 0m, DateTimeOffset.UtcNow), 1m);
        book.ApplyFill(new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "AAPL", -10, 105m, 0m, DateTimeOffset.UtcNow), 1m);

        var value = book.Value(static _ => null);
        var state = book.AsPortfolioState(static _ => null);

        value.Positions.Should().BeEmpty();
        value.Equity.Should().Be(10_050m);
        state.RealisedPnl.Should().Be(50m);
        state.Positions.Should().BeEmpty();
    }
}
