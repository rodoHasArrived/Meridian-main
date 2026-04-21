using FluentAssertions;
using Meridian.Backtesting.Sdk;
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
    public async Task GetAsync_WithSelectedLedgerIds_ConstrainsConsolidationToSelectedRuns()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), "meridian-fund-ledger-tests", $"{Guid.NewGuid():N}.json");

        try
        {
            var fundContext = new FundContextService(storagePath);
            await fundContext.UpsertProfileAsync(new FundProfileDetail(
                FundProfileId: "alpha-fund",
                DisplayName: "Alpha Fund",
                LegalEntityName: "Alpha Fund LP",
                BaseCurrency: "USD",
                DefaultWorkspaceId: "governance",
                DefaultLandingPageTag: "FundLedger",
                DefaultLedgerScope: FundLedgerScope.Consolidated));

            var store = new StrategyRunStore();
            await store.RecordRunAsync(BuildRun("run-one", "alpha-fund", 100m));
            await store.RecordRunAsync(BuildRun("run-two", "alpha-fund", 250m));

            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService()));
            var service = new FundLedgerReadService(workspaceService, fundContext);

            var allLedgers = await service.GetAsync(new FundLedgerQuery("alpha-fund"));
            var selected = await service.GetAsync(new FundLedgerQuery("alpha-fund", SelectedLedgerIds: ["run-one"]));

            allLedgers.Should().NotBeNull();
            selected.Should().NotBeNull();
            allLedgers!.JournalEntryCount.Should().Be(2);
            selected!.JournalEntryCount.Should().Be(1);
            allLedgers.AssetBalance.Should().Be(350m);
            selected.AssetBalance.Should().Be(100m);
        }
        finally
        {
            if (File.Exists(storagePath))
            {
                File.Delete(storagePath);
            }
        }
    }

    [Fact]
    public async Task GetAsync_WithUnknownSelectedLedgerIds_ReturnsEmptyLedgerSummary()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), "meridian-fund-ledger-tests", $"{Guid.NewGuid():N}.json");

        try
        {
            var fundContext = new FundContextService(storagePath);
            await fundContext.UpsertProfileAsync(new FundProfileDetail(
                FundProfileId: "alpha-fund",
                DisplayName: "Alpha Fund",
                LegalEntityName: "Alpha Fund LP",
                BaseCurrency: "USD",
                DefaultWorkspaceId: "governance",
                DefaultLandingPageTag: "FundLedger",
                DefaultLedgerScope: FundLedgerScope.Consolidated));

            var store = new StrategyRunStore();
            await store.RecordRunAsync(BuildRun("run-one", "alpha-fund", 100m));

            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService()));
            var service = new FundLedgerReadService(workspaceService, fundContext);

            var summary = await service.GetAsync(new FundLedgerQuery("alpha-fund", SelectedLedgerIds: ["unknown-ledger"]));

            summary.Should().NotBeNull();
            summary!.JournalEntryCount.Should().Be(0);
            summary.TrialBalance.Should().BeEmpty();
            summary.AssetBalance.Should().Be(0m);
        }
        finally
        {
            if (File.Exists(storagePath))
            {
                File.Delete(storagePath);
            }
        }
    }

    private static StrategyRunEntry BuildRun(string runId, string fundProfileId, decimal securitiesDebit)
    {
        var timestamp = new DateTimeOffset(2026, 4, 11, 14, 0, 0, TimeSpan.Zero);
        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, timestamp, "Selection test entry",
        [
            (LedgerAccounts.Securities("AAPL"), securitiesDebit, 0m),
            (LedgerAccounts.CapitalAccount, 0m, securitiesDebit)
        ]);

        var request = new BacktestRequest(
            From: new DateOnly(2026, 4, 10),
            To: new DateOnly(2026, 4, 11),
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
            WinRate: 0d,
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
            Snapshots: [],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(1),
            TotalEventsProcessed: 1);

        return StrategyRunEntry.Start("selection-test", "Selection Test", RunType.Backtest) with
        {
            RunId = runId,
            StartedAt = timestamp,
            EndedAt = timestamp.AddMinutes(1),
            Metrics = result,
            FundProfileId = fundProfileId,
            FundDisplayName = "Alpha Fund"
        };
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
