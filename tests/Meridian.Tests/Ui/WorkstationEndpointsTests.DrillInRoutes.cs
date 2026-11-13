using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Monitoring;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using IReconciliationApiService = Meridian.Contracts.Reconciliation.IReconciliationApiService;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ReconciliationCaseSummaryDto = Meridian.Contracts.Reconciliation.ReconciliationCaseSummaryDto;
using ReconciliationQueueAccountStatusDto = Meridian.Contracts.Reconciliation.ReconciliationQueueAccountStatusDto;
using StatementImportSummaryDto = Meridian.Contracts.Reconciliation.StatementImportSummaryDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    // -----------------------------------------------------------------------
    // Drill-in route tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MapWorkstationEndpoints_EquityCurveRoute_ShouldReturnCurveForRunWithSnapshots()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var run = StrategyRunDrillInTests_BuildRunWithMultipleSnapshots("drillcurve-1", 50_000m);
        await store.RecordRunAsync(run);

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drillcurve-1/equity-curve");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("runId").GetString().Should().Be("drillcurve-1");
        doc.RootElement.GetProperty("points").GetArrayLength().Should().Be(3);
        doc.RootElement.GetProperty("sharpeRatio").GetDouble().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_EquityCurveRoute_ShouldReturn404ForMissingRun()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewStrategies);
        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/no-such-run/equity-curve");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FillsRoute_ShouldReturnAllFills()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var run = StrategyRunDrillInTests_BuildRunWithFills("drillfills-1", 3);
        await store.RecordRunAsync(run);

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drillfills-1/fills");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("runId").GetString().Should().Be("drillfills-1");
        doc.RootElement.GetProperty("mode").GetString().Should().Be("Backtest");
        doc.RootElement.GetProperty("totalFills").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("fills").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FillsRoute_ShouldFilterBySymbol()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var run = StrategyRunDrillInTests_BuildRunWithFills("drillfills-2", 4);
        await store.RecordRunAsync(run);

        var client = app.GetTestClient();
        // All fills are for MSFT in the helper; querying for AAPL should return 0.
        var response = await client.GetAsync("/api/workstation/runs/drillfills-2/fills?symbol=AAPL");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("totalFills").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("fills").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_AttributionRoute_ShouldReturnSymbolBreakdown()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var run = StrategyRunDrillInTests_BuildRunWithAttribution("drillattr-1");
        await store.RecordRunAsync(run);

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drillattr-1/attribution");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("runId").GetString().Should().Be("drillattr-1");
        doc.RootElement.GetProperty("mode").GetString().Should().Be("Backtest");
        doc.RootElement.GetProperty("bySymbol").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_LedgerTrialBalanceRoute_ShouldReturnAllLines()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("drilltb-1"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drilltb-1/ledger/trial-balance");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_LedgerTrialBalanceRoute_ShouldFilterByAccountType()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("drilltb-2"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drilltb-2/ledger/trial-balance?accountType=Asset");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        // Every returned line must be of accountType Asset
        foreach (var line in doc.RootElement.EnumerateArray())
        {
            line.GetProperty("accountType").GetString().Should().Be("Asset");
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_LedgerTrialBalanceRoute_ShouldFilterByCanonicalDimensions()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("drilltb-dim") with
        {
            FundProfileId = "fund-core",
            ParameterSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["accountScopeId"] = "acct-ops",
                ["entityScopeId"] = "entity-book",
                ["ledgerBookId"] = "book-gaap",
                ["sleeveScopeId"] = "sleeve-alpha",
                ["organizationId"] = "org-ops",
                ["customerId"] = "customer-investor-services",
                ["vendorId"] = "vendor-custodian",
                ["projectId"] = "project-close-automation",
                ["externalGl.Department"] = "InvestmentOps"
            }
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync(
            "/api/workstation/runs/drilltb-dim/ledger/trial-balance?fundId=fund-core&entityId=entity-book&ledgerBookId=book-gaap&sleeveId=sleeve-alpha&strategyId=recon-strategy&portfolioId=recon-portfolio&accountId=acct-ops&organizationId=org-ops&customerId=customer-investor-services&vendorId=vendor-custodian&projectId=project-close-automation&externalGl.Department=InvestmentOps");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var line in doc.RootElement.EnumerateArray())
        {
            var dimensions = line.GetProperty("dimensions");
            dimensions.GetProperty("fundId").GetString().Should().Be("fund-core");
            dimensions.GetProperty("entityId").GetString().Should().Be("entity-book");
            dimensions.GetProperty("bookId").GetString().Should().Be("book-gaap");
            dimensions.GetProperty("sleeveId").GetString().Should().Be("sleeve-alpha");
            dimensions.GetProperty("strategyId").GetString().Should().Be("recon-strategy");
            dimensions.GetProperty("portfolioId").GetString().Should().Be("recon-portfolio");
            dimensions.GetProperty("accountId").GetString().Should().Be("acct-ops");
            dimensions.GetProperty("organizationId").GetString().Should().Be("org-ops");
            dimensions.GetProperty("customerId").GetString().Should().Be("customer-investor-services");
            dimensions.GetProperty("vendorId").GetString().Should().Be("vendor-custodian");
            dimensions.GetProperty("projectId").GetString().Should().Be("project-close-automation");
            dimensions.GetProperty("externalGlDimensions").GetProperty("Department").GetString().Should().Be("InvestmentOps");
        }

        var mismatch = await client.GetAsync(
            "/api/workstation/runs/drilltb-dim/ledger/trial-balance?entityId=entity-other");

        mismatch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var mismatchDoc = await JsonDocument.ParseAsync(await mismatch.Content.ReadAsStreamAsync());
        mismatchDoc.RootElement.GetArrayLength().Should().Be(0);

        var bookMismatch = await client.GetAsync(
            "/api/workstation/runs/drilltb-dim/ledger/trial-balance?bookId=book-tax");

        bookMismatch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var bookMismatchDoc = await JsonDocument.ParseAsync(await bookMismatch.Content.ReadAsStreamAsync());
        bookMismatchDoc.RootElement.GetArrayLength().Should().Be(0);

        var projectMismatch = await client.GetAsync(
            "/api/workstation/runs/drilltb-dim/ledger/trial-balance?projectId=project-tax-close");

        projectMismatch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var projectMismatchDoc = await JsonDocument.ParseAsync(await projectMismatch.Content.ReadAsStreamAsync());
        projectMismatchDoc.RootElement.GetArrayLength().Should().Be(0);

        var externalGlMismatch = await client.GetAsync(
            "/api/workstation/runs/drilltb-dim/ledger/trial-balance?externalGl.Department=FundAccounting");

        externalGlMismatch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var externalGlMismatchDoc = await JsonDocument.ParseAsync(await externalGlMismatch.Content.ReadAsStreamAsync());
        externalGlMismatchDoc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_LedgerJournalRoute_ShouldReturnAllEntries()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("drillj-1"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drillj-1/ledger/journal");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_LedgerJournalRoute_ShouldFilterByCanonicalDimensions()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("drillj-dim") with
        {
            FundProfileId = "fund-core",
            ParameterSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["accountScopeId"] = "acct-ops",
                ["entityScopeId"] = "entity-book",
                ["bookId"] = "book-gaap",
                ["sleeveScopeId"] = "sleeve-alpha",
                ["organizationId"] = "org-ops",
                ["customerId"] = "customer-investor-services",
                ["vendorId"] = "vendor-custodian",
                ["projectId"] = "project-close-automation",
                ["externalGl.Department"] = "InvestmentOps"
            }
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync(
            "/api/workstation/runs/drillj-dim/ledger/journal?fundId=fund-core&entityId=entity-book&bookId=book-gaap&sleeveId=sleeve-alpha&strategyId=recon-strategy&portfolioId=recon-portfolio&accountId=acct-ops&organizationId=org-ops&customerId=customer-investor-services&vendorId=vendor-custodian&projectId=project-close-automation&externalGlDimensionKey=Department&externalGlDimensionValue=InvestmentOps");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var line in doc.RootElement.EnumerateArray())
        {
            var dimensions = line.GetProperty("dimensions");
            dimensions.GetProperty("fundId").GetString().Should().Be("fund-core");
            dimensions.GetProperty("entityId").GetString().Should().Be("entity-book");
            dimensions.GetProperty("bookId").GetString().Should().Be("book-gaap");
            dimensions.GetProperty("sleeveId").GetString().Should().Be("sleeve-alpha");
            dimensions.GetProperty("strategyId").GetString().Should().Be("recon-strategy");
            dimensions.GetProperty("portfolioId").GetString().Should().Be("recon-portfolio");
            dimensions.GetProperty("accountId").GetString().Should().Be("acct-ops");
            dimensions.GetProperty("organizationId").GetString().Should().Be("org-ops");
            dimensions.GetProperty("customerId").GetString().Should().Be("customer-investor-services");
            dimensions.GetProperty("vendorId").GetString().Should().Be("vendor-custodian");
            dimensions.GetProperty("projectId").GetString().Should().Be("project-close-automation");
            dimensions.GetProperty("externalGlDimensions").GetProperty("Department").GetString().Should().Be("InvestmentOps");
        }

        var mismatch = await client.GetAsync(
            "/api/workstation/runs/drillj-dim/ledger/journal?fundId=fund-other");

        mismatch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var mismatchDoc = await JsonDocument.ParseAsync(await mismatch.Content.ReadAsStreamAsync());
        mismatchDoc.RootElement.GetArrayLength().Should().Be(0);

        var bookMismatch = await client.GetAsync(
            "/api/workstation/runs/drillj-dim/ledger/journal?ledgerBookId=book-tax");

        bookMismatch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var bookMismatchDoc = await JsonDocument.ParseAsync(await bookMismatch.Content.ReadAsStreamAsync());
        bookMismatchDoc.RootElement.GetArrayLength().Should().Be(0);

        var vendorMismatch = await client.GetAsync(
            "/api/workstation/runs/drillj-dim/ledger/journal?vendorId=vendor-admin");

        vendorMismatch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var vendorMismatchDoc = await JsonDocument.ParseAsync(await vendorMismatch.Content.ReadAsStreamAsync());
        vendorMismatchDoc.RootElement.GetArrayLength().Should().Be(0);

        var externalGlMismatch = await client.GetAsync(
            "/api/workstation/runs/drillj-dim/ledger/journal?externalGlDimensionKey=Department&externalGlDimensionValue=FundAccounting");

        externalGlMismatch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var externalGlMismatchDoc = await JsonDocument.ParseAsync(await externalGlMismatch.Content.ReadAsStreamAsync());
        externalGlMismatchDoc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_LedgerJournalRoute_ShouldFilterByFromDate()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("drillj-2"));

        var client = app.GetTestClient();
        // Use a future date; all ledger entries are at 2026-03-21, so nothing should match.
        var future = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var encoded = Uri.EscapeDataString(future.ToString("O"));
        var response = await client.GetAsync($"/api/workstation/runs/drillj-2/ledger/journal?from={encoded}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_GetStrategyRunsRoute_ShouldReturnRunsForStrategy()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun("strat-run-1", "strat-alpha", "Alpha Strategy", RunType.Backtest, DateTimeOffset.UtcNow.AddHours(-2)));
        await store.RecordRunAsync(BuildRun("strat-run-2", "strat-alpha", "Alpha Strategy", RunType.Paper, DateTimeOffset.UtcNow.AddHours(-1)));
        await store.RecordRunAsync(BuildRun("strat-run-3", "strat-beta", "Beta Strategy", RunType.Backtest, DateTimeOffset.UtcNow));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/strategies/strat-alpha/runs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var ids = doc.RootElement.EnumerateArray().Select(r => r.GetProperty("runId").GetString()).ToArray();
        ids.Should().Contain("strat-run-1").And.Contain("strat-run-2");
        ids.Should().NotContain("strat-run-3");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_GetStrategyRunsRoute_ShouldFilterByType()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun("typed-run-1", "strat-gamma", "Gamma Strategy", RunType.Backtest, DateTimeOffset.UtcNow.AddHours(-2)));
        await store.RecordRunAsync(BuildRun("typed-run-2", "strat-gamma", "Gamma Strategy", RunType.Paper, DateTimeOffset.UtcNow.AddHours(-1)));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/strategies/strat-gamma/runs?type=Backtest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var ids = doc.RootElement.EnumerateArray().Select(r => r.GetProperty("runId").GetString()).ToArray();
        ids.Should().Contain("typed-run-1");
        ids.Should().NotContain("typed-run-2");
    }

    // Helper shims to reuse StrategyRunDrillInTests factory logic directly

    private static StrategyRunEntry StrategyRunDrillInTests_BuildRunWithMultipleSnapshots(string runId, decimal initialEquity)
    {
        var startedAt = new DateTimeOffset(2026, 1, 2, 9, 30, 0, TimeSpan.Zero);
        var equities = new[] { initialEquity, initialEquity * 0.95m, initialEquity * 0.98m };
        var snapshots = equities
            .Select((eq, i) => new PortfolioSnapshot(
                Timestamp: startedAt.AddDays(i),
                Date: DateOnly.FromDateTime(startedAt.AddDays(i).UtcDateTime),
                Cash: eq * 0.3m,
                MarginBalance: 0m,
                LongMarketValue: eq * 0.7m,
                ShortMarketValue: 0m,
                TotalEquity: eq,
                DailyReturn: 0m,
                Positions: new Dictionary<string, Position>(),
                Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
                DayCashFlows: []))
            .ToArray();

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(startedAt.AddDays(2).UtcDateTime),
            Symbols: ["SPY"],
            InitialCash: initialEquity);

        var metrics = new BacktestMetrics(
            InitialCapital: initialEquity, FinalEquity: equities[^1],
            GrossPnl: equities[^1] - initialEquity + 50m, NetPnl: equities[^1] - initialEquity,
            TotalReturn: (equities[^1] - initialEquity) / initialEquity,
            AnnualizedReturn: 0.06m, SharpeRatio: 1.1, SortinoRatio: 1.3, CalmarRatio: 0.8,
            MaxDrawdown: initialEquity * 0.05m, MaxDrawdownPercent: 0.05m, MaxDrawdownRecoveryDays: 5,
            ProfitFactor: 1.4, WinRate: 0.55, TotalTrades: 6, WinningTrades: 4, LosingTrades: 2,
            TotalCommissions: 50m, TotalMarginInterest: 10m, TotalShortRebates: 2m, Xirr: 0.07,
            SymbolAttribution: new Dictionary<string, Meridian.Backtesting.Sdk.SymbolAttribution>
            {
                ["SPY"] = new("SPY", equities[^1] - initialEquity, 0m, 6, 50m, 8m)
            });

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SPY" },
            Snapshots: snapshots, CashFlows: [], Fills: [], Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(30), TotalEventsProcessed: 300);

        return new StrategyRunEntry(
            RunId: runId, StrategyId: "curve-strat", StrategyName: "Curve Strategy",
            RunType: RunType.Backtest, StartedAt: startedAt, EndedAt: startedAt.AddDays(3),
            Metrics: result);
    }

    private static StrategyRunEntry StrategyRunDrillInTests_BuildRunWithFills(string runId, int fillCount)
    {
        var startedAt = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
        var fills = Enumerable.Range(0, fillCount)
            .Select(i => new FillEvent(
                FillId: Guid.NewGuid(), OrderId: Guid.NewGuid(), Symbol: "MSFT",
                FilledQuantity: 10L, FillPrice: 350m + i, Commission: 1.00m,
                FilledAt: startedAt.AddMinutes(i * 5), AccountId: "default-brokerage"))
            .ToArray();

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(startedAt.AddDays(1).UtcDateTime),
            Symbols: ["MSFT"], InitialCash: 100_000m);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m, FinalEquity: 101_000m, GrossPnl: 1_010m, NetPnl: 1_000m,
            TotalReturn: 0.01m, AnnualizedReturn: 0.12m, SharpeRatio: 1.5, SortinoRatio: 1.8,
            CalmarRatio: 0.9, MaxDrawdown: 200m, MaxDrawdownPercent: 0.002m, MaxDrawdownRecoveryDays: 2,
            ProfitFactor: 2.0, WinRate: 0.70, TotalTrades: fillCount, WinningTrades: fillCount,
            LosingTrades: 0, TotalCommissions: fillCount * 1.00m, TotalMarginInterest: 0m,
            TotalShortRebates: 0m, Xirr: 0.10,
            SymbolAttribution: new Dictionary<string, Meridian.Backtesting.Sdk.SymbolAttribution>
            {
                ["MSFT"] = new("MSFT", 1_000m, 0m, fillCount, fillCount * 1.00m, 0m)
            });

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MSFT" },
            Snapshots: [], CashFlows: [], Fills: fills, Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(5), TotalEventsProcessed: 100);

        return new StrategyRunEntry(
            RunId: runId, StrategyId: "fill-strat", StrategyName: "Fill Strategy",
            RunType: RunType.Backtest, StartedAt: startedAt, EndedAt: startedAt.AddDays(1),
            Metrics: result);
    }

    private static StrategyRunEntry StrategyRunDrillInTests_BuildRunWithAttribution(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);
        var attribution = new Dictionary<string, Meridian.Backtesting.Sdk.SymbolAttribution>
        {
            ["AAPL"] = new("AAPL", 5_000m, 1_200m, 8, 45m, 20m),
            ["MSFT"] = new("MSFT", 3_000m, -200m, 4, 22m, 10m),
            ["SPY"] = new("SPY", 1_000m, 500m, 2, 10m, 5m)
        };

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(startedAt.AddMonths(1).UtcDateTime),
            Symbols: ["AAPL", "MSFT", "SPY"], InitialCash: 100_000m);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m, FinalEquity: 110_500m, GrossPnl: 10_577m, NetPnl: 10_500m,
            TotalReturn: 0.105m, AnnualizedReturn: 0.42m, SharpeRatio: 2.1, SortinoRatio: 2.5,
            CalmarRatio: 1.2, MaxDrawdown: 1_500m, MaxDrawdownPercent: 0.015m, MaxDrawdownRecoveryDays: 4,
            ProfitFactor: 3.2, WinRate: 0.75, TotalTrades: 14, WinningTrades: 11, LosingTrades: 3,
            TotalCommissions: 77m, TotalMarginInterest: 0m, TotalShortRebates: 0m, Xirr: 0.40,
            SymbolAttribution: attribution);

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL", "MSFT", "SPY" },
            Snapshots: [], CashFlows: [], Fills: [], Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromMinutes(2), TotalEventsProcessed: 500);

        return new StrategyRunEntry(
            RunId: runId, StrategyId: "attr-strat", StrategyName: "Attribution Strategy",
            RunType: RunType.Backtest, StartedAt: startedAt, EndedAt: startedAt.AddMonths(1),
            Metrics: result);
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

    private sealed class StubSecurityMasterQueryService :
        ISecurityMasterQueryService
    {
        private readonly Dictionary<Guid, SecurityDetailDto> _details = [];
        private readonly Dictionary<Guid, IReadOnlyList<SecurityMasterEventEnvelope>> _history = [];
        private readonly Dictionary<Guid, SecurityEconomicDefinitionRecord> _economicDefinitions = [];
        private readonly Dictionary<Guid, TradingParametersDto> _tradingParameters = [];
        private readonly Dictionary<Guid, IReadOnlyList<CorporateActionDto>> _corporateActions = [];
        private readonly List<SecuritySummaryDto> _summaries = [];

        public void Register(SecuritySummaryDto summary, SecurityDetailDto detail)
            => RegisterSecurity(summary, detail);

        public void RegisterSecurity(SecuritySummaryDto summary, SecurityDetailDto detail)
        {
            _summaries.Add(summary);
            _details[detail.SecurityId] = detail;
        }

        public void RegisterEconomicDefinition(Guid securityId, SecurityEconomicDefinitionRecord record)
        {
            _economicDefinitions[securityId] = record;
        }

        public void RegisterTradingParameters(Guid securityId, TradingParametersDto tradingParameters)
        {
            _tradingParameters[securityId] = tradingParameters;
        }

        public void RegisterCorporateActions(Guid securityId, IReadOnlyList<CorporateActionDto> corporateActions)
        {
            _corporateActions[securityId] = corporateActions;
        }

        public void RegisterHistory(Guid securityId, IReadOnlyList<SecurityMasterEventEnvelope> history)
        {
            _history[securityId] = history;
        }

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
        {
            _details.TryGetValue(securityId, out var detail);
            return Task.FromResult<SecurityDetailDto?>(detail);
        }

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
            => GetByIdAsync(securityId, ct);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null)
        {
            var detail = _details.Values.FirstOrDefault(item =>
                item.Identifiers.Any(id =>
                    id.Kind == identifierKind &&
                    string.Equals(id.Value, identifierValue, StringComparison.OrdinalIgnoreCase)));
            return Task.FromResult<SecurityDetailDto?>(detail);
        }

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
        {
            var rows = _summaries
                .Where(item =>
                    item.DisplayName.Contains(request.Query, StringComparison.OrdinalIgnoreCase) ||
                    item.PrimaryIdentifier.Contains(request.Query, StringComparison.OrdinalIgnoreCase))
                .Take(request.Take)
                .ToArray();
            return Task.FromResult<IReadOnlyList<SecuritySummaryDto>>(rows);
        }

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
        {
            if (!_history.TryGetValue(request.SecurityId, out var history))
            {
                return Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(Array.Empty<SecurityMasterEventEnvelope>());
            }

            var rows = history.Take(request.Take).ToArray();
            return Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(rows);
        }

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
        {
            _economicDefinitions.TryGetValue(securityId, out var record);
            return Task.FromResult<SecurityEconomicDefinitionRecord?>(record);
        }

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
        {
            _tradingParameters.TryGetValue(securityId, out var tradingParameters);
            return Task.FromResult<TradingParametersDto?>(tradingParameters);
        }

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
        {
            if (_corporateActions.TryGetValue(securityId, out var corporateActions))
            {
                return Task.FromResult(corporateActions);
            }

            return Task.FromResult<IReadOnlyList<CorporateActionDto>>(Array.Empty<CorporateActionDto>());
        }

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);
        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }

    private sealed class StubSecurityMasterConflictService : ISecurityMasterConflictService
    {
        private readonly Dictionary<Guid, SecurityMasterConflict> _conflicts;

        public StubSecurityMasterConflictService(IReadOnlyList<SecurityMasterConflict> conflicts)
        {
            _conflicts = conflicts.ToDictionary(conflict => conflict.ConflictId);
        }

        public List<ResolveConflictRequest> ResolvedRequests { get; } = [];

        public Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SecurityMasterConflict>>(
                _conflicts.Values
                    .Where(conflict => string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(conflict => conflict.DetectedAt)
                    .ToArray());

        public Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct)
        {
            _conflicts.TryGetValue(conflictId, out var conflict);
            return Task.FromResult<SecurityMasterConflict?>(conflict);
        }

        public Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct)
        {
            ResolvedRequests.Add(request);
            if (!_conflicts.TryGetValue(request.ConflictId, out var existing))
            {
                return Task.FromResult<SecurityMasterConflict?>(null);
            }

            var updated = existing with
            {
                Status = string.Equals(request.Resolution, "Dismiss", StringComparison.OrdinalIgnoreCase)
                    ? "Dismissed"
                    : "Resolved"
            };
            _conflicts[request.ConflictId] = updated;
            return Task.FromResult<SecurityMasterConflict?>(updated);
        }

        public Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct)
            => Task.CompletedTask;

        public Task RecordFieldConflictsAsync(SecurityProjectionRecord previous, SecurityProjectionRecord incoming, CancellationToken ct)
            => Task.CompletedTask;

        public Task ReconcileOpenFieldConflictsAsync(SecurityProjectionRecord persisted, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class RecordingLedgerJournalStore : ILedgerJournalStore
    {
        public List<LedgerJournalEntryWrite> Appended { get; } = [];
        private readonly Dictionary<Guid, DateTimeOffset> _recordedAt = [];

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.Entry.IsBalanced)
            {
                throw new LedgerValidationException("Journal entry must be balanced.");
            }

            Appended.Add(entry);
            _recordedAt[entry.Entry.JournalEntryId] = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryAsync(LedgerJournalEntryQuery query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(Appended
                .Select((entry, index) => (entry, sequence: index + 1L))
                .Where(item => item.entry.PeriodId == query.PeriodId && item.entry.LedgerBookId == query.LedgerBookId)
                .Select(item => new LedgerJournalEntryRecord(item.entry.Entry, item.entry.AggregateId,
                    item.entry.PeriodId, item.entry.CommandId, item.entry.CorrelationId, item.sequence,
                    _recordedAt[item.entry.Entry.JournalEntryId], item.entry.AccountingBasis)).ToArray());
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);
        }

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerAccountingPeriod?>(null);
        }

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>([]);
        }

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(period);
        }

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerBookRecord?>(null);
        }

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerBookRecord>>([]);
        }

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(book);
        }
    }

    private sealed class StubReconciliationApiService : IReconciliationApiService
    {
        private static readonly StatementRunSummaryDto StatementRun = new(
            RunId: "statement-run-1",
            ImportId: "import-1",
            Status: StatementRunStatus.ReviewRequired,
            StartedAtUtc: new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero),
            CompletedAtUtc: new DateTimeOffset(2026, 5, 27, 12, 1, 0, TimeSpan.Zero),
            PositionMatches: 3,
            CashMatches: 2,
            TransactionMatches: 1,
            OpenExceptionCount: 1);

        public List<StatementRunCreateDto> CreatedRequests { get; } = [];
        public List<string> ReconciledRunIds { get; } = [];
        public List<(string RunId, StatementRunReconcileRequestDto Request)> ReconciledRequests { get; } = [];
        public List<ReconciliationBreakQueueScope> ObservedScopes { get; } = [];
        public int ListOpenStatementBreaksCallCount { get; private set; }

        public Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            return Task.FromResult<IReadOnlyList<StatementImportSummaryDto>>(
            [
                new(
                    ImportId: "import-1",
                    Broker: "custodian",
                    StatementDate: "2026-05-27",
                    ImportedAtUtc: "2026-05-27T12:00:00Z",
                    RawRowCount: 8,
                    NormalizedRowCount: 6)
            ]);
        }

        public Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            return Task.FromResult<IReadOnlyList<StatementRunSummaryDto>>([StatementRun]);
        }

        public Task<StatementRunDto?> CreateStatementRunAsync(
            StatementRunCreateDto request,
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            CreatedRequests.Add(request);
            return Task.FromResult<StatementRunDto?>(BuildRunDto("created-statement-run", StatementRunStatus.Completed));
        }

        public Task<bool> OwnsStatementRunAsync(
            string runId,
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            return Task.FromResult(string.Equals(runId, StatementRun.RunId, StringComparison.OrdinalIgnoreCase));
        }

        public Task<StatementRunDto?> GetStatementRunAsync(
            string runId,
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            return Task.FromResult(
                string.Equals(runId, StatementRun.RunId, StringComparison.OrdinalIgnoreCase)
                    ? BuildRunDto(StatementRun.RunId, StatementRun.Status)
                    : null);
        }

        public Task<StatementRunValidationDto?> GetStatementRunValidationAsync(
            string runId,
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            return Task.FromResult<StatementRunValidationDto?>(
                string.Equals(runId, StatementRun.RunId, StringComparison.OrdinalIgnoreCase)
                    ? new StatementRunValidationDto(runId, [], IsBlocked: false)
                    : null);
        }

        public Task<IReadOnlyList<StatementRunBreakDto>?> ListStatementRunBreaksAsync(
            string runId,
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            return Task.FromResult<IReadOnlyList<StatementRunBreakDto>?>(
                string.Equals(runId, StatementRun.RunId, StringComparison.OrdinalIgnoreCase)
                    ? [BuildBreakDto()]
                    : null);
        }

        public Task<StatementRunDto?> ReconcileStatementRunAsync(
            string runId,
            StatementRunReconcileRequestDto request,
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            ReconciledRunIds.Add(runId);
            ReconciledRequests.Add((runId, request));
            return Task.FromResult<StatementRunDto?>(
                string.Equals(runId, StatementRun.RunId, StringComparison.OrdinalIgnoreCase)
                    ? BuildRunDto(runId, StatementRunStatus.ReviewRequired)
                    : null);
        }

        public Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            return Task.FromResult<IReadOnlyList<StatementRunExceptionDto>>(
            [
                new(
                    BreakId: "break-1",
                    RunId: StatementRun.RunId,
                    ImportId: StatementRun.ImportId,
                    SourceReference: "row-42",
                    BreakCode: "cash-delta",
                    Category: "Cash",
                    Delta: 10m,
                    Tolerance: 1m,
                    ToleranceBreached: true,
                    CreatedAtUtc: "2026-05-27T12:02:00Z",
                    Status: "Open")
            ]);
        }

        public Task<IReadOnlyList<StatementBreakDto>> ListOpenStatementBreaksAsync(
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            ListOpenStatementBreaksCallCount++;
            var run = BuildRunDto(StatementRun.RunId, StatementRun.Status);
            var statementBreak = run.Breaks!.Single() with
            {
                StatementReference = $"{StatementRun.ImportId}:row-42",
                InternalReference = StatementRun.RunId,
                Owner = "statement-owner",
                LastObservedAtUtc = new DateTimeOffset(2026, 5, 27, 12, 2, 0, TimeSpan.Zero),
                RecommendedAction = "ReviewAndResolve",
                EvidenceLink = "/api/workstation/reconciliation/exceptions/break-1"
            };
            return Task.FromResult<IReadOnlyList<StatementBreakDto>>([statementBreak]);
        }


        private static StatementRunDto BuildRunDto(string runId, StatementRunStatus status) => new(
            RunId: runId,
            Status: status,
            Source: new StatementSourceDto(
                SourceId: "import-1",
                SourceKind: "custodian",
                SourceSystem: "Sample Custodian",
                AccountId: "fund-1",
                AccountName: "External Account",
                Currency: "USD",
                PeriodStart: new DateOnly(2026, 5, 1),
                PeriodEnd: new DateOnly(2026, 5, 27)),
            StartedAtUtc: StatementRun.StartedAtUtc,
            CompletedAtUtc: StatementRun.CompletedAtUtc,
            SourceFileName: "statement.csv",
            SourceFileHash: "ABC123",
            MappingProfileId: "mapping-v1",
            MappingProfileVersion: 1,
            ToleranceProfileId: "tolerance-v1",
            ToleranceProfileVersion: 1,
            ImportedBy: "ops-user",
            ImportedAtUtc: StatementRun.StartedAtUtc,
            ValidationIssues: [],
            Positions: [],
            CashBalances: [],
            Transactions: [],
            MatchSummary: new StatementMatchSummaryDto(6, 5, 5, 0, 1, 1, 0, 1, 0),
            Breaks: [new StatementBreakDto(
                BreakId: "break-1",
                BreakType: StatementBreakType.CashBalanceMismatch,
                Severity: StatementValidationSeverity.Error,
                MatchTier: StatementMatchTier.Unmatched,
                StatementReference: "row-42",
                Description: "Cash delta",
                StatementAmount: 10m,
                BookAmount: 20m,
                Delta: 10m,
                Tolerance: 1m,
                Currency: "USD",
                CreatedAtUtc: new DateTimeOffset(2026, 5, 27, 12, 2, 0, TimeSpan.Zero),
                Status: "Open")],
            Cases: [],
            ImportId: "import-1",
            FundProfileId: "fund-1");

        private static StatementRunBreakDto BuildBreakDto() => new(
            BreakId: "break-1",
            RunId: StatementRun.RunId,
            ImportId: StatementRun.ImportId,
            SourceReference: "row-42",
            BreakType: StatementBreakType.CashBalanceMismatch,
            Category: "Cash",
            Delta: 10m,
            Tolerance: 1m,
            ToleranceBreached: true,
            CreatedAtUtc: new DateTimeOffset(2026, 5, 27, 12, 2, 0, TimeSpan.Zero),
            Status: "Open");

        public Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            return Task.FromResult<IReadOnlyList<ReconciliationCaseSummaryDto>>([]);
        }

        public Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(
            ReconciliationBreakQueueScope accessScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObservedScopes.Add(accessScope);
            return Task.FromResult<IReadOnlyList<ReconciliationQueueAccountStatusDto>>([]);
        }
    }

    private sealed class StubSecurityMasterIngestStatusService : ISecurityMasterIngestStatusService
    {
        public SecurityMasterIngestStatusSnapshot GetSnapshot()
            => new(
                ActiveImport: null,
                LastCompleted: null);
    }
}
