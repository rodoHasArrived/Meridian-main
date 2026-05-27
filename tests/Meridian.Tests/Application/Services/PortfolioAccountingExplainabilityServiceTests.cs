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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed class PortfolioAccountingExplainabilityServiceTests
{
    [Fact]
    public async Task GetLedgerDrillDownAsync_ReturnsTrialBalanceJournalAndAccountSummaries()
    {
        var fixture = await CreateFixtureAsync();
        var service = new PortfolioAccountingReadService(
            fixture.WorkspaceReadService,
            reconciliationRunService: null,
            securityReferenceLookup: null,
            optionsMonitor: new StaticOptionsMonitor<AccountingExplainabilityOptions>(new AccountingExplainabilityOptions()));

        var result = await service.GetLedgerDrillDownAsync(new LedgerDrillDownQuery(fixture.FundProfileId));

        result.Should().NotBeNull();
        result!.Ledger.TrialBalance.Should().NotBeEmpty();
        result.Ledger.Journal.Should().NotBeEmpty();
        result.Accounts.Should().ContainSingle();
    }

    [Fact]
    public async Task GetFinancingAnalysisAsync_ProjectsCashAndFinancingRollups()
    {
        var fixture = await CreateFixtureAsync();
        var service = new PortfolioAccountingReadService(
            fixture.WorkspaceReadService,
            reconciliationRunService: null,
            securityReferenceLookup: null,
            optionsMonitor: new StaticOptionsMonitor<AccountingExplainabilityOptions>(new AccountingExplainabilityOptions()));

        var result = await service.GetFinancingAnalysisAsync(new FinancingAnalysisQuery(fixture.FundProfileId));

        result.Should().NotBeNull();
        result!.CashFinancing.TotalCash.Should().BeGreaterThanOrEqualTo(0m);
        result.CashFinancing.TotalEquity.Should().BeGreaterThanOrEqualTo(0m);
        result.Summary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExplainEquityChangeAsync_ComputesWaterfall_FromLedgerComponents()
    {
        var fixture = await CreateFixtureAsync();
        var service = new AccountingExplainabilityService(
            fixture.WorkspaceReadService,
            fixture.PortfolioReadService,
            new StaticOptionsMonitor<AccountingExplainabilityOptions>(new AccountingExplainabilityOptions()),
            NullLogger<AccountingExplainabilityService>.Instance);

        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-1);
        var result = await service.ExplainEquityChangeAsync(new EquityChangeQuery(fixture.FundProfileId, from, to));

        result.Should().NotBeNull();
        result!.Components.Should().NotBeEmpty();
        result.ClosingEquity.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public async Task ExplainEquityChangeAsync_FlagsUnclassifiedDelta_WhenResidualExists()
    {
        var fixture = await CreateFixtureAsync();
        var service = new AccountingExplainabilityService(
            fixture.WorkspaceReadService,
            fixture.PortfolioReadService,
            new StaticOptionsMonitor<AccountingExplainabilityOptions>(new AccountingExplainabilityOptions
            {
                MaterialityThreshold = 0.0001m
            }),
            NullLogger<AccountingExplainabilityService>.Instance);

        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-1);
        var result = await service.ExplainEquityChangeAsync(new EquityChangeQuery(fixture.FundProfileId, from, to));

        result.Should().NotBeNull();
        result!.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ReconcilePnlAsync_UsesTolerance_FromOptions()
    {
        var fixture = await CreateFixtureAsync();
        var service = new AccountingExplainabilityService(
            fixture.WorkspaceReadService,
            fixture.PortfolioReadService,
            new StaticOptionsMonitor<AccountingExplainabilityOptions>(new AccountingExplainabilityOptions
            {
                ReconciliationTolerance = decimal.MaxValue
            }),
            NullLogger<AccountingExplainabilityService>.Instance);

        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-1);
        var result = await service.ReconcilePnlAsync(new PnlReconciliationQuery(fixture.FundProfileId, from, to));

        result.Should().NotBeNull();
        result!.IsWithinTolerance.Should().BeTrue();
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var fundId = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId)));
        var accountService = new InMemoryFundAccountService();
        var strategyRepository = new StrategyRunStore();
        var securityMaster = new NullSecurityMasterQueryService();
        var portfolioReadService = new PortfolioReadService();
        var workspaceReadService = new FundOperationsWorkspaceReadService(
            accountService,
            strategyRepository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Custody,
            AccountCode: "CUST-001",
            DisplayName: "Primary Custody",
            BaseCurrency: "USD",
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-5),
            CreatedBy: "test",
            FundId: fundId,
            LedgerReference: "fund-ledger"));
        await strategyRepository.RecordRunAsync(BuildRun(fundProfileId));

        return new TestFixture(fundProfileId, workspaceReadService, portfolioReadService);
    }

    private static StrategyRunEntry BuildRun(string fundProfileId)
    {
        var asOf = DateTimeOffset.UtcNow;
        var ledger = new Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, asOf.AddMinutes(-5), "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, asOf, "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
        ]);

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(asOf.AddDays(-1).UtcDateTime),
            To: DateOnly.FromDateTime(asOf.UtcDateTime),
            Symbols: ["AAPL"],
            InitialCash: 1_000m,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_050m,
            GrossPnl: 50m,
            NetPnl: 50m,
            TotalReturn: 0.05m,
            AnnualizedReturn: 0.05m,
            SharpeRatio: 1.0,
            SortinoRatio: 1.0,
            CalmarRatio: 1.0,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1.0,
            WinRate: 1.0,
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 0m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.05d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());
        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL" },
            Snapshots: [],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromSeconds(5),
            TotalEventsProcessed: 10);

        return StrategyRunEntry.Start("strategy-1", "Strategy", RunType.Paper) with
        {
            RunId = "run-1",
            StartedAt = asOf.AddMinutes(-30),
            EndedAt = asOf,
            Metrics = result,
            PortfolioId = "portfolio-1",
            LedgerReference = "ledger-1",
            AuditReference = "audit-1",
            FundProfileId = fundProfileId,
            FundDisplayName = "Alpha Fund"
        };
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

    private sealed record TestFixture(
        string FundProfileId,
        FundOperationsWorkspaceReadService WorkspaceReadService,
        PortfolioReadService PortfolioReadService);

    private sealed class StaticOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        public TOptions CurrentValue { get; } = currentValue;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
