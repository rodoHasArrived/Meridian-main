using FluentAssertions;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;
using Meridian.Strategies.Services;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class ReconciliationProjectionServiceTests
{
    private static readonly ReconciliationRunRequest DefaultRequest =
        new("run-1", AmountTolerance: 0.01m, MaxAsOfDriftMinutes: 5);

    // ── BuildChecks – null guards ────────────────────────────────────────────

    [Fact]
    public void BuildChecks_NullDetail_Throws()
    {
        var service = new ReconciliationProjectionService();
        var act = () => service.BuildChecks(null!, DefaultRequest);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildChecks_NullRequest_Throws()
    {
        var service = new ReconciliationProjectionService();
        var detail = BuildDetail(null, null);
        var act = () => service.BuildChecks(detail, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── BuildChecks – both portfolio and ledger present ──────────────────────

    [Fact]
    public void BuildChecks_BothPresent_ProducesCashBalanceAndEquityChecks()
    {
        var portfolio = BuildPortfolioSummary(cash: 40_000m, totalEquity: 110_000m);
        var ledger = BuildLedgerSummary(cashBalance: 40_000m, assetBalance: 120_000m, liabilityBalance: 10_000m);
        var detail = BuildDetail(portfolio, ledger);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(detail, DefaultRequest);

        checks.Should().NotBeEmpty();
        checks.Should().Contain(c => c.CheckId == "cash-balance");
        checks.Should().Contain(c => c.CheckId == "net-equity");
    }

    [Fact]
    public void BuildChecks_BothPresent_CashBalanceCheckHasBothAmounts()
    {
        var portfolio = BuildPortfolioSummary(cash: 40_000m, totalEquity: 110_000m);
        var ledger = BuildLedgerSummary(cashBalance: 40_000m, assetBalance: 120_000m, liabilityBalance: 10_000m);
        var detail = BuildDetail(portfolio, ledger);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(detail, DefaultRequest);

        var cashCheck = checks.Single(c => c.CheckId == "cash-balance");
        cashCheck.HasExpectedAmount.Should().BeTrue();
        cashCheck.HasActualAmount.Should().BeTrue();
        cashCheck.ExpectedAmount.Should().Be(40_000m);
        cashCheck.ActualAmount.Should().Be(40_000m);
    }

    [Fact]
    public void BuildChecks_BothPresent_NetEquityUsesAssetMinusLiability()
    {
        var portfolio = BuildPortfolioSummary(cash: 30_000m, totalEquity: 110_000m);
        // assetBalance=120k, liabilityBalance=10k → ledger net = 110k
        var ledger = BuildLedgerSummary(cashBalance: 30_000m, assetBalance: 120_000m, liabilityBalance: 10_000m);
        var detail = BuildDetail(portfolio, ledger);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(detail, DefaultRequest);

        var equityCheck = checks.Single(c => c.CheckId == "net-equity");
        equityCheck.ExpectedAmount.Should().Be(110_000m); // portfolio equity
        equityCheck.ActualAmount.Should().Be(110_000m);   // ledger net assets
    }

    // ── BuildChecks – position coverage ──────────────────────────────────────

    [Fact]
    public void BuildChecks_LongPositionInPortfolioAndLedger_ProducesCoverageCheck()
    {
        var portfolio = BuildPortfolioSummaryWithPositions(
            new[] { ("AAPL", isShort: false) },
            cash: 50_000m, totalEquity: 100_000m);
        var ledger = BuildLedgerSummaryWithPositions(
            longs: ["AAPL"], shorts: [],
            cashBalance: 50_000m, assetBalance: 100_000m, liabilityBalance: 0m);
        var detail = BuildDetail(portfolio, ledger);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(detail, DefaultRequest);

        checks.Should().Contain(c => c.CheckId == "long-AAPL");
    }

    [Fact]
    public void BuildChecks_ShortPositionInPortfolio_ProducesShortCoverageCheck()
    {
        var portfolio = BuildPortfolioSummaryWithPositions(
            new[] { ("MSFT", isShort: true) },
            cash: 50_000m, totalEquity: 100_000m);
        var ledger = BuildLedgerSummaryWithPositions(
            longs: [], shorts: ["MSFT"],
            cashBalance: 50_000m, assetBalance: 100_000m, liabilityBalance: 0m);
        var detail = BuildDetail(portfolio, ledger);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(detail, DefaultRequest);

        checks.Should().Contain(c => c.CheckId == "short-MSFT");
    }

    [Fact]
    public void BuildChecks_LedgerHasPositionNotInPortfolio_ProducesLedgerExtraCoverageCheck()
    {
        // Ledger has GOOG but portfolio does not
        var portfolio = BuildPortfolioSummary(cash: 50_000m, totalEquity: 100_000m);
        var ledger = BuildLedgerSummaryWithPositions(
            longs: ["GOOG"], shorts: [],
            cashBalance: 50_000m, assetBalance: 100_000m, liabilityBalance: 0m);
        var detail = BuildDetail(portfolio, ledger);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(detail, DefaultRequest);

        checks.Should().Contain(c => c.CheckId == "ledger-long-GOOG");
    }

    // ── BuildChecks – portfolio only / ledger only ───────────────────────────

    [Fact]
    public void BuildChecks_PortfolioOnlyNoLedger_ProducesMissingLedgerCheck()
    {
        var portfolio = BuildPortfolioSummary(cash: 40_000m, totalEquity: 100_000m);
        var detail = BuildDetail(portfolio, null);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(detail, DefaultRequest);

        checks.Should().Contain(c => c.CheckId == "ledger-summary-missing");
    }

    [Fact]
    public void BuildChecks_LedgerOnlyNoPortfolio_ProducesMissingPortfolioCheck()
    {
        var ledger = BuildLedgerSummary(cashBalance: 40_000m, assetBalance: 100_000m, liabilityBalance: 0m);
        var detail = BuildDetail(null, ledger);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(detail, DefaultRequest);

        checks.Should().Contain(c => c.CheckId == "portfolio-summary-missing");
    }

    [Fact]
    public void BuildChecks_NeitherPresent_ReturnsEmptyList()
    {
        var detail = BuildDetail(null, null);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildChecks(detail, DefaultRequest);

        checks.Should().BeEmpty();
    }

    // ── BuildBankingChecks – null guard ──────────────────────────────────────

    [Fact]
    public void BuildBankingChecks_NullTransactions_Throws()
    {
        var service = new ReconciliationProjectionService();
        var act = () => service.BuildBankingChecks(null!, null);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── BuildBankingChecks – no data ─────────────────────────────────────────

    [Fact]
    public void BuildBankingChecks_EmptyTransactionsAndNoLedger_ReturnsEmpty()
    {
        var service = new ReconciliationProjectionService();
        var checks = service.BuildBankingChecks([], null);
        checks.Should().BeEmpty();
    }

    // ── BuildBankingChecks – both present ────────────────────────────────────

    [Fact]
    public void BuildBankingChecks_BothPresent_ProducesSingleAmountCheck()
    {
        var txn = BuildBankTransaction(amount: 50_000m);
        var ledger = BuildLedgerSummary(cashBalance: 50_000m, assetBalance: 60_000m, liabilityBalance: 0m);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildBankingChecks([txn], ledger);

        checks.Should().ContainSingle();
        var check = checks[0];
        check.CheckId.Should().Be("bank-net-vs-ledger-cash");
        check.ExpectedAmount.Should().Be(50_000m); // bank net
        check.ActualAmount.Should().Be(50_000m);   // ledger cash
    }

    [Fact]
    public void BuildBankingChecks_VoidedTransactionsExcluded()
    {
        var activeTxn = BuildBankTransaction(amount: 40_000m, isVoided: false);
        var voidedTxn = BuildBankTransaction(amount: 10_000m, isVoided: true);
        var ledger = BuildLedgerSummary(cashBalance: 40_000m, assetBalance: 50_000m, liabilityBalance: 0m);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildBankingChecks([activeTxn, voidedTxn], ledger);

        checks.Should().ContainSingle();
        checks[0].ExpectedAmount.Should().Be(40_000m); // only active transaction
    }

    // ── BuildBankingChecks – one side missing ────────────────────────────────

    [Fact]
    public void BuildBankingChecks_BankDataButNoLedger_ProducesMissingLedgerCoverage()
    {
        var txn = BuildBankTransaction(amount: 25_000m);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildBankingChecks([txn], null);

        checks.Should().ContainSingle();
        checks[0].CheckId.Should().Be("bank-ledger-coverage-missing");
        checks[0].ActualPresent.Should().BeFalse();
    }

    [Fact]
    public void BuildBankingChecks_LedgerButNoBankTransactions_ProducesMissingBankCoverage()
    {
        var ledger = BuildLedgerSummary(cashBalance: 30_000m, assetBalance: 40_000m, liabilityBalance: 0m);

        var service = new ReconciliationProjectionService();
        var checks = service.BuildBankingChecks([], ledger);

        checks.Should().ContainSingle();
        checks[0].CheckId.Should().Be("bank-coverage-missing");
        checks[0].ExpectedPresent.Should().BeFalse();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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

        return new StrategyRunDetail(
            Summary: summary,
            Parameters: new Dictionary<string, string>(),
            Portfolio: portfolio,
            Ledger: ledger);
    }

    private static PortfolioSummary BuildPortfolioSummary(decimal cash, decimal totalEquity)
        => BuildPortfolioSummaryWithPositions([], cash, totalEquity);

    private static PortfolioSummary BuildPortfolioSummaryWithPositions(
        IEnumerable<(string Symbol, bool IsShort)> positionSpecs,
        decimal cash,
        decimal totalEquity)
    {
        var asOf = DateTimeOffset.UtcNow;
        var positions = positionSpecs
            .Select(static p => new PortfolioPositionSummary(
                Symbol: p.Symbol,
                Quantity: p.IsShort ? -100 : 100,
                AverageCostBasis: 100m,
                RealizedPnl: 1_000m,
                UnrealizedPnl: 500m,
                IsShort: p.IsShort))
            .ToArray();

        return new PortfolioSummary(
            PortfolioId: "run-1-portfolio",
            RunId: "run-1",
            AsOf: asOf,
            Cash: cash,
            LongMarketValue: totalEquity - cash,
            ShortMarketValue: 0m,
            GrossExposure: totalEquity - cash,
            NetExposure: totalEquity - cash,
            TotalEquity: totalEquity,
            RealizedPnl: positions.Sum(static p => p.RealizedPnl),
            UnrealizedPnl: positions.Sum(static p => p.UnrealizedPnl),
            Commissions: 50m,
            Financing: 5m,
            Positions: positions);
    }

    private static LedgerSummary BuildLedgerSummary(
        decimal cashBalance, decimal assetBalance, decimal liabilityBalance)
        => BuildLedgerSummaryWithPositions([], [], cashBalance, assetBalance, liabilityBalance);

    private static LedgerSummary BuildLedgerSummaryWithPositions(
        IReadOnlyList<string> longs,
        IReadOnlyList<string> shorts,
        decimal cashBalance,
        decimal assetBalance,
        decimal liabilityBalance)
    {
        var asOf = DateTimeOffset.UtcNow;

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
            AsOf: asOf,
            JournalEntryCount: 2,
            LedgerEntryCount: trialBalance.Count,
            AssetBalance: assetBalance,
            LiabilityBalance: liabilityBalance,
            EquityBalance: assetBalance - liabilityBalance,
            RevenueBalance: 10_000m,
            ExpenseBalance: 50m,
            TrialBalance: trialBalance,
            Journal: []);
    }

    private static BankTransactionDto BuildBankTransaction(
        decimal amount, bool isVoided = false)
        => new BankTransactionDto(
            BankTransactionId: Guid.NewGuid(),
            EntityId: Guid.NewGuid(),
            TransactionType: "Wire",
            EffectiveDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TransactionDate: DateOnly.FromDateTime(DateTime.UtcNow),
            SettlementDate: DateOnly.FromDateTime(DateTime.UtcNow),
            Amount: amount,
            Currency: "USD",
            ExternalRef: null,
            RecordedAt: DateTimeOffset.UtcNow,
            IsVoided: isVoided);
}
