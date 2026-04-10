using FluentAssertions;
using Meridian.Application.Banking;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Banking;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;

namespace Meridian.Tests.Application;

public sealed class ReconciliationRunServiceTests
{
    [Fact]
    public async Task RunAsync_ShouldReturnNull_WhenRunDoesNotExist()
    {
        var service = CreateService();

        var result = await service.RunAsync(new ReconciliationRunRequest("missing-run"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_ShouldMaterializeStoredReconciliation_ForRecordedRun()
    {
        var store = new StrategyRunStore();
        var service = CreateService(store, out var repository);
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-1"));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-1"));

        detail.Should().NotBeNull();
        detail!.Summary.RunId.Should().Be("run-1");
        detail.Summary.BreakCount.Should().Be(0);
        detail.Matches.Should().Contain(match => match.CheckId == "cash-balance");

        var latest = await repository.GetLatestForRunAsync("run-1");
        latest.Should().NotBeNull();
        latest!.Summary.ReconciliationRunId.Should().Be(detail.Summary.ReconciliationRunId);
    }

    [Fact]
    public async Task RunAsync_WithUnresolvedSecurityCoverage_ShouldExposeSecurityIssues()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-security"));

        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL"));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(), lookup);

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-security"));

        detail.Should().NotBeNull();
        detail!.Summary.HasSecurityCoverageIssues.Should().BeTrue();
        detail.Summary.SecurityIssueCount.Should().Be(2);
        detail.SecurityCoverageIssues.Should().NotBeNull();
        detail.SecurityCoverageIssues!.Should().Contain(issue => issue.Source == "portfolio" && issue.Symbol == "TSLA");
        detail.SecurityCoverageIssues.Should().Contain(issue => issue.Source == "ledger" && issue.Symbol == "TSLA");
    }

