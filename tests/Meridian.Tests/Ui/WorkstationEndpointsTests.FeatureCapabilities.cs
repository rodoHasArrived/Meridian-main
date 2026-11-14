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
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
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
    public async Task MapWorkstationEndpoints_FeatureCapabilityToggle_ShouldRequireMutationPermission()
    {
        var configPath = Path.Combine(Path.GetTempPath(), "meridian-feature-capability-permission-" + Guid.NewGuid().ToString("N"), "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, "{}");

        await using var app = await CreateAppAsync(services =>
        {
            RegisterConfigStores(services, configPath);
            services.AddSingleton<FeatureCapabilitySettingsService>();
        }, currentUserPermissions: UserPermission.ViewStrategies);
        var client = app.GetTestClient();

        var response = await client.PutAsJsonAsync(
            UiApiRoutes.WorkstationFeatureCapabilityByKey.Replace("{capabilityKey}", "desktop.data.security-master"),
            new FeatureCapabilityToggleRequest(true),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WhenTenantScopeIsMissing_ShouldRejectRequest()
    {
        await using var app = await CreateAppAsync(currentUserCompanyId: null);
        var client = app.GetTestClient();

        var response = await client.GetAsync(UiApiRoutes.WorkstationSession);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("tenant-scoped workstation request context is required");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_CanonicalWorkspaceRouteConstants_WithoutBackingServices_ShouldReturnServiceUnavailable()
    {
        await using var app = await CreateAppAsync(
            currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ViewReporting | UserPermission.ViewStrategies | UserPermission.ViewTrades | UserPermission.ViewHistoricalData);
        var client = app.GetTestClient();

        UiApiRoutes.WorkstationStrategy.Should().Be("/api/workstation/strategy");
        UiApiRoutes.WorkstationData.Should().Be("/api/workstation/data");
        UiApiRoutes.WorkstationAccounting.Should().Be("/api/workstation/accounting");
        UiApiRoutes.WorkstationReporting.Should().Be("/api/workstation/reporting");
        UiApiRoutes.WorkstationReportingStructuredExport.Should().Be("/api/workstation/reporting/structured-exports/{exportId}");
        UiApiRoutes.WorkstationTrading.Should().Be("/api/workstation/trading");

        // Workspace read surfaces must fail honestly with 503 when their backing services are
        // not registered instead of serving fabricated fallback payloads as live data.
        var workspaceRoutes = new[]
        {
            UiApiRoutes.WorkstationStrategy,
            UiApiRoutes.WorkstationData,
            UiApiRoutes.WorkstationAccounting,
            UiApiRoutes.WorkstationReporting,
            UiApiRoutes.WorkstationTrading,
            UiApiRoutes.WorkstationResearch,
            UiApiRoutes.WorkstationDataOperations,
            UiApiRoutes.WorkstationGovernance
        };

        foreach (var route in workspaceRoutes)
        {
            var response = await client.GetAsync(route);
            response.StatusCode.Should().Be(
                HttpStatusCode.ServiceUnavailable,
                $"{route} has no backing services registered and must not fabricate data");
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithStrategyReadService_ShouldReturnServiceBackedBootstrapPayloads()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

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
        session.RootElement.GetProperty("role").GetString().Should().Be("Strategy Lead");
        session.RootElement.GetProperty("environment").GetString().Should().Be("paper");
        session.RootElement.GetProperty("activeWorkspace").GetString().Should().Be("trading");
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
        research.RootElement.GetProperty("plotTool").GetProperty("workspace").GetProperty("title").GetString()
            .Should()
            .Contain("Retained");
        research.RootElement.GetProperty("plotTool").GetProperty("analyticsEvidenceStatus").GetString().Should().Be("Unavailable");
        research.RootElement.GetProperty("plotTool").GetProperty("workspace").GetProperty("points").GetArrayLength().Should().Be(0);
        research.RootElement.GetProperty("plotTool").GetProperty("statistics").GetProperty("distributionBars").GetArrayLength().Should().Be(0);
        research.RootElement.GetProperty("plotTool").GetProperty("statistics").GetProperty("sampleRows").GetArrayLength().Should().Be(0);
        research.RootElement.GetProperty("plotTool").GetProperty("tabs").GetArrayLength().Should().Be(2);

        research.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "active-runs" &&
                               metric.GetProperty("value").GetString() == "0");

        using var strategy = await ReadJsonAsync(client, "/api/workstation/strategy");
        strategy.RootElement.GetProperty("workspace").GetProperty("totalRuns").GetInt32().Should().Be(2);
        strategy.RootElement.GetProperty("runs")[0].GetProperty("id").GetString().Should().Be("run-latest");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithoutStrategyReadService_ShouldReturnServiceUnavailableInsteadOfFabricatedPayloads()
    {
        await using var app = await CreateAppAsync(
            currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ViewReporting | UserPermission.ViewStrategies | UserPermission.ViewTrades | UserPermission.ViewHistoricalData);
        var client = app.GetTestClient();

        // The session bootstrap payload stays available with honest zeroed workspace counters.
        using var session = await ReadJsonAsync(client, "/api/workstation/session");
        session.RootElement.GetProperty("displayName").GetString().Should().Be("Meridian Operator");
        session.RootElement.GetProperty("role").GetString().Should().Be("Strategy Lead");
        session.RootElement.GetProperty("environment").GetString().Should().Be("paper");
        session.RootElement.GetProperty("activeWorkspace").GetString().Should().Be("strategy");
        session.RootElement.GetProperty("commandCount").GetInt32().Should().Be(6);
        session.RootElement.GetProperty("workspaceSummary").GetProperty("totalRuns").GetInt32().Should().Be(0);

        // Workspace payload endpoints must not invent runs, breaks, providers, backfills, or
        // exports when the backing read services are missing: they respond 503 with a problem
        // detail naming the missing service.
        foreach (var route in new[]
        {
            "/api/workstation/research",
            "/api/workstation/strategy",
            "/api/workstation/governance",
            "/api/workstation/accounting",
            "/api/workstation/reporting",
            "/api/workstation/data"
        })
        {
            var response = await client.GetAsync(route);
            response.StatusCode.Should().Be(
                HttpStatusCode.ServiceUnavailable,
                $"{route} must not serve fabricated fallback data");
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("registered", $"{route} problem detail must name the missing service");
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityRoutes_ShouldUseSharedWorkflowService()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();

        var startResponse = await client.PostAsJsonAsync(
            "/api/workstation/operations/continuity",
            new OperationsStartWorkflowRequestDto(
                fundAccountId,
                "2026-05",
                SecurityMasterSnapshotId: Guid.NewGuid(),
                BrokerSource: "custodian",
                Actor: "ops-user",
                Rationale: "Open monthly close lane"),
            ServerJsonOptions);

        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var startJson = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
        var workflow = startJson.RootElement.GetProperty("workflow");
        var workflowId = workflow.GetProperty("workflowId").GetGuid();
        workflow.GetProperty("status").GetString().Should().Be(nameof(OperationsWorkflowStatusDto.CollectingBrokerData));
        workflow.GetProperty("version").GetInt64().Should().Be(1);
        workflow.GetProperty("timeline").GetArrayLength().Should().Be(1);

        using var detail = await ReadJsonAsync(client, $"/api/workstation/operations/continuity/{workflowId}");
        detail.RootElement.GetProperty("workflowId").GetGuid().Should().Be(workflowId);
        detail.RootElement.GetProperty("fundAccountId").GetGuid().Should().Be(fundAccountId);

        using var timeline = await ReadJsonAsync(client, $"/api/workstation/operations/continuity/{workflowId}/timeline");
        timeline.RootElement.GetArrayLength().Should().Be(1);
        timeline.RootElement[0].GetProperty("eventType").GetString().Should().Be("workflow-started");

        using var summaries = await ReadJsonAsync(client, $"/api/workstation/operations/continuity?fundAccountId={fundAccountId}");
        summaries.RootElement.GetArrayLength().Should().Be(1);
        summaries.RootElement[0].GetProperty("workflowId").GetGuid().Should().Be(workflowId);
    }
}
