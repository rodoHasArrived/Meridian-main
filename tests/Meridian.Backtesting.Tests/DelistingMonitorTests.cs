using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Direct coverage of the day-end delisting sweep: a position whose symbol stops producing data
/// is force-liquidated at the haircut last price once the grace period passes, and a liquidation
/// the account refuses is recorded and disclosed instead of ending the run.
/// </summary>
public sealed class DelistingMonitorTests
{
    private const string Account = "acct";
    private static readonly DateOnly LastData = new(2026, 3, 2);

    private static SimulatedPortfolio Portfolio(bool allowMargin, bool allowShortSelling, decimal cash = 10_000m) => new(
        [
            new FinancialAccount(Account, "Test", FinancialAccountKind.Brokerage, "Broker", cash,
                new FinancialAccountRules(AllowMargin: allowMargin, AllowShortSelling: allowShortSelling))
        ],
        Account,
        new FixedCommissionModel(0m));

    private static FillEvent Fill(string symbol, long quantity, decimal price) =>
        new(Guid.NewGuid(), Guid.NewGuid(), symbol, quantity, price, 0m, DateTimeOffset.UtcNow, Account);

    [Fact]
    public void LongPosition_PastTheGracePeriod_IsLiquidatedAtTheHaircutLastPrice()
    {
        var portfolio = Portfolio(allowMargin: false, allowShortSelling: false);
        portfolio.ProcessFill(Fill("GONE", 100, 20m));
        portfolio.UpdateLastPrice("GONE", 20m);
        var monitor = new DelistingMonitor(DelistingPolicy.LiquidateAtLastPrice, haircutPercent: 0.10m, graceDays: 5);
        monitor.RecordEvent("GONE", LastData);
        var fills = new List<FillEvent>();

        monitor.ProcessDayEnd(LastData.AddDays(5), portfolio, [], fills, NullLogger.Instance);
        fills.Should().BeEmpty("the grace period has not passed yet");

        monitor.ProcessDayEnd(LastData.AddDays(6), portfolio, [], fills, NullLogger.Instance);

        var liquidation = fills.Should().ContainSingle().Subject;
        liquidation.FilledQuantity.Should().Be(-100);
        liquidation.FillPrice.Should().Be(18m, "a long receives the last price less the 10% haircut");
        portfolio.GetCurrentPositions().Should().NotContainKey("GONE");
        monitor.Liquidations.Should().ContainSingle(item => item.Symbol == "GONE" && item.LastDataDate == LastData);
        monitor.FailedLiquidationSymbols.Should().BeEmpty();
    }

    [Fact]
    public void HoldPolicy_NeverLiquidates()
    {
        var portfolio = Portfolio(allowMargin: false, allowShortSelling: false);
        portfolio.ProcessFill(Fill("GONE", 100, 20m));
        portfolio.UpdateLastPrice("GONE", 20m);
        var monitor = new DelistingMonitor(DelistingPolicy.Hold, haircutPercent: 0.10m, graceDays: 5);
        monitor.RecordEvent("GONE", LastData);
        var fills = new List<FillEvent>();

        monitor.ProcessDayEnd(LastData.AddDays(30), portfolio, [], fills, NullLogger.Instance);

        fills.Should().BeEmpty();
        portfolio.GetCurrentPositions()["GONE"].Quantity.Should().Be(100);
    }

    [Fact]
    public void RefusedLiquidation_IsRecordedAndTheRunContinues()
    {
        // A short whose cover would need margin the account does not allow: the account refuses
        // the liquidation fill before changing anything. That refusal must be disclosed, not end
        // the run, and the position stays open at its stale mark.
        var portfolio = Portfolio(allowMargin: false, allowShortSelling: true, cash: 1_000m);
        portfolio.ProcessFill(Fill("GONE", -100, 10m));
        portfolio.UpdateLastPrice("GONE", 50m);
        var monitor = new DelistingMonitor(DelistingPolicy.LiquidateAtLastPrice, haircutPercent: 0.10m, graceDays: 5);
        monitor.RecordEvent("GONE", LastData);
        var fills = new List<FillEvent>();
        var cash = portfolio.Cash;

        var sweep = () => monitor.ProcessDayEnd(LastData.AddDays(6), portfolio, [], fills, NullLogger.Instance);

        sweep.Should().NotThrow();
        fills.Should().BeEmpty();
        monitor.FailedLiquidationSymbols.Should().Contain("GONE");
        monitor.Liquidations.Should().BeEmpty();
        portfolio.GetCurrentPositions()["GONE"].Quantity.Should().Be(-100);
        portfolio.Cash.Should().Be(cash);
    }
}
