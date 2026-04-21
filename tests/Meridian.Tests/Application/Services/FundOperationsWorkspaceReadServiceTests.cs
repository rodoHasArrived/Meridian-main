using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Application.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed class FundOperationsWorkspaceReadServiceTests
{
    [Fact]
    public async Task GetWorkspaceAsync_WithRunsAccountsAndBanking_ReturnsAggregatedWorkspace()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var fundId = TranslateFundProfileId(fundProfileId);
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        var bankAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Bank,
            AccountCode: "BANK-001",
            DisplayName: "Operating Cash",
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "test",
            FundId: fundId,
            LedgerReference: "FUND-TB",
            BankDetails: new BankAccountDetailsDto(
                AccountNumber: "1234567890",
                BankName: "Meridian Bank",
                BranchName: null,
                Iban: null,
                BicSwift: null,
                RoutingNumber: null,
                SortCode: null,
                IntermediaryBankBic: null,
                IntermediaryBankName: null,
                BeneficiaryName: null,
                BeneficiaryAddress: null)));
        var custodyAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Custody,
            AccountCode: "CUST-001",
            DisplayName: "Core Custody",
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "test",
            FundId: fundId,
            LedgerReference: "FUND-TB"));

        await accountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: bankAccount.AccountId,
            AsOfDate: new DateOnly(2026, 4, 11),
            Currency: "USD",
            CashBalance: 2_500m,
            Source: "bank",
            RecordedBy: "test",
            PendingSettlement: 150m));
        await accountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: custodyAccount.AccountId,
            AsOfDate: new DateOnly(2026, 4, 11),
            Currency: "USD",
            CashBalance: 750m,
            Source: "custody",
            RecordedBy: "test",
            SecuritiesMarketValue: 400m));
        await accountService.IngestBankStatementAsync(new IngestBankStatementRequest(
            BatchId: Guid.NewGuid(),
            AccountId: bankAccount.AccountId,
            StatementDate: new DateOnly(2026, 4, 11),
            BankName: "Meridian Bank",
            Notes: "test",
            Lines:
            [
                new BankStatementLineDto(
                    LineId: Guid.NewGuid(),
                    BatchId: Guid.NewGuid(),
                    AccountId: bankAccount.AccountId,
                    TransactionDate: new DateOnly(2026, 4, 11),
                    ValueDate: new DateOnly(2026, 4, 11),
                    Amount: 250m,
                    Currency: "USD",
                    TransactionType: "Contribution",
                    Description: "Capital contribution",
                    Reference: "BANK-REF-001",
                    ClosingBalance: 2_500m)
            ],
            LoadedBy: "test"));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-governance-001",
            strategyId: "carry-1",
            strategyName: "Carry Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
            Currency: "USD"));

        workspace.DisplayName.Should().Be("Alpha Income Fund");
        workspace.RecordedRunCount.Should().Be(1);
        workspace.RelatedRunIds.Should().ContainSingle().Which.Should().Be("run-governance-001");
        workspace.Accounts.Should().HaveCount(2);
        workspace.BankSnapshots.Should().ContainSingle(snapshot => snapshot.AccountId == bankAccount.AccountId);
        workspace.CashFinancing.PendingSettlement.Should().Be(150m);
        workspace.Ledger.JournalEntryCount.Should().BeGreaterThan(0);
        workspace.Ledger.TrialBalance.Should().NotBeEmpty();
        workspace.Nav.ComponentCount.Should().BeGreaterThan(0);
        workspace.Reporting.ProfileCount.Should().BeGreaterThan(0);
        workspace.Workspace.TotalAccounts.Should().Be(2);
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithBlankFundProfileId_ThrowsArgumentException()
    {
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        var act = () => service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(" "));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithSelectedLedgerIds_ConstrainsWorkspaceToSelection()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-selection-001",
            strategyId: "carry-1",
            strategyName: "Carry Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));
        await repository.RecordRunAsync(BuildRun(
            runId: "run-selection-002",
            strategyId: "carry-2",
            strategyName: "Carry Strategy 2",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            SelectedLedgerIds: ["run-selection-001"]));

        workspace.RecordedRunCount.Should().Be(1);
        workspace.RelatedRunIds.Should().ContainSingle().Which.Should().Be("run-selection-001");
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithUnknownSelectedLedgerIds_ReturnsEmptyLedgerProjection()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-known-001",
            strategyId: "carry-1",
            strategyName: "Carry Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            SelectedLedgerIds: ["run-does-not-exist"]));

        workspace.RecordedRunCount.Should().Be(0);
        workspace.RelatedRunIds.Should().BeEmpty();
        workspace.Ledger.JournalEntryCount.Should().Be(0);
        workspace.Ledger.TrialBalance.Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewReportPackAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.PreviewReportPackAsync(
            new FundReportPackPreviewRequestDto("fund-cancel"),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static StrategyRunEntry BuildRun(
        string runId,
        string strategyId,
        string strategyName,
        string fundProfileId,
        string fundDisplayName)
    {
        var startedAt = new DateTimeOffset(2026, 4, 11, 14, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(30);
        var ledger = CreateLedger();
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m)
        };
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: BacktestDefaults.DefaultBrokerageAccountId,
            DisplayName: "Primary Brokerage",
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Simulated Broker",
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            Equity: 1_150m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            TotalEquity: 1_150m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

        var request = new BacktestRequest(
            From: new DateOnly(2026, 4, 10),
            To: new DateOnly(2026, 4, 11),
            Symbols: ["AAPL"],
            InitialCash: 1_000m,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_150m,
            GrossPnl: 150m,
            NetPnl: 150m,
            TotalReturn: 0.15m,
            AnnualizedReturn: 0.15m,
            SharpeRatio: 1.2,
            SortinoRatio: 1.2,
            CalmarRatio: 1.2,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1.0,
            WinRate: 1.0,
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 1m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.15,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>
            {
                ["AAPL"] = new("AAPL", 150m, 0m, 1, 1m, 0m)
            });
        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL" },
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromSeconds(5),
            TotalEventsProcessed: 10);

        return StrategyRunEntry.Start(strategyId, strategyName, RunType.Paper) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = completedAt,
            Metrics = result,
            PortfolioId = $"{strategyId}-paper-portfolio",
            LedgerReference = $"{strategyId}-paper-ledger",
            AuditReference = $"audit-{runId}",
            FundProfileId = fundProfileId,
            FundDisplayName = fundDisplayName
        };
    }

    private static Meridian.Ledger.Ledger CreateLedger()
    {
        var ledger = new Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 4, 11, 14, 0, 0, TimeSpan.Zero), "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 4, 11, 14, 10, 0, TimeSpan.Zero), "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
        ]);
        return ledger;
    }

    private static void PostBalancedEntry(
        Meridian.Ledger.Ledger ledger,
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

    private static Guid TranslateFundProfileId(string fundProfileId)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId)));
}
