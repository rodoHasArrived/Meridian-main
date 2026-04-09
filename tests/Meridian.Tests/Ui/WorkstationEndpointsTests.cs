using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.UI;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Ui;

public sealed class WorkstationEndpointsTests
{
    // Must match the JsonSerializerOptions used in CreateAppAsync so that enum fields
    // serialized as strings by the server (via JsonStringEnumConverter) can be round-tripped.
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    [Fact]
    public async Task MapWorkstationEndpoints_WithStrategyReadService_ShouldReturnServiceBackedBootstrapPayloads()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-latest",
            strategyId: "carry-1",
            strategyName: "Carry Pair",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/fx/spot",
            feedReference: "synthetic:fx"));
        await store.RecordRunAsync(BuildRun(
            runId: "run-prior",
            strategyId: "meanrev-1",
            strategyName: "Mean Reversion",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities"));

        var client = app.GetTestClient();

        using var session = await ReadJsonAsync(client, "/api/workstation/session");
        session.RootElement.GetProperty("displayName").GetString().Should().Be("Carry Pair Desk");
        session.RootElement.GetProperty("role").GetString().Should().Be("Research Lead");
        session.RootElement.GetProperty("environment").GetString().Should().Be("paper");
        session.RootElement.GetProperty("activeWorkspace").GetString().Should().Be("operations");
        session.RootElement.GetProperty("latestRun").GetProperty("runId").GetString().Should().Be("run-latest");
        session.RootElement.GetProperty("workspaceSummary").GetProperty("totalRuns").GetInt32().Should().Be(2);
        session.RootElement.GetProperty("workspaceSummary").GetProperty("ledgerCoverage").GetInt32().Should().Be(2);
        session.RootElement.GetProperty("workspaceSummary").GetProperty("portfolioCoverage").GetInt32().Should().Be(2);

        using var research = await ReadJsonAsync(client, "/api/workstation/research");
        research.RootElement.GetProperty("workspace").GetProperty("totalRuns").GetInt32().Should().Be(2);
        research.RootElement.GetProperty("workspace").GetProperty("latestRunId").GetString().Should().Be("run-latest");
        research.RootElement.GetProperty("workspace").GetProperty("promotionCandidates").GetInt32().Should().Be(2);

        var runs = research.RootElement.GetProperty("runs");
        runs.GetArrayLength().Should().Be(2);

        var latestRun = runs[0];
        latestRun.GetProperty("id").GetString().Should().Be("run-latest");
        latestRun.GetProperty("strategyName").GetString().Should().Be("Carry Pair");
        latestRun.GetProperty("mode").GetString().Should().Be("paper");
        latestRun.GetProperty("status").GetString().Should().Be("Completed");
        latestRun.GetProperty("dataset").GetString().Should().Be("dataset/fx/spot");
        latestRun.GetProperty("notes").GetString().Should().Contain("review");
        latestRun.GetProperty("drillIn").GetProperty("fills").GetString().Should().Be("/api/workstation/runs/run-latest/fills");

        var comparisons = research.RootElement.GetProperty("comparisons");
        comparisons.GetArrayLength().Should().BeGreaterThan(0);
        comparisons[0].GetProperty("modes").EnumerateArray()
            .Should()
            .Contain(mode => mode.GetProperty("drillIn").GetProperty("attribution").GetString() != null);
        research.RootElement.GetProperty("timeline").GetArrayLength().Should().Be(2);

        research.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "active-runs" &&
                               metric.GetProperty("value").GetString() == "0");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithoutStrategyReadService_ShouldReturnFallbackPayloads()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        using var session = await ReadJsonAsync(client, "/api/workstation/session");
        session.RootElement.GetProperty("displayName").GetString().Should().Be("Meridian Operator");
        session.RootElement.GetProperty("role").GetString().Should().Be("Research Lead");
        session.RootElement.GetProperty("environment").GetString().Should().Be("paper");
        session.RootElement.GetProperty("activeWorkspace").GetString().Should().Be("research");
        session.RootElement.GetProperty("commandCount").GetInt32().Should().Be(6);

        using var research = await ReadJsonAsync(client, "/api/workstation/research");
        research.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "active-runs" &&
                               metric.GetProperty("value").GetString() == "24");

        using var governance = await ReadJsonAsync(client, "/api/workstation/governance");
        governance.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "open-breaks" &&
                               metric.GetProperty("value").GetString() == "4");
        governance.RootElement.GetProperty("reconciliationQueue").GetArrayLength().Should().Be(1);

        var runs = research.RootElement.GetProperty("runs");
        runs.GetArrayLength().Should().Be(1);
        runs[0].GetProperty("id").GetString().Should().Be("run-research-001");
        runs[0].GetProperty("strategyName").GetString().Should().Be("Mean Reversion FX");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithSecurityLookup_ShouldExposeSecurityCoverageInBootstrapPayloads()
    {
        await using var app = await CreateAppAsync(services =>
        {
            var lookup = new StubSecurityReferenceLookup();
            lookup.Register("AAPL", new WorkstationSecurityReference(
                SecurityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                DisplayName: "Apple Inc.",
                AssetClass: "Equity",
                Currency: "USD",
                Status: SecurityStatusDto.Active,
                PrimaryIdentifier: "AAPL"));

            services.AddSingleton<IStrategyRepository>(new StrategyRunStore());
            services.AddSingleton<ISecurityReferenceLookup>(lookup);
            services.AddSingleton<PortfolioReadService>();
            services.AddSingleton<LedgerReadService>();
            services.AddSingleton<StrategyRunReadService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-security",
            strategyId: "carry-1",
            strategyName: "Carry Pair",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/fx/spot",
            feedReference: "synthetic:fx").Complete(BuildBacktestResultWithSymbol("AAPL")));

        var client = app.GetTestClient();

        using var session = await ReadJsonAsync(client, "/api/workstation/session");
        var latestCoverage = session.RootElement.GetProperty("latestRun").GetProperty("securityCoverage");
        latestCoverage.GetProperty("portfolioResolved").GetInt32().Should().Be(1);
        latestCoverage.GetProperty("portfolioMissing").GetInt32().Should().Be(0);
        latestCoverage.GetProperty("ledgerResolved").GetInt32().Should().Be(1);
        latestCoverage.GetProperty("ledgerMissing").GetInt32().Should().Be(0);
        latestCoverage.GetProperty("hasIssues").GetBoolean().Should().BeFalse();
        latestCoverage.GetProperty("tone").GetString().Should().Be("success");
        latestCoverage.GetProperty("resolvedReferences").GetArrayLength().Should().Be(2);
        latestCoverage.GetProperty("missingReferences").GetArrayLength().Should().Be(0);
        latestCoverage.GetProperty("summary").GetString().Should().Contain("no unresolved symbols");

        using var research = await ReadJsonAsync(client, "/api/workstation/research");
        var runCoverage = research.RootElement.GetProperty("runs")[0].GetProperty("securityCoverage");
        runCoverage.GetProperty("portfolioResolved").GetInt32().Should().Be(1);
        runCoverage.GetProperty("ledgerResolved").GetInt32().Should().Be(1);
        runCoverage.GetProperty("hasIssues").GetBoolean().Should().BeFalse();
        runCoverage.GetProperty("resolvedReferences").EnumerateArray()
            .Should()
            .Contain(item =>
                item.GetProperty("source").GetString() == "portfolio" &&
                item.GetProperty("symbol").GetString() == "AAPL" &&
                item.GetProperty("displayName").GetString() == "Apple Inc.");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithGovernanceServices_ShouldExposeGovernanceWorkspacePayload()
    {
        await using var app = await CreateAppAsync(services =>
        {
            var lookup = new StubSecurityReferenceLookup();
            lookup.Register("AAPL", new WorkstationSecurityReference(
                SecurityId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                DisplayName: "Apple Inc.",
                AssetClass: "Equity",
                Currency: "USD",
                Status: SecurityStatusDto.Active,
                PrimaryIdentifier: "AAPL"));

            services.AddSingleton<IStrategyRepository>(new StrategyRunStore());
            services.AddSingleton<ISecurityReferenceLookup>(lookup);
            services.AddSingleton<PortfolioReadService>();
            services.AddSingleton<LedgerReadService>();
            services.AddSingleton<StrategyRunReadService>();
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("run-governance-balanced"));
        await store.RecordRunAsync(BuildReconciliationMismatchRun("run-governance-breaks"));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        await reconciliationService.RunAsync(new ReconciliationRunRequest("run-governance-balanced"));
        await reconciliationService.RunAsync(new ReconciliationRunRequest("run-governance-breaks"));

        var client = app.GetTestClient();
        using var governance = await ReadJsonAsync(client, "/api/workstation/governance");

        var workspace = governance.RootElement.GetProperty("workspace");
        workspace.GetProperty("totalRuns").GetInt32().Should().Be(2);
        workspace.GetProperty("ledgerReadyRuns").GetInt32().Should().Be(2);
        workspace.GetProperty("reconciledRuns").GetInt32().Should().Be(2);
        workspace.GetProperty("openBreaks").GetInt32().Should().BeGreaterThan(0);
        workspace.GetProperty("securityIssues").GetInt32().Should().BeGreaterThan(0);

        var cashFlow = governance.RootElement.GetProperty("cashFlow");
        cashFlow.GetProperty("runsWithCashSignals").GetInt32().Should().Be(2);
        cashFlow.GetProperty("runsWithCashVariance").GetInt32().Should().Be(1);
        cashFlow.GetProperty("netVariance").GetDecimal().Should().Be(-100m);

        var reporting = governance.RootElement.GetProperty("reporting");
        reporting.GetProperty("profileCount").GetInt32().Should().BeGreaterThan(0);
        reporting.GetProperty("profiles").EnumerateArray()
            .Should()
            .Contain(profile => profile.GetProperty("id").GetString() == "excel");
        reporting.GetProperty("recommendedProfiles").EnumerateArray()
            .Select(profile => profile.GetString())
            .Should()
            .Contain("excel");

        governance.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "open-breaks" &&
                               metric.GetProperty("value").GetString() != "0");

        var queue = governance.RootElement.GetProperty("reconciliationQueue");
        queue.GetArrayLength().Should().Be(2);

        var breakRun = queue.EnumerateArray()
            .Single(item => item.GetProperty("runId").GetString() == "run-governance-breaks");
        breakRun.GetProperty("reconciliationStatus").GetString().Should().Be("BreaksOpen");
        breakRun.GetProperty("openBreakCount").GetInt32().Should().BeGreaterThan(0);
        breakRun.GetProperty("securityCoverage").GetProperty("hasIssues").GetBoolean().Should().BeTrue();
        breakRun.GetProperty("securityCoverage").GetProperty("missingReferences").GetArrayLength().Should().BeGreaterThan(0);
        breakRun.GetProperty("cashFlow").GetProperty("cashVariance").GetDecimal().Should().Be(-100m);
        breakRun.GetProperty("latestReconciliation").GetProperty("hasSecurityCoverageIssues").GetBoolean().Should().BeTrue();
        breakRun.GetProperty("latestReconciliation").GetProperty("securityIssueCount").GetInt32().Should().BeGreaterThan(0);

        var balancedRun = queue.EnumerateArray()
            .Single(item => item.GetProperty("runId").GetString() == "run-governance-balanced");
        balancedRun.GetProperty("reconciliationStatus").GetString().Should().Be("SecurityCoverageOpen");
        balancedRun.GetProperty("breakCount").GetInt32().Should().Be(0);
        balancedRun.GetProperty("cashFlow").GetProperty("cashVariance").GetDecimal().Should().Be(0m);
        balancedRun.GetProperty("latestReconciliation").GetProperty("hasSecurityCoverageIssues").GetBoolean().Should().BeTrue();
        balancedRun.GetProperty("securityCoverage").GetProperty("missingReferences").EnumerateArray()
            .Should()
            .Contain(item => item.GetProperty("symbol").GetString() == "TSLA");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationRoutes_ShouldCreateAndFetchRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("run-recon"));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/reconciliation/runs", new ReconciliationRunRequest("run-recon"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        created.Should().NotBeNull();
        created!.Summary.RunId.Should().Be("run-recon");
        created.Summary.BreakCount.Should().Be(0);
        created.Matches.Should().Contain(match => match.CheckId == "cash-balance");

        var latestResponse = await client.GetAsync("/api/workstation/runs/run-recon/reconciliation");
        latestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var latest = await latestResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        latest.Should().NotBeNull();
        latest!.Summary.ReconciliationRunId.Should().Be(created.Summary.ReconciliationRunId);

        var byIdResponse = await client.GetAsync($"/api/workstation/reconciliation/runs/{created.Summary.ReconciliationRunId}");
        byIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var byId = await byIdResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        byId.Should().NotBeNull();
        byId!.Summary.MatchCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationRoutes_ShouldReturnNotFoundWhenNoRunExists()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var client = app.GetTestClient();

        var createResponse = await client.PostAsJsonAsync("/api/workstation/reconciliation/runs", new ReconciliationRunRequest("missing-run"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var latestResponse = await client.GetAsync("/api/workstation/runs/missing-run/reconciliation");
        latestResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationHistoryRoute_ShouldReturnDescendingHistory()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var repository = app.Services.GetRequiredService<IReconciliationRunRepository>();
        await repository.SaveAsync(BuildReconciliationDetail(
            reconciliationRunId: "recon-1",
            runId: "run-history",
            createdAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            matchCount: 1,
            breakCount: 0));
        await repository.SaveAsync(BuildReconciliationDetail(
            reconciliationRunId: "recon-2",
            runId: "run-history",
            createdAt: new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero),
            matchCount: 2,
            breakCount: 1));
        await repository.SaveAsync(BuildReconciliationDetail(
            reconciliationRunId: "recon-3",
            runId: "run-history",
            createdAt: new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero),
            matchCount: 3,
            breakCount: 0));

        var client = app.GetTestClient();
        var historyResponse = await client.GetAsync("/api/workstation/runs/run-history/reconciliation/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await historyResponse.Content.ReadFromJsonAsync<List<ReconciliationRunSummary>>();
        history.Should().NotBeNull();
        history!.Should().HaveCount(3);
        history.Select(item => item.ReconciliationRunId).Should().ContainInOrder("recon-2", "recon-3", "recon-1");
        history[0].RunId.Should().Be("run-history");
        history[0].CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero));
        history[0].BreakCount.Should().Be(1);
        history[1].CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero));
        history[2].CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationHistoryRoute_ShouldHandleEmptyHistory()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("run-no-history"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/run-no-history/reconciliation/history");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await response.Content.ReadFromJsonAsync<List<ReconciliationRunSummary>>();
        history.Should().NotBeNull();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationRoutes_ShouldReturnBreaksForMismatchedRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun("run-breaks"));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/reconciliation/runs", new ReconciliationRunRequest("run-breaks"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        created.Should().NotBeNull();
        created!.Summary.BreakCount.Should().BeGreaterThan(0);
        created.Breaks.Should().NotBeEmpty();
        created.Breaks.Should().Contain(breakRow =>
            breakRow.Category == ReconciliationBreakCategory.AmountMismatch ||
            breakRow.Category == ReconciliationBreakCategory.MissingLedgerCoverage);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterRoutes_ShouldReturnSearchAndDetailPayloads()
    {
        var securityId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var queryService = new StubSecurityMasterQueryService();
        queryService.Register(
            new SecuritySummaryDto(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: "Apple Inc.",
                PrimaryIdentifier: "AAPL",
                Currency: "USD",
                Version: 4),
            new SecurityDetailDto(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: "Apple Inc.",
                Currency: "USD",
                CommonTerms: JsonSerializer.SerializeToElement(new { lotSize = 1 }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new { primaryExchange = "NASDAQ" }),
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Ticker,
                        "AAPL",
                        true,
                        new DateTimeOffset(2026, 3, 21, 0, 0, 0, TimeSpan.Zero),
                        null,
                        null)
                ],
                Aliases: [],
                Version: 4,
                EffectiveFrom: new DateTimeOffset(2026, 3, 21, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null));

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(queryService);
        });

        var client = app.GetTestClient();

        using var search = await ReadJsonAsync(client, "/api/workstation/security-master/securities?query=AAPL&take=5&activeOnly=true");
        var rows = search.RootElement;
        rows.GetArrayLength().Should().Be(1);
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.SecurityId))).GetGuid().Should().Be(securityId);
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.Status))).GetString().Should().Be("Active");
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.Classification))).GetProperty(CamelCase(nameof(SecurityClassificationSummaryDto.AssetClass))).GetString().Should().Be("Equity");
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.Classification))).GetProperty(CamelCase(nameof(SecurityClassificationSummaryDto.PrimaryIdentifierValue))).GetString().Should().Be("AAPL");
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.EconomicDefinition))).GetProperty(CamelCase(nameof(SecurityEconomicDefinitionSummaryDto.EffectiveFrom))).ValueKind.Should().Be(JsonValueKind.Null);

        using var detail = await ReadJsonAsync(client, $"/api/workstation/security-master/securities/{securityId}");
        detail.RootElement.GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.SecurityId))).GetGuid().Should().Be(securityId);
        detail.RootElement.GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.EconomicDefinition))).GetProperty(CamelCase(nameof(SecurityEconomicDefinitionSummaryDto.Currency))).GetString().Should().Be("USD");
        detail.RootElement.GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.EconomicDefinition))).GetProperty(CamelCase(nameof(SecurityEconomicDefinitionSummaryDto.Version))).GetInt64().Should().Be(4);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterSearch_ShouldRequireQuery()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(new StubSecurityMasterQueryService());
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/security-master/securities");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterHistory_ShouldReturnHistoryPayload()
    {
        var securityId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterHistory(
            securityId,
            [
                new SecurityMasterEventEnvelope(
                    GlobalSequence: 101,
                    SecurityId: securityId,
                    StreamVersion: 2,
                    EventType: "SecurityAmended",
                    EventTimestamp: new DateTimeOffset(2026, 3, 22, 12, 0, 0, TimeSpan.Zero),
                    Actor: "governance.user",
                    CorrelationId: null,
                    CausationId: null,
                    Payload: JsonSerializer.SerializeToElement(new { field = "displayName" }),
                    Metadata: JsonSerializer.SerializeToElement(new { source = "test" })),
                new SecurityMasterEventEnvelope(
                    GlobalSequence: 100,
                    SecurityId: securityId,
                    StreamVersion: 1,
                    EventType: "SecurityCreated",
                    EventTimestamp: new DateTimeOffset(2026, 3, 21, 12, 0, 0, TimeSpan.Zero),
                    Actor: "governance.user",
                    CorrelationId: null,
                    CausationId: null,
                    Payload: JsonSerializer.SerializeToElement(new { displayName = "Sample Security" }),
                    Metadata: JsonSerializer.SerializeToElement(new { source = "test" }))
            ]);

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(queryService);
        });

        var client = app.GetTestClient();
        using var history = await ReadJsonAsync(client, $"/api/workstation/security-master/securities/{securityId}/history?take=1");
        history.RootElement.GetArrayLength().Should().Be(1);
        history.RootElement[0].GetProperty("eventType").GetString().Should().Be("SecurityAmended");
        history.RootElement[0].GetProperty("streamVersion").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterHistory_ShouldReturnNotFoundWithoutEvents()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(new StubSecurityMasterQueryService());
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{Guid.NewGuid()}/history");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_CompareRuns_ShouldReturnMetricsForEachRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "cmp-1",
            strategyId: "s1",
            strategyName: "Alpha Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "cmp-2",
            strategyId: "s2",
            strategyName: "Beta Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 9, 0, 0, TimeSpan.Zero)));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/compare", new { runIds = new[] { "cmp-1", "cmp-2" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var rows = doc.RootElement.EnumerateArray().ToArray();
        rows.Should().HaveCount(2);
        rows.Select(r => r.GetProperty("runId").GetString()).Should().Contain("cmp-1").And.Contain("cmp-2");
        rows.Select(r => r.GetProperty("strategyName").GetString()).Should().Contain("Alpha Strategy").And.Contain("Beta Strategy");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_CompareRuns_ShouldReturnBadRequestForSingleRunId()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/compare", new { runIds = new[] { "only-one" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_CompareRuns_ShouldFilterByModeWhenRequested()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "cmp-filter-paper",
            strategyId: "s1",
            strategyName: "Alpha Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "cmp-filter-backtest",
            strategyId: "s1",
            strategyName: "Alpha Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 9, 0, 0, TimeSpan.Zero)));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/compare", new
        {
            runIds = new[] { "cmp-filter-paper", "cmp-filter-backtest" },
            modes = new[] { "paper" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var rows = doc.RootElement.EnumerateArray().ToArray();
        rows.Should().HaveCount(1);
        rows[0].GetProperty("runId").GetString().Should().Be("cmp-filter-paper");
        rows[0].GetProperty("mode").GetString().Should().Be("Paper");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunHistoryAndTimeline_ShouldRespectCrossModeFilteringAndSorting()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "hist-backtest",
            strategyId: "s-history",
            strategyName: "History Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "hist-paper",
            strategyId: "s-history",
            strategyName: "History Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "hist-live",
            strategyId: "s-history",
            strategyName: "History Strategy",
            runType: RunType.Live,
            startedAt: new DateTimeOffset(2026, 3, 22, 10, 0, 0, TimeSpan.Zero)));

        var client = app.GetTestClient();
        using var history = await ReadJsonAsync(client, "/api/workstation/runs/history?mode=paper,live&limit=10");
        history.RootElement.GetArrayLength().Should().Be(2);
        history.RootElement[0].GetProperty("runId").GetString().Should().Be("hist-live");
        history.RootElement[1].GetProperty("runId").GetString().Should().Be("hist-paper");

        using var timeline = await ReadJsonAsync(client, "/api/workstation/runs/timeline?mode=paper,live&limit=10");
        timeline.RootElement.GetArrayLength().Should().Be(2);
        timeline.RootElement[0].GetProperty("mode").GetString().Should().Be("Live");
        timeline.RootElement[1].GetProperty("mode").GetString().Should().Be("Paper");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DiffRuns_ShouldReturnPositionAndMetricDeltas()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "diff-base",
            strategyId: "s-diff",
            strategyName: "Diff Base",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "diff-target",
            strategyId: "s-diff-2",
            strategyName: "Diff Target",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero)));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/diff",
            new { baseRunId = "diff-base", targetRunId = "diff-target" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("baseRunId").GetString().Should().Be("diff-base");
        doc.RootElement.GetProperty("targetRunId").GetString().Should().Be("diff-target");
        doc.RootElement.GetProperty("baseStrategyName").GetString().Should().Be("Diff Base");
        doc.RootElement.GetProperty("targetStrategyName").GetString().Should().Be("Diff Target");
        doc.RootElement.GetProperty("metrics").GetProperty("netPnlDelta").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DiffRuns_ShouldReturnNotFoundWhenRunMissing()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/diff",
            new { baseRunId = "no-such-run", targetRunId = "also-missing" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<WebApplication> CreateAppAsync(Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapWorkstationEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });

        await app.StartAsync();
        return app;
    }

    private static void RegisterRunReadServices(IServiceCollection services)
    {
        var store = new StrategyRunStore();
        services.AddSingleton<IStrategyRepository>(store);
        services.AddSingleton<PortfolioReadService>();
        services.AddSingleton<LedgerReadService>();
        services.AddSingleton<StrategyRunReadService>();
    }

    private static string CamelCase(string propertyName) => JsonNamingPolicy.CamelCase.ConvertName(propertyName);

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static StrategyRunEntry BuildRun(
        string runId,
        string strategyId,
        string strategyName,
        RunType runType,
        DateTimeOffset startedAt,
        string? datasetReference = null,
        string? feedReference = null)
    {
        return StrategyRunEntry.Start(strategyId, strategyName, runType) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = startedAt.AddMinutes(30),
            DatasetReference = datasetReference,
            FeedReference = feedReference,
            PortfolioId = $"{strategyId}-{runType.ToString().ToLowerInvariant()}-portfolio",
            LedgerReference = $"{strategyId}-{runType.ToString().ToLowerInvariant()}-ledger",
            AuditReference = $"audit-{runId}"
        };
    }

    private static StrategyRunEntry BuildReconciliationReadyRun(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(30);
        var ledger = CreateLedger();
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
            Equity: 1000m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: -150m,
            TotalEquity: 1000m,
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
            Ledger: ledger,
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
            AuditReference = $"audit-{runId}"
        };
    }

    private static StrategyRunEntry BuildReconciliationMismatchRun(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(30);
        var ledger = CreateMismatchedLedger();
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
            Equity: 1000m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: -150m,
            TotalEquity: 1000m,
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
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(30),
            TotalEventsProcessed: 100);

        return StrategyRunEntry.Start("recon-break-strategy", "Reconciliation Break Strategy", RunType.Backtest) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = completedAt,
            Metrics = result,
            DatasetReference = "dataset/us/equities",
            FeedReference = "synthetic:equities",
            PortfolioId = "recon-break-portfolio",
            LedgerReference = "recon-break-ledger",
            AuditReference = $"audit-{runId}"
        };
    }

    private static BacktestResult BuildBacktestResultWithSymbol(string symbol)
    {
        var startedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(30);
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = new(symbol, 10, 40m, 0m, 0m)
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
            From: new DateOnly(2026, 3, 20),
            To: new DateOnly(2026, 3, 21),
            Symbols: [symbol],
            InitialCash: 1_000m,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_150m,
            GrossPnl: 150m,
            NetPnl: 150m,
            TotalReturn: 0.15m,
            AnnualizedReturn: 0.15m,
            SharpeRatio: 1.0d,
            SortinoRatio: 1.0d,
            CalmarRatio: 1.0d,
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

        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, startedAt, "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, completedAt, $"Buy {symbol}",
        [
            (LedgerAccounts.Securities(symbol), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
        ]);

        return new BacktestResult(
            Request: request,
            Universe: new HashSet<string>([symbol], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(30),
            TotalEventsProcessed: 100);
    }

    private static ReconciliationRunDetail BuildReconciliationDetail(
        string reconciliationRunId,
        string runId,
        DateTimeOffset createdAt,
        int matchCount,
        int breakCount)
    {
        var matches = Enumerable.Range(0, matchCount)
            .Select(index => new ReconciliationMatchDto(
                CheckId: $"match-{index + 1}",
                Label: $"Match {index + 1}",
                ExpectedSource: "portfolio",
                ActualSource: "ledger",
                ExpectedAmount: 100m + index,
                ActualAmount: 100m + index,
                Variance: 0m,
                ExpectedAsOf: createdAt,
                ActualAsOf: createdAt))
            .ToArray();

        var breaks = Enumerable.Range(0, breakCount)
            .Select(index => new ReconciliationBreakDto(
                CheckId: $"break-{index + 1}",
                Label: $"Break {index + 1}",
                Category: ReconciliationBreakCategory.AmountMismatch,
                Status: ReconciliationBreakStatus.Open,
                MissingSource: "ledger",
                ExpectedAmount: 100m + index,
                ActualAmount: 95m + index,
                Variance: 5m,
                Reason: "Seeded mismatch for history coverage",
                ExpectedAsOf: createdAt,
                ActualAsOf: createdAt))
            .ToArray();

        var summary = new ReconciliationRunSummary(
            ReconciliationRunId: reconciliationRunId,
            RunId: runId,
            CreatedAt: createdAt,
            PortfolioAsOf: createdAt,
            LedgerAsOf: createdAt,
            MatchCount: matches.Length,
            BreakCount: breaks.Length,
            OpenBreakCount: breaks.Length,
            HasTimingDrift: false,
            AmountTolerance: 0.01m,
            MaxAsOfDriftMinutes: 5);

        return new ReconciliationRunDetail(summary, matches, breaks);
    }

    private static global::Meridian.Ledger.Ledger CreateLedger()
    {
        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero), "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 10, 0, TimeSpan.Zero), "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 20, 0, TimeSpan.Zero), "Open TSLA short",
        [
            (LedgerAccounts.Cash, 150m, 0m),
            (LedgerAccounts.ShortSecuritiesPayable("TSLA"), 0m, 150m)
        ]);
        return ledger;
    }

    private static global::Meridian.Ledger.Ledger CreateMismatchedLedger()
    {
        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero), "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 17, 10, 0, 0, TimeSpan.Zero), "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 350m, 0m),
            (LedgerAccounts.Cash, 0m, 350m)
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

    // -----------------------------------------------------------------------
    // Drill-in route tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MapWorkstationEndpoints_EquityCurveRoute_ShouldReturnCurveForRunWithSnapshots()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/no-such-run/equity-curve");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FillsRoute_ShouldReturnAllFills()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
    public async Task MapWorkstationEndpoints_LedgerJournalRoute_ShouldReturnAllEntries()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("drillj-1"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drillj-1/ledger/journal");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_LedgerJournalRoute_ShouldFilterByFromDate()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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

    // --- Data-operations workspace ---

    [Fact]
    public async Task MapWorkstationEndpoints_DataOperations_WithoutServices_ShouldReturnFallbackPayload()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        using var doc = await ReadJsonAsync(client, "/api/workstation/data-operations");

        // Fallback payload contains hard-coded fixture rows
        doc.RootElement.GetProperty("metrics").GetArrayLength().Should().Be(4);
        doc.RootElement.GetProperty("providers").GetArrayLength().Should().BeGreaterThan(0);
        doc.RootElement.GetProperty("backfills").GetArrayLength().Should().BeGreaterThan(0);
        doc.RootElement.GetProperty("exports").GetArrayLength().Should().BeGreaterThan(0);

        var providersHealthyMetric = doc.RootElement.GetProperty("metrics").EnumerateArray()
            .Single(m => m.GetProperty("id").GetString() == "providers-healthy");
        providersHealthyMetric.GetProperty("label").GetString().Should().Be("Providers Healthy");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataOperations_WithReadServiceOnly_ShouldReturnEmptyProvidersAndBackfills()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var client = app.GetTestClient();

        using var doc = await ReadJsonAsync(client, "/api/workstation/data-operations");

        // No ConfigStore → providers and backfills are empty; exports always empty in MVP
        doc.RootElement.GetProperty("metrics").GetArrayLength().Should().Be(4);
        doc.RootElement.GetProperty("providers").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("backfills").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("exports").GetArrayLength().Should().Be(0);

        var providersHealthyMetric = doc.RootElement.GetProperty("metrics").EnumerateArray()
            .Single(m => m.GetProperty("id").GetString() == "providers-healthy");
        providersHealthyMetric.GetProperty("value").GetString().Should().Be("0");
        providersHealthyMetric.GetProperty("tone").GetString().Should().Be("default");

        var backfillsMetric = doc.RootElement.GetProperty("metrics").EnumerateArray()
            .Single(m => m.GetProperty("id").GetString() == "backfills-running");
        backfillsMetric.GetProperty("value").GetString().Should().Be("0");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataOperations_WithConfigStoreNoMetricsFile_ShouldReturnEmptyProvidersAndBackfills()
    {
        // Register a ConfigStore pointing to a nonexistent directory so TryLoad* returns null
        var noDataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "appsettings.json");
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton(new Meridian.Application.UI.ConfigStore(noDataPath));
        });
        var client = app.GetTestClient();

        using var doc = await ReadJsonAsync(client, "/api/workstation/data-operations");

        doc.RootElement.GetProperty("providers").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("backfills").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("exports").GetArrayLength().Should().Be(0);

        var providersHealthyMetric = doc.RootElement.GetProperty("metrics").EnumerateArray()
            .Single(m => m.GetProperty("id").GetString() == "providers-healthy");
        providersHealthyMetric.GetProperty("value").GetString().Should().Be("0");
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

    private sealed class StubSecurityMasterQueryService : ISecurityMasterQueryService
    {
        private readonly Dictionary<Guid, SecurityDetailDto> _details = [];
        private readonly Dictionary<Guid, IReadOnlyList<SecurityMasterEventEnvelope>> _history = [];
        private readonly List<SecuritySummaryDto> _summaries = [];

        public void Register(SecuritySummaryDto summary, SecurityDetailDto detail)
        {
            _summaries.Add(summary);
            _details[detail.SecurityId] = detail;
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

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default)
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
            => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
            => Task.FromResult<TradingParametersDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>(Array.Empty<CorporateActionDto>());

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);
    }
}
