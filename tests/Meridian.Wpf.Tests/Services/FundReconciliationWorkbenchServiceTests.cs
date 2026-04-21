#if WINDOWS
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Application.FundAccounts;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Services;

public sealed class FundReconciliationWorkbenchServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_SortsBreakQueueAndIncludesStrategyAndAccountRuns()
    {
        var context = await CreateContextAsync();

        var snapshot = await context.Service.GetSnapshotAsync("alpha-fund");

        snapshot.BreakQueueItems.Should().HaveCount(3);
        snapshot.BreakQueueItems.Select(item => item.BreakId).Should().ContainInOrder(
            "run-fund-ops:open-large",
            "run-fund-ops:open-small",
            "run-fund-ops:reviewed-gap");
        snapshot.RunRows.Should().Contain(row => row.SourceType == FundReconciliationSourceType.StrategyRun);
        snapshot.RunRows.Should().Contain(row => row.SourceType == FundReconciliationSourceType.AccountRun);
        snapshot.RunRows.First().HasOpenExceptions.Should().BeTrue();
        snapshot.InReviewBreakCount.Should().Be(1);
    }

    [Fact]
    public async Task GetBreakDetailAsync_HighlightsFocusedBreak_AndIncludesSecurityCoverage()
    {
        var context = await CreateContextAsync();
        var snapshot = await context.Service.GetSnapshotAsync("alpha-fund");
        var breakRow = snapshot.BreakQueueItems.First(item => item.BreakId == "run-fund-ops:open-large");

        var detail = await context.Service.GetBreakDetailAsync(breakRow, "USD");

        detail.Should().NotBeNull();
        detail!.SourceType.Should().Be(FundReconciliationSourceType.StrategyRun);
        detail.SupportsBreakActions.Should().BeTrue();
        detail.ExceptionRows.Should().ContainSingle(row => row.IsHighlighted && row.RowKey == "open-large");
        detail.SecurityCoverageRows.Should().ContainSingle(row => row.Symbol == "TSLA");
        detail.AuditRows.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetRunDetailAsync_ForAccountRun_ReturnsReadOnlyNormalizedRows()
    {
        var context = await CreateContextAsync();
        var snapshot = await context.Service.GetSnapshotAsync("alpha-fund");
        var accountRun = snapshot.RunRows.Single(row => row.SourceType == FundReconciliationSourceType.AccountRun);

        var detail = await context.Service.GetRunDetailAsync(accountRun, "USD");

        detail.Should().NotBeNull();
        detail!.SourceType.Should().Be(FundReconciliationSourceType.AccountRun);
        detail.SupportsBreakActions.Should().BeFalse();
        detail.AccountId.Should().Be(accountRun.AccountId);
        detail.AllCheckRows.Should().NotBeEmpty();
        detail.ExceptionRows.Should().BeEmpty();
        detail.AuditRows.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    private static async Task<WorkbenchContext> CreateContextAsync()
    {
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            "meridian-workbench-service-tests",
            $"{Guid.NewGuid():N}.json");

        var fundContext = new FundContextService(storagePath);
        await fundContext.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "alpha-fund",
            DisplayName: "Alpha Fund",
            LegalEntityName: "Alpha Fund LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "governance",
            DefaultLandingPageTag: "FundLedger",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            EntityIds: ["entity-alpha"],
            SleeveIds: ["sleeve-credit"],
            VehicleIds: ["vehicle-master"],
            IsDefault: true));
        await fundContext.SelectFundProfileAsync("alpha-fund");

        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL"));

        var store = new StrategyRunStore();
        await store.RecordRunAsync(BuildFundScopedRun("run-fund-ops"));

        var portfolioReadService = new PortfolioReadService(lookup);
        var ledgerReadService = new LedgerReadService(lookup);
        var runReadService = new StrategyRunReadService(store, portfolioReadService, ledgerReadService);
        var workspaceService = new StrategyRunWorkspaceService(store, runReadService, fundContext);

        var fundAccountService = new InMemoryFundAccountService();
        var fundId = ToFundId("alpha-fund");
        var entityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var sleeveId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var vehicleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var accountId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        await fundAccountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: accountId,
            AccountType: AccountTypeDto.Custody,
            AccountCode: "CUST-001",
            DisplayName: "Primary Custody",
            BaseCurrency: "USD",
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
            CreatedBy: "test",
            EntityId: entityId,
            FundId: fundId,
            SleeveId: sleeveId,
            VehicleId: vehicleId,
            Institution: "Northern Trust"));
        await fundAccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: accountId,
            AsOfDate: new DateOnly(2026, 3, 21),
            Currency: "USD",
            CashBalance: 750m,
            Source: "test",
            RecordedBy: "test",
            SecuritiesMarketValue: 250m));
        await fundAccountService.ReconcileAccountAsync(new ReconcileAccountRequest(
            AccountId: accountId,
            AsOfDate: new DateOnly(2026, 3, 21),
            RequestedBy: "test"));

        var fundAccountReadService = new FundAccountReadService(fundAccountService);
        var reconciliationRepository = new InMemoryReconciliationRunRepository();
        var strategyReconciliationService = new ReconciliationRunService(
            runReadService,
            new ReconciliationProjectionService(),
            reconciliationRepository);
        var reconciliationReadService = new ReconciliationReadService(
            fundAccountService,
            fundAccountReadService,
            workspaceService,
            strategyReconciliationService);

        var apiClient = new FakeWorkstationReconciliationApiClient(
        [
            new ReconciliationBreakQueueItem(
                BreakId: "run-fund-ops:open-large",
                RunId: "run-fund-ops",
                StrategyName: "Reconciliation Strategy",
                Category: ReconciliationBreakCategory.AmountMismatch,
                Status: ReconciliationBreakQueueStatus.Open,
                Variance: 25m,
                Reason: "Large amount mismatch remains open.",
                AssignedTo: null,
                DetectedAt: new DateTimeOffset(2026, 3, 21, 16, 40, 0, TimeSpan.Zero),
                LastUpdatedAt: new DateTimeOffset(2026, 3, 21, 16, 40, 0, TimeSpan.Zero)),
            new ReconciliationBreakQueueItem(
                BreakId: "run-fund-ops:reviewed-gap",
                RunId: "run-fund-ops",
                StrategyName: "Reconciliation Strategy",
                Category: ReconciliationBreakCategory.TimingMismatch,
                Status: ReconciliationBreakQueueStatus.InReview,
                Variance: 40m,
                Reason: "Statement ingestion is still under review.",
                AssignedTo: "desktop-user",
                DetectedAt: new DateTimeOffset(2026, 3, 21, 16, 41, 0, TimeSpan.Zero),
                LastUpdatedAt: new DateTimeOffset(2026, 3, 21, 16, 45, 0, TimeSpan.Zero),
                ReviewedBy: "desktop-user",
                ReviewedAt: new DateTimeOffset(2026, 3, 21, 16, 45, 0, TimeSpan.Zero)),
            new ReconciliationBreakQueueItem(
                BreakId: "run-fund-ops:open-small",
                RunId: "run-fund-ops",
                StrategyName: "Reconciliation Strategy",
                Category: ReconciliationBreakCategory.MissingLedgerCoverage,
                Status: ReconciliationBreakQueueStatus.Open,
                Variance: 5m,
                Reason: "A smaller ledger-only mismatch is still open.",
                AssignedTo: null,
                DetectedAt: new DateTimeOffset(2026, 3, 21, 16, 42, 0, TimeSpan.Zero),
                LastUpdatedAt: new DateTimeOffset(2026, 3, 21, 16, 42, 0, TimeSpan.Zero))
        ],
        [
            BuildStrategyDetail("run-fund-ops")
        ]);

        var service = new FundReconciliationWorkbenchService(
            reconciliationReadService,
            fundAccountService,
            workspaceService,
            apiClient);

        return new WorkbenchContext(service);
    }

    private static Guid ToFundId(string fundProfileId)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId.Trim())));

    private static ReconciliationRunDetail BuildStrategyDetail(string runId)
        => new(
            Summary: new ReconciliationRunSummary(
                ReconciliationRunId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                RunId: runId,
                CreatedAt: new DateTimeOffset(2026, 3, 21, 16, 50, 0, TimeSpan.Zero),
                PortfolioAsOf: new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero),
                LedgerAsOf: new DateTimeOffset(2026, 3, 21, 16, 31, 0, TimeSpan.Zero),
                MatchCount: 2,
                BreakCount: 3,
                OpenBreakCount: 2,
                HasTimingDrift: false,
                AmountTolerance: 0.01m,
                MaxAsOfDriftMinutes: 5,
                SecurityIssueCount: 1,
                HasSecurityCoverageIssues: true),
            Matches:
            [
                new ReconciliationMatchDto(
                    CheckId: "cash-balance",
                    Label: "Cash balance",
                    ExpectedSource: "Ledger",
                    ActualSource: "Portfolio",
                    ExpectedAmount: 750m,
                    ActualAmount: 750m,
                    Variance: 0m,
                    ExpectedAsOf: new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero),
                    ActualAsOf: new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero))
            ],
            Breaks:
            [
                new ReconciliationBreakDto(
                    CheckId: "open-large",
                    Label: "TSLA market value",
                    Category: ReconciliationBreakCategory.AmountMismatch,
                    Status: ReconciliationBreakStatus.Open,
                    MissingSource: "Portfolio",
                    ExpectedAmount: 150m,
                    ActualAmount: 125m,
                    Variance: 25m,
                    Reason: "Broker statement has not been normalized yet.",
                    ExpectedAsOf: new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero),
                    ActualAsOf: new DateTimeOffset(2026, 3, 21, 16, 29, 0, TimeSpan.Zero)),
                new ReconciliationBreakDto(
                    CheckId: "reviewed-gap",
                    Label: "Timing drift",
                    Category: ReconciliationBreakCategory.TimingMismatch,
                    Status: ReconciliationBreakStatus.Investigating,
                    MissingSource: "Ledger",
                    ExpectedAmount: 50m,
                    ActualAmount: 10m,
                    Variance: 40m,
                    Reason: "Statement timestamp drift exceeds tolerance.",
                    ExpectedAsOf: new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero),
                    ActualAsOf: new DateTimeOffset(2026, 3, 21, 16, 00, 0, TimeSpan.Zero)),
                new ReconciliationBreakDto(
                    CheckId: "open-small",
                    Label: "Ledger coverage",
                    Category: ReconciliationBreakCategory.MissingLedgerCoverage,
                    Status: ReconciliationBreakStatus.Open,
                    MissingSource: "Ledger",
                    ExpectedAmount: 25m,
                    ActualAmount: 20m,
                    Variance: 5m,
                    Reason: "Ledger posting is late.",
                    ExpectedAsOf: new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero),
                    ActualAsOf: new DateTimeOffset(2026, 3, 21, 16, 28, 0, TimeSpan.Zero))
            ],
            SecurityCoverageIssues:
            [
                new ReconciliationSecurityCoverageIssueDto(
                    Source: "Portfolio",
                    Symbol: "TSLA",
                    AccountName: "Primary Custody",
                    Reason: "Security Master coverage is missing.")
            ]);

    private static StrategyRunEntry BuildFundScopedRun(string runId)
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
            Equity: 1_000m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: -150m,
            TotalEquity: 1_000m,
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
            AuditReference = $"audit-{runId}",
            FundProfileId = "alpha-fund",
            FundDisplayName = "Alpha Fund"
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
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 10, 0, 0, TimeSpan.Zero), "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 20, 0, 0, TimeSpan.Zero), "Open TSLA short",
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

    private sealed record WorkbenchContext(FundReconciliationWorkbenchService Service);
}
#endif
