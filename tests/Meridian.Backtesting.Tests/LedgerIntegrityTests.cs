using FluentAssertions;
using Meridian.Backtesting.Metrics;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Ledger;

namespace Meridian.Backtesting.Tests;

public sealed class LedgerIntegrityTests
{
    private static readonly DateTimeOffset Start = new(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private const string AccountId = BacktestDefaults.DefaultBrokerageAccountId;

    [Fact]
    public void ReturnOfCapital_RelievesEachLotsBasisAndOnlyRecognizesExcessAsGain()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = Create(ledger);
        portfolio.ProcessFill(Fill(2, 3m));
        portfolio.ProcessFill(Fill(3, 10m));

        portfolio.ApplyAssetEvent(new(Start.AddDays(1), "SPY", AssetEventType.ReturnOfCapital, CashPerShare: 4m));

        var lots = portfolio.GetOpenLots("SPY");
        lots.Select(lot => lot.CostBasis()).Should().Equal(0m, 18m);
        ledger.GetBalance(LedgerAccounts.Securities("SPY", AccountId)).Should().Be(18m);
        ledger.GetBalance(LedgerAccounts.RealizedGainFor(AccountId)).Should().Be(2m);
        ledger.HasAccount(LedgerAccounts.CorporateActionIncomeFor(AccountId)).Should().BeFalse();
        var snapshot = Snapshot(portfolio);
        portfolio.LedgerIntegrityBreaks.Should().BeEmpty();
        var flow = snapshot.DayCashFlows.OfType<AssetEventCashFlow>().Single();
        flow.Amount.Should().Be(20m);
        flow.BasisReturned.Should().Be(18m);
        var metrics = BacktestMetricsEngine.Compute([snapshot], snapshot.DayCashFlows, [],
            new(new(2024, 1, 2), new(2024, 1, 3), InitialCash: 10_000m));
        metrics.SymbolAttribution["SPY"].RealizedPnl.Should().Be(2m);

        portfolio.ProcessFill(Fill(-5, 10m, Start.AddDays(2)));
        ledger.GetBalance(LedgerAccounts.Securities("SPY", AccountId)).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.RealizedGainFor(AccountId)).Should().Be(34m);
        Snapshot(portfolio, 3);
        portfolio.LedgerIntegrityBreaks.Should().BeEmpty();
    }

    [Fact]
    public void ReturnOfCapital_PreservesExactCompositeBasisAcrossSubsequentSale()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = Create(ledger);
        portfolio.ProcessFill(Fill(1, 10m));
        portfolio.ProcessFill(Fill(2, 20m));
        portfolio.ApplyAssetEvent(new(Start.AddHours(1), "SPY", AssetEventType.Split, PositionFactor: 1.5m));
        var priorBasis = portfolio.GetOpenLots().Sum(lot => lot.CostBasis());

        portfolio.ApplyAssetEvent(new(Start.AddDays(1), "SPY", AssetEventType.ReturnOfCapital, CashPerShare: 1.25m));

        portfolio.GetOpenLots().Sum(lot => lot.CostBasis()).Should().Be(priorBasis - 5m);
        Snapshot(portfolio);
        portfolio.LedgerIntegrityBreaks.Should().BeEmpty();
        portfolio.ProcessFill(Fill(-4, 20m, Start.AddDays(2)));
        Snapshot(portfolio, 3);
        portfolio.LedgerIntegrityBreaks.Should().BeEmpty();
    }

    [Fact]
    public void ReturnOfCapital_NegativeDistributionRefusesBeforeMutatingBasisOrCash()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = Create(ledger);
        portfolio.ProcessFill(Fill(3, 10m));
        portfolio.ApplyAssetEvent(new(Start.AddHours(1), "SPY", AssetEventType.ReturnOfCapital, CashPerShare: 2m));
        var cash = portfolio.Cash;
        var act = () => portfolio.ApplyAssetEvent(new(Start.AddDays(1), "SPY", AssetEventType.ReturnOfCapital, CashPerShare: -2m));
        act.Should().Throw<InvalidOperationException>().WithMessage("*original basis*");
        portfolio.Cash.Should().Be(cash);
        portfolio.GetOpenLots().Single().CostBasis().Should().Be(24m);
        Snapshot(portfolio);
        portfolio.LedgerIntegrityBreaks.Should().BeEmpty();
        ledger.GetBalance(LedgerAccounts.RealizedGainFor(AccountId)).Should().Be(0m);
    }

    [Fact]
    public void DividendAndFractionalDisposal_ReconcileEachAccountIncludingShortBooks()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var accounts = new[]
        {
            FinancialAccount.CreateDefaultBrokerage(10_000m, 0, 0, "long"),
            FinancialAccount.CreateDefaultBrokerage(10_000m, 0, 0, "short")
        };
        var portfolio = new SimulatedPortfolio(accounts, "long", new FixedCommissionModel(0), ledger, Start);
        portfolio.ProcessFill(Fill(3, 20m) with { AccountId = "long" });
        portfolio.ProcessFill(Fill(-3, 20m) with { AccountId = "short" });
        portfolio.ApplyAssetEvent(new(Start.AddHours(1), "SPY", AssetEventType.Dividend, CashPerShare: 1m));
        portfolio.ApplyAssetEvent(new(Start.AddDays(1), "SPY", AssetEventType.ReverseSplit, PositionFactor: 0.5m, ReferencePrice: 40m));

        Snapshot(portfolio);

        portfolio.LedgerIntegrityBreaks.Should().BeEmpty();
        ledger.HasAccount(LedgerAccounts.Cash).Should().BeFalse();
        ledger.GetBalance(LedgerAccounts.Securities("SPY", "long")).Should().Be(40m);
        ledger.GetBalance(LedgerAccounts.ShortSecuritiesPayable("SPY", "short")).Should().Be(40m);
    }

    [Fact]
    public void DayEnd_RetainsCashBasisAndClearingBreaksAndIgnoresFuturePostings()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = Create(ledger);
        portfolio.ProcessFill(Fill(2, 10m));
        ledger.PostLines(Start.AddHours(1), "Incorrect allocation",
            [(LedgerAccounts.AllocationControl, 7m, 0m), (LedgerAccounts.CashAccount(AccountId), 0m, 7m)]);
        ledger.PostLines(Start.AddHours(2), "Orphan position",
            [(LedgerAccounts.Securities("ORPHAN", AccountId), 9m, 0m), (LedgerAccounts.CapitalAccountFor(AccountId), 0m, 9m)]);
        ledger.PostLines(Start.AddDays(10), "Future cash",
            [(LedgerAccounts.CashAccount(AccountId), 100m, 0m), (LedgerAccounts.CapitalAccountFor(AccountId), 0m, 100m)]);

        Snapshot(portfolio);

        portfolio.LedgerIntegrityBreaks.Should().HaveCount(3);
        portfolio.LedgerIntegrityBreaks.Should().Contain(b => b.Check == "AccountCash" && b.Difference == -7m);
        portfolio.LedgerIntegrityBreaks.Should().Contain(b => b.Check == "SecuritiesBasis" && b.Symbol == "ORPHAN" && b.Difference == 9m);
        portfolio.LedgerIntegrityBreaks.Should().Contain(b => b.Check == "TransferClearing" && b.Difference == 7m);
    }

    [Fact]
    public void StrictDayEnd_ThrowsWithInspectableBreaks()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new SimulatedPortfolio(10_000m, new FixedCommissionModel(0), 0, 0, ledger, Start)
        { FailOnLedgerIntegrityBreak = true };
        ledger.PostLines(Start.AddHours(1), "Missing portfolio cash",
            [(LedgerAccounts.CashAccount(AccountId), 10m, 0m), (LedgerAccounts.CapitalAccountFor(AccountId), 0m, 10m)]);

        var act = () => Snapshot(portfolio);
        act.Should().Throw<LedgerIntegrityException>().Which.Breaks.Should().ContainSingle()
            .Which.Difference.Should().Be(10m);
    }

    private static SimulatedPortfolio Create(Meridian.Ledger.Ledger ledger) =>
        new(10_000m, new FixedCommissionModel(0), 0, 0, ledger, Start);

    private static FillEvent Fill(long quantity, decimal price, DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "SPY", quantity, price, 0m, at ?? Start.AddMinutes(1));

    private static PortfolioSnapshot Snapshot(SimulatedPortfolio portfolio, int day = 1) =>
        portfolio.TakeSnapshot(Start.AddDays(day).AddHours(23), DateOnly.FromDateTime(Start.AddDays(day).UtcDateTime));
}
