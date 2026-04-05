using System.IO;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class StrategyRunWorkspaceServiceTests
{
    [Fact]
    public async Task RecordBacktestRunAsync_ShouldExposeRecordedRunAcrossBrowserAndDrillIns()
    {
        var service = StrategyRunWorkspaceService.Instance;
        var request = new BacktestRequest(
            From: new DateOnly(2026, 3, 1),
            To: new DateOnly(2026, 3, 20),
            Symbols: ["AAPL", "MSFT"],
            InitialCash: 100_000m,
            DataRoot: "./data/test");
        var result = BuildResult();

        var runId = await service.RecordBacktestRunAsync(request, "Buy & Hold (equal-weight)", result);

        runId.Should().NotBeNullOrEmpty();

        var detail = await service.GetRunDetailAsync(runId);
        detail.Should().NotBeNull();
        detail!.Summary.Mode.Should().Be(StrategyRunMode.Backtest);
        detail.Summary.StrategyId.Should().Be("buy-hold-equal-weight");
        detail.Portfolio.Should().NotBeNull();
        detail.Ledger.Should().NotBeNull();
        detail.Parameters.Should().ContainKey("symbols");

        var portfolio = await service.GetPortfolioAsync(runId);
        portfolio.Should().NotBeNull();
        portfolio!.Positions.Should().ContainSingle(position => position.Symbol == "AAPL");

        var ledger = await service.GetLedgerAsync(runId);
        ledger.Should().NotBeNull();
        ledger!.TrialBalance.Should().NotBeEmpty();

        var latest = await service.GetLatestRunAsync();
        latest.Should().NotBeNull();
        latest!.RunId.Should().Be(runId);
    }

    [Fact]
    public async Task RecordBacktestRunAsync_WithInjectedSecurityLookup_ShouldExposeSecurityCoverage()
    {
        var store = new StrategyRunStore();
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL"));

        var service = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(lookup),
            new Meridian.Strategies.Services.LedgerReadService(lookup));
        var request = new BacktestRequest(
            From: new DateOnly(2026, 3, 1),
            To: new DateOnly(2026, 3, 20),
            Symbols: ["AAPL", "MSFT"],
            InitialCash: 100_000m,
            DataRoot: "./data/test");

        var runId = await service.RecordBacktestRunAsync(request, "Buy & Hold (equal-weight)", BuildResult());
        var detail = await service.GetRunDetailAsync(runId);

        detail.Should().NotBeNull();
        detail!.Portfolio.Should().NotBeNull();
        detail.Portfolio!.SecurityResolvedCount.Should().Be(1);
        detail.Portfolio.SecurityMissingCount.Should().Be(0);
        detail.Portfolio.Positions[0].Security.Should().NotBeNull();
        detail.Portfolio.Positions[0].Security!.DisplayName.Should().Be("Apple Inc.");

        detail.Ledger.Should().NotBeNull();
        detail.Ledger!.SecurityResolvedCount.Should().Be(1);
        detail.Ledger.SecurityMissingCount.Should().Be(0);
        detail.Ledger.TrialBalance.Should().Contain(line =>
            line.Symbol == "AAPL" &&
            line.Security != null &&
            line.Security.DisplayName == "Apple Inc.");
    }

    [Fact]
    public async Task RecordBacktestRunAsync_WithActiveFund_StampsFundContextOnRunModels()
    {
        var fundContext = await CreateFundContextAsync();
        var store = new StrategyRunStore();
        var service = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(),
            new Meridian.Strategies.Services.LedgerReadService(),
            fundContext);

        var runId = await service.RecordBacktestRunAsync(
            new BacktestRequest(
                From: new DateOnly(2026, 3, 1),
                To: new DateOnly(2026, 3, 20),
                Symbols: ["AAPL"],
                InitialCash: 100_000m,
                DataRoot: "./data/test"),
            "Fund Scoped Backtest",
            BuildResult());

        var detail = await service.GetRunDetailAsync(runId);

        detail.Should().NotBeNull();
        detail!.Summary.FundProfileId.Should().Be("alpha-credit");
        detail.Summary.FundDisplayName.Should().Be("Alpha Credit");
        detail.Portfolio.Should().NotBeNull();
        detail.Portfolio!.FundProfileId.Should().Be("alpha-credit");
        detail.Ledger.Should().NotBeNull();
        detail.Ledger!.FundProfileId.Should().Be("alpha-credit");
    }

    [Fact]
    public async Task SetActiveRunContextAsync_ShouldExposePortfolioLedgerAndRiskPreview()
    {
        var store = new StrategyRunStore();
        var service = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(),
            new Meridian.Strategies.Services.LedgerReadService());

        var runId = await service.RecordBacktestRunAsync(
            new BacktestRequest(
                From: new DateOnly(2026, 3, 1),
                To: new DateOnly(2026, 3, 20),
                Symbols: ["AAPL"],
                InitialCash: 100_000m,
                DataRoot: "./data/test"),
            "Context Run",
            BuildResult());

        var context = await service.SetActiveRunContextAsync(runId);

        context.Should().NotBeNull();
        context!.RunId.Should().Be(runId);
        context.StrategyName.Should().Be("Context Run");
        context.PortfolioPreview.Should().Contain("positions");
        context.LedgerPreview.Should().Contain("trial-balance");
        context.CanPromoteToPaper.Should().BeTrue();
    }

    [Fact]
    public async Task PromoteToPaperAsync_ShouldCreatePaperRunAndAdvanceActiveContext()
    {
        var store = new StrategyRunStore();
        var service = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(),
            new Meridian.Strategies.Services.LedgerReadService());

        var sourceRunId = await service.RecordBacktestRunAsync(
            new BacktestRequest(
                From: new DateOnly(2026, 3, 1),
                To: new DateOnly(2026, 3, 20),
                Symbols: ["AAPL"],
                InitialCash: 100_000m,
                DataRoot: "./data/test"),
            "Promotion Run",
            BuildResult());

        var promotedContext = await service.PromoteToPaperAsync(sourceRunId);
        var summary = await service.GetTradingSummaryAsync();

        promotedContext.Should().NotBeNull();
        promotedContext!.ModeLabel.Should().Be("Paper");
        promotedContext.RunId.Should().NotBe(sourceRunId);
        summary.PaperRunCount.Should().Be(1);
        summary.ActiveRunContext.Should().NotBeNull();
        summary.ActiveRunContext!.RunId.Should().Be(promotedContext.RunId);
    }

    [Fact]
    public async Task GetTradingSummaryAsync_WithActiveFund_FiltersLegacyRunsOutOfDefaultTradingPosture()
    {
        var fundContext = await CreateFundContextAsync();
        var store = new StrategyRunStore();
        await store.RecordRunAsync(new Meridian.Strategies.Models.StrategyRunEntry(
            RunId: "fund-paper",
            StrategyId: "alpha-1",
            StrategyName: "Alpha",
            RunType: RunType.Paper,
            StartedAt: new DateTimeOffset(2026, 3, 21, 9, 0, 0, TimeSpan.Zero),
            EndedAt: null,
            Metrics: null,
            FundProfileId: "alpha-credit",
            FundDisplayName: "Alpha Credit"));
        await store.RecordRunAsync(new Meridian.Strategies.Models.StrategyRunEntry(
            RunId: "legacy-paper",
            StrategyId: "legacy-1",
            StrategyName: "Legacy",
            RunType: RunType.Paper,
            StartedAt: new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero),
            EndedAt: null,
            Metrics: null));

        var service = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(),
            new Meridian.Strategies.Services.LedgerReadService(),
            fundContext);

        var summary = await service.GetTradingSummaryAsync();

        summary.PaperRunCount.Should().Be(1);
        summary.LiveRunCount.Should().Be(0);
    }

    [Fact]
    public async Task FundLedgerReadService_GetAsync_BuildsConsolidatedSummaryForSelectedFund()
    {
        var fundContext = await CreateFundContextAsync();
        var store = new StrategyRunStore();
        var runService = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(),
            new Meridian.Strategies.Services.LedgerReadService(),
            fundContext);
        await runService.RecordBacktestRunAsync(
            new BacktestRequest(
                From: new DateOnly(2026, 3, 1),
                To: new DateOnly(2026, 3, 20),
                Symbols: ["AAPL"],
                InitialCash: 100_000m,
                DataRoot: "./data/test"),
            "Ledger Fund Run",
            BuildResult());

        var ledgerService = new FundLedgerReadService(runService, fundContext);

        var summary = await ledgerService.GetAsync(new FundLedgerQuery(
            FundProfileId: "alpha-credit",
            ScopeKind: FundLedgerScope.Consolidated));

        summary.Should().NotBeNull();
        summary!.FundProfileId.Should().Be("alpha-credit");
        summary.FundDisplayName.Should().Be("Alpha Credit");
        summary.JournalEntryCount.Should().Be(2);
        summary.LedgerEntryCount.Should().BeGreaterThan(0);
        summary.TrialBalance.Should().NotBeEmpty();
        summary.Journal.Should().HaveCount(2);
        summary.EntityCount.Should().Be(1);
        summary.SleeveCount.Should().Be(1);
        summary.VehicleCount.Should().Be(1);
    }

    [Fact]
    public async Task FundAccountReadService_GetAccountsAsync_ProjectsGroupedFundAccounts()
    {
        var fundContext = await CreateFundContextAsync();
        var fundAccountService = new InMemoryFundAccountService();
        var fundId = FundProfileKeyTranslator.ToFundId("alpha-credit");
        var bankAccountId = Guid.NewGuid();
        var custodyAccountId = Guid.NewGuid();

        await fundAccountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: bankAccountId,
            AccountType: AccountTypeDto.Bank,
            AccountCode: "BANK-001",
            DisplayName: "Operating Cash",
            BaseCurrency: "USD",
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-5),
            CreatedBy: "tests",
            FundId: fundId,
            BankDetails: new BankAccountDetailsDto(
                AccountNumber: "1234567890",
                BankName: "First Meridian Bank",
                BranchName: "Phoenix",
                Iban: null,
                BicSwift: null,
                RoutingNumber: "111000111",
                SortCode: null,
                IntermediaryBankBic: null,
                IntermediaryBankName: null,
                BeneficiaryName: "Alpha Credit",
                BeneficiaryAddress: null)));

        await fundAccountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: custodyAccountId,
            AccountType: AccountTypeDto.Custody,
            AccountCode: "CUST-001",
            DisplayName: "Prime Custody",
            BaseCurrency: "USD",
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-5),
            CreatedBy: "tests",
            FundId: fundId,
            CustodianDetails: new CustodianAccountDetailsDto(
                SubAccountNumber: "SUB-01",
                DtcParticipantCode: "DTC1",
                CrestMemberCode: null,
                EuroclearAccountNumber: null,
                ClearstreamAccountNumber: null,
                PrimebrokerGiveupCode: "PB01",
                SafekeepingLocation: "NYC",
                ServiceAgreementReference: "AG-1")));

        await fundAccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: bankAccountId,
            AsOfDate: new DateOnly(2026, 4, 1),
            Currency: "USD",
            CashBalance: 150_000m,
            Source: "bank-feed",
            RecordedBy: "tests",
            SecuritiesMarketValue: 5_000m,
            PendingSettlement: 2_500m));

        await fundAccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: custodyAccountId,
            AsOfDate: new DateOnly(2026, 4, 1),
            Currency: "USD",
            CashBalance: 25_000m,
            Source: "custody-feed",
            RecordedBy: "tests",
            SecuritiesMarketValue: 300_000m));

        await fundAccountService.ReconcileAccountAsync(new ReconcileAccountRequest(
            AccountId: bankAccountId,
            AsOfDate: new DateOnly(2026, 4, 1),
            RequestedBy: "tests"));

        var service = new FundAccountReadService(fundAccountService);

        var accounts = await service.GetAccountsAsync(fundContext.CurrentFundProfile!.FundProfileId);

        accounts.Should().HaveCount(2);
        accounts.Should().ContainSingle(account =>
            account.AccountId == bankAccountId &&
            account.BankName == "First Meridian Bank" &&
            account.AccountNumberMasked == "****7890" &&
            account.ReconciliationRuns == 1 &&
            account.NetAssetValue == 155_000m);
        accounts.Should().ContainSingle(account =>
            account.AccountId == custodyAccountId &&
            account.AccountType == AccountTypeDto.Custody &&
            account.SecuritiesMarketValue == 300_000m);
    }

    [Fact]
    public async Task CashFinancingAndReconciliationReadServices_GetAsync_ReturnFundScopedPosture()
    {
        var fundContext = await CreateFundContextAsync();
        var store = new StrategyRunStore();
        var runService = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(),
            new Meridian.Strategies.Services.LedgerReadService(),
            fundContext);
        await runService.RecordBacktestRunAsync(
            new BacktestRequest(
                From: new DateOnly(2026, 3, 1),
                To: new DateOnly(2026, 3, 20),
                Symbols: ["AAPL"],
                InitialCash: 100_000m,
                DataRoot: "./data/test"),
            "Capital posture run",
            BuildResult());

        var fundAccountService = new InMemoryFundAccountService();
        var fundId = FundProfileKeyTranslator.ToFundId("alpha-credit");
        var bankAccountId = Guid.NewGuid();

        await fundAccountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: bankAccountId,
            AccountType: AccountTypeDto.Bank,
            AccountCode: "BANK-OPS",
            DisplayName: "Operations Bank",
            BaseCurrency: "USD",
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-5),
            CreatedBy: "tests",
            FundId: fundId,
            BankDetails: new BankAccountDetailsDto(
                AccountNumber: "555500001234",
                BankName: "Northern Trust",
                BranchName: null,
                Iban: null,
                BicSwift: null,
                RoutingNumber: null,
                SortCode: null,
                IntermediaryBankBic: null,
                IntermediaryBankName: null,
                BeneficiaryName: "Alpha Credit",
                BeneficiaryAddress: null)));

        await fundAccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: bankAccountId,
            AsOfDate: new DateOnly(2026, 4, 2),
            Currency: "USD",
            CashBalance: 225_000m,
            Source: "bank-feed",
            RecordedBy: "tests",
            SecuritiesMarketValue: 10_000m,
            PendingSettlement: 5_000m));

        await fundAccountService.IngestBankStatementAsync(new IngestBankStatementRequest(
            BatchId: Guid.NewGuid(),
            AccountId: bankAccountId,
            StatementDate: new DateOnly(2026, 4, 2),
            BankName: "Northern Trust",
            FileName: "statement.csv",
            Lines:
            [
                new BankStatementLineDto(
                    StatementLineId: Guid.NewGuid(),
                    BatchId: Guid.NewGuid(),
                    AccountId: bankAccountId,
                    StatementDate: new DateOnly(2026, 4, 2),
                    ValueDate: new DateOnly(2026, 4, 2),
                    Amount: 25_000m,
                    Currency: "USD",
                    TransactionType: "Wire",
                    Description: "Capital contribution",
                    ExternalReference: "wire-1",
                    RunningBalance: 225_000m)
            ],
            LoadedBy: "tests"));

        await fundAccountService.ReconcileAccountAsync(new ReconcileAccountRequest(
            AccountId: bankAccountId,
            AsOfDate: new DateOnly(2026, 4, 2),
            RequestedBy: "tests"));

        var accountReadService = new FundAccountReadService(fundAccountService);
        var cashService = new CashFinancingReadService(runService, accountReadService);
        var reconciliationService = new ReconciliationReadService(
            fundAccountService,
            accountReadService,
            runService);

        var cash = await cashService.GetAsync("alpha-credit", "USD");
        var reconciliation = await reconciliationService.GetAsync("alpha-credit");
        var bankSnapshots = await accountReadService.GetBankSnapshotsAsync("alpha-credit");

        cash.TotalCash.Should().Be(225_000m);
        cash.GrossExposure.Should().BeGreaterThan(0m);
        cash.TotalEquity.Should().BeGreaterThan(0m);
        cash.Highlights.Should().NotBeEmpty();

        reconciliation.RunCount.Should().Be(1);
        reconciliation.OpenBreakCount.Should().Be(0);
        reconciliation.RecentRuns.Should().ContainSingle(item => item.AccountId == bankAccountId);

        bankSnapshots.Should().ContainSingle(snapshot =>
            snapshot.AccountId == bankAccountId &&
            snapshot.BankName == "Northern Trust" &&
            snapshot.StatementLineCount == 1 &&
            snapshot.LatestTransactionType == "Wire");
    }

    private static async Task<FundContextService> CreateFundContextAsync()
    {
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            "meridian-fund-context-tests",
            $"{Guid.NewGuid():N}.json");

        var service = new FundContextService(storagePath);
        await service.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "alpha-credit",
            DisplayName: "Alpha Credit",
            LegalEntityName: "Alpha Credit Master Fund LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "governance",
            DefaultLandingPageTag: "GovernanceShell",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            EntityIds: ["entity-1"],
            SleeveIds: ["sleeve-1"],
            VehicleIds: ["vehicle-1"],
            IsDefault: true));
        await service.SelectFundProfileAsync("alpha-credit");
        return service;
    }

    private static BacktestResult BuildResult()
    {
        var startedAt = new DateTimeOffset(2026, 3, 20, 14, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(15);

        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 100, 185m, 1_500m, 3_000m)
        };

        var account = FinancialAccount.CreateDefaultBrokerage(100_000m, 0.05, 0.02);
        var accountSnapshots = new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            [account.AccountId] = new FinancialAccountSnapshot(
                AccountId: account.AccountId,
                DisplayName: account.DisplayName,
                Kind: account.Kind,
                Institution: account.Institution,
                Cash: 82_000m,
                MarginBalance: 0m,
                LongMarketValue: 21_000m,
                ShortMarketValue: 0m,
                Equity: 103_000m,
                Positions: positions,
                Rules: account.Rules!)
        };

        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 82_000m,
            MarginBalance: 0m,
            LongMarketValue: 21_000m,
            ShortMarketValue: 0m,
            TotalEquity: 103_000m,
            DailyReturn: 0.03m,
            Positions: positions,
            Accounts: accountSnapshots,
            DayCashFlows: Array.Empty<CashFlowEntry>());

        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var equity = new LedgerAccount("Owner Equity", LedgerAccountType.Equity);
        var gains = new LedgerAccount("Trading Gains", LedgerAccountType.Revenue, Symbol: "AAPL");

        ledger.PostLines(startedAt, "initial-capital", new[]
        {
            (cash, 100_000m, 0m),
            (equity, 0m, 100_000m)
        });

        ledger.PostLines(completedAt, "close-run", new[]
        {
            (cash, 3_000m, 0m),
            (gains, 0m, 3_000m)
        });

        return new BacktestResult(
            Request: new BacktestRequest(
                From: new DateOnly(2026, 3, 1),
                To: new DateOnly(2026, 3, 20),
                Symbols: ["AAPL", "MSFT"],
                InitialCash: 100_000m,
                DataRoot: "./data/test"),
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL", "MSFT" },
            Snapshots: [snapshot],
            CashFlows: Array.Empty<CashFlowEntry>(),
            Fills:
            [
                new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "AAPL", 100, 185m, 4m, startedAt.AddMinutes(1), account.AccountId)
            ],
            Metrics: new BacktestMetrics(
                InitialCapital: 100_000m,
                FinalEquity: 103_000m,
                GrossPnl: 3_004m,
                NetPnl: 3_000m,
                TotalReturn: 0.03m,
                AnnualizedReturn: 0.03m,
                SharpeRatio: 1.1,
                SortinoRatio: 1.1,
                CalmarRatio: 0.8,
                MaxDrawdown: 800m,
                MaxDrawdownPercent: 0.008m,
                MaxDrawdownRecoveryDays: 2,
                ProfitFactor: 1.6,
                WinRate: 1.0,
                TotalTrades: 1,
                WinningTrades: 1,
                LosingTrades: 0,
                TotalCommissions: 4m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m,
                Xirr: 0.12,
                SymbolAttribution: new Dictionary<string, SymbolAttribution>
                {
                    ["AAPL"] = new("AAPL", 3_000m, 1_500m, 1, 4m, 0m)
                }),
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(15),
            TotalEventsProcessed: 1_250);
    }

    private sealed class StubSecurityReferenceLookup : Meridian.Strategies.Services.ISecurityReferenceLookup
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
