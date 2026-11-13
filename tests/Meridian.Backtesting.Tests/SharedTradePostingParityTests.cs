using FluentAssertions;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;

namespace Meridian.Backtesting.Tests;

public sealed class SharedTradePostingParityTests
{
    [Theory]
    [InlineData(false, 120, 6L)]
    [InlineData(false, 80, 6L)]
    [InlineData(true, 80, 6L)]
    [InlineData(true, 120, 6L)]
    [InlineData(false, 100, 10L)]
    [InlineData(true, 100, 10L)]
    [InlineData(false, 120, 15L)]
    [InlineData(true, 80, 15L)]
    public void BacktestAndPaper_PostTheSameEconomicsForPartialClosesAndDirectionChanges(
        bool initiallyShort, int closingPrice, long closingQuantity)
    {
        var backtestLedger = new Meridian.Ledger.Ledger();
        var paperLedger = new Meridian.Ledger.Ledger();
        var timestamp = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);
        var backtest = new SimulatedPortfolio(10_000m, new FixedCommissionModel(0m), 0, 0, backtestLedger, timestamp);
        var paper = new PaperTradingPortfolio(10_000m, paperLedger);
        var openingQuantity = initiallyShort ? -10L : 10L;
        var signedClosingQuantity = initiallyShort ? closingQuantity : -closingQuantity;

        ApplyToBoth(openingQuantity, 100m, 1m, timestamp);
        ApplyToBoth(signedClosingQuantity, closingPrice, 3m, timestamp.AddMinutes(1));

        var expectedCash = 10_000m - openingQuantity * 100m - signedClosingQuantity * closingPrice - 4m;
        var expectedPnl = Math.Min(10L, closingQuantity) * (closingPrice - 100m) * (initiallyShort ? -1m : 1m);
        var expectedLongBasis = initiallyShort
            ? Math.Max(closingQuantity - 10L, 0L) * closingPrice
            : Math.Max(10L - closingQuantity, 0L) * 100m;
        var expectedShortProceeds = initiallyShort
            ? Math.Max(10L - closingQuantity, 0L) * 100m
            : Math.Max(closingQuantity - 10L, 0L) * closingPrice;

        backtest.Cash.Should().Be(expectedCash);
        paper.Cash.Should().Be(expectedCash);
        paper.RealisedPnl.Should().Be(expectedPnl);
        AssertBalances(backtestLedger, BacktestDefaults.DefaultBrokerageAccountId);
        AssertBalances(paperLedger, null);
        Totals(backtestLedger).Should().BeEquivalentTo(Totals(paperLedger));
        backtestLedger.Journal.Should().HaveCount(paperLedger.Journal.Count);
        backtestLedger.Journal.Should().OnlyContain(entry => entry.IsBalanced);
        paperLedger.Journal.Should().OnlyContain(entry => entry.IsBalanced);

        void ApplyToBoth(long quantity, decimal price, decimal commission, DateTimeOffset filledAt)
        {
            var orderId = Guid.NewGuid();
            backtest.ProcessFill(new FillEvent(Guid.NewGuid(), orderId, "SPY", quantity, price, commission, filledAt));
            paper.ApplyFill(new ExecutionReport
            {
                OrderId = orderId.ToString("D"),
                ReportType = ExecutionReportType.Fill,
                Symbol = "SPY",
                Side = quantity > 0 ? OrderSide.Buy : OrderSide.Sell,
                OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
                OrderQuantity = Math.Abs(quantity),
                FilledQuantity = Math.Abs(quantity),
                FillPrice = price,
                Commission = commission,
                Timestamp = filledAt
            });
        }

        void AssertBalances(Meridian.Ledger.Ledger ledger, string? accountId)
        {
            IChartOfAccounts chart = StandardChartOfAccounts.Instance;
            ledger.GetBalance(chart.Resolve(LedgerAccountRole.Cash, accountId)).Should().Be(expectedCash);
            ledger.GetBalance(chart.Resolve(LedgerAccountRole.Securities, accountId, "SPY")).Should().Be(expectedLongBasis);
            ledger.GetBalance(chart.Resolve(LedgerAccountRole.ShortSecuritiesPayable, accountId, "SPY")).Should().Be(expectedShortProceeds);
            ledger.GetBalance(chart.Resolve(LedgerAccountRole.RealizedGain, accountId)).Should().Be(Math.Max(expectedPnl, 0m));
            ledger.GetBalance(chart.Resolve(LedgerAccountRole.RealizedLoss, accountId)).Should().Be(Math.Max(-expectedPnl, 0m));
            ledger.GetBalance(chart.Resolve(LedgerAccountRole.CommissionExpense, accountId)).Should().Be(4m);
        }
    }

    private static Dictionary<LedgerAccount, (decimal Debit, decimal Credit)> Totals(Meridian.Ledger.Ledger ledger)
        => ledger.Journal.SelectMany(entry => entry.Lines)
            .GroupBy(line => line.Account with { FinancialAccountId = null })
            .ToDictionary(group => group.Key, group => (group.Sum(line => line.Debit), group.Sum(line => line.Credit)));
}
