using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Credentials;
using Meridian.DataIntegration.Monitoring;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
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
    [Fact]
    public async Task MapWorkstationEndpoints_CompareRuns_ShouldReturnBadRequestForSingleRunId()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/compare", new { runIds = new[] { "only-one" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_CompareRuns_ShouldReturnBadRequestWhenTooManyRunIdsProvided()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var runIds = Enumerable.Range(1, 11).Select(index => $"cmp-{index}").ToArray();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/workstation/runs/compare", new { runIds });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MapStrategyRunsCompare_ShouldReturnBadRequestWhenTooManyRunIdsProvided()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewStrategies);

        var ids = string.Join(',', Enumerable.Range(1, 11).Select(index => $"cmp-{index}"));
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/strategies/runs/compare?ids={ids}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_CompareRuns_ShouldFilterByModeWhenRequested()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

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
        }, currentUserPermissions: UserPermission.ViewStrategies);

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
    public async Task MapWorkstationEndpoints_RunLineageTimeline_ShouldExposeDedicatedLineageContract()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "lineage-endpoint-backtest",
            strategyId: "s-lineage-endpoint",
            strategyName: "Lineage Endpoint Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "lineage-endpoint-paper",
            strategyId: "s-lineage-endpoint",
            strategyName: "Lineage Endpoint Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero)) with
        {
            ParentRunId = "lineage-endpoint-backtest"
        });

        var client = app.GetTestClient();
        using var timeline = await ReadJsonAsync(client, "/api/workstation/runs/lineage-timeline?mode=paper,backtest&limit=10");
        var rows = timeline.RootElement.EnumerateArray().ToArray();

        rows.Should().HaveCount(4);
        rows[0].GetProperty("runId").GetString().Should().Be("lineage-endpoint-backtest");
        rows[0].GetProperty("eventType").GetString().Should().Be("RunStarted");
        rows[1].GetProperty("eventType").GetString().Should().Be("RunCompleted");
        rows[2].GetProperty("runId").GetString().Should().Be("lineage-endpoint-paper");
        rows[2].GetProperty("parentCanonicalRunKey").GetString().Should().Be("lineage-endpoint-backtest");
        foreach (var row in rows)
        {
            row.TryGetProperty("canonicalRunKey", out var _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DiffRuns_ShouldReturnPositionAndMetricDeltas()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("diff-base") with
        {
            StrategyId = "diff-strategy",
            StrategyName = "Diff Base",
            ParameterSet = new Dictionary<string, string>
            {
                ["strategyVersion"] = "v1.0.0"
            }
        });
        await store.RecordRunAsync(BuildActivePaperRun("diff-target", withBreaks: false) with
        {
            StrategyId = "diff-strategy",
            StrategyName = "Diff Target",
            ParentRunId = "diff-base",
            ParameterSet = new Dictionary<string, string>
            {
                ["strategyVersion"] = "v1.1.0"
            }
        });

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
        doc.RootElement.GetProperty("metrics").GetProperty("finalEquityDelta").GetDecimal().Should().Be(0m);
        doc.RootElement.GetProperty("metrics").GetProperty("maxDrawdownDelta").GetDecimal().Should().Be(0m);
        doc.RootElement.GetProperty("metrics").GetProperty("sharpeRatioDelta").GetDouble().Should().Be(0d);
        doc.RootElement.GetProperty("baseMode").GetString().Should().Be("Backtest");
        doc.RootElement.GetProperty("targetMode").GetString().Should().Be("Paper");
        doc.RootElement.GetProperty("baseEngine").GetString().Should().Be("MeridianNative");
        doc.RootElement.GetProperty("targetEngine").GetString().Should().Be("BrokerPaper");
        doc.RootElement.GetProperty("baseStrategyId").GetString().Should().Be("diff-strategy");
        doc.RootElement.GetProperty("targetStrategyId").GetString().Should().Be("diff-strategy");
        doc.RootElement.GetProperty("baseStrategyVersion").GetString().Should().Be("v1.0.0");
        doc.RootElement.GetProperty("targetStrategyVersion").GetString().Should().Be("v1.1.0");
        doc.RootElement.GetProperty("lineageRelation").GetString().Should().Be("DirectChild");
        doc.RootElement.GetProperty("compatibilityLevel").GetString().Should().Be("CrossEngine");
        doc.RootElement.TryGetProperty("baseArtifactCompleteness", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("targetArtifactCompleteness", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("compatibilityWarnings", out _).Should().BeTrue();
        doc.RootElement.GetProperty("compatibilityWarnings").EnumerateArray()
            .Select(static warning => warning.GetString())
            .Should()
            .Contain(warning => warning!.Contains("Strategy version comparison active", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DiffRuns_ShouldReturnNotFoundWhenRunMissing()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/diff",
            new { baseRunId = "no-such-run", targetRunId = "also-missing" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_PortfolioWorkspace_ShouldReturnTypedPayloadWithRunsAndPositions()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewTrades);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "portfolio-run-backtest",
            strategyId: "strat-portfolio",
            strategyName: "Portfolio Test Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities"));
        await store.RecordRunAsync(BuildRun(
            runId: "portfolio-run-paper",
            strategyId: "strat-portfolio",
            strategyName: "Portfolio Test Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities"));

        var client = app.GetTestClient();

        using var portfolio = await ReadJsonAsync(client, "/api/workstation/portfolio");

        // Metrics are present and non-empty
        var metrics = portfolio.RootElement.GetProperty("metrics");
        metrics.GetArrayLength().Should().Be(4);
        metrics.EnumerateArray().Should().Contain(m =>
            m.GetProperty("id").GetString() == "portfolio-runs" &&
            m.GetProperty("value").GetString() == "2");

        // Positions array exists (empty when no live portfolio)
        portfolio.RootElement.GetProperty("positions").GetArrayLength().Should().BeGreaterThanOrEqualTo(0);

        // Risk state is present
        var risk = portfolio.RootElement.GetProperty("risk");
        risk.GetProperty("state").GetString().Should().NotBeNullOrEmpty();
        risk.GetProperty("summary").GetString().Should().NotBeNullOrEmpty();

        // Brokerage state is present
        var brokerage = portfolio.RootElement.GetProperty("brokerage");
        brokerage.GetProperty("provider").GetString().Should().NotBeNullOrEmpty();
        brokerage.GetProperty("connection").GetString().Should().NotBeNullOrEmpty();

        // Run-linked equity panel has both runs
        var runs = portfolio.RootElement.GetProperty("runs");
        runs.GetArrayLength().Should().Be(2);
        runs.EnumerateArray()
            .Select(r => r.GetProperty("runId").GetString())
            .Should().BeEquivalentTo("portfolio-run-paper", "portfolio-run-backtest");
        runs.EnumerateArray().Should().AllSatisfy(r =>
        {
            r.GetProperty("strategyName").GetString().Should().Be("Portfolio Test Strategy");
            r.GetProperty("mode").GetString().Should().BeOneOf("paper", "backtest");
            r.GetProperty("notes").GetString().Should().NotBeNull();
        });

        // Cash-flow summary is populated so the Portfolio workspace can surface
        // cash posture and ledger variance without falling back to Governance fixtures.
        var cashFlow = portfolio.RootElement.GetProperty("cashFlow");
        cashFlow.ValueKind.Should().NotBe(JsonValueKind.Null);
        cashFlow.GetProperty("tone").GetString().Should().NotBeNullOrEmpty();
        cashFlow.GetProperty("summary").GetString().Should().NotBeNullOrEmpty();
        cashFlow.TryGetProperty("totalCash", out _).Should().BeTrue();
        cashFlow.TryGetProperty("totalLedgerCash", out _).Should().BeTrue();
        cashFlow.TryGetProperty("netVariance", out _).Should().BeTrue();
        cashFlow.TryGetProperty("runsWithCashSignals", out _).Should().BeTrue();
        cashFlow.TryGetProperty("runsWithCashVariance", out _).Should().BeTrue();
    }
}
