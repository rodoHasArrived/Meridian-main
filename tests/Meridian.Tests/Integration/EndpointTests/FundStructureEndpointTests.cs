using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Application.FundAccounts;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class FundStructureEndpointTests
{
    private readonly EndpointTestFixture _fixture;
    private readonly HttpClient _client;

    public FundStructureEndpointTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
    }

    [Fact]
    public async Task GetWorkspaceView_WithSeededFundProfile_ReturnsWorkspaceProjection()
    {
        var seed = await SeedFundWorkspaceAsync();

        var response = await _client.GetAsync(
            $"/api/fund-structure/workspace-view?fundProfileId={Uri.EscapeDataString(seed.FundProfileId)}&currency=USD");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<FundOperationsWorkspaceDto>();

        payload.Should().NotBeNull();
        payload!.FundProfileId.Should().Be(seed.FundProfileId);
        payload.DisplayName.Should().Be(seed.DisplayName);
        payload.RecordedRunCount.Should().BeGreaterThan(0);
        payload.Accounts.Should().Contain(account => account.AccountId == seed.BankAccountId);
        payload.BankSnapshots.Should().Contain(snapshot => snapshot.AccountId == seed.BankAccountId);
        payload.Ledger.JournalEntryCount.Should().BeGreaterThan(0);
        payload.Nav.TotalNav.Should().NotBe(0m);
        payload.Reporting.ProfileCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetWorkspaceView_WithScopeQueryParameters_ParsesSelectionAndReturnsScopedLedgerDto()
    {
        var seed = await SeedFundWorkspaceAsync();

        var response = await _client.GetAsync(
            $"/api/fund-structure/workspace-view?fundProfileId={Uri.EscapeDataString(seed.FundProfileId)}&scopeKind=Entity&scopeId=entity-alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<FundOperationsWorkspaceDto>();

        payload.Should().NotBeNull();
        payload!.Ledger.ScopeKind.Should().Be(FundLedgerScope.Entity);
        payload.Ledger.ScopeId.Should().Be("entity-alpha");
        payload.Ledger.TrialBalance.Should().NotBeNull();
        payload.Ledger.Journal.Should().NotBeNull();
    }

    [Fact]
    public async Task GetWorkspaceView_WithInvalidScopeKind_FallsBackToConsolidatedScope()
    {
        var seed = await SeedFundWorkspaceAsync();

        var response = await _client.GetAsync(
            $"/api/fund-structure/workspace-view?fundProfileId={Uri.EscapeDataString(seed.FundProfileId)}&scopeKind=invalid-value&scopeId=entity-alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<FundOperationsWorkspaceDto>();

        payload.Should().NotBeNull();
        payload!.Ledger.ScopeKind.Should().Be(FundLedgerScope.Consolidated);
        payload.Ledger.ScopeId.Should().Be("entity-alpha");
    }

    [Fact]
    public async Task GetWorkspaceView_WithoutFundProfileId_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/fund-structure/workspace-view");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetWorkspaceView_WithSelectedLedgerIds_ConstrainsWorkspaceProjection()
    {
        var seed = await SeedFundWorkspaceAsync(["run-selected-001", "run-selected-002", "run-selected-003"]);

        var response = await _client.GetAsync(
            $"/api/fund-structure/workspace-view?fundProfileId={Uri.EscapeDataString(seed.FundProfileId)}&selectedLedgerIds=run-selected-001&selectedLedgerIds=run-selected-003");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<FundOperationsWorkspaceDto>();

        payload.Should().NotBeNull();
        payload!.RecordedRunCount.Should().Be(2);
        payload.RelatedRunIds.Should().BeEquivalentTo(["run-selected-001", "run-selected-003"]);
        payload.Ledger.JournalEntryCount.Should().Be(4);
    }

    [Fact]
    public async Task PreviewReportPack_WithSeededFundProfile_ReturnsPreview()
    {
        var seed = await SeedFundWorkspaceAsync();
        var request = new FundReportPackPreviewRequestDto(
            FundProfileId: seed.FundProfileId,
            ReportKind: GovernanceReportKindDto.TrialBalance,
            AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
            Currency: "USD");

        var response = await _client.PostAsJsonAsync(
            "/api/fund-structure/report-pack-preview",
            request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<FundReportPackPreviewDto>();

        payload.Should().NotBeNull();
        payload!.FundProfileId.Should().Be(seed.FundProfileId);
        payload.DisplayName.Should().Be(seed.DisplayName);
        payload.ReportKind.Should().Be(GovernanceReportKindDto.TrialBalance);
        payload.TrialBalanceLineCount.Should().BeGreaterThan(0);
        payload.AssetClassSectionCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetCashFlowView_WithBlankLedgerGroupId_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/fund-structure/cash-flow-view?scopeKind=LedgerGroup&ledgerGroupId=%20%20%20");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetCashFlowView_WithInvalidLedgerGroupId_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/fund-structure/cash-flow-view?scopeKind=LedgerGroup&ledgerGroupId=BAD/GROUP");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetCashFlowView_WithCanonicalizedUnassignedLedgerGroupId_ReturnsNormalizedScope()
    {
        var response = await _client.GetAsync("/api/fund-structure/cash-flow-view?scopeKind=LedgerGroup&ledgerGroupId=%20UNASSIGNED%20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<GovernanceCashFlowViewDto>();

        payload.Should().NotBeNull();
        payload!.Scope.LedgerGroupId.Should().NotBeNull();
        payload.Scope.LedgerGroupId.GetValueOrDefault().Should().Be(LedgerGroupId.Unassigned);
        payload.Scope.DisplayName.Should().Be(LedgerGroupId.UnassignedValue);
    }

    private async Task<SeededFundWorkspace> SeedFundWorkspaceAsync(IReadOnlyList<string>? runIds = null)
    {
        var fundProfileId = $"fund-endpoint-{Guid.NewGuid():N}";
        var displayName = $"Endpoint Fund {Guid.NewGuid():N}"[..22];
        var fundId = TranslateFundProfileId(fundProfileId);

        var accountService = _fixture.Services.GetRequiredService<IFundAccountService>();
        var repository = _fixture.Services.GetRequiredService<IStrategyRepository>();

        var bankAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Bank,
            AccountCode: $"BANK-{Guid.NewGuid():N}"[..13].ToUpperInvariant(),
            DisplayName: "Endpoint Operating Cash",
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "endpoint-test",
            FundId: fundId,
            LedgerReference: "ENDPOINT-TB",
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
                BeneficiaryAddress: null)))
            ;

        await accountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: bankAccount.AccountId,
            AsOfDate: new DateOnly(2026, 4, 11),
            Currency: "USD",
            CashBalance: 2_500m,
            Source: "endpoint-test",
            RecordedBy: "endpoint-test",
            PendingSettlement: 125m))
            ;

        await accountService.IngestBankStatementAsync(new IngestBankStatementRequest(
            BatchId: Guid.NewGuid(),
            AccountId: bankAccount.AccountId,
            StatementDate: new DateOnly(2026, 4, 11),
            BankName: "Meridian Bank",
            Notes: "endpoint test",
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
                    Reference: "BANK-ENDPOINT-001",
                    ClosingBalance: 2_500m)
            ],
            LoadedBy: "endpoint-test"));

        foreach (var runId in runIds ?? [$"run-endpoint-{Guid.NewGuid():N}"])
        {
            await repository.RecordRunAsync(BuildRun(
                runId: runId,
                strategyId: $"carry-{Guid.NewGuid():N}"[..12],
                strategyName: "Carry Strategy",
                fundProfileId: fundProfileId,
                fundDisplayName: displayName));
        }

        return new SeededFundWorkspace(fundProfileId, displayName, bankAccount.AccountId);
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
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 4, 11, 14, 10, 0, 0, TimeSpan.Zero), "Buy AAPL",
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
        => new(MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId.Trim())));

    private sealed record SeededFundWorkspace(
        string FundProfileId,
        string DisplayName,
        Guid BankAccountId);
}
