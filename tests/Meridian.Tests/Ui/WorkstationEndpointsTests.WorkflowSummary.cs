using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
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
    [Fact]
    public async Task MapWorkstationEndpoints_WithStrategyReadService_ShouldEncodeRunDrillInRoutes()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run latest/encoded",
            strategyId: "carry-encoded",
            strategyName: "Encoded Carry",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/fx/spot",
            feedReference: "synthetic:fx"));

        var client = app.GetTestClient();
        using var research = await ReadJsonAsync(client, "/api/workstation/research");

        var run = research.RootElement.GetProperty("runs")[0];
        run.GetProperty("id").GetString().Should().Be("run latest/encoded");
        run.GetProperty("drillIn").GetProperty("equityCurve").GetString()
            .Should()
            .Be("/api/workstation/runs/run%20latest%2Fencoded/equity-curve");
        run.GetProperty("drillIn").GetProperty("fills").GetString()
            .Should()
            .Be("/api/workstation/runs/run%20latest%2Fencoded/fills");
        run.GetProperty("drillIn").GetProperty("cashFlows").GetString()
            .Should()
            .Be("/api/portfolio/run%20latest%2Fencoded/cash-flows");

        var response = await client.GetAsync(UiApiRoutes.WorkstationStrategyBriefing);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var briefing = await response.Content.ReadFromJsonAsync<StrategyBriefingDto>(ServerJsonOptions);
        briefing.Should().NotBeNull();
        briefing!.InsightFeed.Widgets[0].DrillInRoute.Should()
            .Be("/api/workstation/runs/run%20latest%2Fencoded/equity-curve");
        briefing.RecentRuns[0].DrillIn.Continuity.Should()
            .Be("/api/workstation/runs/run%20latest%2Fencoded/continuity");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithoutStrategyReadService_ShouldReturnServiceUnavailableStrategyBriefing()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewStrategies);
        var client = app.GetTestClient();

        // Without a strategy run read service the briefing endpoints must not invent runs,
        // watchlists, alerts, or insight tiles: they respond 503.
        var response = await client.GetAsync(UiApiRoutes.WorkstationStrategyBriefing);
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("not registered");

        var legacyResponse = await client.GetAsync(UiApiRoutes.WorkstationResearchBriefing);
        legacyResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithoutContext_ShouldPrioritizeChooseContextForTradingAndAccounting()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        var summary = await ReadWorkflowSummaryAsync(client, "/api/workstation/workflow-summary");

        GetWorkspace(summary, "trading").NextAction.Label.Should().Be("Choose Context");
        GetWorkspace(summary, "trading").NextAction.TargetPageTag.Should().Be("TradingShell");
        GetWorkspace(summary, "accounting").NextAction.Label.Should().Be("Choose Context");
        GetWorkspace(summary, "accounting").NextAction.TargetPageTag.Should().Be("AccountingShell");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithPaperCandidate_ShouldReflectStrategyToTradingHandoff()
    {
        // Both ends of the handoff, so the caller needs both families: the summary now emits only the
        // workspace cards the caller can read, and ViewTrades alone would omit the strategy card this
        // test is about. Which caller sees which card is pinned by the projection tests below.
        await using var app = await CreateAppAsync(
            services => RegisterRunReadServices(services),
            currentUserPermissions: RolePermissions.For(UserRole.Admin));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("workflow-backtest-candidate") with
        {
            FundProfileId = "northwind-income",
            FundDisplayName = "Northwind Income"
        });

        var client = app.GetTestClient();
        var summary = await ReadWorkflowSummaryAsync(
            client,
            "/api/workstation/workflow-summary?hasOperatingContext=true&operatingContext=Northwind%20Income&fundProfileId=northwind-income&fundDisplayName=Northwind%20Income");

        var strategy = GetWorkspace(summary, "strategy");
        strategy.StatusLabel.Should().Be("Candidate for paper review");
        strategy.NextAction.Label.Should().Be("Send to Trading Review");
        strategy.NextAction.TargetPageTag.Should().Be("TradingShell");

        var trading = GetWorkspace(summary, "trading");
        trading.StatusLabel.Should().Be("Candidate awaiting paper review");
        trading.NextAction.Label.Should().Be("Review Candidate for Paper");
        trading.NextAction.TargetPageTag.Should().Be("TradingShell");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithActivePaperRunAndNoBreaks_ShouldKeepTradingActiveAndAccountingReady()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewTrades);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("workflow-paper-active", withBreaks: false));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        await reconciliationService.RunAsync(new ReconciliationRunRequest("workflow-paper-active"));

        var client = app.GetTestClient();
        var summary = await ReadWorkflowSummaryAsync(
            client,
            "/api/workstation/workflow-summary?hasOperatingContext=true&operatingContext=Northwind%20Income&fundProfileId=northwind-income&fundDisplayName=Northwind%20Income");

        var trading = GetWorkspace(summary, "trading");
        trading.StatusLabel.Should().Be("Active paper cockpit");
        trading.NextAction.Label.Should().Be("Open Active Cockpit");

        var accounting = GetWorkspace(summary, "accounting");
        accounting.StatusLabel.Should().Be("Accounting review ready");
        accounting.NextAction.Label.Should().Be("Open Accounting Shell");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithReconciliationBreaks_ShouldEscalateAccountingNextAction()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services), currentUserPermissions: UserPermission.ViewTrades);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("workflow-paper-breaks", withBreaks: true));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        await reconciliationService.RunAsync(new ReconciliationRunRequest("workflow-paper-breaks"));

        var client = app.GetTestClient();
        var summary = await ReadWorkflowSummaryAsync(
            client,
            "/api/workstation/workflow-summary?hasOperatingContext=true&operatingContext=Northwind%20Income&fundProfileId=northwind-income&fundDisplayName=Northwind%20Income");

        var accounting = GetWorkspace(summary, "accounting");
        accounting.StatusLabel.Should().Be("Reconciliation breaks require review");
        accounting.NextAction.Label.Should().Be("Review Reconciliation Breaks");
        accounting.NextAction.TargetPageTag.Should().Be("FundReconciliation");
        accounting.PrimaryBlocker.IsBlocking.Should().BeTrue();
        summary.AssuranceScore.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        summary.AssuranceScore.Score.Should().BeLessThan(100);
        summary.AssuranceScore.Components.Should().Contain(component =>
            component.ComponentId == "accounting" &&
            component.Status == EvidenceStatusDto.ReviewRequired &&
            component.Detail.Contains("Reconciliation breaks require review", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(nameof(UserPermission.ViewConfig))]
    [InlineData(nameof(UserPermission.ModifyConfig))]
    public async Task MapWorkstationEndpoints_WorkflowSummary_ForSettingsOnlyOperator_ShouldServeTheSettingsCard(string permissionName)
    {
        // The projection adds the Settings card unconditionally, and the Settings-family reads --
        // GetWorkstationExtensibilityCatalog and the tenant-template list -- admit ViewConfig or
        // ModifyConfig. A caller holding one of those was refused the summary in the browser while the
        // desktop lane, which reaches the service in process with no route filter, composed the same
        // card for it. Admission has to equal what the projection serves, in both directions.
        var permission = Enum.Parse<UserPermission>(permissionName);
        await using var app = await CreateAppAsync(
            services => RegisterRunReadServices(services),
            currentUserPermissions: permission);

        var response = await app.GetTestClient().GetAsync("/api/workstation/workflow-summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<OperatorWorkflowHomeSummary>(ServerJsonOptions);
        payload.Should().NotBeNull();
        payload!.Workspaces.Select(static workspace => workspace.WorkspaceId)
            .Should().Contain("settings");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithoutRuns_ShouldReturnStableNonNullContracts()
    {
        // The full seven-card contract, so the caller must be able to read all seven: the summary now
        // emits only the workspace cards the caller can read. A narrower caller is the subject of the
        // projection tests below, not of this shape check.
        await using var app = await CreateAppAsync(
            services => RegisterRunReadServices(services),
            currentUserPermissions: RolePermissions.For(UserRole.Admin));
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/workstation/workflow-summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<OperatorWorkflowHomeSummary>(ServerJsonOptions);
        summary.Should().NotBeNull();
        summary!.Workspaces.Should().HaveCount(7);
        summary.AssuranceScore.Should().NotBeNull();
        summary.AssuranceScore.Components.Should().HaveCount(7);
        summary.AssuranceScore.Components.Should().Contain(component =>
            component.ComponentId == "trading" &&
            component.Status == EvidenceStatusDto.ReviewRequired);
        summary.Workspaces.Select(workspace => workspace.WorkspaceId).Should().BeEquivalentTo(
            "trading",
            "portfolio",
            "accounting",
            "reporting",
            "strategy",
            "data",
            "settings");
        foreach (var workspace in summary.Workspaces)
        {
            workspace.NextAction.Should().NotBeNull();
            workspace.PrimaryBlocker.Should().NotBeNull();
            workspace.Evidence.Should().NotBeNull();
        }
        GetWorkspace(summary, "strategy").NextAction.Label.Should().Be("Start Backtest");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingPayload_ShouldSurfacePaperGatewayBrokerGap()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton(new BrokerageConfiguration
            {
                Gateway = "paper",
                LiveExecutionEnabled = true
            });
        }, currentUserPermissions: UserPermission.ViewTrades);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-paper-live-review",
            strategyId: "carry-1",
            strategyName: "Carry Pair",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero)));

        var client = app.GetTestClient();
        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");

        var brokerage = trading.RootElement.GetProperty("brokerage");
        brokerage.GetProperty("provider").GetString().Should().Be("Paper trading");
        var brokerageNotes = string.Join(" ", brokerage.GetProperty("notes").EnumerateArray().Select(n => n.GetString()));
        brokerageNotes.Should().Contain("blocked");
        brokerageNotes.Should().Contain("paper trading");
        trading.RootElement.GetProperty("positions").GetArrayLength().Should().Be(0,
            "an empty financial population must not contain an informational placeholder holding");
    }

    /// <summary>
    /// The other direction of the same projection: a strategy reader keeps the card its permission
    /// names and loses the trading and accounting cards, so the gate is a projection rather than a
    /// second admission test that happens to pass for one role.
    /// </summary>
    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummary_WithStrategyPermissionOnly_ShouldOmitTradingAndAccounting()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun($"run-workflow-strategy-{Guid.NewGuid():N}"));

        var response = await app.GetTestClient().GetAsync("/api/workstation/workflow-summary?hasOperatingContext=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await response.Content.ReadFromJsonAsync<OperatorWorkflowHomeSummary>(ServerJsonOptions);
        summary.Should().NotBeNull();

        var workspaceIds = summary!.Workspaces.Select(static workspace => workspace.WorkspaceId).ToArray();
        workspaceIds.Should().Contain("strategy");
        workspaceIds.Should().NotContain(
            "trading",
            "the trading card reports governed run posture, which ViewTrades governs");
        workspaceIds.Should().NotContain(
            "accounting",
            "ViewStrategies is in none of the reconciliation read permissions the accounting card draws on");
    }

    /// <summary>
    /// The route's admission set has to be the union of the projection's family sets in both
    /// directions. Under-admitting is the failure this pins: the Data card is granted to the same
    /// permissions the Data workspace itself admits, so a 403 at the gate would withhold the only
    /// card such a caller can read. ViewHistoricalData is the case that names it -- the built-in
    /// ReadOnly role holds it, and holds no other permission in any family.
    /// </summary>
    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummary_WithDataWorkspacePermissionOnly_ShouldReturnTheDataWorkspace()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewHistoricalData);

        var response = await app.GetTestClient().GetAsync("/api/workstation/workflow-summary?hasOperatingContext=true");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the projection grants this caller the data card, so the gate must admit it");

        var summary = await response.Content.ReadFromJsonAsync<OperatorWorkflowHomeSummary>(ServerJsonOptions);
        summary.Should().NotBeNull();

        var workspaceIds = summary!.Workspaces.Select(static workspace => workspace.WorkspaceId).ToArray();
        workspaceIds.Should().Contain("data");
        workspaceIds.Should().NotContain(new[] { "trading", "accounting", "strategy" });
    }

    /// <summary>
    /// The shell bootstraps from /api/workstation/session unconditionally, so the route must stay
    /// reachable for every authenticated operator -- FundAccountant, Controller and Compliance hold
    /// no strategy permission, and a 403 here fails their whole bootstrap. The strategy-run digest
    /// the payload embeds is still withheld from them.
    /// </summary>
    [Fact]
    public async Task MapWorkstationEndpoints_Session_WithoutStrategyPermission_ShouldReturnShellPayloadWithoutRunDigest()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: RolePermissions.For(UserRole.FundAccountant),
            currentUserRole: UserRole.FundAccountant);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun($"run-session-gate-{Guid.NewGuid():N}"));

        var response = await app.GetTestClient().GetAsync("/api/workstation/session");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the shell bootstrap must not 403 for a non-strategy operator");

        var session = await response.Content.ReadFromJsonAsync<WorkstationSessionPayload>(ServerJsonOptions);
        session.Should().NotBeNull();
        session!.LatestRun.Should().BeNull("the run digest belongs to the strategy permission");
        session.WorkspaceSummary.TotalRuns.Should().Be(0, "run counts are part of the withheld digest");
        session.DisplayName.Should().Be("Meridian Operator", "the display name is built from the latest run's strategy name");
        session.Role.Should().Be(
            nameof(UserRole.FundAccountant),
            "the masthead and Settings role catalog must reflect the authenticated principal, not the latest strategy run");
        session.Environment.Should().Be(
            "research",
            "environment posture is the real run mode, not the redacted default -- the masthead drives its "
            + "live-money warning from this field, and the withheld payload would have reported \"paper\"");
    }
}
