#if WINDOWS
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Controls;
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
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class FundLedgerViewModelTests
{
    [Fact]
    public void LoadAsync_PopulatesStructureLabels_SecurityCoverage_AndStrategyLevelReconciliation()
    {
        WpfTestThread.Run(async () =>
        {
            var storagePath = Path.Combine(
                Path.GetTempPath(),
                "meridian-fund-ledger-tests",
                $"{Guid.NewGuid():N}.json");

            try
            {
                var navigation = NavigationService.Instance;
                navigation.ResetForTests();
                navigation.Initialize(new Frame());

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

                var fundAccountReadService = new FundAccountReadService(fundAccountService);
                var cashFinancingReadService = new CashFinancingReadService(workspaceService, fundAccountReadService);
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
                var fundLedgerReadService = new FundLedgerReadService(workspaceService, fundContext);

                using var viewModel = new FundLedgerViewModel(
                    fundLedgerReadService,
                    fundContext,
                    navigation,
                    fundAccountReadService,
                    cashFinancingReadService,
                    reconciliationReadService,
                    workspaceService);

                await viewModel.LoadAsync();

                viewModel.Accounts.Should().ContainSingle();
                viewModel.Accounts[0].StructureLabel.Should().Contain("Entity");
                viewModel.Accounts[0].StructureLabel.Should().Contain("Sleeve");
                viewModel.Accounts[0].StructureLabel.Should().Contain("Vehicle");

                viewModel.PortfolioPositions.Should().Contain(position =>
                    position.Symbol == "AAPL" &&
                    position.HasSecurityCoverage &&
                    position.SecurityDisplayName == "Apple Inc.");
                viewModel.PortfolioPositions.Should().Contain(position =>
                    position.Symbol == "TSLA" &&
                    !position.HasSecurityCoverage &&
                    position.CoverageLabel == "Unresolved");
                viewModel.SecurityCoverageText.Should().Contain("need Security Master coverage");

                viewModel.ReconciliationRuns.Should().Contain(item =>
                    item.ScopeLabel == "Strategy Run" &&
                    item.RunId == "run-fund-ops" &&
                    item.HasSecurityCoverageIssues &&
                    item.SecurityIssueCount == 2);
                viewModel.OverviewStatusText.Should().Contain("unresolved security mapping");
            }
            finally
            {
                if (File.Exists(storagePath))
                {
                    File.Delete(storagePath);
                }
            }
        });
    }

    private static Guid ToFundId(string fundProfileId)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId.Trim())));

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
}
#endif
