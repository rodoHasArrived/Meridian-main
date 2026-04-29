using FluentAssertions;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;
using Meridian.Strategies.Services;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class ReconciliationProjectionServiceTests
{
    [Fact]
    public void BuildChecks_NullInputs_Throws()
    {
        var service = new ReconciliationProjectionService();
        var act = () => service.BuildChecks(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildChecks_BothPresent_ProducesCashBalanceAndEquityChecks()
    {
        var inputs = BuildInputs(
            BuildPortfolioSummary(cash: 40_000m, totalEquity: 110_000m),
            BuildLedgerSummary(cashBalance: 40_000m, assetBalance: 120_000m, liabilityBalance: 10_000m));

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(inputs);

        checks.Should().Contain(c => c.CheckId == "cash-balance");
        checks.Should().Contain(c => c.CheckId == "net-equity");
    }

    [Fact]
    public void BuildChecks_LedgerHasPositionNotInPortfolio_ProducesLedgerExtraCoverageCheck()
    {
        var inputs = BuildInputs(
            BuildPortfolioSummary(cash: 50_000m, totalEquity: 100_000m),
            BuildLedgerSummaryWithPositions(
                longs: ["GOOG"], shorts: [],
                cashBalance: 50_000m, assetBalance: 100_000m, liabilityBalance: 0m));

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(inputs);

        checks.Should().Contain(c => c.CheckId == "ledger-long-GOOG");
    }

    [Fact]
    public void BuildChecks_InternalCashAndLedger_ProducesBankNetCheck()
    {
        var inputs = BuildInputs(
            portfolio: null,
            ledger: BuildLedgerSummary(cashBalance: 50_000m, assetBalance: 50_000m, liabilityBalance: 0m),
            internalCash: [BuildCashMovement(50_000m)]);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(inputs);

        checks.Should().ContainSingle(c => c.CheckId == "bank-net-vs-ledger-cash");
    }

    [Fact]
    public void BuildChecks_ExternalStatementRows_ProducesExternalStatementComparison()
    {
        var inputs = BuildInputs(
            portfolio: null,
            ledger: null,
            internalCash: [BuildCashMovement(90m)],
            externalRows: [new ReconciliationExternalStatementInput("row-1", 100m, DateTimeOffset.UtcNow, "custodian")]);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(inputs);

        checks.Should().ContainSingle(c => c.CheckId == "external-statement-vs-internal-cash");
    }

    private static ReconciliationNormalizedInputs BuildInputs(
        PortfolioSummary? portfolio,
        LedgerSummary? ledger,
        IReadOnlyList<ReconciliationCashMovementInput>? internalCash = null,
        IReadOnlyList<ReconciliationExternalStatementInput>? externalRows = null)
    {
        var detail = BuildDetail(portfolio, ledger);
        var portfolioAdapter = new StrategyPortfolioReconciliationSourceAdapter();
        var ledgerAdapter = new StrategyLedgerReconciliationSourceAdapter();

        return new ReconciliationNormalizedInputs(
            Portfolio: portfolioAdapter.Adapt(detail),
            Ledger: ledgerAdapter.Adapt(detail),
            InternalCashMovements: internalCash ?? [],
            ExternalStatementRows: externalRows ?? []);
    }

    private static ReconciliationCashMovementInput BuildCashMovement(decimal amount, bool isVoided = false)
        => new(
            MovementId: Guid.NewGuid().ToString("N"),
            Amount: amount,
            AsOf: DateTimeOffset.UtcNow,
            IsVoided: isVoided,
            Source: "bank",
            BankTransaction: new BankTransactionDto(
                Guid.NewGuid(), Guid.NewGuid(), "Wire",
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow),
                amount, "USD", null, DateTimeOffset.UtcNow, isVoided));

    private static StrategyRunDetail BuildDetail(PortfolioSummary? portfolio, LedgerSummary? ledger)
    {
        var summary = new StrategyRunSummary(
            RunId: "run-1",
            StrategyId: "strategy-1",
            StrategyName: "Test",
            Mode: StrategyRunMode.Backtest,
            Engine: StrategyRunEngine.MeridianNative,
            Status: StrategyRunStatus.Completed,
            StartedAt: DateTimeOffset.UtcNow.AddHours(-2),
            CompletedAt: DateTimeOffset.UtcNow,
            DatasetReference: null,
            FeedReference: null,
            PortfolioId: null,
            LedgerReference: null,
            NetPnl: 10_000m,
            TotalReturn: 0.1m,
            FinalEquity: 110_000m,
            FillCount: 2,
            LastUpdatedAt: DateTimeOffset.UtcNow);

        return new StrategyRunDetail(summary, new Dictionary<string, string>(), portfolio, ledger);
    }

    private static PortfolioSummary BuildPortfolioSummary(decimal cash, decimal totalEquity)
        => new(
            PortfolioId: "run-1-portfolio",
            RunId: "run-1",
            AsOf: DateTimeOffset.UtcNow,
            Cash: cash,
            LongMarketValue: totalEquity - cash,
            ShortMarketValue: 0m,
            GrossExposure: totalEquity - cash,
            NetExposure: totalEquity - cash,
            TotalEquity: totalEquity,
            RealizedPnl: 0m,
            UnrealizedPnl: 0m,
            Commissions: 0m,
            Financing: 0m,
            Positions: []);

    private static LedgerSummary BuildLedgerSummary(decimal cashBalance, decimal assetBalance, decimal liabilityBalance)
        => BuildLedgerSummaryWithPositions([], [], cashBalance, assetBalance, liabilityBalance);

    private static LedgerSummary BuildLedgerSummaryWithPositions(
        IReadOnlyList<string> longs,
        IReadOnlyList<string> shorts,
        decimal cashBalance,
        decimal assetBalance,
        decimal liabilityBalance)
    {
        var trialBalance = new List<LedgerTrialBalanceLine>
        {
            new("Cash", "Asset", null, null, cashBalance, 2)
        };

        foreach (var sym in longs)
            trialBalance.Add(new LedgerTrialBalanceLine("Securities", "Asset", sym, null, 10_000m, 1));

        foreach (var sym in shorts)
            trialBalance.Add(new LedgerTrialBalanceLine("Short Securities Payable", "Liability", sym, null, -10_000m, 1));

        return new LedgerSummary(
            LedgerReference: "run-1-ledger",
            RunId: "run-1",
            AsOf: DateTimeOffset.UtcNow,
            JournalEntryCount: 2,
            LedgerEntryCount: trialBalance.Count,
            AssetBalance: assetBalance,
            LiabilityBalance: liabilityBalance,
            EquityBalance: assetBalance - liabilityBalance,
            RevenueBalance: 0m,
            ExpenseBalance: 0m,
            TrialBalance: trialBalance,
            Journal: []);
    }
}
