#if WINDOWS
using System.IO;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class FundLedgerReadServiceTests
{
    [Fact]
    public async Task GetAsync_FullConsolidatedWithoutSelection_ReturnsAggregatedFundLedger()
    {
        var storagePath = CreateStoragePath();
        try
        {
            var (service, firstAccountId, secondAccountId) = await CreateServiceAsync(storagePath);

            var summary = await service.GetAsync(new FundLedgerQuery(
                FundProfileId: "alpha-fund",
                AsOf: new DateTimeOffset(2026, 4, 1, 23, 0, 0, TimeSpan.Zero)));

            summary.Should().NotBeNull();
            summary!.ScopeKind.Should().Be(FundLedgerScope.Consolidated);
            summary.ScopeId.Should().BeNull();
            summary.JournalEntryCount.Should().Be(4);
            summary.TrialBalance.Should().Contain(line => line.FinancialAccountId == firstAccountId.ToString());
            summary.TrialBalance.Should().Contain(line => line.FinancialAccountId == secondAccountId.ToString());
        }
        finally
        {
            DeleteIfExists(storagePath);
        }
    }

    [Fact]
    public async Task GetAsync_PerLedgerSliceSelection_ReturnsSelectedScopeMetadataAndNoCrossScopeRows()
    {
        var storagePath = CreateStoragePath();
        try
        {
            var (service, _, _) = await CreateServiceAsync(storagePath);

            var summary = await service.GetAsync(new FundLedgerQuery(
                FundProfileId: "alpha-fund",
                AsOf: new DateTimeOffset(2026, 4, 1, 23, 0, 0, TimeSpan.Zero),
                ScopeKind: FundLedgerScope.Entity,
                ScopeId: "entity-alpha"));

            summary.Should().NotBeNull();
            summary!.ScopeKind.Should().Be(FundLedgerScope.Entity);
            summary.ScopeId.Should().Be("entity-alpha");
            summary.JournalEntryCount.Should().Be(0);
            summary.TrialBalance.Should().BeEmpty();
        }
        finally
        {
            DeleteIfExists(storagePath);
        }
    }

    [Fact]
    public async Task GetAsync_MultiLedgerConsolidation_ExcludesOtherFundAndHonorsAsOfCutoff()
    {
        var storagePath = CreateStoragePath();
        try
        {
            var (service, _, _) = await CreateServiceAsync(storagePath);

            var earlySummary = await service.GetAsync(new FundLedgerQuery(
                FundProfileId: "alpha-fund",
                AsOf: new DateTimeOffset(2026, 4, 1, 10, 30, 0, TimeSpan.Zero)));
            var fullSummary = await service.GetAsync(new FundLedgerQuery(
                FundProfileId: "alpha-fund",
                AsOf: new DateTimeOffset(2026, 4, 1, 23, 0, 0, TimeSpan.Zero)));

            earlySummary.Should().NotBeNull();
            fullSummary.Should().NotBeNull();
            earlySummary!.JournalEntryCount.Should().Be(2);
            fullSummary!.JournalEntryCount.Should().Be(4);
            fullSummary.FundProfileId.Should().Be("alpha-fund");
        }
        finally
        {
            DeleteIfExists(storagePath);
        }
    }

    [Fact]
    public async Task GetAsync_UnknownOrInvalidSelectionIds_ReturnsEmptyScopedProjection()
    {
        var storagePath = CreateStoragePath();
        try
        {
            var (service, _, _) = await CreateServiceAsync(storagePath);

            var missingEntity = await service.GetAsync(new FundLedgerQuery(
                FundProfileId: "alpha-fund",
                AsOf: new DateTimeOffset(2026, 4, 1, 23, 0, 0, TimeSpan.Zero),
                ScopeKind: FundLedgerScope.Entity,
                ScopeId: "entity-missing"));
            var emptyScopeId = await service.GetAsync(new FundLedgerQuery(
                FundProfileId: "alpha-fund",
                AsOf: new DateTimeOffset(2026, 4, 1, 23, 0, 0, TimeSpan.Zero),
                ScopeKind: FundLedgerScope.Sleeve,
                ScopeId: string.Empty));

            missingEntity.Should().NotBeNull();
            missingEntity!.TrialBalance.Should().BeEmpty();
            missingEntity.Journal.Should().BeEmpty();
            emptyScopeId.Should().NotBeNull();
            emptyScopeId!.TrialBalance.Should().BeEmpty();
            emptyScopeId.Journal.Should().BeEmpty();
        }
        finally
        {
            DeleteIfExists(storagePath);
        }
    }

    private static async Task<(FundLedgerReadService Service, Guid FirstAccountId, Guid SecondAccountId)> CreateServiceAsync(string storagePath)
    {
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
            SleeveIds: ["sleeve-main"],
            VehicleIds: ["vehicle-master"],
            IsDefault: true));

        var store = new StrategyRunStore();
        var firstAccountId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        var secondAccountId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

        await store.RecordRunAsync(BuildRun("run-a", "alpha-fund", firstAccountId, new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun("run-b", "alpha-fund", secondAccountId, new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun("run-other", "beta-fund", Guid.Parse("cccccccc-3333-3333-3333-333333333333"), new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero)));

        var runReadService = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var workspaceService = new StrategyRunWorkspaceService(store, runReadService, fundContext);
        return (new FundLedgerReadService(workspaceService, fundContext), firstAccountId, secondAccountId);
    }

    private static StrategyRunEntry BuildRun(string runId, string fundProfileId, Guid accountId, DateTimeOffset startedAt)
    {
        var completedAt = startedAt.AddMinutes(20);
        var request = new BacktestRequest(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 1), ["AAPL"], 1_000m, "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_010m,
            GrossPnl: 10m,
            NetPnl: 10m,
            TotalReturn: 0.01m,
            AnnualizedReturn: 0.01m,
            SharpeRatio: 0d,
            SortinoRatio: 0d,
            CalmarRatio: 0d,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1d,
            WinRate: 1d,
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 1m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(["AAPL"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: CreateLedger(accountId, startedAt),
            ElapsedTime: TimeSpan.FromMinutes(20),
            TotalEventsProcessed: 10);

        return StrategyRunEntry.Start("strategy", "Strategy", RunType.Backtest) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = completedAt,
            Metrics = result,
            FundProfileId = fundProfileId,
            FundDisplayName = fundProfileId
        };
    }

    private static IReadOnlyLedger CreateLedger(Guid accountId, DateTimeOffset timestamp)
    {
        var ledger = new global::Meridian.Ledger.Ledger();
        Post(ledger, timestamp, "Capital", [
            (LedgerAccounts.CashAccount(accountId.ToString()), 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        Post(ledger, timestamp.AddMinutes(1), "Security", [
            (LedgerAccounts.Securities("AAPL", accountId.ToString()), 200m, 0m),
            (LedgerAccounts.CashAccount(accountId.ToString()), 0m, 200m)
        ]);
        return ledger;
    }

    private static void Post(global::Meridian.Ledger.Ledger ledger, DateTimeOffset timestamp, string description, IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> lines)
    {
        var journalId = Guid.NewGuid();
        var entries = lines
            .Select(line => new LedgerEntry(Guid.NewGuid(), journalId, timestamp, line.Account, line.Debit, line.Credit, description))
            .ToArray();
        ledger.Post(new JournalEntry(journalId, timestamp, description, entries));
    }

    private static string CreateStoragePath() => Path.Combine(Path.GetTempPath(), "meridian-fund-ledger-tests", $"{Guid.NewGuid():N}.json");

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
#endif