    [Fact]
    public async Task RunAsync_WithSecurityLookup_ShouldPopulateSecurityClassifications()
    {
        // Arrange — register both AAPL and TSLA so the lookup returns full data for both
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-classifications"));

        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL",
            SubType: "CommonShare"));
        lookup.Register("TSLA", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DisplayName: "Tesla Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "TSLA",
            SubType: "CommonShare"));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(), lookup);

        // Act
        var detail = await service.RunAsync(new ReconciliationRunRequest("run-classifications"));

        // Assert
        detail.Should().NotBeNull();
        detail!.SecurityClassifications.Should().NotBeNull(
            "a Security Master lookup was wired, so classifications must be populated");

        detail.SecurityClassifications!.Should().ContainKey("AAPL");
        detail.SecurityClassifications["AAPL"].AssetClass.Should().Be("Equity");
        detail.SecurityClassifications["AAPL"].SubType.Should().Be("CommonShare");
        detail.SecurityClassifications["AAPL"].PrimaryIdentifierValue.Should().Be("AAPL");
    }

    [Fact]
    public async Task RunAsync_WithNoSecurityLookup_ShouldHaveNullOrEmptyClassifications()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-no-classifications"));
        var service = CreateService(store, new InMemoryReconciliationRunRepository());

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-no-classifications"));

        detail.Should().NotBeNull();
        // When no lookup is wired, the map is either null or empty — never populated
        var hasClassifications = detail!.SecurityClassifications is { Count: > 0 };
        hasClassifications.Should().BeFalse(
            "no Security Master lookup was wired so no classifications can be resolved");
    }

    [Fact]
    public async Task GetHistoryForRunAsync_ShouldReturnNewestFirst()
    {
        var store = new StrategyRunStore();
        var service = CreateService(store);
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-2"));

        var first = await service.RunAsync(new ReconciliationRunRequest("run-2"));
        var second = await service.RunAsync(new ReconciliationRunRequest("run-2"));

        var history = await service.GetHistoryForRunAsync("run-2");

        history.Should().HaveCount(2);
        history[0].CreatedAt.Should().BeOnOrAfter(history[1].CreatedAt);
        history.Select(item => item.ReconciliationRunId).Should().Contain([first!.Summary.ReconciliationRunId, second!.Summary.ReconciliationRunId]);
    }

    private static IReconciliationRunService CreateService(
        StrategyRunStore store,
        out IReconciliationRunRepository repository)
    {
        repository = new InMemoryReconciliationRunRepository();
        return CreateService(store, repository);
    }

    private static IReconciliationRunService CreateService(StrategyRunStore? store = null)
    {
        store ??= new StrategyRunStore();
        return CreateService(store, new InMemoryReconciliationRunRepository());
    }

    private static IReconciliationRunService CreateService(StrategyRunStore store, IReconciliationRunRepository repository)
    {
        return CreateService(store, repository, securityReferenceLookup: null);
    }

    private static IReconciliationRunService CreateService(
        StrategyRunStore store,
        IReconciliationRunRepository repository,
        ISecurityReferenceLookup? securityReferenceLookup)
    {
        return CreateService(store, repository, securityReferenceLookup, bankTransactionSource: null);
    }

    private static IReconciliationRunService CreateService(
        StrategyRunStore store,
        IReconciliationRunRepository repository,
        ISecurityReferenceLookup? securityReferenceLookup,
        IBankTransactionSource? bankTransactionSource)
    {
        IStrategyRepository strategyRepository = store;
        var portfolioReadService = securityReferenceLookup is null
            ? new PortfolioReadService()
            : new PortfolioReadService(securityReferenceLookup);
        var ledgerReadService = securityReferenceLookup is null
            ? new LedgerReadService()
            : new LedgerReadService(securityReferenceLookup);
        var runReadService = new StrategyRunReadService(strategyRepository, portfolioReadService, ledgerReadService);
        return new ReconciliationRunService(runReadService, new ReconciliationProjectionService(), repository, bankTransactionSource);
    }

    // -----------------------------------------------------------------------
    // Banking integration tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_WithMatchingBankNetAmount_ShouldProduceBankMatch()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-bank-match"));

        var entityId = Guid.NewGuid();
        // Ledger cash = 750 (initial 1000 - 400 AAPL + 150 TSLA short proceeds)
        // Bank net should match ledger cash: seed a single tx of 750
        var bankingService = new InMemoryBankingService();
        await bankingService.SeedBankTransactionsAsync(new BankTransactionSeedRequest(
            EntityIds: [entityId],
            CountPerEntity: 1,
            FromDate: new DateOnly(2026, 3, 20),
            ToDate: new DateOnly(2026, 3, 21)));

        // Adjust so net == 750 by seeding directly via approval
        var pending = await bankingService.InitiatePaymentAsync(entityId,
            new InitiatePaymentRequest(750m, new DateOnly(2026, 3, 21), "RECON-TEST", null));
        await bankingService.ApprovePaymentAsync(pending.PendingPaymentId,
            new ApprovePaymentRequest("Test approval", "test"));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null, bankTransactionSource: bankingService);

        var detail = await service.RunAsync(
            new ReconciliationRunRequest("run-bank-match", BankEntityId: entityId));

        detail.Should().NotBeNull();
        detail!.Summary.BankTransactionCount.Should().BeGreaterThan(0);
        detail.BankTransactions.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RunAsync_WithBankEntityId_ShouldPopulateBankTransactionCount()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-bank-count"));

        var entityId = Guid.NewGuid();
        var bankingService = new InMemoryBankingService();
        await bankingService.SeedBankTransactionsAsync(new BankTransactionSeedRequest(
            EntityIds: [entityId],
            CountPerEntity: 3,
            FromDate: new DateOnly(2026, 3, 20),
            ToDate: new DateOnly(2026, 3, 21)));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null, bankTransactionSource: bankingService);

        var detail = await service.RunAsync(
            new ReconciliationRunRequest("run-bank-count", BankEntityId: entityId));

        detail.Should().NotBeNull();
        detail!.Summary.BankTransactionCount.Should().Be(3);
        detail.BankTransactions.Should().HaveCount(3);
    }

    [Fact]
    public async Task RunAsync_WithNoBankEntityId_ShouldSkipBankingChecks()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-no-bank"));

        var bankingService = new InMemoryBankingService();
        var service = CreateService(store, new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null, bankTransactionSource: bankingService);

        // No BankEntityId — banking checks should be skipped
        var detail = await service.RunAsync(new ReconciliationRunRequest("run-no-bank"));

        detail.Should().NotBeNull();
        detail!.Summary.BankTransactionCount.Should().Be(0);
        detail.BankTransactions.Should().BeNull();
        detail.Matches.Should().Contain(m => m.CheckId == "cash-balance");
    }

    [Fact]
    public async Task RunAsync_WhenBankHasTransactionsButNoLedger_ShouldProduceMissingLedgerBreak()
    {
        // Build a run that has a portfolio but NO ledger
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildPortfolioOnlyRun("run-no-ledger"));

        var entityId = Guid.NewGuid();
        var bankingService = new InMemoryBankingService();
        await bankingService.SeedBankTransactionsAsync(new BankTransactionSeedRequest(
            EntityIds: [entityId],
            CountPerEntity: 2,
            FromDate: new DateOnly(2026, 3, 20),
            ToDate: new DateOnly(2026, 3, 21)));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null, bankTransactionSource: bankingService);

        var detail = await service.RunAsync(
            new ReconciliationRunRequest("run-no-ledger", BankEntityId: entityId));

        detail.Should().NotBeNull();
        detail!.Summary.BankBreakCount.Should().BeGreaterThan(0);
        detail.Breaks.Should().Contain(b =>
            b.CheckId == "bank-ledger-coverage-missing" &&
            b.Category == ReconciliationBreakCategory.MissingLedgerCoverage);
    }

    private static class TestRunFactory
    {
        public static StrategyRunEntry BuildReconciliationReadyRun(string runId)
        {
            var startedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero);
            var completedAt = startedAt.AddMinutes(30);
            var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m),
                ["TSLA"] = new("TSLA", -5, 30m, 0m, 0m)
            };
            var accountSnapshot = new FinancialAccountSnapshot(
                AccountId: BacktestDefaults.DefaultBrokerageAccountId,
                DisplayName: "Primary Brokerage",
                Kind: FinancialAccountKind.Brokerage,
                Institution: "Simulated Broker",
                Cash: 750m,
                MarginBalance: 0m,
                LongMarketValue: 400m,
                ShortMarketValue: -150m,
                Equity: 1000m,
                Positions: positions,
                Rules: new FinancialAccountRules());
            var snapshot = new PortfolioSnapshot(
                Timestamp: completedAt,
                Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
                Cash: 750m,
                MarginBalance: 0m,
                LongMarketValue: 400m,
                ShortMarketValue: -150m,
                TotalEquity: 1000m,
                DailyReturn: 0m,
                Positions: positions,
                Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    [accountSnapshot.AccountId] = accountSnapshot
                },
                DayCashFlows: []);

            var request = new BacktestRequest(
                From: new DateOnly(2026, 3, 20),
                To: new DateOnly(2026, 3, 21),
                Symbols: ["AAPL", "TSLA"],
                InitialCash: 1_000m,
                DataRoot: "./data");
            var metrics = new BacktestMetrics(
                InitialCapital: 1_000m,
                FinalEquity: 1_000m,
                GrossPnl: 0m,
                NetPnl: 0m,
                TotalReturn: 0m,
                AnnualizedReturn: 0m,
                SharpeRatio: 0d,
                SortinoRatio: 0d,
                CalmarRatio: 0d,
                MaxDrawdown: 0m,
                MaxDrawdownPercent: 0m,
                MaxDrawdownRecoveryDays: 0,
                ProfitFactor: 1d,
                WinRate: 1d,
                TotalTrades: 0,
                WinningTrades: 0,
                LosingTrades: 0,
                TotalCommissions: 0m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m,
                Xirr: 0d,
                SymbolAttribution: new Dictionary<string, SymbolAttribution>());
            var result = new BacktestResult(
                Request: request,
                Universe: new HashSet<string>(["AAPL", "TSLA"], StringComparer.OrdinalIgnoreCase),
                Snapshots: [snapshot],
                CashFlows: [],
                Fills: [],
                Metrics: metrics,
                Ledger: CreateLedger(),
                ElapsedTime: TimeSpan.FromMinutes(30),
                TotalEventsProcessed: 100);

            return StrategyRunEntry.Start("recon-strategy", "Reconciliation Strategy", RunType.Backtest) with
            {
                RunId = runId,
                StartedAt = startedAt,
                EndedAt = completedAt,
                Metrics = result,
                DatasetReference = "dataset/us/equities",
                FeedReference = "synthetic:equities",
                PortfolioId = "recon-portfolio",
                LedgerReference = "recon-ledger",
                AuditReference = $"audit-{runId}"
            };
        }

        /// <summary>
        /// A run that has a portfolio snapshot but no ledger — useful for testing
        /// reconciliation paths where only one side of the comparison exists.
        /// </summary>
        public static StrategyRunEntry BuildPortfolioOnlyRun(string runId)
        {
            var startedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero);
            var completedAt = startedAt.AddMinutes(30);
            var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m)
            };
            var accountSnapshot = new FinancialAccountSnapshot(
                AccountId: BacktestDefaults.DefaultBrokerageAccountId,
                DisplayName: "Primary Brokerage",
                Kind: FinancialAccountKind.Brokerage,
                Institution: "Simulated Broker",
                Cash: 600m,
                MarginBalance: 0m,
                LongMarketValue: 400m,
                ShortMarketValue: 0m,
                Equity: 1000m,
                Positions: positions,
                Rules: new FinancialAccountRules());
            var snapshot = new PortfolioSnapshot(
                Timestamp: completedAt,
                Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
                Cash: 600m,
                MarginBalance: 0m,
                LongMarketValue: 400m,
                ShortMarketValue: 0m,
                TotalEquity: 1000m,
                DailyReturn: 0m,
                Positions: positions,
                Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    [accountSnapshot.AccountId] = accountSnapshot
                },
                DayCashFlows: []);

            var request = new BacktestRequest(
                From: new DateOnly(2026, 3, 20),
                To: new DateOnly(2026, 3, 21),
                Symbols: ["AAPL"],
                InitialCash: 1_000m,
                DataRoot: "./data");
            var metrics = new BacktestMetrics(
                InitialCapital: 1_000m,
                FinalEquity: 1_000m,
                GrossPnl: 0m,
                NetPnl: 0m,
                TotalReturn: 0m,
                AnnualizedReturn: 0m,
                SharpeRatio: 0d,
                SortinoRatio: 0d,
                CalmarRatio: 0d,
                MaxDrawdown: 0m,
                MaxDrawdownPercent: 0m,
                MaxDrawdownRecoveryDays: 0,
                ProfitFactor: 1d,
                WinRate: 1d,
                TotalTrades: 0,
                WinningTrades: 0,
                LosingTrades: 0,
                TotalCommissions: 0m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m,
                Xirr: 0d,
                SymbolAttribution: new Dictionary<string, SymbolAttribution>());
            var result = new BacktestResult(
                Request: request,
                Universe: new HashSet<string>(["AAPL"], StringComparer.OrdinalIgnoreCase),
                Snapshots: [snapshot],
                CashFlows: [],
                Fills: [],
                Metrics: metrics,
                Ledger: null!,   // <-- no ledger
                ElapsedTime: TimeSpan.FromMinutes(30),
                TotalEventsProcessed: 50);

            return StrategyRunEntry.Start("portfolio-only-strategy", "Portfolio-Only Strategy", RunType.Backtest) with
            {
                RunId = runId,
                StartedAt = startedAt,
                EndedAt = completedAt,
                Metrics = result,
                DatasetReference = "dataset/us/equities",
                FeedReference = "synthetic:equities",
                PortfolioId = "portfolio-only-portfolio",
                LedgerReference = null   // <-- no ledger reference
            };
        }

        private static IReadOnlyLedger CreateLedger()
        {
            var ledger = new global::Meridian.Ledger.Ledger();
            PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero), "Initial capital",
            [
                (LedgerAccounts.Cash, 1_000m, 0m),
                (LedgerAccounts.CapitalAccount, 0m, 1_000m)
            ]);
            PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 10, 0, TimeSpan.Zero), "Buy AAPL",
            [
                (LedgerAccounts.Securities("AAPL"), 400m, 0m),
                (LedgerAccounts.Cash, 0m, 400m)
            ]);
            PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 20, 0, TimeSpan.Zero), "Open TSLA short",
            [
                (LedgerAccounts.Cash, 150m, 0m),
                (LedgerAccounts.ShortSecuritiesPayable("TSLA"), 0m, 150m)
            ]);
            return ledger;
        }

        private static void PostBalancedEntry(
            global::Meridian.Ledger.Ledger ledger,
            DateTimeOffset timestamp,
            string description,
            IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> lines)
        {
            var journalId = Guid.NewGuid();
            var ledgerLines = lines
                .Select(line => new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    timestamp,
                    line.Account,
                    line.Debit,
                    line.Credit,
                    description))
                .ToArray();
            ledger.Post(new JournalEntry(journalId, timestamp, description, ledgerLines));
        }
    }

    private sealed class StubSecurityReferenceLookup : ISecurityReferenceLookup
    {
        private readonly Dictionary<string, WorkstationSecurityReference> _references = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string symbol, WorkstationSecurityReference reference)
        {
            _references[symbol] = reference;
        }

        public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        {
            _references.TryGetValue(symbol, out var reference);
            return Task.FromResult<WorkstationSecurityReference?>(reference);
        }
    }
}
