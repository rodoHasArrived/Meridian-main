#if WINDOWS
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Application.FundAccounts;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class FundLedgerReadServiceTests
{
    [Fact]
    public async Task GetAsync_WithEmptySelectedLedgerIds_MatchesFullFundConsolidation()
    {
        using var context = await CreateContextAsync();

        var full = await context.Service.GetAsync(new FundLedgerQuery(
            FundProfileId: "alpha-fund",
            AsOf: context.AsOf));
        var explicitEmpty = await context.Service.GetAsync(new FundLedgerQuery(
            FundProfileId: "alpha-fund",
            AsOf: context.AsOf,
            SelectedLedgerIds: Array.Empty<string>()));

        full.Should().NotBeNull();
        explicitEmpty.Should().NotBeNull();
        explicitEmpty!.JournalEntryCount.Should().Be(full!.JournalEntryCount);
        explicitEmpty.LedgerEntryCount.Should().Be(full.LedgerEntryCount);
        explicitEmpty.AssetBalance.Should().Be(full.AssetBalance);
        explicitEmpty.EquityBalance.Should().Be(full.EquityBalance);
        explicitEmpty.ConsolidatedTotals.Should().BeEquivalentTo(full.ConsolidatedTotals);
        explicitEmpty.LedgerSlices.Should().BeEquivalentTo(full.LedgerSlices);
    }

    [Fact]
    public async Task GetAsync_WithSelectedLedgerIds_ConstrainsConsolidationToSelectedRuns()
    {
        using var context = await CreateContextAsync();

        var allLedgers = await context.Service.GetAsync(new FundLedgerQuery(
            FundProfileId: "alpha-fund",
            AsOf: context.AsOf));
        var selected = await context.Service.GetAsync(new FundLedgerQuery(
            FundProfileId: "alpha-fund",
            AsOf: context.AsOf,
            SelectedLedgerIds: ["run-one", "run-three"]));

        allLedgers.Should().NotBeNull();
        selected.Should().NotBeNull();
        allLedgers!.JournalEntryCount.Should().Be(6);
        selected!.JournalEntryCount.Should().Be(4);
        allLedgers.AssetBalance.Should().Be(3_000m);
        selected.AssetBalance.Should().Be(1_500m);
        allLedgers.ConsolidatedTotals!.AssetBalance.Should().Be(3_000m);
        selected.ConsolidatedTotals!.AssetBalance.Should().Be(1_500m);
        selected.LedgerSlices!.Single(slice => slice.ScopeKind == FundLedgerScope.Consolidated)
            .Totals.AssetBalance.Should().Be(1_500m);
    }

    [Fact]
    public async Task GetAsync_WithUnknownSelectedLedgerIds_ReturnsEmptyLedgerSummary()
    {
        using var context = await CreateContextAsync();

        var summary = await context.Service.GetAsync(new FundLedgerQuery(
            FundProfileId: "alpha-fund",
            AsOf: context.AsOf,
            SelectedLedgerIds: ["unknown-ledger"]));

        summary.Should().NotBeNull();
        summary!.JournalEntryCount.Should().Be(0);
        summary.TrialBalance.Should().BeEmpty();
        summary.AssetBalance.Should().Be(0m);
        summary.ConsolidatedTotals.Should().NotBeNull();
        summary.ConsolidatedTotals!.AssetBalance.Should().Be(0m);
        summary.LedgerSlices.Should().Contain(slice => slice.ScopeKind == FundLedgerScope.Consolidated);
        summary.LedgerSlices.Should().OnlyContain(slice => slice.Totals.JournalEntryCount == 0);
    }

    [Fact]
    public async Task GetAsync_WithFundAccountMappings_MaterializesPerLedgerSlicesAndScopedQuery()
    {
        using var context = await CreateContextAsync();

        var summary = await context.Service.GetAsync(new FundLedgerQuery(
            FundProfileId: "alpha-fund",
            AsOf: context.AsOf));
        var scoped = await context.Service.GetAsync(new FundLedgerQuery(
            FundProfileId: "alpha-fund",
            AsOf: context.AsOf,
            ScopeKind: FundLedgerScope.Entity,
            ScopeId: context.FirstEntityScopeId));
        var selectedScoped = await context.Service.GetAsync(new FundLedgerQuery(
            FundProfileId: "alpha-fund",
            AsOf: context.AsOf,
            ScopeKind: FundLedgerScope.Entity,
            ScopeId: context.FirstEntityScopeId,
            SelectedLedgerIds: ["run-one", "run-two"]));

        summary.Should().NotBeNull();
        scoped.Should().NotBeNull();
        selectedScoped.Should().NotBeNull();

        var consolidatedSlice = summary!.LedgerSlices!.Single(slice => slice.ScopeKind == FundLedgerScope.Consolidated);
        var entitySlice = summary.LedgerSlices.Single(slice =>
            slice.ScopeKind == FundLedgerScope.Entity &&
            string.Equals(slice.ScopeId, context.FirstEntityScopeId, StringComparison.OrdinalIgnoreCase));
        var sleeveSlice = summary.LedgerSlices.Single(slice =>
            slice.ScopeKind == FundLedgerScope.Sleeve &&
            string.Equals(slice.ScopeId, context.FirstSleeveScopeId, StringComparison.OrdinalIgnoreCase));
        var vehicleSlice = summary.LedgerSlices.Single(slice =>
            slice.ScopeKind == FundLedgerScope.Vehicle &&
            string.Equals(slice.ScopeId, context.FirstVehicleScopeId, StringComparison.OrdinalIgnoreCase));

        consolidatedSlice.LedgerKey.Should().Be("Fund");
        consolidatedSlice.LedgerGroupId.Should().Be("fund");
        consolidatedSlice.Totals.AssetBalance.Should().Be(3_000m);

        entitySlice.LedgerKey.Should().Be($"Entity:{context.FirstEntityScopeId}");
        entitySlice.LedgerGroupId.Should().Be(context.FirstEntityScopeId);
        entitySlice.Journal.Should().HaveCount(4);
        entitySlice.TrialBalance.Should().Contain(line =>
            string.Equals(line.FinancialAccountId, context.FirstAccountId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        entitySlice.Metadata.Should().NotBeNull();
        entitySlice.Metadata!["ledgerKey"].Should().Be(entitySlice.LedgerKey);

        sleeveSlice.Totals.AssetBalance.Should().Be(1_500m);
        vehicleSlice.Totals.AssetBalance.Should().Be(1_500m);

        scoped!.ScopeKind.Should().Be(FundLedgerScope.Entity);
        scoped.ScopeId.Should().Be(context.FirstEntityScopeId);
        scoped.JournalEntryCount.Should().Be(4);
        scoped.AssetBalance.Should().Be(1_500m);
        scoped.ConsolidatedTotals!.AssetBalance.Should().Be(3_000m);

        selectedScoped!.JournalEntryCount.Should().Be(2);
        selectedScoped.AssetBalance.Should().Be(1_000m);
        selectedScoped.ConsolidatedTotals.Should().NotBeNull();
        selectedScoped.ConsolidatedTotals!.AssetBalance.Should().Be(2_500m);
    }

    [Fact]
    public async Task GetReconciliationSnapshotAsync_WithSelectedLedgerIds_UsesSelectedLedgerUniverse()
    {
        using var context = await CreateContextAsync();

        var snapshot = await context.Service.GetReconciliationSnapshotAsync(new FundLedgerQuery(
            FundProfileId: "alpha-fund",
            AsOf: context.AsOf,
            SelectedLedgerIds: ["run-one"]));

        snapshot.Should().NotBeNull();
        snapshot!.Consolidated.JournalEntryCount.Should().Be(2);
        snapshot.Consolidated.LedgerEntryCount.Should().Be(4);
        snapshot.Consolidated.Balances.Should().Contain(line =>
            line.AccountName == "Securities" &&
            line.Symbol == "AAPL" &&
            line.FinancialAccountId == context.FirstAccountId.ToString("D") &&
            line.Balance == 200m);
        snapshot.Entities.Should().ContainSingle().Which.Key.Should().Be(context.FirstEntityScopeId);
        snapshot.Sleeves.Should().ContainSingle().Which.Key.Should().Be(context.FirstSleeveScopeId);
        snapshot.Vehicles.Should().ContainSingle().Which.Key.Should().Be(context.FirstVehicleScopeId);
    }

    private static async Task<TestContext> CreateContextAsync()
    {
        var storagePath = CreateStoragePath();
        var fundContext = new FundContextService(storagePath);

        var firstEntityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var firstSleeveId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var firstVehicleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var secondEntityId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var secondSleeveId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var secondVehicleId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var firstAccountId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        var secondAccountId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

        await fundContext.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "alpha-fund",
            DisplayName: "Alpha Fund",
            LegalEntityName: "Alpha Fund LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "governance",
            DefaultLandingPageTag: "FundLedger",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            EntityIds: [firstEntityId.ToString("D"), secondEntityId.ToString("D")],
            SleeveIds: [firstSleeveId.ToString("D"), secondSleeveId.ToString("D")],
            VehicleIds: [firstVehicleId.ToString("D"), secondVehicleId.ToString("D")],
            IsDefault: true));

        var fundAccountService = new InMemoryFundAccountService();
        var fundId = ToFundId("alpha-fund");

        await CreateAccountAsync(fundAccountService, fundId, firstAccountId, firstEntityId, firstSleeveId, firstVehicleId, "ALPHA-CUST-1");
        await CreateAccountAsync(fundAccountService, fundId, secondAccountId, secondEntityId, secondSleeveId, secondVehicleId, "ALPHA-CUST-2");

        var store = new StrategyRunStore();
        await store.RecordRunAsync(BuildRun("run-one", "alpha-fund", firstAccountId, startingCash: 1_000m, securityCost: 200m, startedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun("run-two", "alpha-fund", secondAccountId, startingCash: 1_500m, securityCost: 300m, startedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun("run-three", "alpha-fund", firstAccountId, startingCash: 500m, securityCost: 150m, startedAt: new DateTimeOffset(2026, 4, 1, 13, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun("run-other", "beta-fund", Guid.Parse("cccccccc-3333-3333-3333-333333333333"), startingCash: 900m, securityCost: 100m, startedAt: new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero)));

        var runReadService = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var workspaceService = new StrategyRunWorkspaceService(store, runReadService, fundContext);
        var fundAccountReadService = new FundAccountReadService(fundAccountService);
        var service = new FundLedgerReadService(workspaceService, fundContext, fundAccountReadService);

        return new TestContext(
            Service: service,
            StoragePath: storagePath,
            AsOf: new DateTimeOffset(2026, 4, 1, 23, 0, 0, TimeSpan.Zero),
            FirstAccountId: firstAccountId,
            FirstEntityScopeId: firstEntityId.ToString("D"),
            FirstSleeveScopeId: firstSleeveId.ToString("D"),
            FirstVehicleScopeId: firstVehicleId.ToString("D"));
    }

    private static async Task CreateAccountAsync(
        InMemoryFundAccountService fundAccountService,
        Guid fundId,
        Guid accountId,
        Guid entityId,
        Guid sleeveId,
        Guid vehicleId,
        string accountCode)
    {
        await fundAccountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: accountId,
            AccountType: AccountTypeDto.Custody,
            AccountCode: accountCode,
            DisplayName: accountCode,
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "test",
            EntityId: entityId,
            FundId: fundId,
            SleeveId: sleeveId,
            VehicleId: vehicleId,
            Institution: "Northern Trust"));
    }

    private static StrategyRunEntry BuildRun(
        string runId,
        string fundProfileId,
        Guid accountId,
        decimal startingCash,
        decimal securityCost,
        DateTimeOffset startedAt)
    {
        var completedAt = startedAt.AddMinutes(20);
        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(startedAt.UtcDateTime),
            Symbols: ["AAPL"],
            InitialCash: startingCash,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: startingCash,
            FinalEquity: startingCash,
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
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 0m,
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
            Ledger: CreateLedger(accountId, startedAt, startingCash, securityCost),
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

    private static IReadOnlyLedger CreateLedger(
        Guid accountId,
        DateTimeOffset timestamp,
        decimal startingCash,
        decimal securityCost)
    {
        var financialAccountId = accountId.ToString("D");
        var ledger = new global::Meridian.Ledger.Ledger();

        Post(ledger, timestamp, "Capital",
        [
            (LedgerAccounts.CashAccount(financialAccountId), startingCash, 0m),
            (LedgerAccounts.CapitalAccount, 0m, startingCash)
        ]);
        Post(ledger, timestamp.AddMinutes(1), "Security purchase",
        [
            (LedgerAccounts.Securities("AAPL", financialAccountId), securityCost, 0m),
            (LedgerAccounts.CashAccount(financialAccountId), 0m, securityCost)
        ]);

        return ledger;
    }

    private static void Post(
        global::Meridian.Ledger.Ledger ledger,
        DateTimeOffset timestamp,
        string description,
        IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> lines)
    {
        var journalId = Guid.NewGuid();
        var entries = lines
            .Select(line => new LedgerEntry(
                Guid.NewGuid(),
                journalId,
                timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                description))
            .ToArray();
        ledger.Post(new JournalEntry(journalId, timestamp, description, entries));
    }

    private static Guid ToFundId(string fundProfileId)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId.Trim())));

    private static string CreateStoragePath()
        => Path.Combine(Path.GetTempPath(), "meridian-fund-ledger-tests", $"{Guid.NewGuid():N}.json");

    private sealed record TestContext(
        FundLedgerReadService Service,
        string StoragePath,
        DateTimeOffset AsOf,
        Guid FirstAccountId,
        string FirstEntityScopeId,
        string FirstSleeveScopeId,
        string FirstVehicleScopeId) : IDisposable
    {
        public void Dispose()
        {
            if (File.Exists(StoragePath))
            {
                File.Delete(StoragePath);
            }
        }
    }
}
#endif
