using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.Application.Monitoring;
using Meridian.Application.OperationsContinuity;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.ProviderSdk;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using IReconciliationApiService = Meridian.Ui.Shared.Contracts.Reconciliation.IReconciliationApiService;
using ReconciliationCaseSummaryDto = Meridian.Ui.Shared.Contracts.Reconciliation.ReconciliationCaseSummaryDto;
using ReconciliationQueueAccountStatusDto = Meridian.Ui.Shared.Contracts.Reconciliation.ReconciliationQueueAccountStatusDto;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using StatementImportSummaryDto = Meridian.Ui.Shared.Contracts.Reconciliation.StatementImportSummaryDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_FeatureCapabilities_ShouldExposeSharedDescriptorOverridesAndPersistToggle()
    {
        var configPath = Path.Combine(Path.GetTempPath(), "meridian-feature-capability-api-" + Guid.NewGuid().ToString("N"), "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, """
            {
              "FeatureCapabilities": {
                "Overrides": {
                  " desktop.data.security-master ": false,
                  "DESKTOP.DATA.SECURITY-MASTER": true
                }
              }
            }
            """);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterConfigStores(services, configPath);
            services.AddSingleton<FeatureCapabilitySettingsService>();
        });
        var client = app.GetTestClient();

        var initial = await client.GetFromJsonAsync<FeatureCapabilitySettingsResponse>(
            "/api/workstation/settings/feature-capabilities",
            ServerJsonOptions);

        initial.Should().NotBeNull();
        initial!.Capabilities.Should().Contain(item => item.CapabilityKey == "desktop.settings.capabilities" && item.IsPermanent);
        initial.Capabilities.Should().Contain(item =>
            item.CapabilityKey == "desktop.data.security-master" &&
            item.IsEnabled == false &&
            item.IsOverridden);

        var response = await client.PutAsJsonAsync(
            "/api/workstation/settings/feature-capabilities/desktop.data.security-master",
            new FeatureCapabilityToggleRequest(true),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<FeatureCapabilitySettingsResponse>(ServerJsonOptions);
        updated.Should().NotBeNull();
        updated!.Capabilities.Should().Contain(item =>
            item.CapabilityKey == "desktop.data.security-master" &&
            item.IsEnabled &&
            item.IsOverridden);

        var persisted = Meridian.Application.UI.ConfigStore.LoadConfig(configPath);
        persisted.FeatureCapabilities.Should().NotBeNull();
        persisted.FeatureCapabilities!.Overrides.Should()
            .ContainKey("desktop.data.security-master")
            .WhoseValue.Should().BeTrue();
    }

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
            .Contain("workstation");
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
    public async Task MapWorkstationEndpoints_WithoutStrategyReadService_ShouldReturnFallbackPayloads()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        using var session = await ReadJsonAsync(client, "/api/workstation/session");
        session.RootElement.GetProperty("displayName").GetString().Should().Be("Meridian Operator");
        session.RootElement.GetProperty("role").GetString().Should().Be("Research Lead");
        session.RootElement.GetProperty("environment").GetString().Should().Be("paper");
        session.RootElement.GetProperty("activeWorkspace").GetString().Should().Be("strategy");
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

        using var accounting = await ReadJsonAsync(client, "/api/workstation/accounting");
        accounting.RootElement.GetProperty("reconciliationQueue").GetArrayLength().Should().Be(1);

        using var reporting = await ReadJsonAsync(client, "/api/workstation/reporting");
        reporting.RootElement.GetProperty("reporting").GetProperty("profiles").GetArrayLength().Should().BeGreaterThan(0);

        var runs = research.RootElement.GetProperty("runs");
        runs.GetArrayLength().Should().Be(1);
        runs[0].GetProperty("id").GetString().Should().Be("run-research-001");
        runs[0].GetProperty("strategyName").GetString().Should().Be("Mean Reversion FX");
        research.RootElement.GetProperty("plotTool").GetProperty("workspace").GetProperty("title").GetString()
            .Should()
            .Contain("Mean Reversion FX");

        using var strategy = await ReadJsonAsync(client, "/api/workstation/strategy");
        strategy.RootElement.GetProperty("runs")[0].GetProperty("id").GetString().Should().Be("run-research-001");
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

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityDetail_ShouldExposeExplicitContinuityContractFields()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();
        var evidence = new OperationsEvidenceLinkDto(
            EvidenceId: "ev-close-checklist",
            Label: "Close Checklist",
            Route: "/api/workstation/reporting/close-checklist/ev-close-checklist",
            Source: "ops-runbook",
            CapturedAtUtc: new DateTimeOffset(2026, 5, 20, 9, 30, 0, TimeSpan.Zero));

        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            Guid.NewGuid(),
            "custodian",
            "spoofed-user",
            EvidenceLinks: [evidence]));
        var workflowId = start.Workflow!.WorkflowId;

        var import = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(start.Workflow.Version, "spoofed-user", EvidenceLinks: [evidence]));
        var normalized = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            new OperationsTransitionRequestDto(import.Workflow!.Version, "spoofed-user", EvidenceLinks: [evidence]));
        var security = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "spoofed-user", EvidenceLinks: [evidence]));
        var draft = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "spoofed-user", "ledger-preview-1", true, EvidenceLinks: [evidence]));
        var validated = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "spoofed-user", true, true, EvidenceLinks: [evidence]));
        var posted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "spoofed-user",
                "ledger-batch-1",
                "period-close",
                true,
                EvidenceLinks: [evidence],
                JournalCandidate: CreateOperationsLedgerJournalCandidate(start.Workflow!.FundAccountId)));
        var reconciled = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/reconciliation/run",
            new OperationsReconciliationRunRequestDto(posted.Workflow!.Version, "spoofed-user", BreakCases: [], EvidenceLinks: [evidence]));
        var posture = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/posture/refresh",
            new OperationsGatePostureRequestDto(
                reconciled.Workflow!.Version,
                "spoofed-user",
                ReportPackReady: false,
                ReportPackId: "report-pack-2026-05",
                EvidenceLinks: [evidence]));

        posture.Workflow.Should().NotBeNull();
        var workflow = posture.Workflow!;
        workflow.EvidenceLinks.Should().NotBeEmpty();
        workflow.ReportPackReadiness.ReportPackId.Should().Be("report-pack-2026-05");
        workflow.ReportPackReadiness.IsReady.Should().BeFalse();
        workflow.NextActions.Should().NotBeEmpty();
        workflow.Blockers.Should().Contain(blocker => blocker.EvidenceLinks.Any());
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityRoutes_ShouldUseTrustedActorInsteadOfRequestActor()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();

        var startResponse = await client.PostAsJsonAsync(
            "/api/workstation/operations/continuity",
            new OperationsStartWorkflowRequestDto(
                Guid.NewGuid(),
                "2026-05",
                SecurityMasterSnapshotId: Guid.NewGuid(),
                BrokerSource: "custodian",
                Actor: "spoofed-user",
                Rationale: "Open monthly close lane"),
            ServerJsonOptions);

        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var startJson = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
        var workflowId = startJson.RootElement.GetProperty("workflow").GetProperty("workflowId").GetGuid();

        using var timeline = await ReadJsonAsync(client, $"/api/workstation/operations/continuity/{workflowId}/timeline");
        timeline.RootElement[0].GetProperty("actor").GetString().Should().Be("ops-user");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityStartRoute_ShouldRequireMutationPermission()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ViewStrategies);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/operations/continuity",
            new OperationsStartWorkflowRequestDto(
                Guid.NewGuid(),
                "2026-05",
                SecurityMasterSnapshotId: Guid.NewGuid(),
                BrokerSource: "custodian",
                Actor: "spoofed-user",
                Rationale: "Open monthly close lane"),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityImportRoute_ShouldRequireMutationPermission()
    {
        await using var privilegedApp = await CreateAppAsync(RegisterOperationsContinuityServices);
        var privilegedClient = privilegedApp.GetTestClient();

        var startResponse = await privilegedClient.PostAsJsonAsync(
            "/api/workstation/operations/continuity",
            new OperationsStartWorkflowRequestDto(
                Guid.NewGuid(),
                "2026-05",
                SecurityMasterSnapshotId: Guid.NewGuid(),
                BrokerSource: "custodian",
                Actor: "ops-user",
                Rationale: "Open monthly close lane"),
            ServerJsonOptions);
        using var startJson = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
        var workflow = startJson.RootElement.GetProperty("workflow");
        var workflowId = workflow.GetProperty("workflowId").GetGuid();
        var expectedVersion = workflow.GetProperty("version").GetInt64();

        await using var restrictedApp = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ViewStrategies);
        var restrictedClient = restrictedApp.GetTestClient();

        var response = await restrictedClient.PostAsJsonAsync(
            $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(
                expectedVersion,
                "spoofed-user",
                "Import broker statement"),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityReopenRoute_ShouldRequireGovernedAdminPermission()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ManageDirectLending);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            $"/api/workstation/operations/continuity/{Guid.NewGuid()}/reopen",
            new OperationsReopenWorkflowRequestDto(
                1,
                "spoofed-user",
                "Reopen workflow",
                "incident-123",
                IsGovernedAdmin: true),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuitySecurityMasterOverrideApprovalRoute_ShouldRequireOverrideApprovalPermission()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ManageDirectLending);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            $"/api/workstation/operations/continuity/{Guid.NewGuid()}/security-master/overrides/override-1/approve",
            new OperationsSecurityMasterOverrideApprovalRequestDto(
                1,
                "spoofed-user",
                "override-1",
                "approve override",
                "policy-1",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityCommandRoutes_ShouldAdvanceToClosed()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();

        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "spoofed-user"));
        var workflowId = start.Workflow!.WorkflowId;

        var import = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(start.Workflow.Version, "spoofed-user"));
        var normalized = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            new OperationsTransitionRequestDto(import.Workflow!.Version, "spoofed-user"));
        var security = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "spoofed-user"));
        var draft = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "spoofed-user", "ledger-preview-1", true));
        var validated = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "spoofed-user", true, true));
        var posted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "spoofed-user",
                "ledger-batch-1",
                "period-close",
                true,
                JournalCandidate: CreateOperationsLedgerJournalCandidate(start.Workflow!.FundAccountId)));
        var reconciled = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/reconciliation/run",
            new OperationsReconciliationRunRequestDto(posted.Workflow!.Version, "spoofed-user", BreakCases: []));
        var posture = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/posture/refresh",
            new OperationsGatePostureRequestDto(reconciled.Workflow!.Version, "spoofed-user", ReportPackReady: true, ReportPackId: "report-pack-1"));
        var submitted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/approval/submit",
            new OperationsSubmitApprovalRequestDto(
                posture.Workflow!.Version,
                "spoofed-user",
                "ops-user",
                "Submit evidence",
                "report-pack-1",
                ChecklistControlApprovals: RequiredOperationsChecklistControlApprovals()));
        var approved = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/approval/approve",
            new OperationsApprovalDecisionRequestDto(
                submitted.Workflow!.Version,
                "spoofed-user",
                "spoofed-reviewer",
                "Approve close",
                "report-pack-1",
                ChecklistControlApprovals: RequiredOperationsChecklistControlApprovals()));
        var closed = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/close",
            new OperationsCloseWorkflowRequestDto(
                approved.Workflow!.Version,
                "spoofed-user",
                "Close period",
                "report-pack-1",
                ChecklistControlApprovals: RequiredOperationsChecklistControlApprovals()));

        closed.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Closed);
        closed.Workflow.Timeline.Should().Contain(entry => entry.EventType == "workflow-closed" && entry.Actor == "ops-user");
        closed.Workflow.Approvals.Should().Contain(approval =>
            approval.Status == OperationsApprovalStateDto.Approved &&
            approval.Operator == "ops-user" &&
            approval.Reviewer == "ops-user");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityApprovalSubmit_ShouldPreserveAssignedReviewerFromRequest()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();

        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "spoofed-user"));
        var workflowId = start.Workflow!.WorkflowId;
        var import = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(start.Workflow.Version, "spoofed-user"));
        var normalized = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            new OperationsTransitionRequestDto(import.Workflow!.Version, "spoofed-user"));
        var security = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "spoofed-user"));
        var draft = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "spoofed-user", "ledger-preview-1", true));
        var validated = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "spoofed-user", true, true));
        var posted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "spoofed-user",
                "ledger-batch-1",
                "period-close",
                true,
                JournalCandidate: CreateOperationsLedgerJournalCandidate(start.Workflow!.FundAccountId)));
        var reconciled = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/reconciliation/run",
            new OperationsReconciliationRunRequestDto(posted.Workflow!.Version, "spoofed-user", BreakCases: []));
        var posture = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/posture/refresh",
            new OperationsGatePostureRequestDto(reconciled.Workflow!.Version, "spoofed-user", ReportPackReady: true, ReportPackId: "report-pack-1"));

        const string assignedReviewer = "independent-reviewer";
        var submitted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/approval/submit",
            new OperationsSubmitApprovalRequestDto(
                posture.Workflow!.Version,
                "spoofed-user",
                assignedReviewer,
                "Submit evidence",
                "report-pack-1",
                ChecklistControlApprovals: RequiredOperationsChecklistControlApprovals()));

        submitted.Workflow!.Approvals.Should().Contain(approval =>
            approval.Status == OperationsApprovalStateDto.Submitted &&
            approval.Operator == "ops-user" &&
            approval.Reviewer == assignedReviewer);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityReconciliationRun_ShouldBridgeRealReconciliationOutputsIntoGatePosture()
    {
        var bankEntityId = Guid.NewGuid();
        var reconciliation = BuildOperationsContinuityReconciliationDetail("recon-ops-1", "run-ops-1");
        var reconciliationService = new StaticReconciliationRunService(reconciliation);
        await using var app = await CreateAppAsync(services =>
        {
            RegisterOperationsContinuityServices(services);
            services.RemoveAll<IReconciliationRunService>();
            services.AddSingleton<IReconciliationRunService>(reconciliationService);
        });
        var client = app.GetTestClient();

        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "spoofed-user"));
        var workflowId = start.Workflow!.WorkflowId;
        var import = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(start.Workflow.Version, "spoofed-user"));
        var normalized = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            new OperationsTransitionRequestDto(import.Workflow!.Version, "spoofed-user"));
        var security = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "spoofed-user"));
        var draft = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "spoofed-user", "ledger-preview-1", true));
        var validated = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "spoofed-user", true, true));
        var posted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "spoofed-user",
                "ledger-batch-1",
                "period-close",
                true,
                JournalCandidate: CreateOperationsLedgerJournalCandidate(start.Workflow!.FundAccountId)));

        var bridged = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/reconciliation/run",
            new OperationsReconciliationRunRequestDto(
                posted.Workflow!.Version,
                "spoofed-user",
                SourceRunId: "run-ops-1",
                BankEntityId: bankEntityId,
                AmountTolerance: 0.05m,
                MaxAsOfDriftMinutes: 10));

        reconciliationService.LastRunRequest.Should().NotBeNull();
        reconciliationService.LastRunRequest!.RunId.Should().Be("run-ops-1");
        reconciliationService.LastRunRequest.BankEntityId.Should().Be(bankEntityId);
        reconciliationService.LastRunRequest.AmountTolerance.Should().Be(0.05m);
        reconciliationService.LastRunRequest.MaxAsOfDriftMinutes.Should().Be(10);
        bridged.Success.Should().BeTrue();
        bridged.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Blocked);
        bridged.Workflow.BreakCases.Should().ContainSingle(breakCase =>
            breakCase.BreakId == "recon-ops-1:bank-break-1" &&
            breakCase.Severity == nameof(ReconciliationBreakSeverity.Critical) &&
            breakCase.EvidenceLinks.Any(link => link.Source == "reconciliation-run"));
        bridged.Workflow.BreakCases.Should().Contain(breakCase =>
            breakCase.CheckId == "SM_RECON_SECURITY_UNRESOLVED" &&
            breakCase.Category == "SecurityMasterCoverage" &&
            breakCase.Symbol == "BOND1" &&
            breakCase.EvidenceLinks.Any(link => link.Source == "security-master-coverage"));
        bridged.Workflow.BreakCases.Should().Contain(breakCase =>
            breakCase.CheckId == "SM_ACCOUNTING_TERMS_INCOMPLETE" &&
            breakCase.Category == "SecurityMasterAccounting" &&
            breakCase.Symbol == "BOND1" &&
            breakCase.EvidenceLinks.Any(link => link.Source == "security-master-accounting"));
        bridged.Workflow.EvidenceLinks.Should().Contain(link =>
            link.EvidenceId == "bank-normalized-activity:recon-ops-1" &&
            link.Source == "bank-normalized-activity");
        bridged.Workflow.Blockers.Should().Contain(blocker =>
            blocker.Code == "SM_ACCOUNTING_TERMS_INCOMPLETE" &&
            blocker.Gate == OperationsGateKeyDto.SecurityMaster);
        bridged.Workflow.Blockers.Should().Contain(blocker =>
            blocker.Code == "RECONCILIATION_CRITICAL_BREAKS_OPEN" &&
            blocker.Gate == OperationsGateKeyDto.Reconciliation);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityRoutes_ShouldReturnConsistentValidationShapeForMissingBody()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();
        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "spoofed-user"));
        var workflowId = start.Workflow!.WorkflowId;

        var endpoints = new[]
        {
            "/api/workstation/operations/continuity",
            $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            $"/api/workstation/operations/continuity/{workflowId}/reconciliation/run",
            $"/api/workstation/operations/continuity/{workflowId}/posture/refresh",
            $"/api/workstation/operations/continuity/{workflowId}/approval/submit",
            $"/api/workstation/operations/continuity/{workflowId}/approval/approve",
            $"/api/workstation/operations/continuity/{workflowId}/approval/reject",
            $"/api/workstation/operations/continuity/{workflowId}/close"
        };

        foreach (var endpoint in endpoints)
        {
            var response = await client.PostAsync(endpoint, content: null);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            json.RootElement.GetProperty("errors").GetProperty("request")[0].GetString().Should().NotBeNullOrWhiteSpace();
        }

        var reopenResponse = await client.PostAsync($"/api/workstation/operations/continuity/{workflowId}/reopen", content: null);
        reopenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataOperationsProviderMetrics_ShouldExposeDk1TrustRationale()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-metrics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "_status"));
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, """{"DataRoot":"."}""");

        var metrics = new ProviderMetricsStatus(
            Timestamp: new DateTimeOffset(2026, 4, 24, 17, 0, 0, TimeSpan.Zero),
            Providers:
            [
                new ProviderMetrics(
                    ProviderId: "yahoo",
                    ProviderType: "Historical bars",
                    IsConnected: true,
                    TradesReceived: 0,
                    DepthUpdatesReceived: 0,
                    QuotesReceived: 2400,
                    ConnectionAttempts: 1,
                    ConnectionFailures: 0,
                    MessagesDropped: 0,
                    ActiveSubscriptions: 4,
                    AverageLatencyMs: 42,
                    MinLatencyMs: 25,
                    MaxLatencyMs: 80,
                    DataQualityScore: 0.96,
                    ConnectionSuccessRate: 1,
                    Timestamp: new DateTimeOffset(2026, 4, 24, 16, 59, 0, TimeSpan.Zero)),
                new ProviderMetrics(
                    ProviderId: "alpaca",
                    ProviderType: "Streaming equities",
                    IsConnected: false,
                    TradesReceived: 12,
                    DepthUpdatesReceived: 0,
                    QuotesReceived: 30,
                    ConnectionAttempts: 4,
                    ConnectionFailures: 4,
                    MessagesDropped: 0,
                    ActiveSubscriptions: 2,
                    AverageLatencyMs: 510,
                    MinLatencyMs: 90,
                    MaxLatencyMs: 900,
                    DataQualityScore: 0.62,
                    ConnectionSuccessRate: 0,
                    Timestamp: new DateTimeOffset(2026, 4, 24, 16, 58, 0, TimeSpan.Zero))
            ],
            TotalProviders: 2,
            HealthyProviders: 1);
        var metricsJson = JsonSerializer.Serialize(metrics, ServerJsonOptions);
        await File.WriteAllTextAsync(Path.Combine(root, "_status", "providers.json"), metricsJson);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterConfigStores(services, configPath);
        },
            currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ManageCredentials);
        var client = app.GetTestClient();

        using var dataOperations = await ReadJsonAsync(client, "/api/workstation/data-operations");
        var providers = dataOperations.RootElement.GetProperty("providers");
        providers.GetArrayLength().Should().Be(2);

        var healthy = providers.EnumerateArray()
            .First(provider => provider.GetProperty("providerId").GetString() == "yahoo");
        healthy.GetProperty("provider").GetString().Should().Be("yahoo");
        healthy.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
        healthy.GetProperty("status").GetString().Should().Be("Healthy");
        healthy.GetProperty("trustScore").GetString().Should().Be("96%");
        healthy.GetProperty("reasonCode").GetString().Should().Be("HEALTHY_BASELINE");
        healthy.GetProperty("recommendedAction").GetString().Should().Contain("no DK1 action");
        healthy.GetProperty("diagnostics").GetArrayLength().Should().BeGreaterThan(0);

        var degraded = providers.EnumerateArray()
            .First(provider => provider.GetProperty("providerId").GetString() == "alpaca");
        degraded.GetProperty("provider").GetString().Should().Be("alpaca");
        degraded.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
        degraded.GetProperty("status").GetString().Should().Be("Degraded");
        degraded.GetProperty("trustScore").GetString().Should().Be("62%");
        degraded.GetProperty("signalSource").GetString().Should().Be("Provider quote/trade stream health telemetry");
        degraded.GetProperty("reasonCode").GetString().Should().Be("PROVIDER_STREAM_DEGRADED");
        degraded.GetProperty("recommendedAction").GetString().Should().Contain("Verify provider connectivity");
        degraded.GetProperty("gateImpact").GetString().Should().Be("Critical");
        degraded.GetProperty("connectionSummary").ValueKind.Should().Be(JsonValueKind.Object);
        degraded.GetProperty("diagnostics").EnumerateArray()
            .Should()
            .Contain(item =>
                item.GetProperty("id").GetString() == "provider-health" &&
                item.GetProperty("statusLabel").GetString() == "Degraded");

        using var data = await ReadJsonAsync(client, "/api/workstation/data");
        data.RootElement.GetProperty("providers").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataOperationsPayload_ShouldAggregateMultipleRoutingConnectionsByProviderFamily()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-routing-summary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(dataRoot);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new { dataRoot }));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterConfigStores(services, configPath);
            services.AddSingleton<IProviderCredentialStore>(_ => new FileProviderCredentialStore(dataRoot));
            services.AddSingleton(NullLogger<ProviderConnectionLifecycleService>.Instance);
            services.AddSingleton<ProviderConnectionLifecycleService>();
            services.AddSingleton<ProviderConnectionService>();
            services.AddSingleton<ProviderBindingService>();
        },
            currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ManageCredentials);

        var connectionService = app.Services.GetRequiredService<ProviderConnectionService>();
        var bindingService = app.Services.GetRequiredService<ProviderBindingService>();

        await connectionService.UpsertAsync(new CreateProviderConnectionRequest(
            ConnectionId: "alpaca-paper",
            ProviderFamilyId: "alpaca",
            DisplayName: "Alpaca Paper",
            ConnectionType: "DataVendor",
            ConnectionMode: "Paper",
            Enabled: true,
            ProductionReady: false));
        await connectionService.UpsertAsync(new CreateProviderConnectionRequest(
            ConnectionId: "alpaca-live",
            ProviderFamilyId: "alpaca",
            DisplayName: "Alpaca Live",
            ConnectionType: "DataVendor",
            ConnectionMode: "Live",
            Enabled: true,
            ProductionReady: true));

        await bindingService.UpsertAsync(new UpdateProviderBindingRequest(
            BindingId: "alpaca-paper-historical",
            Capability: nameof(ProviderCapabilityKind.HistoricalBars),
            ConnectionId: "alpaca-paper",
            FailoverConnectionIds: ["alpaca-live"]));
        await bindingService.UpsertAsync(new UpdateProviderBindingRequest(
            BindingId: "alpaca-live-realtime",
            Capability: nameof(ProviderCapabilityKind.RealtimeMarketData),
            ConnectionId: "alpaca-live"));

        using var dataOperations = await ReadJsonAsync(client: app.GetTestClient(), "/api/workstation/data-operations");
        var providers = dataOperations.RootElement.GetProperty("providers").EnumerateArray().ToArray();

        providers.Count(provider => provider.GetProperty("providerId").GetString() == "alpaca").Should().Be(1);

        var alpaca = providers.First(provider => provider.GetProperty("providerId").GetString() == "alpaca");
        alpaca.GetProperty("connectionSummary").ValueKind.Should().Be(JsonValueKind.Object);
        var routingSummary = alpaca.GetProperty("routingSummary");
        routingSummary.GetProperty("connectionId").GetString().Should().Be("alpaca-live");
        routingSummary.GetProperty("providerFamilyId").GetString().Should().Be("alpaca");
        routingSummary.GetProperty("productionReady").GetBoolean().Should().BeTrue();
        routingSummary.GetProperty("bindingCount").GetInt32().Should().Be(2);
        routingSummary.GetProperty("fallbackRouteCount").GetInt32().Should().Be(1);

        alpaca.GetProperty("diagnostics").EnumerateArray()
            .Should()
            .Contain(item =>
                item.GetProperty("id").GetString() == "routing-readiness" &&
                item.GetProperty("detail").GetString()!.Contains("Bindings 2; fallback routes 1; production ready True.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataOperationsPayload_WithoutManageCredentials_ShouldNotExposeConnectionSummary()
    {
        await using var app = await CreateAppAsync();
        using var dataOperations = await ReadJsonAsync(app.GetTestClient(), "/api/workstation/data-operations");
        var providers = dataOperations.RootElement.GetProperty("providers").EnumerateArray().ToArray();

        providers.Should().NotBeEmpty();
        providers.Should().OnlyContain(provider =>
            provider.GetProperty("connectionSummary").ValueKind == JsonValueKind.Null ||
            provider.GetProperty("connectionSummary").ValueKind == JsonValueKind.Undefined);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_KernelObservability_ShouldSurfaceActiveAndHistoricalAlertMetrics()
    {
        var observability = CreateRecoveredKernelObservability();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(observability);
        });

        var client = app.GetTestClient();

        using var dataOperations = await ReadJsonAsync(client, "/api/workstation/data-operations");
        dataOperations.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric =>
                metric.GetProperty("id").GetString() == "kernel-critical-jumps" &&
                metric.GetProperty("value").GetString() == "0" &&
                metric.GetProperty("delta").GetString() == "1 total" &&
                metric.GetProperty("tone").GetString() == "success");

        var dataOpsKernel = dataOperations.RootElement.GetProperty("kernelObservability");
        dataOpsKernel.GetProperty("activeAlerts").GetInt32().Should().Be(0);
        dataOpsKernel.GetProperty("totalAlerts").GetInt32().Should().Be(1);
        dataOpsKernel.GetProperty("alerts").GetInt32().Should().Be(1);
        dataOpsKernel.GetProperty("determinismChecksEnabled").GetBoolean().Should().BeTrue();
        dataOpsKernel.GetProperty("domains").GetArrayLength().Should().Be(1);

        var domain = dataOpsKernel.GetProperty("domains")[0];
        domain.GetProperty("domain").GetString().Should().Be(nameof(ProviderCapabilityKind.HistoricalBars));
        domain.GetProperty("throughputPerMinute").GetDouble().Should().BeGreaterThan(0);
        domain.GetProperty("latencyMs").GetProperty("p95").GetDouble()
            .Should()
            .BeGreaterThanOrEqualTo(domain.GetProperty("latencyMs").GetProperty("p50").GetDouble());
        domain.GetProperty("drift").GetProperty("methodology").GetString().Should().Be("totalVariationDistance");
        domain.GetProperty("lastUpdatedUtc").ValueKind.Should().Be(JsonValueKind.String);

        var criticalSeverityRate = domain.GetProperty("criticalSeverityRate");
        criticalSeverityRate.GetProperty("jumpAlertActive").GetBoolean().Should().BeFalse();
        criticalSeverityRate.GetProperty("jumpAlertCount").GetInt32().Should().Be(1);
        criticalSeverityRate.GetProperty("shortWindowSamples").GetInt32().Should().Be(30);
        criticalSeverityRate.GetProperty("longWindowSamples").GetInt32().Should().Be(90);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("minimumSampleCount").GetInt32().Should().Be(20);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("minimumShortRate").GetDouble().Should().Be(0.25);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("zeroBaselineShortRate").GetDouble().Should().Be(0.35);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("relativeMultiplier").GetDouble().Should().Be(2.0);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("absoluteIncrease").GetDouble().Should().Be(0.15);

        using var governance = await ReadJsonAsync(client, "/api/workstation/governance");
        governance.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric =>
                metric.GetProperty("id").GetString() == "kernel-critical-jumps" &&
                metric.GetProperty("value").GetString() == "0" &&
                metric.GetProperty("tone").GetString() == "success");

        var governanceKernel = governance.RootElement.GetProperty("kernelObservability");
        governanceKernel.GetProperty("activeAlerts").GetInt32().Should().Be(0);
        governanceKernel.GetProperty("totalAlerts").GetInt32().Should().Be(1);
        governanceKernel.GetProperty("domains")[0]
            .GetProperty("criticalSeverityRate")
            .GetProperty("jumpAlertCount")
            .GetInt32()
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithStrategyReadService_ShouldReturnTypedResearchBriefing()
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
        var response = await client.GetAsync("/api/workstation/research/briefing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var briefing = await response.Content.ReadFromJsonAsync<ResearchBriefingDto>(ServerJsonOptions);

        briefing.Should().NotBeNull();
        briefing!.Workspace.TotalRuns.Should().Be(2);
        briefing.Workspace.LatestRunId.Should().Be("run-latest");
        briefing.Workspace.HasLedgerCoverage.Should().BeTrue();
        briefing.InsightFeed.Widgets.Should().HaveCount(2);
        briefing.RecentRuns.Should().HaveCount(2);
        briefing.RecentRuns[0].RunId.Should().Be("run-latest");
        briefing.RecentRuns[0].DrillIn.Continuity.Should().Be("/api/workstation/runs/run-latest/continuity");
        briefing.SavedComparisons.Should().NotBeEmpty();
        briefing.Alerts.Should().NotBeEmpty();
        briefing.Watchlists.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithStrategyReadService_ShouldEncodeRunDrillInRoutes()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

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

        var response = await client.GetAsync("/api/workstation/research/briefing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var briefing = await response.Content.ReadFromJsonAsync<ResearchBriefingDto>(ServerJsonOptions);
        briefing.Should().NotBeNull();
        briefing!.InsightFeed.Widgets[0].DrillInRoute.Should()
            .Be("/api/workstation/runs/run%20latest%2Fencoded/equity-curve");
        briefing.RecentRuns[0].DrillIn.Continuity.Should()
            .Be("/api/workstation/runs/run%20latest%2Fencoded/continuity");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithoutStrategyReadService_ShouldReturnFallbackResearchBriefing()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/workstation/research/briefing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var briefing = await response.Content.ReadFromJsonAsync<ResearchBriefingDto>(ServerJsonOptions);

        briefing.Should().NotBeNull();
        briefing!.Workspace.TotalRuns.Should().Be(24);
        briefing.Workspace.LatestRunId.Should().Be("run-research-001");
        briefing.InsightFeed.Widgets.Should().HaveCount(3);
        briefing.Watchlists.Should().HaveCount(2);
        briefing.RecentRuns.Should().ContainSingle(run => run.RunId == "run-research-001");
        briefing.Alerts.Should().NotBeEmpty();
        briefing.WhatChanged.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithoutContext_ShouldPrioritizeChooseContextForTradingAndAccounting()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithoutRuns_ShouldReturnStableNonNullContracts()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/workstation/workflow-summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<OperatorWorkflowHomeSummary>(ServerJsonOptions);
        summary.Should().NotBeNull();
        summary!.Workspaces.Should().HaveCount(7);
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
        });

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
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldJoinSessionReplayAuditControlsAndPromotion()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-readiness", Guid.NewGuid().ToString("N"));
        var automationRoot = Path.Combine(rootPath, "provider-validation", "_automation");
        WriteReadyDk1Packet(automationRoot);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton(new Dk1TrustGateReadinessOptions(automationRoot));
            services.AddSingleton<Dk1TrustGateReadinessService>();
            services.AddSingleton(_ => new ExecutionAuditTrailService(
                new ExecutionAuditTrailOptions(Path.Combine(rootPath, "audit")),
                NullLogger<ExecutionAuditTrailService>.Instance));
            services.AddSingleton<IPaperSessionStore>(_ => new JsonlFilePaperSessionStore(
                Path.Combine(rootPath, "sessions"),
                NullLogger<JsonlFilePaperSessionStore>.Instance));
            services.AddSingleton<PaperSessionPersistenceService>(sp => new PaperSessionPersistenceService(
                NullLogger<PaperSessionPersistenceService>.Instance,
                sp.GetRequiredService<IPaperSessionStore>(),
                sp.GetRequiredService<ExecutionAuditTrailService>()));
            services.AddSingleton<ExecutionOperatorControlService>(sp => new ExecutionOperatorControlService(
                new ExecutionOperatorControlOptions(Path.Combine(rootPath, "controls")),
                NullLogger<ExecutionOperatorControlService>.Instance,
                sp.GetRequiredService<ExecutionAuditTrailService>()));
            services.AddSingleton<BacktestToLivePromoter>();
            services.AddSingleton<IPromotionRecordStore>(_ => new JsonlPromotionRecordStore(
                Path.Combine(rootPath, "promotions"),
                NullLogger<JsonlPromotionRecordStore>.Instance));
            services.AddSingleton<IGovernanceReportPackRepository>(_ => new FileGovernanceReportPackRepository(
                Path.Combine(rootPath, "report-packs"),
                NullLogger<FileGovernanceReportPackRepository>.Instance));
            services.AddSingleton<PromotionService>(sp => new PromotionService(
                sp.GetRequiredService<IStrategyRepository>(),
                sp.GetRequiredService<BacktestToLivePromoter>(),
                sp.GetRequiredService<IPromotionRecordStore>(),
                NullLogger<PromotionService>.Instance,
                operatorControls: sp.GetRequiredService<ExecutionOperatorControlService>(),
                auditTrail: sp.GetRequiredService<ExecutionAuditTrailService>()));
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        const string fundProfileId = "wave2-readiness-fund";
        await store.RecordRunAsync(BuildRun(
            runId: "run-wave2-backtest",
            strategyId: "strat-wave2",
            strategyName: "Wave 2 Acceptance",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 24, 15, 30, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities",
            fundProfileId: fundProfileId).Complete(BuildBacktestResultWithSymbol("AAPL")) with
            {
                RunId = "run-wave2-backtest",
                AuditReference = "audit-run-wave2-backtest",
                FundProfileId = fundProfileId,
                FundDisplayName = "Wave 2 Readiness Fund"
            });

        var persistence = app.Services.GetRequiredService<PaperSessionPersistenceService>();
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-wave2",
            StrategyName: "Wave 2 Acceptance",
            InitialCash: 250_000m,
            Symbols: ["AAPL"]));
        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-wave2", "AAPL", 10m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-wave2", "AAPL", 10m, 190m));
        var verification = await persistence.VerifyReplayAsync(session.SessionId);

        var controls = app.Services.GetRequiredService<ExecutionOperatorControlService>();
        await controls.SetDefaultPositionLimitAsync(
            100_000m,
            "risk.lead",
            "Daily paper desk limit accepted for Wave 2.");

        var decision = await app.Services.GetRequiredService<PromotionService>().ApproveAsync(new PromotionApprovalRequest(
            RunId: "run-wave2-backtest",
            ApprovedBy: "ops.lead",
            ApprovalReason: "Replay, audit, and paper controls accepted for Wave 2.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper)));

        decision.Success.Should().BeTrue();
        var reportPackRepository = app.Services.GetRequiredService<IGovernanceReportPackRepository>();
        var reportPack = await reportPackRepository.SaveAsync(
            new FundReportPackSnapshotDto(
                ReportId: Guid.NewGuid(),
                FundProfileId: fundProfileId,
                DisplayName: "Wave 2 Readiness Fund",
                ReportKind: GovernanceReportKindDto.TrialBalance,
                Currency: "USD",
                AsOf: new DateTimeOffset(2026, 4, 24, 16, 0, 0, TimeSpan.Zero),
                GeneratedAt: new DateTimeOffset(2026, 4, 24, 16, 5, 0, TimeSpan.Zero),
                TotalNetAssets: 250_000m,
                AuditActor: "ops.lead",
                CorrelationId: "wave2-report-pack",
                DecisionRationale: "Wave 2 readiness evidence accepted.",
                Provenance: new FundReportPackProvenanceDto(
                    RelatedRunIds: ["run-wave2-backtest"],
                    JournalEntryCount: 1,
                    LedgerEntryCount: 2,
                    TrialBalanceLineCount: 2,
                    ReconciliationRunCount: 0,
                    OpenReconciliationBreakCount: 0,
                    SecurityResolvedCount: 1,
                    SecurityMissingCount: 0,
                    LineagePointers: [],
                    SourceSnapshotHash: new string('a', 64)),
                Artifacts: [],
                Warnings: [])
            {
                Status = GovernanceReportPackStatusDto.Validated,
                ValidationIssues = [],
                LifecycleEvents =
                [
                    new FundReportPackLifecycleEventDto(
                        FromStatus: GovernanceReportPackStatusDto.Draft,
                        ToStatus: GovernanceReportPackStatusDto.Generated,
                        ChangedAt: new DateTimeOffset(2026, 4, 24, 16, 5, 0, TimeSpan.Zero),
                        Actor: "ops.lead",
                        Reason: "Test report pack generated.",
                        CorrelationId: "wave2-report-pack"),
                    new FundReportPackLifecycleEventDto(
                        FromStatus: GovernanceReportPackStatusDto.Generated,
                        ToStatus: GovernanceReportPackStatusDto.Validated,
                        ChangedAt: new DateTimeOffset(2026, 4, 24, 16, 5, 0, TimeSpan.Zero),
                        Actor: "ops.lead",
                        Reason: "Test report pack validated.",
                        CorrelationId: "wave2-report-pack")
                ]
            },
            [new GovernanceReportPackArtifactContent(
                "trial-balance",
                GovernanceReportArtifactFormatDto.Json,
                "trial-balance.json",
                Encoding.UTF8.GetBytes("""{"status":"ready"}"""))]);
        (await reportPackRepository.FindLatestByRunIdAsync("run-wave2-backtest"))
            .Should()
            .NotBeNull();

        var client = app.GetTestClient();
        var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            "/api/workstation/trading/readiness",
            ServerJsonOptions);

        readiness.Should().NotBeNull();
        readiness!.ActiveSession.Should().NotBeNull();
        readiness.ActiveSession!.SessionId.Should().Be(session.SessionId);
        readiness.ActiveSession.OrderCount.Should().Be(1);
        readiness.Replay.Should().NotBeNull();
        readiness.Replay!.IsConsistent.Should().BeTrue();
        readiness.Replay.ComparedFillCount.Should().Be(1);
        readiness.Replay.ComparedOrderCount.Should().Be(1);
        readiness.Replay.LastPersistedFillAt.Should().NotBeNull();
        readiness.Replay.LastPersistedOrderUpdateAt.Should().NotBeNull();
        readiness.Replay.VerificationAuditId.Should().Be(verification!.VerificationAuditId);
        readiness.Controls.CircuitBreakerOpen.Should().BeFalse();
        readiness.Controls.ManualOverrideCount.Should().Be(0);
        readiness.Promotion.Should().NotBeNull();
        readiness.Promotion!.ApprovalStatus.Should().Be(PromotionDecisionKinds.Approved);
        readiness.Promotion.ApprovedBy.Should().Be("ops.lead");
        readiness.Promotion.AuditReference.Should().Be(decision.AuditReference);
        readiness.Promotion.ApprovalChecklist.Should().BeEquivalentTo(PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper));
        readiness.Controls.DefaultMaxPositionSize.Should().Be(100_000m);
        readiness.Controls.ExplainableEvidenceCount.Should().Be(1);
        readiness.Controls.UnexplainedEvidenceCount.Should().Be(0);
        readiness.Controls.RecentEvidence.Should().ContainSingle(evidence =>
            evidence.Action == "DefaultPositionLimitUpdated" &&
            evidence.Actor == "risk.lead" &&
            evidence.Scope == "default-position-limit" &&
            evidence.Reason.Contains("Daily paper desk limit", StringComparison.OrdinalIgnoreCase) &&
            evidence.IsExplained);
        readiness.TrustGate.Status.Should().Be("ready-for-operator-review");
        readiness.TrustGate.ReadyForOperatorReview.Should().BeTrue();
        readiness.TrustGate.RequiredSampleCount.Should().Be(4);
        readiness.TrustGate.ReadySampleCount.Should().Be(4);
        readiness.TrustGate.ValidatedEvidenceDocumentCount.Should().Be(4);
        readiness.TrustGate.SampleReviews.Should().ContainSingle(sample =>
            sample.SampleId == "DK1-ALPACA-QUOTE-GOLDEN" &&
            sample.Provider == "Alpaca" &&
            sample.AcceptanceCheck.Contains("golden", StringComparison.OrdinalIgnoreCase));
        readiness.TrustGate.EvidenceDocuments.Should().ContainSingle(document =>
            document.Gate == "explainability" &&
            document.Status == "validated" &&
            document.Path == "docs/status/evidence/dk1-trust-rationale-mapping.md");
        readiness.TrustGate.TrustRationaleContract.Should().NotBeNull();
        readiness.TrustGate.TrustRationaleContract!.Status.Should().Be("validated");
        readiness.TrustGate.TrustRationaleContract.RequiredReasonCodes.Should().Contain("PROVIDER_STREAM_DEGRADED");
        readiness.TrustGate.BaselineThresholdContract.Should().NotBeNull();
        readiness.TrustGate.BaselineThresholdContract!.Status.Should().Be("validated");
        readiness.TrustGate.BaselineThresholdContract.FpFnReviewRequired.Should().BeTrue();
        readiness.TrustGate.Detail.Should().Contain("explainability validated").And.Contain("calibration validated");
        readiness.TrustGate.OperatorSignoffStatus.Should().Be("pending");
        readiness.TrustGate.OperatorSignoff.Should().NotBeNull();
        readiness.TrustGate.OperatorSignoff!.MissingOwners.Should().BeEquivalentTo(
            ["Data Operations", "Provider Reliability", "Trading"]);
        readiness.TrustGate.OperatorSignoff.SignedOwners.Should().BeEmpty();
        readiness.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.ReadyForPaperOperation.Should().BeFalse();
        readiness.AcceptanceGates.Should().HaveCount(9);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "session" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.SessionId == session.SessionId);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == verification!.VerificationAuditId);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "audit-controls" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "risk-rules" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "promotion" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == decision.AuditReference);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "dk1-trust" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.Detail.Contains("sign-off remains pending", StringComparison.OrdinalIgnoreCase));
        readiness.ReportPack.Should().NotBeNull();
        readiness.ReportPack!.Detail.Should().Contain("run-wave2-backtest");
        readiness.ReportPack.ReportId.Should().Be(reportPack.ReportId);
        readiness.ReportPack.RelatedRunIds.Should().Contain("run-wave2-backtest");
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "report-pack" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == reportPack.ReportId.ToString("D"));
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "reconciliation" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "brokerage-sync" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.EvidenceCompleteness.Should().NotBeNull();
        readiness.EvidenceCompleteness!.ReadyGateCount.Should().Be(6);
        readiness.EvidenceCompleteness.TotalGateCount.Should().Be(9);
        readiness.EvidenceCompleteness.ReviewGateIds.Should().BeEquivalentTo(["risk-rules", "dk1-trust", "reconciliation"]);
        readiness.WorkItems.Should().ContainSingle(item =>
            item.Kind == OperatorWorkItemKindDto.ProviderTrustGate &&
            item.Tone == OperatorWorkItemToneDto.Warning &&
            item.Detail.Contains("sign-off remains pending", StringComparison.OrdinalIgnoreCase));
        readiness.WorkItems.Should().NotContain(item => item.Tone == OperatorWorkItemToneDto.Critical);
        readiness.WorkItems.Should().NotContain(item => item.Kind == OperatorWorkItemKindDto.PaperReplay);
        readiness.WorkItems.Should().NotContain(item => item.Kind == OperatorWorkItemKindDto.PromotionReview);

        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");
        var embeddedReadiness = trading.RootElement.GetProperty("readiness");
        embeddedReadiness
            .GetProperty("activeSession")
            .GetProperty("sessionId")
            .GetString()
            .Should()
            .Be(session.SessionId);
        embeddedReadiness.GetProperty("overallStatus").GetString().Should().Be(readiness.OverallStatus.ToString());
        embeddedReadiness.GetProperty("acceptanceGates").GetArrayLength().Should().Be(readiness.AcceptanceGates.Count);
    }

    [Fact]
    public async Task Scenario_BacktestToPaperCockpitReadiness_ApiPromotionSessionReplayAndStaleRecoveryStayTraceable()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-backtest-paper", Guid.NewGuid().ToString("N"));

        await using var app = await CreateAppAsync(
            services =>
            {
                RegisterRunReadServices(services);
                services.AddSingleton(_ => new ExecutionAuditTrailService(
                    new ExecutionAuditTrailOptions(Path.Combine(rootPath, "audit")),
                    NullLogger<ExecutionAuditTrailService>.Instance));
                services.AddSingleton<IPaperSessionStore>(_ => new JsonlFilePaperSessionStore(
                    Path.Combine(rootPath, "sessions"),
                    NullLogger<JsonlFilePaperSessionStore>.Instance));
                services.AddSingleton<PaperSessionPersistenceService>(sp => new PaperSessionPersistenceService(
                    NullLogger<PaperSessionPersistenceService>.Instance,
                    sp.GetRequiredService<IPaperSessionStore>(),
                    sp.GetRequiredService<ExecutionAuditTrailService>()));
                services.AddSingleton<ExecutionOperatorControlService>(sp => new ExecutionOperatorControlService(
                    new ExecutionOperatorControlOptions(Path.Combine(rootPath, "controls")),
                    NullLogger<ExecutionOperatorControlService>.Instance,
                    sp.GetRequiredService<ExecutionAuditTrailService>()));
                services.AddSingleton<BacktestToLivePromoter>();
                services.AddSingleton<IPromotionRecordStore>(_ => new JsonlPromotionRecordStore(
                    Path.Combine(rootPath, "promotions"),
                    NullLogger<JsonlPromotionRecordStore>.Instance));
                services.AddSingleton<PromotionService>(sp => new PromotionService(
                    sp.GetRequiredService<IStrategyRepository>(),
                    sp.GetRequiredService<BacktestToLivePromoter>(),
                    sp.GetRequiredService<IPromotionRecordStore>(),
                    NullLogger<PromotionService>.Instance,
                    operatorControls: sp.GetRequiredService<ExecutionOperatorControlService>(),
                    auditTrail: sp.GetRequiredService<ExecutionAuditTrailService>()));
            },
            mapExecutionApi: true,
            mapPromotionApi: true,
            currentUserPermissions: UserPermission.ManageStrategies | UserPermission.ExecuteTrades | UserPermission.ManageOrders);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-api-backtest",
            strategyId: "strat-api-paper",
            strategyName: "API Paper Continuity",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 25, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities").Complete(BuildBacktestResultWithSymbol("AAPL")) with
            {
                RunId = "run-api-backtest",
                AuditReference = "audit-run-api-backtest"
            });

        var client = app.GetTestClient();

        var approveResponse = await client.PostAsync(
            UiApiRoutes.PromotionApprove,
            JsonContent(new PromotionApprovalRequest(
                RunId: "run-api-backtest",
                ReviewNotes: "Backtest qualifies for paper acceptance.",
                ApprovedBy: "spoofed",
                ApprovalReason: "Backtest evidence approved for paper cockpit validation.",
                ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper))));
        approveResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var approval = await ReadAsync<PromotionDecisionResult>(approveResponse);
        approval.Success.Should().BeTrue();
        approval.NewRunId.Should().NotBeNullOrWhiteSpace();
        approval.ApprovedBy.Should().Be("ops-user");

        var wrongSessionResponse = await client.PostAsync(
            UiApiRoutes.ExecutionSessionCreate,
            JsonContent(new CreatePaperSessionRequest(
                StrategyId: "strat-other",
                StrategyName: "Unrelated Strategy",
                InitialCash: 100_000m,
                Symbols: ["MSFT"])));
        wrongSessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var mismatchReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            UiApiRoutes.WorkstationTradingReadiness,
            ServerJsonOptions);
        mismatchReadiness.Should().NotBeNull();
        mismatchReadiness!.ActiveSession.Should().BeNull();
        mismatchReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "session" &&
            gate.Status == TradingAcceptanceGateStatusDto.Blocked &&
            gate.RunId == approval.NewRunId);
        mismatchReadiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"paper-session-mismatch-{approval.NewRunId!.ToLowerInvariant()}" &&
            item.Tone == OperatorWorkItemToneDto.Critical &&
            item.Detail.Contains("strat-api-paper", StringComparison.OrdinalIgnoreCase));

        var sessionResponse = await client.PostAsync(
            UiApiRoutes.ExecutionSessionCreate,
            JsonContent(new CreatePaperSessionRequest(
                StrategyId: "strat-api-paper",
                StrategyName: "API Paper Continuity",
                InitialCash: 125_000m,
                Symbols: ["AAPL"])));
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await ReadAsync<PaperSessionSummaryDto>(sessionResponse);

        var persistence = app.Services.GetRequiredService<PaperSessionPersistenceService>();
        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-api-1", "AAPL", 10m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-api-1", "AAPL", 10m, 190m));

        var controls = app.Services.GetRequiredService<ExecutionOperatorControlService>();
        await controls.SetDefaultPositionLimitAsync(
            100_000m,
            "risk.lead",
            "Paper cockpit risk budget accepted for API workflow.");

        var replayResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionReplay.Replace("{sessionId}", session.SessionId, StringComparison.Ordinal));
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await ReadAsync<PaperSessionReplayVerificationDto>(replayResponse);
        replay.IsConsistent.Should().BeTrue();
        replay.ComparedFillCount.Should().Be(1);
        replay.ComparedOrderCount.Should().Be(1);
        replay.VerificationAuditId.Should().NotBeNullOrWhiteSpace();

        var readyReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            UiApiRoutes.WorkstationTradingReadiness,
            ServerJsonOptions);
        readyReadiness.Should().NotBeNull();
        readyReadiness!.ActiveSession.Should().NotBeNull();
        readyReadiness.ActiveSession!.SessionId.Should().Be(session.SessionId);
        readyReadiness.Replay.Should().NotBeNull();
        readyReadiness.Replay!.VerificationAuditId.Should().Be(replay.VerificationAuditId);
        readyReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == replay.VerificationAuditId);
        readyReadiness.Controls.ExplainableEvidenceCount.Should().Be(1);
        readyReadiness.Controls.UnexplainedEvidenceCount.Should().Be(0);
        readyReadiness.Controls.RecentEvidence.Should().ContainSingle(evidence =>
            evidence.Action == "DefaultPositionLimitUpdated" &&
            evidence.Actor == "risk.lead" &&
            evidence.Scope == "default-position-limit" &&
            evidence.Reason.Contains("risk budget", StringComparison.OrdinalIgnoreCase) &&
            evidence.IsExplained);
        readyReadiness.Promotion.Should().NotBeNull();
        readyReadiness.Promotion!.SourceRunId.Should().Be("run-api-backtest");
        readyReadiness.Promotion.TargetRunId.Should().Be(approval.NewRunId);
        readyReadiness.Promotion.ApprovedBy.Should().Be("ops-user");
        readyReadiness.Promotion.AuditReference.Should().Be(approval.AuditReference);
        readyReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "promotion" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.RunId == "run-api-backtest" &&
            gate.AuditReference == approval.AuditReference);

        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-api-2", "AAPL", 2m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-api-2", "AAPL", 2m, 191m));

        var staleReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            UiApiRoutes.WorkstationTradingReadiness,
            ServerJsonOptions);
        staleReadiness.Should().NotBeNull();
        staleReadiness!.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.AuditReference == replay.VerificationAuditId &&
            gate.Detail.Contains("stale", StringComparison.OrdinalIgnoreCase));
        staleReadiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}" &&
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.Tone == OperatorWorkItemToneDto.Warning);

        var recoveryResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionReplay.Replace("{sessionId}", session.SessionId, StringComparison.Ordinal));
        recoveryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recovery = await ReadAsync<PaperSessionReplayVerificationDto>(recoveryResponse);
        recovery.IsConsistent.Should().BeTrue();
        recovery.ComparedFillCount.Should().Be(2);
        recovery.ComparedOrderCount.Should().Be(2);
        recovery.VerificationAuditId.Should().NotBe(replay.VerificationAuditId);

        var recoveredReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            UiApiRoutes.WorkstationTradingReadiness,
            ServerJsonOptions);
        recoveredReadiness.Should().NotBeNull();
        recoveredReadiness!.Replay.Should().NotBeNull();
        recoveredReadiness.Replay!.VerificationAuditId.Should().Be(recovery.VerificationAuditId);
        recoveredReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == recovery.VerificationAuditId);
        recoveredReadiness.WorkItems.Should().NotContain(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}");

        var detailResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionById.Replace("{sessionId}", session.SessionId, StringComparison.Ordinal));
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await ReadAsync<PaperSessionDetailDto>(detailResponse);
        detail.Symbols.Should().Equal("AAPL");
        detail.OrderHistory.Should().HaveCount(2);
        detail.LedgerEntryCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldRefreshAfterReplayTrustAndPromotionStateChanges()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-readiness-refresh", Guid.NewGuid().ToString("N"));
        var automationRoot = Path.Combine(rootPath, "provider-validation", "_automation");
        WriteReadyDk1Packet(automationRoot);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            RegisterPromotionServices(services, Path.Combine(rootPath, "promotions"));
            services.AddSingleton(new Dk1TrustGateReadinessOptions(automationRoot));
            services.AddSingleton<Dk1TrustGateReadinessService>();
            services.AddSingleton(_ => new ExecutionAuditTrailService(
                new ExecutionAuditTrailOptions(Path.Combine(rootPath, "audit")),
                NullLogger<ExecutionAuditTrailService>.Instance));
            services.AddSingleton<IPaperSessionStore>(_ => new JsonlFilePaperSessionStore(
                Path.Combine(rootPath, "sessions"),
                NullLogger<JsonlFilePaperSessionStore>.Instance));
            services.AddSingleton<PaperSessionPersistenceService>(sp => new PaperSessionPersistenceService(
                NullLogger<PaperSessionPersistenceService>.Instance,
                sp.GetRequiredService<IPaperSessionStore>(),
                sp.GetRequiredService<ExecutionAuditTrailService>()));
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-refresh-backtest",
            strategyId: "strat-refresh",
            strategyName: "Refresh Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 28, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities").Complete(BuildBacktestResultWithSymbol("AAPL")));

        var persistence = app.Services.GetRequiredService<PaperSessionPersistenceService>();
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto("strat-refresh", "Refresh Strategy", 100_000m, ["AAPL"]));
        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("refresh-order-1", "AAPL", 10m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("refresh-order-1", "AAPL", 10m, 190m));
        var verification = await persistence.VerifyReplayAsync(session.SessionId);

        var client = app.GetTestClient();
        var before = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(UiApiRoutes.WorkstationTradingReadiness, ServerJsonOptions);
        before.Should().NotBeNull();
        before!.Replay!.VerificationAuditId.Should().Be(verification!.VerificationAuditId);
        before.Promotion!.ApprovalStatus.Should().BeNull();
        before.TrustGate.OperatorSignoffStatus.Should().Be("pending");

        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("refresh-order-2", "AAPL", 5m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("refresh-order-2", "AAPL", 5m, 191m));
        await app.Services.GetRequiredService<IPromotionRecordStore>().AppendAsync(new StrategyPromotionRecord(
            PromotionId: "promotion-refresh-backtest",
            StrategyId: "strat-refresh",
            StrategyName: "Refresh Strategy",
            SourceRunType: RunType.Backtest,
            TargetRunType: RunType.Paper,
            SourceRunId: "run-refresh-backtest",
            TargetRunId: "run-refresh-paper",
            QualifyingSharpe: 1.2d,
            QualifyingMaxDrawdownPercent: 0.05m,
            QualifyingTotalReturn: 0.12m,
            Decision: PromotionDecisionKinds.Approved,
            PromotedAt: new DateTimeOffset(2026, 4, 28, 16, 0, 0, TimeSpan.Zero),
            ApprovalReason: "Ready after review.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            AuditReference: "audit-refresh-promotion",
            ApprovedBy: "ops.refresh"));
        WriteReadyDk1Packet(
            automationRoot,
            """
            {
              "requiredOwners": [ "Data Operations", "Provider Reliability", "Trading" ],
              "signedOwners": [ "Data Operations", "Provider Reliability", "Trading" ],
              "status": "signed",
              "requiredBeforeDk1Exit": true,
              "signedAtUtc": "2026-04-28T16:00:00Z"
            }
            """);

        var after = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(UiApiRoutes.WorkstationTradingReadiness, ServerJsonOptions);
        after.Should().NotBeNull();
        after!.ActiveSession!.OrderCount.Should().Be(2);
        after.Replay!.ComparedOrderCount.Should().Be(1);
        after.AcceptanceGates.Should().ContainSingle(g => g.GateId == "replay" && g.Status == TradingAcceptanceGateStatusDto.ReviewRequired);
        after.Promotion!.ApprovalStatus.Should().Be(PromotionDecisionKinds.Approved);
        after.TrustGate.OperatorSignoffStatus.Should().Be("signed");
        after.SnapshotVersion.Should().NotBe(before.SnapshotVersion);
        after.SnapshotMaterializedAt.Should().BeAfter(before.SnapshotMaterializedAt);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldNotUseStalePromotionHistoryForLatestRun()
    {
        var promotionRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "promotion-history",
            Guid.NewGuid().ToString("N"));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            RegisterPromotionServices(services, promotionRoot);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-stale-backtest",
            strategyId: "stale-strategy",
            strategyName: "Stale Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 22, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities").Complete(BuildBacktestResultWithSymbol("MSFT")));

        await app.Services.GetRequiredService<IPromotionRecordStore>().AppendAsync(new StrategyPromotionRecord(
            PromotionId: "promotion-stale-backtest",
            StrategyId: "stale-strategy",
            StrategyName: "Stale Strategy",
            SourceRunType: RunType.Backtest,
            TargetRunType: RunType.Paper,
            SourceRunId: "run-stale-backtest",
            TargetRunId: "run-stale-paper",
            QualifyingSharpe: 1.2d,
            QualifyingMaxDrawdownPercent: 0.05m,
            QualifyingTotalReturn: 0.12m,
            Decision: PromotionDecisionKinds.Approved,
            PromotedAt: new DateTimeOffset(2026, 4, 22, 15, 0, 0, TimeSpan.Zero),
            ApprovalReason: "Older promotion was approved for a previous cockpit run.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            AuditReference: "audit-stale-promotion",
            ApprovedBy: "ops.archive"));

        await store.RecordRunAsync(BuildRun(
            runId: "run-current-backtest",
            strategyId: "current-strategy",
            strategyName: "Current Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 24, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities").Complete(BuildBacktestResultWithSymbol("AAPL")));

        var readiness = await app
            .GetTestClient()
            .GetFromJsonAsync<TradingOperatorReadinessDto>(
                "/api/workstation/trading/readiness",
                ServerJsonOptions);

        readiness.Should().NotBeNull();
        readiness!.Promotion.Should().NotBeNull();
        readiness.Promotion!.SourceRunId.Should().Be("run-current-backtest");
        readiness.Promotion.ApprovalStatus.Should().BeNull();
        readiness.Promotion.AuditReference.Should().Be("audit-run-current-backtest");
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "promotion" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.RunId == "run-current-backtest" &&
            gate.AuditReference == "audit-run-current-backtest");
        readiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "promotion-trace-incomplete-run-current-backtest" &&
            item.Kind == OperatorWorkItemKindDto.PromotionReview &&
            item.AuditReference == "audit-run-current-backtest");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldRequireReplayRefreshWhenSessionChangesAfterVerification()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-readiness-stale-replay", Guid.NewGuid().ToString("N"));

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(_ => new ExecutionAuditTrailService(
                new ExecutionAuditTrailOptions(Path.Combine(rootPath, "audit")),
                NullLogger<ExecutionAuditTrailService>.Instance));
            services.AddSingleton<IPaperSessionStore>(_ => new JsonlFilePaperSessionStore(
                Path.Combine(rootPath, "sessions"),
                NullLogger<JsonlFilePaperSessionStore>.Instance));
            services.AddSingleton<PaperSessionPersistenceService>(sp => new PaperSessionPersistenceService(
                NullLogger<PaperSessionPersistenceService>.Instance,
                sp.GetRequiredService<IPaperSessionStore>(),
                sp.GetRequiredService<ExecutionAuditTrailService>()));
        });

        var client = app.GetTestClient();
        var persistence = app.Services.GetRequiredService<PaperSessionPersistenceService>();

        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-stale-replay",
            StrategyName: "Stale Replay",
            InitialCash: 100_000m,
            Symbols: ["AAPL", "MSFT"]));

        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-before-replay", "AAPL", 10m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-before-replay", "AAPL", 10m, 190m));

        var verification = await persistence.VerifyReplayAsync(session.SessionId);
        verification.Should().NotBeNull();
        verification!.VerificationAuditId.Should().NotBeNullOrWhiteSpace();

        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-after-replay", "MSFT", 5m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-after-replay", "MSFT", 5m, 320m));

        var staleReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            "/api/workstation/trading/readiness",
            ServerJsonOptions);

        staleReadiness.Should().NotBeNull();
        staleReadiness!.ActiveSession.Should().NotBeNull();
        staleReadiness.ActiveSession!.SessionId.Should().Be(session.SessionId);
        staleReadiness.ActiveSession.FillCount.Should().Be(2);
        staleReadiness.ActiveSession.OrderCount.Should().Be(2);
        staleReadiness.ActiveSession.LedgerEntryCount.Should().BeGreaterThan(verification.ComparedLedgerEntryCount);
        staleReadiness.Replay.Should().NotBeNull();
        staleReadiness.Replay!.ComparedFillCount.Should().Be(1);
        staleReadiness.Replay.ComparedOrderCount.Should().Be(1);
        staleReadiness.Replay.VerificationAuditId.Should().Be(verification.VerificationAuditId);
        staleReadiness.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.ReviewRequired);
        staleReadiness.ReadyForPaperOperation.Should().BeFalse();
        staleReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.AuditReference == verification.VerificationAuditId &&
            gate.Detail.Contains("stale", StringComparison.OrdinalIgnoreCase));
        staleReadiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}" &&
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.Tone == OperatorWorkItemToneDto.Warning &&
            item.AuditReference == verification.VerificationAuditId &&
            item.Detail.Contains("orders active=2, verified=1", StringComparison.OrdinalIgnoreCase));

        var staleInbox = await client.GetFromJsonAsync<OperatorInboxDto>(
            "/api/workstation/operator/inbox",
            ServerJsonOptions);
        staleInbox.Should().NotBeNull();
        staleInbox!.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}" &&
            item.AuditReference == verification.VerificationAuditId &&
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.Tone == OperatorWorkItemToneDto.Warning);

        var refreshedVerification = await persistence.VerifyReplayAsync(session.SessionId);
        refreshedVerification.Should().NotBeNull();
        refreshedVerification!.ComparedFillCount.Should().Be(2);
        refreshedVerification.ComparedOrderCount.Should().Be(2);
        refreshedVerification.VerificationAuditId.Should().NotBe(verification.VerificationAuditId);

        var recoveredReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            "/api/workstation/trading/readiness",
            ServerJsonOptions);
        recoveredReadiness.Should().NotBeNull();
        recoveredReadiness!.Replay.Should().NotBeNull();
        recoveredReadiness.Replay!.VerificationAuditId.Should().Be(refreshedVerification.VerificationAuditId);
        recoveredReadiness.Replay.ComparedFillCount.Should().Be(2);
        recoveredReadiness.Replay.ComparedOrderCount.Should().Be(2);
        recoveredReadiness.OverallStatus.Should().NotBe(TradingAcceptanceGateStatusDto.ReviewRequired);
        recoveredReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == refreshedVerification.VerificationAuditId);
        recoveredReadiness.WorkItems.Should().NotContain(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}");

        var recoveredInbox = await client.GetFromJsonAsync<OperatorInboxDto>(
            "/api/workstation/operator/inbox",
            ServerJsonOptions);
        recoveredInbox.Should().NotBeNull();
        recoveredInbox!.WorkItems.Should().NotContain(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldFlagUnexplainedRiskControlAuditEvidence()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-readiness-risk", Guid.NewGuid().ToString("N"));

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(_ => new ExecutionAuditTrailService(
                new ExecutionAuditTrailOptions(Path.Combine(rootPath, "audit")),
                NullLogger<ExecutionAuditTrailService>.Instance));
        });

        await app.Services.GetRequiredService<ExecutionAuditTrailService>().RecordAsync(new ExecutionAuditEntry(
            AuditId: "audit-risk-missing-context",
            Category: "Order",
            Action: "OrderRejected",
            Outcome: "Rejected",
            OccurredAt: new DateTimeOffset(2026, 4, 26, 18, 0, 0, TimeSpan.Zero)));

        var readiness = await app
            .GetTestClient()
            .GetFromJsonAsync<TradingOperatorReadinessDto>(
                "/api/workstation/trading/readiness",
                ServerJsonOptions);

        readiness.Should().NotBeNull();
        readiness!.Controls.UnexplainedEvidenceCount.Should().Be(1);
        readiness.Controls.ExplainableEvidenceCount.Should().Be(0);
        var evidence = readiness.Controls.RecentEvidence.Should().ContainSingle().Which;
        evidence.AuditId.Should().Be("audit-risk-missing-context");
        evidence.IsExplained.Should().BeFalse();
        evidence.MissingFields.Should().BeEquivalentTo(["actor", "scope", "reason"]);
        readiness.Controls.ExplainabilityWarnings.Should().ContainSingle(warning =>
            warning.Contains("OrderRejected", StringComparison.OrdinalIgnoreCase) &&
            warning.Contains("actor, scope, reason", StringComparison.OrdinalIgnoreCase));

        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "audit-controls" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.AuditReference == "audit-risk-missing-context");
        readiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "execution-evidence-incomplete" &&
            item.Kind == OperatorWorkItemKindDto.ExecutionControl &&
            item.AuditReference == "audit-risk-missing-context" &&
            item.Detail.Contains("actor, scope, reason", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldExposeRequiredContractFieldsAndDegradeWhenEvidenceIsMissing()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-readiness-contract", Guid.NewGuid().ToString("N"));
        var automationRoot = Path.Combine(rootPath, "provider-validation", "_automation");

        WriteReadyDk1Packet(
            automationRoot,
            packetStatus: "not-ready",
            blockersJson: """
                [
                  "DK1 trust packet requires refreshed provider parity evidence before operator review."
                ]
                """);

        try
        {
            await using var app = await CreateAppAsync(services =>
            {
                RegisterRunReadServices(services);
                RegisterPromotionServices(services, Path.Combine(rootPath, "promotions"));
                services.AddSingleton(new Dk1TrustGateReadinessOptions(automationRoot));
                services.AddSingleton<Dk1TrustGateReadinessService>();
                services.AddSingleton(_ => new ExecutionAuditTrailService(
                    new ExecutionAuditTrailOptions(Path.Combine(rootPath, "audit")),
                    NullLogger<ExecutionAuditTrailService>.Instance));
                services.AddSingleton<IPaperSessionStore>(_ => new JsonlFilePaperSessionStore(
                    Path.Combine(rootPath, "sessions"),
                    NullLogger<JsonlFilePaperSessionStore>.Instance));
                services.AddSingleton<PaperSessionPersistenceService>(sp => new PaperSessionPersistenceService(
                    NullLogger<PaperSessionPersistenceService>.Instance,
                    sp.GetRequiredService<IPaperSessionStore>(),
                    sp.GetRequiredService<ExecutionAuditTrailService>()));
            });

            var store = app.Services.GetRequiredService<IStrategyRepository>();
            await store.RecordRunAsync(BuildRun(
                runId: "run-contract-backtest",
                strategyId: "strat-contract",
                strategyName: "Contract Coverage",
                runType: RunType.Backtest,
                startedAt: new DateTimeOffset(2026, 4, 28, 13, 0, 0, TimeSpan.Zero),
                datasetReference: "dataset/us/equities",
                feedReference: "synthetic:equities").Complete(BuildBacktestResultWithSymbol("AAPL")));

            var session = await app.Services.GetRequiredService<PaperSessionPersistenceService>().CreateSessionAsync(new CreatePaperSessionDto(
                StrategyId: "strat-contract",
                StrategyName: "Contract Coverage",
                InitialCash: 125_000m,
                Symbols: ["AAPL"]));

            var client = app.GetTestClient();
            using var response = await client.GetAsync(UiApiRoutes.WorkstationTradingReadiness);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var payload = await ReadJsonAsync(client, UiApiRoutes.WorkstationTradingReadiness);
            var root = payload.RootElement;
            root.TryGetProperty("overallStatus", out var overallStatus).Should().BeTrue();
            root.TryGetProperty("readyForPaperOperation", out var readyForPaperOperation).Should().BeTrue();
            root.TryGetProperty("trustGate", out var trustGate).Should().BeTrue();
            root.TryGetProperty("replay", out var replay).Should().BeTrue();
            root.TryGetProperty("promotion", out var promotion).Should().BeTrue();
            root.TryGetProperty("acceptanceGates", out var acceptanceGates).Should().BeTrue();
            root.TryGetProperty("workItems", out var workItems).Should().BeTrue();
            overallStatus.ValueKind.Should().Be(JsonValueKind.String);
            readyForPaperOperation.ValueKind.Should().Be(JsonValueKind.False);
            trustGate.ValueKind.Should().Be(JsonValueKind.Object);
            replay.ValueKind.Should().Be(JsonValueKind.Null);
            promotion.ValueKind.Should().Be(JsonValueKind.Object);
            acceptanceGates.ValueKind.Should().Be(JsonValueKind.Array);
            workItems.ValueKind.Should().Be(JsonValueKind.Array);

            var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
                UiApiRoutes.WorkstationTradingReadiness,
                ServerJsonOptions);
            readiness.Should().NotBeNull();

            readiness!.ActiveSession.Should().NotBeNull();
            readiness.ActiveSession!.SessionId.Should().Be(session.SessionId);
            readiness.TrustGate.Should().NotBeNull();
            readiness.TrustGate.Status.Should().Be("not-ready");
            readiness.TrustGate.Blockers.Should().NotBeEmpty();
            readiness.Replay.Should().BeNull();
            readiness.Promotion.Should().NotBeNull();
            readiness.Promotion!.RequiresReview.Should().BeTrue();
            readiness.Promotion.ApprovalStatus.Should().BeNull();
            readiness.Promotion.State.Should().NotBeNullOrWhiteSpace();
            readiness.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.Blocked);

            readiness.AcceptanceGates.Should().ContainSingle(gate =>
                gate.GateId == "dk1-trust" &&
                gate.Status == TradingAcceptanceGateStatusDto.Blocked);
            readiness.AcceptanceGates.Should().ContainSingle(gate =>
                gate.GateId == "replay" &&
                gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
                gate.SessionId == session.SessionId);
            readiness.AcceptanceGates.Should().ContainSingle(gate =>
                gate.GateId == "promotion" &&
                gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
                gate.RunId == "run-contract-backtest");
            readiness.WorkItems.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.ProviderTrustGate &&
                item.Tone == OperatorWorkItemToneDto.Critical);
            readiness.WorkItems.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.PaperReplay &&
                item.Tone == OperatorWorkItemToneDto.Warning);
            readiness.WorkItems.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.PromotionReview &&
                item.Tone == OperatorWorkItemToneDto.Warning);

            // Contract-level matrix for required readiness dimensions and deterministic critical-before-warning triage.
            trustGate.TryGetProperty("status", out var trustGateStatus).Should().BeTrue();
            trustGate.TryGetProperty("operatorSignoffStatus", out var trustGateSignoffStatus).Should().BeTrue();
            trustGateStatus.ValueKind.Should().Be(JsonValueKind.String);
            trustGateSignoffStatus.ValueKind.Should().Be(JsonValueKind.String);
            promotion.TryGetProperty("approvalChecklist", out var promotionChecklist).Should().BeTrue();
            promotionChecklist.ValueKind.Should().Be(JsonValueKind.Array);
            root.TryGetProperty("activeSession", out var activeSession).Should().BeTrue();
            activeSession.ValueKind.Should().Be(JsonValueKind.Object);
            root.TryGetProperty("brokerageSync", out var brokerageSync).Should().BeTrue();
            brokerageSync.ValueKind.Should().Be(JsonValueKind.Null);
            var readinessWorkItems = readiness.WorkItems.ToList();
            var firstCritical = readinessWorkItems.FindIndex(item => item.Tone == OperatorWorkItemToneDto.Critical);
            var firstWarning = readinessWorkItems.FindIndex(item => item.Tone == OperatorWorkItemToneDto.Warning);
            firstCritical.Should().BeGreaterThanOrEqualTo(0);
            firstWarning.Should().BeGreaterThanOrEqualTo(0);
            firstCritical.Should().BeLessThan(firstWarning, "critical readiness blockers should be triaged before warning items");
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadinessWithoutRegisteredReadinessService_ShouldUseSharedReadinessBuilder()
    {
        await using var app = await CreateAppAsync();

        var readiness = await app
            .GetTestClient()
            .GetFromJsonAsync<TradingOperatorReadinessDto>(
                "/api/workstation/trading/readiness",
                ServerJsonOptions);

        readiness.Should().NotBeNull();
        readiness!.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.Blocked);
        readiness.ReadyForPaperOperation.Should().BeFalse();
        readiness.AcceptanceGates
            .Select(static gate => gate.GateId)
            .Should()
            .Equal(
                "session",
                "replay",
                "audit-controls",
                "risk-rules",
                "promotion",
                "dk1-trust",
                "report-pack",
                "reconciliation",
                "brokerage-sync");
        readiness.ReportPack.Should().NotBeNull();
        readiness.ReportPack!.Status.Should().Be(TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.EvidenceCompleteness.Should().NotBeNull();
        readiness.EvidenceCompleteness!.ReviewGateIds.Should().Contain("report-pack");
        readiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "paper-session-missing" &&
            item.Label == "No active paper session" &&
            item.Tone == OperatorWorkItemToneDto.Critical);
        readiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "promotion-decision-missing" &&
            item.Label == "Promotion decision required");
    }


    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_WithFundAccountId_ShouldSurfaceBrokerageSyncFreshnessAndDivergencePosture()
    {
        var fundAccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-readiness", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            var status = await brokerageSync.RunSyncAsync(
                fundAccountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-404", "ops-review"));

            await using var app = await CreateAppAsync(services => services.AddSingleton(brokerageSync));

            var readiness = await app
                .GetTestClient()
                .GetFromJsonAsync<TradingOperatorReadinessDto>(
                    $"/api/workstation/trading/readiness?fundAccountId={fundAccountId:D}",
                    ServerJsonOptions);

            readiness.Should().NotBeNull();
            readiness!.BrokerageSync.Should().NotBeNull();
            readiness.BrokerageSync!.FundAccountId.Should().Be(fundAccountId);
            readiness.BrokerageSync.Health.Should().Be(WorkstationBrokerageSyncHealth.Failed);
            readiness.BrokerageSync.SecurityMissingCount.Should().BeGreaterThanOrEqualTo(0);
            readiness.BrokerageSync.Warnings.Should().Contain(w => w.Contains("missing", StringComparison.OrdinalIgnoreCase));
            readiness.WorkItems.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == fundAccountId &&
                item.Workspace == "Settings" &&
                item.TargetRoute == "/settings#alpaca-provider-setup");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldProjectTradingReadinessWorkItemsWithNavigation()
    {
        await using var app = await CreateAppAsync();

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.CriticalCount.Should().BeGreaterThanOrEqualTo(1);
        inbox.ReviewCount.Should().Be(inbox.CriticalCount + inbox.WarningCount);
        inbox.Items.Should().ContainSingle(item =>
            item.WorkItemId == "paper-session-missing" &&
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.Workspace == "Trading" &&
            item.TargetRoute == UiApiRoutes.WorkstationTradingReadiness &&
            item.TargetPageTag == "TradingShell");
        inbox.Items.Should().ContainSingle(item =>
            item.WorkItemId == "promotion-decision-missing" &&
            item.TargetRoute == UiApiRoutes.WorkstationTradingReadiness &&
            item.TargetPageTag == "TradingShell");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldKeepHighSeverityRoutesResolvable()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-inbox-route-resolution-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        await app.Services.GetRequiredService<IReconciliationRunService>().RunAsync(new ReconciliationRunRequest(runId));

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        inbox.Should().NotBeNull();
        var routeCriticalItems = inbox!.Items
            .Where(item => item.Tone is OperatorWorkItemToneDto.Critical or OperatorWorkItemToneDto.Warning)
            .ToArray();

        routeCriticalItems.Should().NotBeEmpty();
        routeCriticalItems.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Workspace) &&
            !string.IsNullOrWhiteSpace(item.TargetPageTag) &&
            !string.IsNullOrWhiteSpace(item.TargetRoute) &&
            item.TargetRoute.StartsWith("/", StringComparison.Ordinal));

        routeCriticalItems.Should().Contain(item =>
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.TargetRoute == UiApiRoutes.WorkstationTradingReadiness &&
            item.TargetPageTag == "TradingShell");
        routeCriticalItems.Should().Contain(item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue &&
            item.TargetPageTag == "AccountingShell");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldReturnDeterministicOrderingAcrossPollingRequests()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var first = await client.GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);
        var second = await client.GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.Items.Select(item => item.WorkItemId)
            .Should()
            .Equal(second!.Items.Select(item => item.WorkItemId));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldMatchReadinessSeverityAcrossWave2Lanes()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
            services.AddSingleton<ReconciliationGovernanceService>();
        });

        var runId = $"run-severity-parity-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        var reconciliation = await app.Services
            .GetRequiredService<IReconciliationRunService>()
            .RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var client = app.GetTestClient();
        var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            "/api/workstation/trading/readiness",
            ServerJsonOptions);
        var inbox = await client.GetFromJsonAsync<OperatorInboxDto>(
            "/api/workstation/operator/inbox",
            ServerJsonOptions);

        readiness.Should().NotBeNull();
        inbox.Should().NotBeNull();

        var readinessSeverityByCategory = NormalizeReadinessSeverity(readiness!.WorkItems);
        var inboxSeverityByCategory = NormalizeInboxSeverity(inbox!.Items);

        AssertCategoryParity("replay", readinessSeverityByCategory, inboxSeverityByCategory, "paper-session-missing");
        AssertCategoryParity("controls", readinessSeverityByCategory, inboxSeverityByCategory, "execution-evidence-incomplete");
        AssertCategoryParity("promotion-traceability", readinessSeverityByCategory, inboxSeverityByCategory, "promotion-decision-missing");
        AssertCategoryParity("reconciliation", readinessSeverityByCategory, inboxSeverityByCategory, "reconciliation-policy-");
        AssertCategoryParity("dk1-trust-gate", readinessSeverityByCategory, inboxSeverityByCategory, "dk1-trust-packet-unavailable");

        static Dictionary<string, int> NormalizeReadinessSeverity(IEnumerable<OperatorWorkItemDto> items)
            => items.GroupBy(static item => GetCategory(item.WorkItemId))
                .ToDictionary(static group => group.Key, static group => group.Max(item => ToSeverityRank(item.Tone)), StringComparer.OrdinalIgnoreCase);

        static Dictionary<string, int> NormalizeInboxSeverity(IEnumerable<OperatorWorkItemDto> items)
            => items.GroupBy(static item => GetCategory(item.WorkItemId))
                .ToDictionary(static group => group.Key, static group => group.Max(item => ToSeverityRank(item.Tone)), StringComparer.OrdinalIgnoreCase);

        static string GetCategory(string workItemId)
        {
            if (workItemId.StartsWith("paper-session-", StringComparison.OrdinalIgnoreCase) ||
                workItemId.StartsWith("paper-replay-", StringComparison.OrdinalIgnoreCase))
            {
                return "replay";
            }

            if (workItemId.StartsWith("execution-", StringComparison.OrdinalIgnoreCase))
            {
                return "controls";
            }

            if (workItemId.StartsWith("promotion-", StringComparison.OrdinalIgnoreCase))
            {
                return "promotion-traceability";
            }

            if (workItemId.StartsWith("reconciliation-policy-", StringComparison.OrdinalIgnoreCase) ||
                workItemId.StartsWith("reconciliation-break-", StringComparison.OrdinalIgnoreCase))
            {
                return "reconciliation";
            }

            if (workItemId.StartsWith("dk1-trust-", StringComparison.OrdinalIgnoreCase))
            {
                return "dk1-trust-gate";
            }

            return $"other:{workItemId}";
        }

        static int ToSeverityRank(OperatorWorkItemToneDto tone) =>
            tone switch
            {
                OperatorWorkItemToneDto.Critical => 2,
                OperatorWorkItemToneDto.Warning => 1,
                _ => 0
            };

        static void AssertCategoryParity(
            string category,
            IReadOnlyDictionary<string, int> readiness,
            IReadOnlyDictionary<string, int> inbox,
            string expectedAnchorId)
        {
            readiness.ContainsKey(category).Should().BeTrue(
                $"readiness lane should expose {category} via anchor '{expectedAnchorId}'");
            inbox.ContainsKey(category).Should().BeTrue(
                $"inbox lane should expose {category} via anchor '{expectedAnchorId}'");

            var readinessSeverity = readiness[category];
            var inboxSeverity = inbox[category];
            inboxSeverity.Should().Be(readinessSeverity,
                $"severity drift detected for {category} (anchor '{expectedAnchorId}')");
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldEmitOnlyLatestRunActionableReviewPacketBlockers()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var olderRunId = $"run-inbox-review-packet-older-{Guid.NewGuid():N}";
        var newestRunId = $"run-inbox-review-packet-newest-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();

        await store.RecordRunAsync(BuildContinuityRun(olderRunId) with
        {
            StartedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero)
        });

        await store.RecordRunAsync(BuildReconciliationReadyRun(newestRunId) with
        {
            StartedAt = new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 3, 21, 18, 30, 0, TimeSpan.Zero)
        });

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item =>
            item.WorkItemId == $"promotion-review-{newestRunId.ToLowerInvariant()}" &&
            item.Tone == OperatorWorkItemToneDto.Warning);
        inbox.Items.Should().NotContain(item =>
            item.WorkItemId == $"promotion-review-{olderRunId.ToLowerInvariant()}");

        var newestReviewPacket = await app
            .GetTestClient()
            .GetFromJsonAsync<StrategyRunReviewPacketDto>(
                $"/api/workstation/runs/{newestRunId}/review-packet",
                ServerJsonOptions);
        var olderReviewPacket = await app
            .GetTestClient()
            .GetFromJsonAsync<StrategyRunReviewPacketDto>(
                $"/api/workstation/runs/{olderRunId}/review-packet",
                ServerJsonOptions);

        newestReviewPacket.Should().NotBeNull();
        olderReviewPacket.Should().NotBeNull();
        newestReviewPacket!.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"promotion-review-{newestRunId.ToLowerInvariant()}" &&
            item.Tone == OperatorWorkItemToneDto.Warning);
        olderReviewPacket!.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"promotion-review-{olderRunId.ToLowerInvariant()}" &&
            item.Tone == OperatorWorkItemToneDto.Warning);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldIncludeReviewPacketBlockersFromRecentRuns()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var olderRunId = $"run-inbox-review-packet-older-{Guid.NewGuid():N}";
        var newestRunId = $"run-inbox-review-packet-newest-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();

        await store.RecordRunAsync(BuildReconciliationReadyRun(olderRunId) with
        {
            StartedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero)
        });

        await store.RecordRunAsync(BuildContinuityRun(newestRunId) with
        {
            StartedAt = new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 3, 21, 18, 30, 0, TimeSpan.Zero)
        });

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item => item.WorkItemId == "paper-session-missing");

        var newestReviewItem = inbox.Items.Should().ContainSingle(item =>
            item.WorkItemId == $"promotion-review-{newestRunId.ToLowerInvariant()}" &&
            item.Kind == OperatorWorkItemKindDto.PromotionReview).Which;
        var olderReviewItem = inbox.Items.Should().ContainSingle(item =>
            item.WorkItemId == $"promotion-review-{olderRunId.ToLowerInvariant()}" &&
            item.Kind == OperatorWorkItemKindDto.PromotionReview).Which;

        newestReviewItem.Tone.Should().Be(OperatorWorkItemToneDto.Warning);
        newestReviewItem.Workspace.Should().Be("Trading");
        newestReviewItem.RunId.Should().Be(newestRunId);
        newestReviewItem.TargetRoute.Should().Be(UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", newestRunId));
        newestReviewItem.TargetPageTag.Should().Be("TradingShell");
        olderReviewItem.Tone.Should().Be(OperatorWorkItemToneDto.Warning);
        olderReviewItem.Workspace.Should().Be("Trading");
        olderReviewItem.RunId.Should().Be(olderRunId);
        olderReviewItem.TargetRoute.Should().Be(UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", olderRunId));
        olderReviewItem.TargetPageTag.Should().Be("TradingShell");
        inbox.Items.Should().OnlyContain(item =>
            !item.WorkItemId.StartsWith("promotion-review-", StringComparison.OrdinalIgnoreCase) ||
            item.Tone == OperatorWorkItemToneDto.Warning ||
            item.Tone == OperatorWorkItemToneDto.Critical);
        inbox.WarningCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_WithFundAccountId_ShouldProjectBrokerageSyncWorkItem()
    {
        var fundAccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-inbox", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            var status = await brokerageSync.RunSyncAsync(
                fundAccountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-404", "ops-review"));
            status.Health.Should().Be(WorkstationBrokerageSyncHealth.Failed);

            await using var app = await CreateAppAsync(services =>
            {
                services.AddSingleton(brokerageSync);
            });

            var inbox = await app
                .GetTestClient()
                .GetFromJsonAsync<OperatorInboxDto>(
                    $"/api/workstation/operator/inbox?fundAccountId={fundAccountId:D}",
                    ServerJsonOptions);

            inbox.Should().NotBeNull();
            var syncItem = inbox!.Items.Should()
                .ContainSingle(item =>
                    item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                    item.FundAccountId == fundAccountId)
                .Which;
            syncItem.Tone.Should().Be(OperatorWorkItemToneDto.Critical);
            syncItem.Workspace.Should().Be("Settings");
            syncItem.TargetRoute.Should().Be("/settings#alpaca-provider-setup");
            syncItem.TargetPageTag.Should().Be("ProviderConnectionCenter");
            syncItem.Detail.Should().Contain("Alpaca credentials are missing.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }


    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_WithFundAccountId_ShouldProjectDegradedBrokerageSyncForIbkrAndRobinhoodAsWarning()
    {
        var ibkrAccountId = Guid.Parse("c7774ca1-08f7-4f89-a4c3-89387fb7cd31");
        var robinhoodAccountId = Guid.Parse("ec705f26-f620-4c71-b13a-3e8cce9018f9");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-inbox-degraded", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            await brokerageSync.RunSyncAsync(ibkrAccountId, new WorkstationBrokerageSyncRunRequestDto("ibkr", "DU-7788", "ops-review"));
            await brokerageSync.RunSyncAsync(robinhoodAccountId, new WorkstationBrokerageSyncRunRequestDto("robinhood", "RH-404", "ops-review"));

            await using var app = await CreateAppAsync(services => services.AddSingleton(brokerageSync));
            var client = app.GetTestClient();

            foreach (var accountId in new[] { ibkrAccountId, robinhoodAccountId })
            {
                var inbox = await client.GetFromJsonAsync<OperatorInboxDto>($"/api/workstation/operator/inbox?fundAccountId={accountId:D}", ServerJsonOptions);
                inbox.Should().NotBeNull();
                var syncItem = inbox!.Items.Should().ContainSingle(item =>
                    item.Kind == OperatorWorkItemKindDto.BrokerageSync && item.FundAccountId == accountId).Which;

                syncItem.Tone.Should().Be(OperatorWorkItemToneDto.Critical);
                syncItem.TargetPageTag.Should().Be("ProviderConnectionCenter");
                syncItem.TargetRoute.Should().Contain("-provider-setup");
                syncItem.Detail.Should().ContainEquivalentOf("credentials are missing");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FundAccountScope_ShouldReturnOnlyRequestedAccountWorkItemsAndPreserveRoutingMetadata()
    {
        var accountA = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var accountB = Guid.Parse("84fb987e-5323-4630-9a0c-d1c30c95fa47");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-scope", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            await brokerageSync.RunSyncAsync(accountA, new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-404", "ops-review"));
            await brokerageSync.RunSyncAsync(accountB, new WorkstationBrokerageSyncRunRequestDto("ibkr", "DU-7788", "ops-review"));

            await using var app = await CreateAppAsync(services => services.AddSingleton(brokerageSync));
            var client = app.GetTestClient();

            var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
                $"/api/workstation/trading/readiness?fundAccountId={accountA:D}",
                ServerJsonOptions);
            var inbox = await client.GetFromJsonAsync<OperatorInboxDto>(
                $"/api/workstation/operator/inbox?fundAccountId={accountA:D}",
                ServerJsonOptions);

            readiness.Should().NotBeNull();
            readiness!.BrokerageSync.Should().NotBeNull();
            readiness.BrokerageSync!.FundAccountId.Should().Be(accountA);
            readiness.WorkItems.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == accountA &&
                item.Workspace == "Settings" &&
                item.TargetRoute == "/settings#alpaca-provider-setup" &&
                item.TargetPageTag == "ProviderConnectionCenter");
            readiness.WorkItems.Should().NotContain(item => item.FundAccountId == accountB);

            inbox.Should().NotBeNull();
            inbox!.Items.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == accountA &&
                item.Workspace == "Settings" &&
                item.TargetRoute == "/settings#alpaca-provider-setup" &&
                item.TargetPageTag == "ProviderConnectionCenter");
            inbox.Items.Should().NotContain(item => item.FundAccountId == accountB);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FundAccountScope_WithUnknownAccount_ShouldNotFallBackToOtherScopedAccounts()
    {
        var knownAccount = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var unknownAccount = Guid.Parse("d8a69792-3313-4067-a311-a4de2133fe81");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-scope-unknown", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            await brokerageSync.RunSyncAsync(knownAccount, new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-404", "ops-review"));

            await using var app = await CreateAppAsync(services => services.AddSingleton(brokerageSync));
            var client = app.GetTestClient();

            var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
                $"/api/workstation/trading/readiness?fundAccountId={unknownAccount:D}",
                ServerJsonOptions);
            var inbox = await client.GetFromJsonAsync<OperatorInboxDto>(
                $"/api/workstation/operator/inbox?fundAccountId={unknownAccount:D}",
                ServerJsonOptions);

            readiness.Should().NotBeNull();
            readiness!.BrokerageSync.Should().NotBeNull("unknown scoped account should report an unlinked brokerage sync posture instead of inheriting another account's sync");
            readiness.BrokerageSync!.Health.ToString().Should().Be("Unlinked");
            readiness.BrokerageSync.ProviderId.Should().BeNull("unknown scoped account should not inherit another account's brokerage provider linkage");
            readiness.WorkItems.Should().NotContain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == knownAccount);

            inbox.Should().NotBeNull();
            inbox!.Items.Should().NotContain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == knownAccount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FundAccountScope_OperatorInboxScopedQueries_ShouldReturnPerAccountBrokerageSyncItemsWithoutCrossAccountLeakage()
    {
        var accountA = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var accountB = Guid.Parse("84fb987e-5323-4630-9a0c-d1c30c95fa47");
        var accountWithoutPendingItems = Guid.Parse("04ef2646-cad4-4775-8235-d63ef5a25d7f");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-scope-global", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            await brokerageSync.RunSyncAsync(accountA, new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-404", "ops-review"));
            await brokerageSync.RunSyncAsync(accountB, new WorkstationBrokerageSyncRunRequestDto("ibkr", "DU-7788", "ops-review"));

            await using var app = await CreateAppAsync(services => services.AddSingleton(brokerageSync));
            var client = app.GetTestClient();

            var accountAInbox = await client.GetFromJsonAsync<OperatorInboxDto>(
                $"/api/workstation/operator/inbox?fundAccountId={accountA:D}",
                ServerJsonOptions);
            var accountBInbox = await client.GetFromJsonAsync<OperatorInboxDto>(
                $"/api/workstation/operator/inbox?fundAccountId={accountB:D}",
                ServerJsonOptions);
            var emptyScopedInbox = await client.GetFromJsonAsync<OperatorInboxDto>(
                $"/api/workstation/operator/inbox?fundAccountId={accountWithoutPendingItems:D}",
                ServerJsonOptions);

            accountAInbox.Should().NotBeNull();
            accountAInbox!.Items.Should().Contain(item => item.Kind == OperatorWorkItemKindDto.BrokerageSync && item.FundAccountId == accountA);

            accountBInbox.Should().NotBeNull();
            accountBInbox!.Items.Should().Contain(item => item.Kind == OperatorWorkItemKindDto.BrokerageSync && item.FundAccountId == accountB);

            emptyScopedInbox.Should().NotBeNull();
            emptyScopedInbox!.Items.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == accountWithoutPendingItems &&
                item.Workspace == "Settings" &&
                item.TargetRoute == "/settings#provider-connection-center" &&
                item.TargetPageTag == "ProviderConnectionCenter");
            emptyScopedInbox.Items.Should().NotContain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId != accountWithoutPendingItems);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldIncludeOpenReconciliationBreaks()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-inbox-break-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        var reconciliation = await app.Services
            .GetRequiredService<IReconciliationRunService>()
            .RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.RunId == runId &&
            item.Workspace == "Accounting");
        var breakItem = inbox.Items.First(item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.RunId == runId &&
            item.Workspace == "Accounting");
        breakItem.WorkItemId.Should().StartWith("reconciliation-break-");
        breakItem.TargetRoute.Should().Be(UiApiRoutes.ReconciliationBreakQueue);
        breakItem.TargetPageTag.Should().Be("AccountingShell");
        breakItem.Detail.Should().Contain("The break is open");
        breakItem.Detail.Should().Contain("Exception route:");
        breakItem.Detail.Should().Contain("sign-off");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldIncludeInReviewReconciliationBreaks()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-inbox-review-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        var reconciliation = await app.Services
            .GetRequiredService<IReconciliationRunService>()
            .RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var breakId = $"{runId}:{reconciliation!.Breaks[0].CheckId}";
        var client = app.GetTestClient();
        var review = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/review",
            new ReviewReconciliationBreakRequest(
                BreakId: breakId,
                AssignedTo: "ops-review",
                ReviewedBy: "qa-review",
                ReviewNote: "Investigating the mismatch."));
        review.StatusCode.Should().Be(HttpStatusCode.OK);

        var inbox = await client.GetFromJsonAsync<OperatorInboxDto>(
            "/api/workstation/operator/inbox",
            ServerJsonOptions);

        inbox.Should().NotBeNull();
        var breakItem = inbox!.Items.First(item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.AuditReference == breakId);
        breakItem.Label.Should().Be("Reconciliation break in review");
        breakItem.TargetRoute.Should().Be(UiApiRoutes.ReconciliationBreakQueue);
        breakItem.TargetPageTag.Should().Be("AccountingShell");
        breakItem.Detail.Should().Contain("The break is in review");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ReadinessOnlyItems_ShouldExposeExpectedClassificationAndMetadata()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationBreakQueueRepository>(
                new FileReconciliationBreakQueueRepository(
                    Path.Combine(Path.GetTempPath(), "meridian-tests", "break-queue", Guid.NewGuid().ToString("N")),
                    NullLogger<FileReconciliationBreakQueueRepository>.Instance));
        });

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().NotBeEmpty();
        inbox.Items.Should().Contain(item => string.Equals(item.WorkItemId, "reconciliation-policy", StringComparison.OrdinalIgnoreCase));
        inbox.Items.Should().NotContain(item => item.WorkItemId.StartsWith("reconciliation-break-", StringComparison.OrdinalIgnoreCase));
        inbox.Items.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.Detail) &&
            !string.IsNullOrWhiteSpace(item.TargetRoute));
        inbox.Items.Should().Contain(item => item.WorkItemId == "paper-session-missing");
        inbox.CriticalCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Critical));
        inbox.WarningCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Warning));
        inbox.ReviewCount.Should().Be(inbox.CriticalCount + inbox.WarningCount);
        inbox.Items.Should().OnlyContain(item => item.PriorityScore > 0 && !string.IsNullOrWhiteSpace(item.PriorityExplanation));
        inbox.Items.Should().BeInDescendingOrder(item => item.PriorityScore);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ReconciliationBreaks_ShouldExposeExpectedClassificationAndMetadata()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-inbox-break-only-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        _ = await app.Services.GetRequiredService<IReconciliationRunService>().RunAsync(new ReconciliationRunRequest(runId));

        var inbox = await app.GetTestClient().GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().NotBeEmpty();
        inbox.Items.Should().Contain(item => item.Kind == OperatorWorkItemKindDto.ReconciliationBreak);
        inbox.Items.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.Detail) &&
            !string.IsNullOrWhiteSpace(item.TargetRoute));
        inbox.Items.Where(item => item.Kind == OperatorWorkItemKindDto.ReconciliationBreak)
            .Should().OnlyContain(item => item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue);
        inbox.CriticalCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Critical));
        inbox.WarningCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Warning));
        inbox.ReviewCount.Should().Be(inbox.CriticalCount + inbox.WarningCount);
        inbox.Items.Should().BeInDescendingOrder(item => item.PriorityScore);
        inbox.Items.Should().OnlyContain(item => item.PriorityScore > 0 && !string.IsNullOrWhiteSpace(item.PriorityExplanation));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_MixedQueue_ShouldRetainBothCategoriesWithoutDropsAndOrderByPriority()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-inbox-mixed-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        _ = await app.Services.GetRequiredService<IReconciliationRunService>().RunAsync(new ReconciliationRunRequest(runId));

        var inbox = await app.GetTestClient().GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item => item.WorkItemId == "paper-session-missing");
        inbox.Items.Should().Contain(item => item.Kind == OperatorWorkItemKindDto.ReconciliationBreak);
        inbox.Items.Should().BeInDescendingOrder(item => item.PriorityScore);
        inbox.Items.Should().OnlyContain(item => item.PriorityScore > 0 && !string.IsNullOrWhiteSpace(item.PriorityExplanation));
        inbox.Items.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.Detail) &&
            !string.IsNullOrWhiteSpace(item.TargetRoute));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_WhenNoReconciliationBreaks_ShouldNotEmitFalseBreakItems()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationBreakQueueRepository>(
                new FileReconciliationBreakQueueRepository(
                    Path.Combine(Path.GetTempPath(), "meridian-tests", "break-queue", Guid.NewGuid().ToString("N")),
                    NullLogger<FileReconciliationBreakQueueRepository>.Instance));
        });

        var inbox = await app.GetTestClient().GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item => string.Equals(item.WorkItemId, "reconciliation-policy", StringComparison.OrdinalIgnoreCase));
        inbox.Items.Should().NotContain(item => item.WorkItemId.StartsWith("reconciliation-break-", StringComparison.OrdinalIgnoreCase));
        inbox.CriticalCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Critical));
        inbox.WarningCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Warning));
        inbox.ReviewCount.Should().Be(inbox.CriticalCount + inbox.WarningCount);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_WhenBreakQueueUnavailable_ShouldReturnTradingReadinessWithWarning()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationBreakQueueRepository>(
                new ThrowingReconciliationBreakQueueRepository());
        });

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item =>
            item.WorkItemId == "paper-session-missing" &&
            item.TargetPageTag == "TradingShell");
        inbox.Items.Should().ContainSingle(item =>
            item.WorkItemId == "reconciliation-break-queue-unavailable" &&
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.Tone == OperatorWorkItemToneDto.Warning &&
            item.Workspace == "Accounting" &&
            item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue &&
            item.TargetPageTag == "AccountingShell");
        inbox.WarningCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Dk1TrustGateReadinessService_WithSignedOperatorPacket_ShouldExposeOwnerEvidence()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-signed-packet",
            Guid.NewGuid().ToString("N"));
        WriteReadyDk1Packet(
            automationRoot,
            """
            {
              "requiredOwners": [ "Data Operations", "Provider Reliability", "Trading" ],
              "status": "signed",
              "requiredBeforeDk1Exit": true,
              "signedOwners": [ "Data Operations", "Provider Reliability", "Trading" ],
              "missingOwners": [],
              "completedAtUtc": "2026-04-26T16:02:00Z",
              "sourcePath": "artifacts/provider-validation/_automation/unit-ready/dk1-operator-signoff.json",
              "approvals": [
                {
                  "owner": "Data Operations",
                  "signedBy": "data.ops",
                  "signedAtUtc": "2026-04-26T15:58:00Z",
                  "decision": "approved",
                  "rationale": "Provider packet reviewed."
                },
                {
                  "owner": "Provider Reliability",
                  "signedBy": "provider.reliability",
                  "signedAtUtc": "2026-04-26T16:00:00Z",
                  "decision": "approved",
                  "rationale": "Threshold and evidence checks accepted."
                },
                {
                  "owner": "Trading",
                  "signedBy": "trading.owner",
                  "signedAtUtc": "2026-04-26T16:02:00Z",
                  "decision": "approved",
                  "rationale": "Cockpit readiness gate accepted."
                }
              ]
            }
            """);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var readiness = await service.GetCurrentAsync();

        readiness.OperatorSignoffStatus.Should().Be("signed");
        readiness.OperatorSignoff.Should().NotBeNull();
        readiness.OperatorSignoff!.SignedOwners.Should().BeEquivalentTo(
            ["Data Operations", "Provider Reliability", "Trading"]);
        readiness.OperatorSignoff.MissingOwners.Should().BeEmpty();
        readiness.OperatorSignoff.CompletedAt.Should().Be(new DateTimeOffset(2026, 4, 26, 16, 2, 0, TimeSpan.Zero));
        readiness.Detail.Should().Contain("operator sign-off is complete");
        readiness.Detail.Should().Contain("explainability validated").And.Contain("calibration validated");
        readiness.Blockers.Should().BeEmpty();
        readiness.TrustRationaleContract.Should().NotBeNull();
        readiness.TrustRationaleContract!.Status.Should().Be("validated");
        readiness.CalibrationVersion.Should().BeNull();
        readiness.CalibrationValidatedAt.Should().BeNull();
        readiness.PromotionPosture.Should().BeNull();
        readiness.PacketPath.Should().NotBeNull();
        Path.IsPathRooted(readiness.PacketPath!).Should().BeFalse();
        readiness.PacketPath.Should().Contain("dk1-pilot-parity-packet.json");
    }

    [Fact]
    public async Task Dk1TrustGateReadinessService_WithEvidenceBundle_ShouldExposeCalibrationPosture()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-calibration-bundle",
            Guid.NewGuid().ToString("N"));
        WriteReadyDk1Packet(automationRoot);
        WriteProviderValidationEvidenceBundle(
            automationRoot,
            """
            {
              "generatedAtUtc": "2026-04-27T09:30:00Z",
              "promotionPosture": {
                "status": "candidate-approved",
                "candidateKernelVersion": "kernel-v2"
              }
            }
            """);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var readiness = await service.GetCurrentAsync();

        readiness.CalibrationVersion.Should().Be("kernel-v2");
        readiness.PromotionPosture.Should().Be("candidate-approved");
        readiness.CalibrationValidatedAt.Should().Be(new DateTimeOffset(2026, 4, 27, 9, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Dk1TrustGateReadinessService_WhenPacketMissing_ShouldNotDiscloseFilesystemPaths()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-missing-packet",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(automationRoot);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var readiness = await service.GetCurrentAsync();

        readiness.Status.Should().Be("packet-unavailable");
        readiness.PacketPath.Should().BeNull();
        readiness.Detail.Should().Be("No DK1 pilot parity packet was found under the configured automation root.");
        readiness.Detail.Should().NotContain(NormalizePathForAssertion(automationRoot));
    }

    [Fact]
    public async Task Dk1TrustGateReadinessService_CacheHit_ShouldNotRescanAutomationRoot()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-cache-hit",
            Guid.NewGuid().ToString("N"));
        WriteReadyDk1Packet(automationRoot);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var first = await service.GetCurrentAsync();
        Directory.Delete(automationRoot, recursive: true);

        var second = await service.GetCurrentAsync();

        second.Status.Should().Be(first.Status);
        second.PacketPath.Should().Be(first.PacketPath);
        second.Detail.Should().Be(first.Detail);
    }

    [Fact]
    public async Task Dk1TrustGateReadinessService_WithLegacyPacketWithoutContracts_ShouldBlockReview()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-legacy-packet",
            Guid.NewGuid().ToString("N"));
        WriteReadyDk1Packet(automationRoot, includeContractReview: false);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var readiness = await service.GetCurrentAsync();

        readiness.ReadyForOperatorReview.Should().BeTrue();
        readiness.Blockers.Should().Contain("DK1 explainability contract is missing from the parity packet.");
        readiness.Blockers.Should().Contain("DK1 calibration contract is missing from the parity packet.");
        readiness.Detail.Should().Contain("explainability contract is missing");
        readiness.TrustRationaleContract.Should().BeNull();
        readiness.BaselineThresholdContract.Should().BeNull();
    }

    [Fact]
    public async Task Dk1TrustGateReadinessService_WithValidatedContractMissingRequiredFields_ShouldBlockReview()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-malformed-contract-packet",
            Guid.NewGuid().ToString("N"));
        WriteReadyDk1Packet(
            automationRoot,
            """
            {
              "requiredOwners": [ "Data Operations", "Provider Reliability", "Trading" ],
              "status": "signed",
              "requiredBeforeDk1Exit": true,
              "signedOwners": [ "Data Operations", "Provider Reliability", "Trading" ],
              "missingOwners": []
            }
            """,
            includeContractReview: false);

        var packetPath = Path.Combine(automationRoot, "unit-ready", "dk1-pilot-parity-packet.json");
        var malformedPacket = File.ReadAllText(packetPath).Replace(
            "\"operatorSignoff\":",
            """
            "trustRationaleContract": { "status": "validated" },
            "baselineThresholdContract": { "status": "validated" },
            "operatorSignoff":
            """);
        File.WriteAllText(packetPath, malformedPacket);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var readiness = await service.GetCurrentAsync();

        readiness.ReadyForOperatorReview.Should().BeTrue();
        readiness.OperatorSignoffStatus.Should().Be("signed");
        readiness.Blockers.Should().Contain("DK1 explainability contract is validated but missing required contract fields.");
        readiness.Blockers.Should().Contain("DK1 calibration contract is validated but missing required contract fields.");
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
        var controlCenter = governance.RootElement.GetProperty("controlCenter");
        controlCenter.GetProperty("closeReadiness").GetString().Should().NotBeNullOrWhiteSpace();
        controlCenter.GetProperty("blockerSeverityDistribution").GetArrayLength().Should().BeGreaterThan(0);
        controlCenter.GetProperty("agingCurves").GetArrayLength().Should().Be(3);
        controlCenter.GetProperty("ownerWorkload").GetArrayLength().Should().BeGreaterThan(0);
        controlCenter.GetProperty("slaBreachCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        controlCenter.GetProperty("trendSnapshots").GetArrayLength().Should().BeGreaterThan(0);
        controlCenter.GetProperty("drillLinks").EnumerateArray()
            .Select(link => link.GetProperty("href").GetString())
            .Should()
            .Contain(new[] { "/trading/readiness", "/accounting/reconciliation", "/reporting/report-packs", "/reporting/evidence" });
        controlCenter.GetProperty("alerts").GetArrayLength().Should().BeGreaterThan(0);

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
    public async Task MapWorkstationEndpoints_ReconciliationStatementRoutes_ShouldUseCanonicalSharedRoutes()
    {
        var service = new StubReconciliationApiService();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
        });

        var client = app.GetTestClient();

        var runs = await client.GetFromJsonAsync<List<StatementRunSummaryDto>>(
            UiApiRoutes.ReconciliationStatementRuns,
            ServerJsonOptions);
        runs.Should().ContainSingle(run => run.RunId == "statement-run-1");

        var run = await client.GetFromJsonAsync<StatementRunDto>(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunById, "runId", "statement-run-1"),
            ServerJsonOptions);
        run.Should().NotBeNull();
        run!.MatchSummary.Should().NotBeNull();
        run.MatchSummary!.BreakCount.Should().Be(1);
        run.Breaks.Should().ContainSingle(statementBreak =>
            statementBreak.BreakId == "break-1"
            && statementBreak.Status == "Open"
            && statementBreak.Severity == StatementValidationSeverity.Error);
        run!.RunId.Should().Be("statement-run-1");

        var validation = await client.GetFromJsonAsync<StatementRunValidationDto>(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunValidation, "runId", "statement-run-1"),
            ServerJsonOptions);
        validation.Should().NotBeNull();
        validation!.IsBlocked.Should().BeFalse();

        var breaks = await client.GetFromJsonAsync<List<StatementRunBreakDto>>(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunBreaks, "runId", "statement-run-1"),
            ServerJsonOptions);
        breaks.Should().ContainSingle(item => item.RunId == "statement-run-1");

        var reconcileResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunReconcile, "runId", "statement-run-1"),
            new StatementRunReconcileRequestDto(Actor: "ops-user"),
            ServerJsonOptions);
        reconcileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconciled = await reconcileResponse.Content.ReadFromJsonAsync<StatementRunDto>(ServerJsonOptions);
        reconciled.Should().NotBeNull();
        reconciled!.RunId.Should().Be("statement-run-1");

        var createdResponse = await client.PostAsJsonAsync(
            UiApiRoutes.ReconciliationStatementRuns,
            new StatementRunCreateDto(
                Broker: "samplebroker",
                SourceInstitution: "Sample Custodian",
                FundAccountId: "fund-1",
                ExternalAccountId: "ext-1",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 27),
                SourcePath: "/tmp/statement.csv",
                OriginalFileName: "statement.csv",
                MappingProfileId: "mapping-v1",
                ToleranceProfileId: "tolerance-v1",
                ImportedBy: "spoofed-user",
                SourceFileHash: "CALLERHASH"),
            ServerJsonOptions);
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        service.CreatedRequests.Should().ContainSingle(request =>
            request.FundAccountId == "fund-1"
            && request.ImportedBy == "ops-user"
            && request.SourceFileHash is null);

        var exceptions = await client.GetFromJsonAsync<List<StatementRunExceptionDto>>(
            UiApiRoutes.ReconciliationStatementExceptions,
            ServerJsonOptions);
        exceptions.Should().ContainSingle(item => item.RunId == "statement-run-1");
    }


    [Fact]
    public async Task MapWorkstationEndpoints_StatementRunMutations_ShouldRequireReconciliationPermission()
    {
        var service = new StubReconciliationApiService();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var client = app.GetTestClient();

        var createResponse = await client.PostAsJsonAsync(
            UiApiRoutes.ReconciliationStatementRuns,
            new StatementRunCreateDto(
                Broker: "samplebroker",
                SourceInstitution: "Sample Custodian",
                FundAccountId: "fund-1",
                ExternalAccountId: "ext-1",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 27),
                SourcePath: "/tmp/statement.csv",
                OriginalFileName: "statement.csv",
                MappingProfileId: "mapping-v1",
                ToleranceProfileId: "tolerance-v1",
                ImportedBy: "ops-user",
                SourceFileHash: "ABC123"),
            ServerJsonOptions);

        var reconcileResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunReconcile, "runId", "statement-run-1"),
            new StatementRunReconcileRequestDto(Actor: "ops-user"),
            ServerJsonOptions);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        reconcileResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.CreatedRequests.Should().BeEmpty();
        service.ReconciledRunIds.Should().BeEmpty();
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
        var response = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest("run-recon"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        created.Should().NotBeNull();
        created!.Summary.RunId.Should().Be("run-recon");
        created.Summary.BreakCount.Should().Be(0);
        created.Matches.Should().Contain(match => match.CheckId == "cash-balance");

        var latestResponse = await client.GetAsync(UiApiRoutes.RunsReconciliation.Replace("{runId}", "run-recon"));
        latestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var latest = await latestResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        latest.Should().NotBeNull();
        latest!.Summary.ReconciliationRunId.Should().Be(created.Summary.ReconciliationRunId);

        var byIdResponse = await client.GetAsync(
            UiApiRoutes.ReconciliationRunById.Replace(
                "{reconciliationRunId}",
                created.Summary.ReconciliationRunId));
        byIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var byId = await byIdResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        byId.Should().NotBeNull();
        byId!.Summary.MatchCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationRunById_ShouldRequireReconciliationMutationPermission()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var repository = app.Services.GetRequiredService<IReconciliationRunRepository>();
        await repository.SaveAsync(BuildReconciliationDetail(
            reconciliationRunId: "recon-permission",
            runId: "run-recon-permission",
            createdAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            matchCount: 1,
            breakCount: 0));

        var client = app.GetTestClient();
        var byIdResponse = await client.GetAsync(
            UiApiRoutes.ReconciliationRunById.Replace(
                "{reconciliationRunId}",
                "recon-permission"));
        byIdResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationCreateRun_ShouldRequireMutationPermission()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("run-recon-create-permission"));

        var client = app.GetTestClient();
        var createResponse = await client.PostAsJsonAsync(
            UiApiRoutes.ReconciliationRuns,
            new ReconciliationRunRequest("run-recon-create-permission"));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

        var createResponse = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest("missing-run"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var latestResponse = await client.GetAsync(UiApiRoutes.RunsReconciliation.Replace("{runId}", "missing-run"));
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
        var historyResponse = await client.GetAsync(UiApiRoutes.RunsReconciliationHistory.Replace("{runId}", "run-history"));
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
        var response = await client.GetAsync(UiApiRoutes.RunsReconciliationHistory.Replace("{runId}", "run-no-history"));

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
        var response = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest("run-breaks"));
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
    public async Task MapWorkstationEndpoints_BreakQueueRoute_ShouldHydrateQueueWithoutGovernanceBootstrap()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-break-queue-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        var reconciliation = await reconciliationService.RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/reconciliation/break-queue");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var queue = await response.Content.ReadFromJsonAsync<List<ReconciliationBreakQueueItem>>(ServerJsonOptions);
        queue.Should().NotBeNull();
        queue!.Should().Contain(item =>
            item.RunId == runId &&
            reconciliation!.Breaks.Any(reconciliationBreak => item.BreakId == $"{runId}:{reconciliationBreak.CheckId}"));
        queue.Should().Contain(item =>
            item.RunId == runId &&
            !string.IsNullOrWhiteSpace(item.ExceptionRoute) &&
            !string.IsNullOrWhiteSpace(item.ToleranceProfileId) &&
            !string.IsNullOrWhiteSpace(item.RequiredSignoffRole) &&
            !string.IsNullOrWhiteSpace(item.SignoffStatus) &&
            item.SourceType == "provider-ledger" &&
            item.SourceFingerprint != null);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueRoute_ShouldSeedStatementBreaksOnceWithSourceMetadata()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(new StubReconciliationApiService());
        });

        var client = app.GetTestClient();
        var first = await client.GetFromJsonAsync<List<ReconciliationBreakQueueItem>>(
            UiApiRoutes.ReconciliationBreakQueue,
            ServerJsonOptions);
        var second = await client.GetFromJsonAsync<List<ReconciliationBreakQueueItem>>(
            UiApiRoutes.ReconciliationBreakQueue,
            ServerJsonOptions);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second!.Should().ContainSingle(item =>
            item.SourceType == "statement" &&
            item.SourceSystem == "statement-reconciliation" &&
            item.SourceImportId == "import-1" &&
            item.SourceBreakId == "break-1" &&
            item.SourceReference == "import-1:row-42" &&
            item.BreakId.StartsWith("statement:", StringComparison.OrdinalIgnoreCase) &&
            item.AssignedTo == "statement-owner" &&
            item.Severity == ReconciliationBreakSeverity.High &&
            item.ToleranceBand == 1m &&
            item.RequiredSignoffRole == "Fund operations lead" &&
            item.SignoffStatus == "pending-signoff" &&
            item.ResolutionNote == null);
        first!.Count(item => item.SourceType == "statement").Should().Be(1);
        second.Count(item => item.SourceType == "statement").Should().Be(1);

        var statementBreak = second.Single(item => item.SourceType == "statement");
        var audit = await client.GetFromJsonAsync<List<ReconciliationBreakQueueAuditEvent>>(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationBreakAudit, "breakId", statementBreak.BreakId),
            ServerJsonOptions);
        audit.Should().ContainSingle(e => e.EventType == "CaseCreated" && e.Source == "statement");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationCalibrationSummary_ShouldAggregateToleranceProfiles()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-calibration-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var reconciliation = await app.Services
            .GetRequiredService<IReconciliationRunService>()
            .RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var summary = await app
            .GetTestClient()
            .GetFromJsonAsync<ReconciliationCalibrationSummaryDto>(
                UiApiRoutes.ReconciliationCalibrationSummary,
                ServerJsonOptions);

        summary.Should().NotBeNull();
        summary!.TotalBreakCount.Should().BeGreaterThan(0);
        summary.ActiveBreakCount.Should().BeGreaterThan(0);
        summary.PendingSignoffCount.Should().BeGreaterThan(0);
        summary.MissingCalibrationMetadataCount.Should().Be(0);
        summary.Status.Should().BeOneOf(
            ReconciliationCalibrationStatusDto.ReviewRequired,
            ReconciliationCalibrationStatusDto.Blocked);
        summary.Profiles.Should().Contain(profile =>
            !string.IsNullOrWhiteSpace(profile.ToleranceProfileId) &&
            !string.IsNullOrWhiteSpace(profile.ExceptionRoute) &&
            profile.TotalBreakCount > 0 &&
            profile.PendingSignoffCount > 0);
        summary.Summary.Should().Contain("reconciliation");
        summary.BreakCountTrend.Should().BeGreaterThanOrEqualTo(0);
        summary.AutoMatchRate.Should().BeInRange(0m, 1m);
        summary.T0ClosureRate.Should().BeInRange(0m, 1m);
        summary.BreakCountAlertThreshold.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationCalibrationSummary_AfterResolution_ShouldMarkReadyForSignoff()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-calibration-resolved-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var reconciliation = await app.Services
            .GetRequiredService<IReconciliationRunService>()
            .RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var client = app.GetTestClient();
        var queue = await client.GetFromJsonAsync<List<ReconciliationBreakQueueItem>>(
            UiApiRoutes.ReconciliationBreakQueue,
            ServerJsonOptions);
        queue.Should().NotBeNull();
        queue.Should().NotBeEmpty();

        foreach (var item in queue!)
        {
            if (item.Status == ReconciliationBreakQueueStatus.Open)
            {
                var review = await client.PostAsJsonAsync(
                    UiApiRoutes.ReconciliationBreakReview.Replace("{breakId}", item.BreakId, StringComparison.Ordinal),
                    new ReviewReconciliationBreakRequest(
                        BreakId: item.BreakId,
                        AssignedTo: "ops-review",
                        ReviewedBy: "qa-review",
                        ReviewNote: "Calibration review completed."));
                review.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            var resolve = await client.PostAsJsonAsync(
                UiApiRoutes.ReconciliationBreakResolve.Replace("{breakId}", item.BreakId, StringComparison.Ordinal),
                new ResolveReconciliationBreakRequest(
                    BreakId: item.BreakId,
                    Status: ReconciliationBreakQueueStatus.Resolved,
                    ResolvedBy: "qa-resolve",
                    ResolutionNote: "Tolerance profile accepted for sign-off.",
                    OperatorRationale: "Tolerance and routing evidence verified."));
            resolve.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var summary = await client.GetFromJsonAsync<ReconciliationCalibrationSummaryDto>(
            UiApiRoutes.ReconciliationCalibrationSummary,
            ServerJsonOptions);

        summary.Should().NotBeNull();
        summary!.Status.Should().Be(ReconciliationCalibrationStatusDto.Ready);
        summary.ActiveBreakCount.Should().Be(0);
        summary.OpenBreakCount.Should().Be(0);
        summary.InReviewBreakCount.Should().Be(0);
        summary.PendingSignoffCount.Should().Be(0);
        summary.CriticalOpenBreakCount.Should().Be(0);
        summary.SignedOffCount.Should().Be(summary.TotalBreakCount);
        summary.Profiles.Should().OnlyContain(profile => profile.PendingSignoffCount == 0);
        summary.Summary.Should().Contain("ready for governance sign-off");
        summary.BreakCountTrend.Should().BeLessThanOrEqualTo(0);
        summary.T0ClosureRate.Should().Be(1m);
        summary.T0ClosureRateAlertTriggered.Should().BeFalse();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueReviewRoute_ShouldHydrateQueueWithoutListBootstrap()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-break-review-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        var reconciliation = await reconciliationService.RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var breakId = $"{runId}:{reconciliation!.Breaks[0].CheckId}";
        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/review",
            new ReviewReconciliationBreakRequest(
                BreakId: breakId,
                AssignedTo: "ops-review",
                ReviewedBy: "qa-review",
                ReviewNote: "Investigating the mismatch."));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<ReconciliationBreakQueueItem>(ServerJsonOptions);
        updated.Should().NotBeNull();
        updated!.RunId.Should().Be(runId);
        updated.Status.Should().Be(ReconciliationBreakQueueStatus.InReview);
        updated.AssignedTo.Should().Be("ops-review");
        updated.SignoffStatus.Should().Be("in-review");
        updated.RequiredSignoffRole.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueResolveRoute_ShouldRequireReviewBeforeResolve()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-break-resolve-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        var reconciliation = await reconciliationService.RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var breakId = $"{runId}:{reconciliation!.Breaks[0].CheckId}";
        var client = app.GetTestClient();

        var invalidResolve = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/resolve",
            new ResolveReconciliationBreakRequest(
                BreakId: breakId,
                Status: ReconciliationBreakQueueStatus.Resolved,
                ResolvedBy: "qa-resolve",
                ResolutionNote: "Skipping review should fail.",
                OperatorRationale: "Attempted without review."));
        invalidResolve.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var review = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/review",
            new ReviewReconciliationBreakRequest(
                BreakId: breakId,
                AssignedTo: "ops-review",
                ReviewedBy: "qa-review",
                ReviewNote: "Investigating."));
        review.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolve = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/resolve",
            new ResolveReconciliationBreakRequest(
                BreakId: breakId,
                Status: ReconciliationBreakQueueStatus.Resolved,
                ResolvedBy: "qa-resolve",
                ResolutionNote: "Issue resolved.",
                OperatorRationale: "Evidence reconciled against source records."));
        resolve.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolved = await resolve.Content.ReadFromJsonAsync<ReconciliationBreakQueueItem>(ServerJsonOptions);
        resolved.Should().NotBeNull();
        resolved!.Status.Should().Be(ReconciliationBreakQueueStatus.InReview);
        resolved.ResolvedBy.Should().Be("ops-user");
        resolved.SignoffStatus.Should().Be("awaiting-approval");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunContinuityRoute_ShouldReturnSharedContinuityPayload()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun("run-continuity"));
        await store.RecordRunAsync(BuildRun(
            runId: "run-continuity-paper",
            strategyId: "recon-strategy",
            strategyName: "Reconciliation Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero)) with
        {
            ParentRunId = "run-continuity",
            FundProfileId = "alpha-credit",
            FundDisplayName = "Alpha Credit"
        });

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        await reconciliationService.RunAsync(new ReconciliationRunRequest("run-continuity"));

        var client = app.GetTestClient();
        using var continuity = await ReadJsonAsync(client, "/api/workstation/runs/run-continuity/continuity");

        continuity.RootElement.GetProperty("run").GetProperty("summary").GetProperty("runId").GetString().Should().Be("run-continuity");
        continuity.RootElement.GetProperty("run").GetProperty("summary").GetProperty("fundProfileId").GetString().Should().Be("alpha-credit");
        continuity.RootElement.GetProperty("lineage").GetProperty("childRuns").GetArrayLength().Should().Be(1);
        continuity.RootElement.GetProperty("lineage").GetProperty("childRuns")[0].GetProperty("runId").GetString().Should().Be("run-continuity-paper");
        continuity.RootElement.GetProperty("cashFlow").GetProperty("totalEntries").GetInt32().Should().Be(3);
        continuity.RootElement.GetProperty("cashFlow").GetProperty("projectedNetPosition").GetDecimal().Should().Be(-376m);
        continuity.RootElement.GetProperty("reconciliation").GetProperty("runId").GetString().Should().Be("run-continuity");
        continuity.RootElement.GetProperty("continuityStatus").GetProperty("hasCashFlow").GetBoolean().Should().BeTrue();
        continuity.RootElement.GetProperty("continuityStatus").GetProperty("hasReconciliation").GetBoolean().Should().BeTrue();
        continuity.RootElement
            .GetProperty("continuityStatus")
            .GetProperty("warnings")
            .EnumerateArray()
            .Select(static warning => warning.GetProperty("code").GetString())
            .Should()
            .Contain("security-coverage");
    }


    [Fact]
    public async Task MapWorkstationEndpoints_ContinuityWarnings_ShouldFlowAcrossResearchTradingGovernanceAndReviewPacket()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var runId = $"run-cross-surface-continuity-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun(runId, includeCashFlows: false, includeFills: false, promotionState: StrategyRunPromotionState.CandidateForLive));

        var client = app.GetTestClient();

        var reviewPacket = await client.GetFromJsonAsync<StrategyRunReviewPacketDto>($"/api/workstation/runs/{runId}/review-packet", ServerJsonOptions);
        reviewPacket.Should().NotBeNull();
        reviewPacket!.Continuity.Should().NotBeNull();
        reviewPacket.Continuity!.ContinuityStatus.HasFills.Should().BeFalse();

        var warningCodes = reviewPacket.Continuity.ContinuityStatus.Warnings.Select(static warning => warning.Code).ToArray();
        warningCodes.Should().Contain(["missing-fills", "lineage-promotion-gap"]);

        var inbox = await client.GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);
        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item => item.WorkItemId.StartsWith("continuity-missing-fills", StringComparison.Ordinal));
        inbox.Items.Should().Contain(item => item.WorkItemId.StartsWith("continuity-lineage-promotion-gap", StringComparison.Ordinal));

        using var continuity = await ReadJsonAsync(client, $"/api/workstation/runs/{runId}/continuity");
        var continuityWarnings = continuity.RootElement
            .GetProperty("continuityStatus")
            .GetProperty("warnings")
            .EnumerateArray()
            .Select(static warning => warning.GetProperty("code").GetString())
            .ToArray();

        continuity.RootElement.GetProperty("continuityStatus").GetProperty("hasFills").GetBoolean().Should().BeFalse();
        continuityWarnings.Should().Contain(["missing-fills", "lineage-promotion-gap"]);
        continuityWarnings.Should().Contain(warningCodes);
    }
    [Fact]
    public async Task MapWorkstationEndpoints_RunContinuityRoute_ShouldReturnNotFoundForMissingRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/no-such-run/continuity");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ContinuityDrillInsAcrossSurfaces_ShouldUseCanonicalRouteAndSharedSchema()
    {
        await using var app = await CreateAppAsync(RegisterRunReadServices);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        await store.RecordRunAsync(BuildContinuityRun("run-continuity-shared"));
        await reconciliationService.RunAsync(new ReconciliationRunRequest("run-continuity-shared"));

        var client = app.GetTestClient();
        var canonicalContinuityRoute = UiApiRoutes.RunsContinuity.Replace("{runId}", "run-continuity-shared", StringComparison.Ordinal);

        using var research = await ReadJsonAsync(client, "/api/workstation/research");
        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");
        using var briefing = await ReadJsonAsync(client, "/api/workstation/research/briefing");

        ContainsStringValue(research.RootElement, canonicalContinuityRoute).Should().BeTrue();
        ContainsStringValue(trading.RootElement, canonicalContinuityRoute).Should().BeTrue();
        ContainsStringValue(briefing.RootElement, canonicalContinuityRoute).Should().BeTrue();

        using var continuity = await ReadJsonAsync(client, canonicalContinuityRoute);
        var propertyNames = continuity.RootElement.EnumerateObject().Select(static property => property.Name).ToArray();
        propertyNames.Should().BeEquivalentTo(["run", "lineage", "cashFlow", "reconciliation", "continuityStatus"]);
        continuity.RootElement.TryGetProperty("continuity", out _).Should().BeFalse("surfaces must not emit continuity-only envelope fields outside the shared DTO contract");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunReviewPacket_ShouldReturnStableActionableWorkItems()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var runId = $"run-review-packet-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun(runId));

        var client = app.GetTestClient();
        var first = await client.GetFromJsonAsync<StrategyRunReviewPacketDto>(
            $"/api/workstation/runs/{runId}/review-packet",
            ServerJsonOptions);
        var second = await client.GetFromJsonAsync<StrategyRunReviewPacketDto>(
            $"/api/workstation/runs/{runId}/review-packet",
            ServerJsonOptions);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.WorkItems.Should().NotBeEmpty();
        first.WorkItems.Select(static item => item.WorkItemId)
            .Should()
            .Equal(second!.WorkItems.Select(static item => item.WorkItemId));
        first.WorkItems.Should().OnlyContain(static item =>
            !string.IsNullOrWhiteSpace(item.Workspace) &&
            !string.IsNullOrWhiteSpace(item.TargetRoute) &&
            !string.IsNullOrWhiteSpace(item.TargetPageTag));
        first.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"promotion-review-{runId.ToLowerInvariant()}" &&
            item.Kind == OperatorWorkItemKindDto.PromotionReview &&
            item.Workspace == "Trading" &&
            item.TargetRoute == UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", runId) &&
            item.TargetPageTag == "TradingShell");
        first.WorkItems.Should().NotContain(static item =>
            item.WorkItemId.StartsWith("operator-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunReviewPacket_ShouldReturnNotFoundForMissingRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var response = await app.GetTestClient().GetAsync("/api/workstation/runs/no-such-run/review-packet");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldReturnForbiddenWithoutViewSecurityMasterPermission()
    {
        var securityId = Guid.Parse("60606060-6060-6060-6060-606060606060");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Locked Security", "LOCK"),
            CreateSecurityDetail(securityId, "Locked Security", "LOCK"));

        await using var app = await CreateAppAsync(
            services => RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([])),
            currentUserPermissions: UserPermission.ViewStrategies);

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldReturnTypedWinningSourceProvenance()
    {
        var securityId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "AAPL"),
            CreateSecurityDetail(securityId, "Apple Inc.", "AAPL"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                "Apple Inc.",
                "AAPL",
                JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = "golden-edm",
                    sourceRecordId = "EDM-123",
                    asOf = "2026-04-20T09:30:00Z",
                    updatedBy = "workflow.bot",
                    reason = "golden-copy"
                })));
        queryService.RegisterTradingParameters(securityId, CreateTradingParameters(securityId));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([]));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.EconomicDefinition.WinningSourceSystem.Should().Be("golden-edm");
        snapshot.EconomicDefinition.WinningSourceRecordId.Should().Be("EDM-123");
        snapshot.EconomicDefinition.WinningSourceUpdatedBy.Should().Be("workflow.bot");
        snapshot.ProvenanceCandidates.Should().ContainSingle(candidate =>
            candidate.IsWinningSource &&
            candidate.SourceSystem == "golden-edm" &&
            candidate.SourceRecordId == "EDM-123");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterInstrumentPassport_ShouldReturnProviderConfidenceRows()
    {
        var securityId = Guid.Parse("69696969-6969-6969-6969-696969696969");
        var effectiveFrom = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "AAPL"),
            new SecurityDetailDto(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: "Apple Inc.",
                Currency: "USD",
                CommonTerms: CreateEmptyJson(),
                AssetSpecificTerms: CreateEmptyJson(),
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Ticker,
                        "AAPL",
                        true,
                        effectiveFrom,
                        Provider: "alpaca")
                ],
                Aliases:
                [
                    new SecurityAliasDto(
                        AliasId: Guid.Parse("F7AA8916-C40E-44F9-B427-9351F09F6C95"),
                        SecurityId: securityId,
                        AliasKind: "ProviderSymbol",
                        AliasValue: "AAPL",
                        Provider: "alpaca",
                        Scope: SecurityAliasScope.Execution,
                        Reason: "Broker symbol mapping",
                        CreatedBy: "ops",
                        CreatedAt: effectiveFrom,
                        ValidFrom: effectiveFrom,
                        ValidTo: null,
                        IsEnabled: true)
                ],
                Version: 4,
                EffectiveFrom: effectiveFrom,
                EffectiveTo: null));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([]));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/passport");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var passport = await response.Content.ReadFromJsonAsync<InstrumentPassportDto>(ServerJsonOptions);

        passport.Should().NotBeNull();
        passport!.SecurityId.Should().Be(securityId);
        passport.ProviderConfidence.Should().ContainSingle(item =>
            item.Provider == "alpaca" &&
            item.Symbol == "AAPL" &&
            item.ProviderSource == "Identifier" &&
            item.ConfidenceScore >= 85m);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldReturnValidationIdentifierAndSchemaProjections()
    {
        var securityId = Guid.Parse("67676767-6767-6767-6767-676767676767");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Acme Funding 2031-A1", "ACME31"),
            new SecurityDetailDto(
                SecurityId: securityId,
                AssetClass: "Bond",
                Status: SecurityStatusDto.Active,
                DisplayName: "Acme Funding 2031-A1",
                Currency: "USD",
                CommonTerms: JsonSerializer.SerializeToElement(new
                {
                    issuerName = "Acme Funding Trust",
                    countryOfRisk = "US",
                    primaryListingMic = "OTCM",
                    settlementCycleDays = 2
                }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    maturity = "2031-12-15",
                    couponType = "Fixed",
                    couponRate = 5.125,
                    issueDate = "2026-01-15",
                    subclass = "AssetBacked"
                }),
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Cusip,
                        "000000AA1",
                        true,
                        new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)),
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.ProviderSymbol,
                        "ACME31 TRACE",
                        false,
                        new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                        null,
                        "TRACE")
                ],
                Aliases:
                [
                    new SecurityAliasDto(
                        AliasId: Guid.Parse("11111111-aaaa-4444-bbbb-111111111111"),
                        SecurityId: securityId,
                        AliasKind: "Ticker",
                        AliasValue: "ACME31",
                        Provider: "Bloomberg",
                        Scope: SecurityAliasScope.Operations,
                        Reason: "desk alias",
                        CreatedBy: "ops",
                        CreatedAt: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                        ValidFrom: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                        ValidTo: null,
                        IsEnabled: true)
                ],
                Version: 7,
                EffectiveFrom: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null));
        queryService.RegisterEconomicDefinition(
            securityId,
            new SecurityEconomicDefinitionRecord(
                SecurityId: securityId,
                AssetClass: "FixedIncome",
                AssetFamily: "StructuredCredit",
                SubType: "AssetBackedSecurity",
                TypeName: "ABS PassThrough",
                IssuerType: "Trust",
                RiskCountry: "US",
                Status: SecurityStatusDto.Active,
                DisplayName: "Acme Funding 2031-A1",
                Currency: "USD",
                Classification: JsonSerializer.SerializeToElement(new
                {
                    assetClass = "FixedIncome",
                    assetFamily = "StructuredCredit",
                    subType = "AssetBackedSecurity"
                }),
                CommonTerms: JsonSerializer.SerializeToElement(new
                {
                    issuerName = "Acme Funding Trust",
                    countryOfRisk = "US"
                }),
                EconomicTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 2,
                    maturity = new
                    {
                        issueDate = "2026-01-15",
                        maturityDate = "2031-12-15"
                    },
                    call = new
                    {
                        firstCallDate = "2028-06-15"
                    },
                    coupon = new
                    {
                        couponType = "Fixed",
                        couponRate = 5.125
                    },
                    cashflowSchedule = new object[]
                    {
                        new
                        {
                            eventId = "cf-20260625",
                            eventType = "InterestPayment",
                            effectiveDate = "2026-06-25",
                            paymentDate = "2026-06-25",
                            expectedAmount = 12500.00m,
                            currency = "USD",
                            postingStatus = "Projected",
                            sourceSystem = "trustee-feed",
                            sourceRecordId = "cashflow-20260625",
                            asOf = "2026-05-20T10:00:00Z",
                            updatedBy = "trustee.ops",
                            reason = "scheduled-interest"
                        }
                    },
                    structuredProduct = new
                    {
                        factor = 0.9825,
                        factorDate = "2026-05-01",
                        collateralType = "AutoLoan",
                        factorHistory = new object[]
                        {
                            new
                            {
                                pointId = "factor-20260401",
                                effectiveDate = "2026-04-01",
                                factor = 0.9910m,
                                sourceSystem = "trustee-feed",
                                sourceRecordId = "factor-20260401",
                                asOf = "2026-04-01T10:00:00Z",
                                updatedBy = "trustee.ops",
                                reason = "monthly-factor"
                            },
                            new
                            {
                                pointId = "factor-20260501",
                                effectiveDate = "2026-05-01",
                                factor = 0.9825m,
                                previousFactor = 0.9910m,
                                sourceSystem = "trustee-feed",
                                sourceRecordId = "factor-20260501",
                                asOf = "2026-05-01T10:00:00Z",
                                updatedBy = "trustee.ops",
                                reason = "monthly-factor"
                            }
                        }
                    }
                }),
                Provenance: JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = "golden-edm",
                    sourceRecordId = "ABS-2031-A1",
                    asOf = "2026-05-20T10:00:00Z",
                    updatedBy = "workflow.bot",
                    reason = "governed merge"
                }),
                Version: 7,
                EffectiveFrom: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null,
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Cusip,
                        "000000AA1",
                        true,
                        new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero))
                ],
                LegacyAssetClass: "Bond",
                LegacyAssetSpecificTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    maturity = "2031-12-15"
                })));
        queryService.RegisterTradingParameters(securityId, CreateTradingParameters(securityId));
        queryService.RegisterCorporateActions(
            securityId,
            [
                new CorporateActionDto(
                    CorpActId: Guid.Parse("22222222-bbbb-4444-cccc-222222222222"),
                    SecurityId: securityId,
                    EventType: "PrincipalPaydown",
                    ExDate: new DateOnly(2026, 5, 25),
                    PayDate: new DateOnly(2026, 5, 27),
                    DividendPerShare: null,
                    Currency: "USD",
                    SplitRatio: null,
                    NewSecurityId: null,
                    DistributionRatio: 0.0085m,
                    AcquirerSecurityId: null,
                    ExchangeRatio: null,
                    SubscriptionPricePerShare: null,
                    RightsPerShare: null)
            ]);
        queryService.RegisterHistory(
            securityId,
            [
                new SecurityMasterEventEnvelope(
                    GlobalSequence: 10,
                    SecurityId: securityId,
                    StreamVersion: 8,
                    EventType: "FactorScheduleRefreshed",
                    EventTimestamp: new DateTimeOffset(2026, 5, 20, 10, 15, 0, TimeSpan.Zero),
                    Actor: "trustee.bot",
                    CorrelationId: Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    CausationId: Guid.Parse("10000000-0000-0000-0000-000000000002"),
                    Payload: JsonSerializer.SerializeToElement(new
                    {
                        factor = 0.9825,
                        factorDate = "2026-05-01",
                        paymentDate = "2026-06-25"
                    }),
                    Metadata: JsonSerializer.SerializeToElement(new
                    {
                        sourceSystem = "trustee-feed",
                        sourceRecordId = "factor-20260501",
                        reason = "monthly remittance"
                    }))
            ]);

        var validationService = new StubSecurityValidationService();
        validationService.RegisterReport(
            securityId,
            new SecurityValidationReportDto(
                SecurityId: securityId,
                Scope: "Security",
                EvaluatedAtUtc: new DateTimeOffset(2026, 5, 20, 10, 5, 0, TimeSpan.Zero),
                HasBlockingIssues: true,
                CriticalIssueCount: 0,
                ErrorIssueCount: 1,
                Issues:
                [
                    new SecurityValidationIssueDto(
                        SecurityValidationSeverityDto.Error,
                        "SM_IDENTIFIER_DUPLICATE_ACTIVE",
                        "Duplicate active identifier exists on the record",
                        "Provider mapping must be reviewed before downstream use.",
                        ["identifiers.ProviderSymbol"],
                        "Expire the duplicate provider symbol and confirm the winning map.",
                        [])
                ]));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(
                services,
                queryService,
                new StubSecurityMasterConflictService([]),
                validationService);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-structured-lots",
            strategyId: "structured-income",
            strategyName: "Structured Income",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/structured-credit",
            feedReference: "synthetic:structured-credit",
            fundProfileId: "alpha-credit").Complete(BuildBacktestResultWithOpenLots(
                symbol: "ACME31",
                quantity: 1_000_000L,
                entryPrice: 0.97m,
                accountId: "structured-income-account",
                accountDisplayName: "Structured Income Account")));

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot?fundProfileId=alpha-credit");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.TrustPosture.Tone.Should().Be(SecurityMasterTrustTone.Blocked);
        snapshot.ValidationReport.Should().NotBeNull();
        snapshot.ValidationReport!.HasBlockingIssues.Should().BeTrue();
        snapshot.ValidationReport.ErrorIssueCount.Should().Be(1);
        snapshot.IdentifierSummary.Should().NotBeNull();
        snapshot.IdentifierSummary!.ProviderMappingCount.Should().Be(2);
        snapshot.IdentifierSummary.DistinctProviderCount.Should().Be(2);
        snapshot.IdentifierSummary.HasProviderMappings.Should().BeTrue();
        snapshot.SchemaCompatibility.Should().NotBeNull();
        snapshot.SchemaCompatibility!.LegacyAssetSpecificTermsSchemaVersion.Should().Be(1);
        snapshot.SchemaCompatibility.EconomicTermsSchemaVersion.Should().Be(2);
        snapshot.SchemaCompatibility.HasLegacyAssetSpecificTerms.Should().BeTrue();
        snapshot.SchemaCompatibility.HasEconomicTerms.Should().BeTrue();
        snapshot.ChangeHistory.Should().NotBeNull();
        snapshot.ChangeHistory!.Should().ContainSingle(item =>
            item.EventType == "FactorScheduleRefreshed" &&
            item.SourceSystem == "trustee-feed" &&
            item.Origin == "Vendor");
        snapshot.ScheduleSummary.Should().NotBeNull();
        snapshot.ScheduleSummary!.SupportsCashflowSchedule.Should().BeTrue();
        snapshot.ScheduleSummary.SupportsFactorHistory.Should().BeTrue();
        snapshot.ScheduleSummary.CurrentFactor.Should().Be(0.9825m);
        snapshot.ScheduleSummary.CurrentFactorDate.Should().Be(new DateOnly(2026, 5, 1));
        snapshot.LotModel.Should().NotBeNull();
        snapshot.LotModel!.QuantityModel.Should().Be("FactorAdjustedFace");
        snapshot.LotModel.UsesFaceValue.Should().BeTrue();
        snapshot.LotModel.SupportsFactorAdjustedExposure.Should().BeTrue();
        snapshot.ScheduleBook.Should().NotBeNull();
        snapshot.ScheduleBook!.FactorHistory.Should().HaveCount(2);
        snapshot.ScheduleBook.Events.Should().Contain(item =>
            item.EventType == "InterestPayment" &&
            item.EffectiveDate == new DateOnly(2026, 6, 25) &&
            item.ExpectedAmount == 12500.00m &&
            item.SourceSystem == "trustee-feed");
        snapshot.ScheduleBook.Events.Should().Contain(item =>
            item.EventType == "PrincipalPaydown" &&
            item.EffectiveDate == new DateOnly(2026, 5, 25) &&
            item.FactorEnd == 0.0085m);
        snapshot.ScheduleBook.ProvenanceHistory.Should().Contain(item =>
            item.Category == "History" &&
            item.EventType == "FactorScheduleRefreshed" &&
            item.SourceSystem == "trustee-feed");
        snapshot.OpenLotReadModel.Should().NotBeNull();
        snapshot.OpenLotReadModel!.SupportsFactorAdjustedExposure.Should().BeTrue();
        snapshot.OpenLotReadModel.CurrentFactor.Should().Be(0.9825m);
        snapshot.OpenLotReadModel.Lots.Should().ContainSingle();
        snapshot.OpenLotReadModel.ProvenanceHistory.Should().ContainSingle(item =>
            item.RunId == "run-structured-lots" &&
            item.SourceSystem == "strategy-run-snapshot");
        var openLot = snapshot.OpenLotReadModel.Lots[0];
        openLot.SecurityId.Should().Be(securityId);
        openLot.PortfolioId.Should().Be("structured-income-backtest-portfolio");
        openLot.RunId.Should().Be("run-structured-lots");
        openLot.AccountScopeId.Should().Be("structured-income-account");
        openLot.AccountScopeDisplayName.Should().Be("Structured Income Account");
        openLot.LotId.Should().NotBeNullOrWhiteSpace();
        openLot.Symbol.Should().Be("ACME31");
        openLot.OriginalQuantity.Should().Be(1_000_000m);
        openLot.CurrentQuantity.Should().Be(1_000_000m);
        openLot.OriginalFace.Should().Be(1_000_000m);
        openLot.CurrentFace.Should().Be(982_500m);
        openLot.FactorAdjustedQuantity.Should().Be(982_500m);
        openLot.FactorAdjustedFace.Should().Be(982_500m);
        openLot.CostBasis.Should().Be(970_000m);
        openLot.EntryPrice.Should().Be(0.97m);
        openLot.UnrealizedPnl.Should().Be(12_500m);
        openLot.SettleDate.Should().Be(new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.Zero));
        snapshot.RecommendedActions.Should().Contain(action =>
            action.Kind == SecurityMasterRecommendedActionKind.EditSelectedSecurity &&
            action.Detail.Contains("blocking validation issue", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldFlagUnsupportedSchemaCompatibilityInSummary()
    {
        var securityId = Guid.Parse("57575757-5757-5757-5757-575757575757");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Legacy Structured Note", "LSN-01"),
            new SecurityDetailDto(
                SecurityId: securityId,
                AssetClass: "Bond",
                Status: SecurityStatusDto.Active,
                DisplayName: "Legacy Structured Note",
                Currency: "USD",
                CommonTerms: JsonSerializer.SerializeToElement(new
                {
                    issuerName = "Legacy Trust",
                    countryOfRisk = "US"
                }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 5,
                    maturity = "2031-12-15"
                }),
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Cusip,
                        "000000AA1",
                        true,
                        new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero))
                ],
                Aliases: [],
                Version: 4,
                EffectiveFrom: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null));
        queryService.RegisterEconomicDefinition(
            securityId,
            new SecurityEconomicDefinitionRecord(
                SecurityId: securityId,
                AssetClass: "FixedIncome",
                AssetFamily: "StructuredCredit",
                SubType: "StructuredNote",
                TypeName: "Legacy Structured Note",
                IssuerType: "Trust",
                RiskCountry: "US",
                Status: SecurityStatusDto.Active,
                DisplayName: "Legacy Structured Note",
                Currency: "USD",
                Classification: JsonSerializer.SerializeToElement(new
                {
                    assetClass = "FixedIncome"
                }),
                CommonTerms: JsonSerializer.SerializeToElement(new
                {
                    issuerName = "Legacy Trust",
                    countryOfRisk = "US"
                }),
                EconomicTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 9,
                    maturity = new
                    {
                        maturityDate = "2031-12-15"
                    }
                }),
                Provenance: JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = "legacy-edm",
                    asOf = "2026-05-20T10:00:00Z",
                    updatedBy = "workflow.bot"
                }),
                Version: 4,
                EffectiveFrom: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null,
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Cusip,
                        "000000AA1",
                        true,
                        new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero))
                ],
                LegacyAssetClass: "Bond",
                LegacyAssetSpecificTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 5,
                    maturity = "2031-12-15"
                })));
        queryService.RegisterTradingParameters(securityId, CreateTradingParameters(securityId));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(
                services,
                queryService,
                new StubSecurityMasterConflictService([]),
                new StubSecurityValidationService());
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.SchemaCompatibility.Should().NotBeNull();
        snapshot.SchemaCompatibility!.LegacyAssetSpecificTermsSchemaVersion.Should().Be(5);
        snapshot.SchemaCompatibility.EconomicTermsSchemaVersion.Should().Be(9);
        snapshot.SchemaCompatibility.Summary.Should().ContainEquivalentOf("compatibility review is required");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldNotRunReconciliationDuringReadOnlyImpactQuery()
    {
        var securityId = Guid.Parse("78787878-7878-7878-7878-787878787878");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "AAPL"),
            CreateSecurityDetail(securityId, "Apple Inc.", "AAPL"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                "Apple Inc.",
                "AAPL",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));
        queryService.RegisterTradingParameters(securityId, CreateTradingParameters(securityId));

        var reconciliationService = new RecordingReconciliationRunService();

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([]));
            services.RemoveAll<IReconciliationRunService>();
            services.AddSingleton<IReconciliationRunService>(reconciliationService);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "sm-readonly-impact",
            strategyId: "security-impact",
            strategyName: "Security impact",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
            fundProfileId: "fund-alpha"));

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot?fundProfileId=fund-alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.DownstreamImpact.Severity.Should().Be(SecurityMasterImpactSeverity.Unknown);
        snapshot.DownstreamImpact.ReconciliationExposureSummary.Should().Contain("not been materialized");
        snapshot.DownstreamImpact.Summary.Should().Contain("not been materialized");
        reconciliationService.GetLatestForRunCallCount.Should().Be(1);
        reconciliationService.RunAsyncCallCount.Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldReturnOnlySelectedSecurityChallengers()
    {
        var selectedSecurityId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var otherSecurityId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var selectedConflictId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(selectedSecurityId, "Apple Inc.", "AAPL"),
            CreateSecurityDetail(selectedSecurityId, "Apple Inc.", "AAPL"));
        queryService.RegisterSecurity(
            CreateSecuritySummary(otherSecurityId, "Microsoft Corp.", "MSFT"),
            CreateSecurityDetail(otherSecurityId, "Microsoft Corp.", "MSFT"));
        queryService.RegisterEconomicDefinition(
            selectedSecurityId,
            CreateEconomicDefinitionRecord(
                selectedSecurityId,
                "Apple Inc.",
                "AAPL",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflicts = new[]
        {
            new SecurityMasterConflict(
                ConflictId: selectedConflictId,
                SecurityId: selectedSecurityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "AAPL",
                ProviderB: "vendor-b",
                ValueB: "AAPL.O",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero),
                Status: "Open"),
            new SecurityMasterConflict(
                ConflictId: Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                SecurityId: otherSecurityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "MSFT",
                ProviderB: "vendor-c",
                ValueB: "MSFT.O",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 11, 0, 0, TimeSpan.Zero),
                Status: "Open")
        };

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService(conflicts));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{selectedSecurityId}/trust-snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.ConflictAssessments.Should().ContainSingle();
        snapshot.ProvenanceCandidates.Count(candidate => !candidate.IsWinningSource).Should().Be(1);
        snapshot.ProvenanceCandidates.Should().OnlyContain(candidate =>
            candidate.IsWinningSource || candidate.ConflictId == selectedConflictId);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldPreserveWinnerByDefault()
    {
        var securityId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
        var conflictId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "AAPL US"),
            CreateSecurityDetail(securityId, "Apple Inc.", "AAPL US"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                "Apple Inc.",
                "AAPL US",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflict = new SecurityMasterConflict(
            ConflictId: conflictId,
            SecurityId: securityId,
            ConflictKind: "IdentifierMismatch",
            FieldPath: "Identifiers.Primary",
            ProviderA: "golden-edm",
            ValueA: "AAPL US",
            ProviderB: "vendor-b",
            ValueB: "AAPL UW",
            DetectedAt: new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero),
            Status: "Open");

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([conflict]));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot?fundProfileId=fund-alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.ConflictAssessments.Should().ContainSingle();
        snapshot.ConflictAssessments[0].Recommendation.Should().Be(SecurityMasterConflictRecommendationKind.PreserveWinner);
        snapshot.ConflictAssessments[0].RecommendedResolution.Should().Be("AcceptA");
        snapshot.ConflictAssessments[0].RecommendedWinner.Should().Contain("Preserve golden-edm");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldDismissEquivalentNormalizedValues()
    {
        var securityId = Guid.Parse("cccccccc-1111-1111-1111-111111111111");
        var conflictId = Guid.Parse("cccccccc-2222-2222-2222-222222222222");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Berkshire Hathaway", "BRK-B"),
            CreateSecurityDetail(securityId, "Berkshire Hathaway", "BRK-B"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                "Berkshire Hathaway",
                "BRK-B",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflict = new SecurityMasterConflict(
            ConflictId: conflictId,
            SecurityId: securityId,
            ConflictKind: "IdentifierMismatch",
            FieldPath: "Identifiers.Primary",
            ProviderA: "golden-edm",
            ValueA: "BRK-B",
            ProviderB: "vendor-b",
            ValueB: "BRK/B",
            DetectedAt: new DateTimeOffset(2026, 4, 20, 12, 30, 0, TimeSpan.Zero),
            Status: "Open");

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([conflict]));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot?fundProfileId=fund-alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.ConflictAssessments.Should().ContainSingle();
        snapshot.ConflictAssessments[0].Recommendation.Should().Be(SecurityMasterConflictRecommendationKind.DismissAsEquivalent);
        snapshot.ConflictAssessments[0].RecommendedResolution.Should().Be("Dismiss");
        snapshot.ConflictAssessments[0].IsBulkEligible.Should().BeTrue();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterBulkResolve_ShouldResolveOnlyEligibleConflicts()
    {
        var securityId = Guid.Parse("dddddddd-1111-1111-1111-111111111111");
        var equivalentConflictId = Guid.Parse("dddddddd-2222-2222-2222-222222222222");
        var blankWinnerConflictId = Guid.Parse("dddddddd-3333-3333-3333-333333333333");
        var skippedConflictId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "BRK-B"),
            CreateSecurityDetail(securityId, string.Empty, "BRK-B"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                string.Empty,
                "BRK-B",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflictService = new StubSecurityMasterConflictService(
        [
            new SecurityMasterConflict(
                ConflictId: equivalentConflictId,
                SecurityId: securityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "BRK-B",
                ProviderB: "vendor-b",
                ValueB: "BRK/B",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 13, 0, 0, TimeSpan.Zero),
                Status: "Open"),
            new SecurityMasterConflict(
                ConflictId: blankWinnerConflictId,
                SecurityId: securityId,
                ConflictKind: "FieldMismatch",
                FieldPath: "DisplayName",
                ProviderA: "golden-edm",
                ValueA: "",
                ProviderB: "vendor-b",
                ValueB: "Apple Inc.",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 13, 5, 0, TimeSpan.Zero),
                Status: "Open"),
            new SecurityMasterConflict(
                ConflictId: skippedConflictId,
                SecurityId: securityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "BRK-B",
                ProviderB: "vendor-b",
                ValueB: "MSFT",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 13, 10, 0, TimeSpan.Zero),
                Status: "Open")
        ]);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, conflictService);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(
            "/api/workstation/security-master/conflicts/bulk-resolve",
            new BulkResolveSecurityMasterConflictsRequest(
                ConflictIds: [equivalentConflictId, blankWinnerConflictId, skippedConflictId],
                ResolvedBy: "desktop-user",
                Reason: "bulk assist",
                FundProfileId: "fund-alpha"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkResolveSecurityMasterConflictsResult>(ServerJsonOptions);

        result.Should().NotBeNull();
        result!.Requested.Should().Be(3);
        result.Eligible.Should().Be(2);
        result.Resolved.Should().Be(2);
        result.Skipped.Should().Be(1);
        result.ResolvedConflictIds.Should().Contain([equivalentConflictId, blankWinnerConflictId]);
        result.SkippedReasons.Should().ContainKey(skippedConflictId);
        conflictService.ResolvedRequests.Select(request => request.ConflictId)
            .Should()
            .Contain([equivalentConflictId, blankWinnerConflictId]);
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
        rows.Should().OnlyContain(row => row.GetProperty("artifactCompleteness").ValueKind == JsonValueKind.Object);
        rows.Should().OnlyContain(row => row.GetProperty("compatibilityWarnings").ValueKind == JsonValueKind.Array);
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
    public async Task MapWorkstationEndpoints_CompareRuns_ShouldReturnBadRequestWhenTooManyRunIdsProvided()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var runIds = Enumerable.Range(1, 11).Select(index => $"cmp-{index}").ToArray();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/workstation/runs/compare", new { runIds });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MapStrategyRunsCompare_ShouldReturnBadRequestWhenTooManyRunIdsProvided()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

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
    public async Task MapWorkstationEndpoints_RunLineageTimeline_ShouldExposeDedicatedLineageContract()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

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
        });

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
        });

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
        });

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

    [Fact]
    public async Task MapWorkstationEndpoints_PortfolioWorkspace_WithoutRunReadService_ShouldReturnEmptyRunsAndStableShape()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        using var portfolio = await ReadJsonAsync(client, "/api/workstation/portfolio");

        portfolio.RootElement.GetProperty("metrics").GetArrayLength().Should().Be(4);
        portfolio.RootElement.GetProperty("positions").GetArrayLength().Should().Be(0);
        portfolio.RootElement.GetProperty("runs").GetArrayLength().Should().Be(0);
        portfolio.RootElement.GetProperty("risk").GetProperty("state").GetString().Should().Be("Healthy");
        portfolio.RootElement.GetProperty("brokerage").GetProperty("connection").GetString().Should().NotBeNullOrEmpty();

        // CashFlow summary is always present (zeroed when there is no run data) so the
        // Portfolio screen never has to discriminate between "no data" and "missing field".
        var cashFlow = portfolio.RootElement.GetProperty("cashFlow");
        cashFlow.ValueKind.Should().NotBe(JsonValueKind.Null);
        cashFlow.GetProperty("runsWithCashSignals").GetInt32().Should().Be(0);
        cashFlow.GetProperty("runsWithCashVariance").GetInt32().Should().Be(0);
        cashFlow.GetProperty("tone").GetString().Should().Be("default");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportingWorkspace_ShouldReturnTypedReportingPayloadWithProfiles()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        using var reporting = await ReadJsonAsync(client, "/api/workstation/reporting");

        var reportingSection = reporting.RootElement.GetProperty("reporting");
        reportingSection.GetProperty("profileCount").GetInt32().Should().BeGreaterThan(0);
        reportingSection.GetProperty("summary").GetString().Should().Contain("profiles are available");
        reportingSection.GetProperty("reportPackTargets").EnumerateArray()
            .Select(t => t.GetString())
            .Should().Contain("board").And.Contain("investor").And.Contain("compliance");

        var profiles = reportingSection.GetProperty("profiles").EnumerateArray().ToArray();
        profiles.Should().NotBeEmpty();
        profiles.Should().AllSatisfy(profile =>
        {
            profile.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
            profile.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
            profile.GetProperty("format").GetString().Should().NotBeNullOrEmpty();
        });

        var recommended = reportingSection.GetProperty("recommendedProfiles").EnumerateArray()
            .Select(r => r.GetString())
            .ToArray();
        recommended.Should().Contain(value => value == "excel" || value == "python-pandas");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingPayload_WithLiveQuote_ReplacesPlaceholderMarkAndComputesUnrealizedPnl()
    {
        var publisher = new Meridian.Tests.TestHelpers.TestMarketEventPublisher();
        var quotes = new Meridian.Domain.Collectors.QuoteCollector(publisher);
        quotes.OnQuote(new Meridian.Contracts.Domain.Models.MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            BidPrice: 199.90m,
            BidSize: 100,
            AskPrice: 200.10m,
            AskSize: 100,
            StreamId: "TEST",
            Venue: "TEST"));

        var position = new Meridian.Execution.Models.ExecutionPosition(
            Symbol: "AAPL",
            Quantity: 100,
            AverageCostBasis: 150m,
            UnrealisedPnl: 0m,
            RealisedPnl: 0m);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new LiveMarkTestPortfolioState(position));
            services.AddSingleton(quotes);
        });

        var client = app.GetTestClient();
        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");

        var rows = trading.RootElement.GetProperty("positions").EnumerateArray().ToArray();
        rows.Should().NotBeEmpty();
        var row = rows[0];
        row.GetProperty("positionKey").GetString().Should().Be("AAPL");
        row.GetProperty("symbol").GetString().Should().Be("AAPL");

        // Mid of (199.90, 200.10) = 200.00
        row.GetProperty("markPrice").GetString().Should().Be("200.00");

        // (200 - 150) * 100 = +5,000 → FormatCurrency renders "+$5K"
        var unrealized = row.GetProperty("unrealizedPnl").GetString();
        unrealized.Should().NotBe("—");
        unrealized.Should().Be("+$5K");

        // |100 * 200| = 20,000 → "+$20K"
        var exposure = row.GetProperty("exposure").GetString();
        exposure.Should().NotBe("—");
        exposure.Should().Be("+$20K");

        // Risk panel net/gross exposure should also reflect live mark, not cost basis
        // (cost-basis exposure would have been |100 * 150| = 15,000 → "+$15K")
        var risk = trading.RootElement.GetProperty("risk");
        risk.GetProperty("netExposure").GetString().Should().Be("+$20K");
        risk.GetProperty("grossExposure").GetString().Should().Be("+$20K");
        // Buying power = Cash (100K, default) → 20,000 / 100,000 = 20% utilisation.
        risk.GetProperty("buyingPowerUsed").GetString().Should().Be("+20.0%");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingPayload_WithoutQuote_FallsBackToLastTradePrice()
    {
        var publisher = new Meridian.Tests.TestHelpers.TestMarketEventPublisher();
        var quotes = new Meridian.Domain.Collectors.QuoteCollector(publisher);
        var trades = new Meridian.Domain.Collectors.TradeDataCollector(publisher, quotes);
        trades.OnTrade(new Meridian.Domain.Models.MarketTradeUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 175m,
            Size: 50,
            Aggressor: Meridian.Contracts.Domain.Enums.AggressorSide.Buy,
            SequenceNumber: 1,
            StreamId: "TEST",
            Venue: "TEST"));

        var position = new Meridian.Execution.Models.ExecutionPosition(
            Symbol: "AAPL",
            Quantity: 100,
            AverageCostBasis: 150m,
            UnrealisedPnl: 0m,
            RealisedPnl: 0m);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new LiveMarkTestPortfolioState(position));
            services.AddSingleton(quotes);
            services.AddSingleton(trades);
        });

        var client = app.GetTestClient();
        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");

        var row = trading.RootElement.GetProperty("positions").EnumerateArray().First();
        row.GetProperty("markPrice").GetString().Should().Be("175.00");
        // (175 - 150) * 100 = +2,500 → "+$2.5K"
        row.GetProperty("unrealizedPnl").GetString().Should().Be("+$2.5K");
        // |100 * 175| = 17,500 → "+$17.5K"
        row.GetProperty("exposure").GetString().Should().Be("+$17.5K");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingPayload_WithoutAnyMarketData_RetainsPlaceholderMark()
    {
        var position = new Meridian.Execution.Models.ExecutionPosition(
            Symbol: "AAPL",
            Quantity: 100,
            AverageCostBasis: 150m,
            UnrealisedPnl: 0m,
            RealisedPnl: 0m);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new LiveMarkTestPortfolioState(position));
        });

        var client = app.GetTestClient();
        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");

        var row = trading.RootElement.GetProperty("positions").EnumerateArray().First();
        row.GetProperty("markPrice").GetString().Should().Be("—");
        row.GetProperty("exposure").GetString().Should().Be("—");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_PortfolioPayload_WithLiveQuote_SurfacesLiveMarkAndExposure()
    {
        var publisher = new Meridian.Tests.TestHelpers.TestMarketEventPublisher();
        var quotes = new Meridian.Domain.Collectors.QuoteCollector(publisher);
        quotes.OnQuote(new Meridian.Contracts.Domain.Models.MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "MSFT",
            BidPrice: 419.50m,
            BidSize: 200,
            AskPrice: 420.50m,
            AskSize: 200,
            StreamId: "TEST",
            Venue: "TEST"));

        var position = new Meridian.Execution.Models.ExecutionPosition(
            Symbol: "MSFT",
            Quantity: 50,
            AverageCostBasis: 400m,
            UnrealisedPnl: 0m,
            RealisedPnl: 0m);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new LiveMarkTestPortfolioState(position));
            services.AddSingleton(quotes);
        });

        var client = app.GetTestClient();
        using var portfolio = await ReadJsonAsync(client, "/api/workstation/portfolio");

        var row = portfolio.RootElement.GetProperty("positions").EnumerateArray().First();
        // Mid of (419.50, 420.50) = 420.00
        row.GetProperty("markPrice").GetString().Should().Be("420.00");
        // (420 - 400) * 50 = +1,000 → "+$1K"
        row.GetProperty("unrealizedPnl").GetString().Should().Be("+$1K");
        // |50 * 420| = 21,000 → "+$21K"
        row.GetProperty("exposure").GetString().Should().Be("+$21K");

        var risk = portfolio.RootElement.GetProperty("risk");
        risk.GetProperty("grossExposure").GetString().Should().Be("+$21K");
        // Buying power = Cash (100K, default) → 21,000 / 100,000 = 21% utilisation.
        risk.GetProperty("buyingPowerUsed").GetString().Should().Be("+21.0%");
    }

    private sealed class LiveMarkTestPortfolioState : Meridian.Execution.Models.IPortfolioState
    {
        public LiveMarkTestPortfolioState(params Meridian.Execution.Models.ExecutionPosition[] positions)
        {
            Positions = positions.ToDictionary(
                position => position.Symbol,
                position => (Meridian.Execution.Sdk.IPosition)position,
                StringComparer.OrdinalIgnoreCase);
            UnrealisedPnl = positions.Sum(static p => p.UnrealisedPnl);
            RealisedPnl = positions.Sum(static p => p.RealisedPnl);
        }

        public decimal Cash => 100_000m;
        public decimal PortfolioValue => 250_000m;
        public decimal UnrealisedPnl { get; }
        public decimal RealisedPnl { get; }
        public IReadOnlyDictionary<string, Meridian.Execution.Sdk.IPosition> Positions { get; }
    }

    private sealed class ThrowingReconciliationBreakQueueRepository : IReconciliationBreakQueueRepository
    {
        public Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(
            ReconciliationBreakQueueStatus? status = null,
            CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<ReconciliationBreakQueueItem?> GetByIdAsync(string breakId, CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<bool> CreateIfMissingAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task SaveAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<bool> DeleteAsync(string breakId, CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(
            ReviewReconciliationBreakRequest request,
            CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(
            ResolveReconciliationBreakRequest request,
            CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(
            string breakId,
            CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");
    }

    private static void WriteReadyDk1Packet(
        string automationRoot,
        string? operatorSignoffJson = null,
        bool includeContractReview = true,
        string packetStatus = "ready-for-operator-review",
        string blockersJson = "[]")
    {
        var packetDirectory = Path.Combine(automationRoot, "unit-ready");
        Directory.CreateDirectory(packetDirectory);
        operatorSignoffJson ??= """
            {
              "requiredOwners": [ "Data Operations", "Provider Reliability", "Trading" ],
              "status": "pending",
              "requiredBeforeDk1Exit": true
            }
            """;
        File.WriteAllText(
            Path.Combine(packetDirectory, "dk1-pilot-parity-packet.json"),
            """
            {
              "generatedAtUtc": "2026-04-25T20:28:38Z",
              "sourceSummary": "artifacts/provider-validation/_automation/unit-ready/wave1-validation-summary.json",
              "status": "__PACKET_STATUS__",
              "sampleReview": {
                "requiredCount": 4,
                "samples": [
                  {
                    "id": "DK1-ALPACA-QUOTE-GOLDEN",
                    "provider": "Alpaca",
                    "requiredStep": "Alpaca core provider confidence",
                    "stepStatus": "passed",
                    "observed": true,
                    "status": "ready",
                    "missingRequirements": [],
                    "evidenceAnchors": [
                      "tests/Meridian.Tests/TestData/Golden/alpaca-quote-pipeline.json",
                      "AlpacaQuotePipelineGoldenTests"
                    ],
                    "acceptanceCheck": "Golden quote pipeline fixture matched the committed output."
                  },
                  {
                    "id": "DK1-ALPACA-PARSER-EDGE-CASES",
                    "provider": "Alpaca",
                    "requiredStep": "Alpaca core provider confidence",
                    "stepStatus": "passed",
                    "observed": true,
                    "status": "ready",
                    "missingRequirements": [],
                    "evidenceAnchors": [
                      "AlpacaMessageParsingTests",
                      "AlpacaQuoteRoutingTests",
                      "AlpacaCredentialAndReconnectTests"
                    ],
                    "acceptanceCheck": "Parser edge cases preserved routing and reconnect behavior."
                  },
                  {
                    "id": "DK1-ROBINHOOD-SUPPORTED-SURFACE",
                    "provider": "Robinhood",
                    "requiredStep": "Robinhood supported surface",
                    "stepStatus": "passed",
                    "observed": true,
                    "status": "ready",
                    "missingRequirements": [],
                    "evidenceAnchors": [
                      "RobinhoodMarketDataClientTests",
                      "RobinhoodBrokerageGatewayTests",
                      "artifacts/provider-validation/robinhood/2026-04-09/manifest.json"
                    ],
                    "acceptanceCheck": "Bounded runtime packet and offline provider surface remain aligned."
                  },
                  {
                    "id": "DK1-YAHOO-HISTORICAL-FALLBACK",
                    "provider": "Yahoo",
                    "requiredStep": "Yahoo historical-only core provider",
                    "stepStatus": "passed",
                    "observed": true,
                    "status": "ready",
                    "missingRequirements": [],
                    "evidenceAnchors": [
                      "YahooFinanceHistoricalDataProviderTests",
                      "YahooFinanceIntradayContractTests"
                    ],
                    "acceptanceCheck": "Historical fallback fixtures remain stable without implying live readiness."
                  }
                ]
              },
              "evidenceDocuments": [
                { "name": "DK1 pilot parity runbook", "gate": "parity", "path": "docs/status/evidence/dk1-pilot-parity-runbook.md", "exists": true, "status": "validated", "missingRequirements": [] },
                { "name": "DK1 trust rationale mapping", "gate": "explainability", "path": "docs/status/evidence/dk1-trust-rationale-mapping.md", "exists": true, "status": "validated", "missingRequirements": [] },
                { "name": "DK1 baseline trust thresholds", "gate": "calibration", "path": "docs/status/evidence/dk1-baseline-trust-thresholds.md", "exists": true, "status": "validated", "missingRequirements": [] },
                { "name": "Provider validation matrix", "gate": "parity", "path": "docs/status/provider-validation-matrix.md", "exists": true, "status": "validated", "missingRequirements": [] }
              ],
              __CONTRACT_REVIEW__
              "operatorSignoff": __OPERATOR_SIGNOFF__,
              "blockers": __BLOCKERS__
            }
            """
            .Replace("__PACKET_STATUS__", packetStatus)
            .Replace("__CONTRACT_REVIEW__", includeContractReview
                ? """
                  "trustRationaleContract": {
                    "documentPath": "docs/status/evidence/dk1-trust-rationale-mapping.md",
                    "requiredPayloadFields": [ "signalSource", "reasonCode", "recommendedAction" ],
                    "requiredReasonCodes": [
                      "HEALTHY_BASELINE",
                      "PROVIDER_STREAM_DEGRADED",
                      "RECONNECT_INSTABILITY",
                      "ERROR_RATE_SPIKE",
                      "LATENCY_REGRESSION",
                      "PARITY_DRIFT_DETECTED",
                      "DATA_COMPLETENESS_GAP",
                      "CALIBRATION_STALE"
                    ],
                    "status": "validated",
                    "missingRequirements": []
                  },
                  "baselineThresholdContract": {
                    "documentPath": "docs/status/evidence/dk1-baseline-trust-thresholds.md",
                    "requiredMetrics": [
                      "Composite trust score",
                      "Connection stability score",
                      "Error-rate score",
                      "Latency score",
                      "Reconnect score"
                    ],
                    "fpFnReviewRequired": true,
                    "status": "validated",
                    "missingRequirements": []
                  },
                """
                : string.Empty)
            .Replace("__OPERATOR_SIGNOFF__", operatorSignoffJson)
            .Replace("__BLOCKERS__", blockersJson));
    }

    private static void WriteProviderValidationEvidenceBundle(string automationRoot, string bundleJson)
    {
        var bundleDirectory = Path.Combine(automationRoot, "unit-ready");
        Directory.CreateDirectory(bundleDirectory);
        File.WriteAllText(
            Path.Combine(bundleDirectory, "provider-validation-evidence-bundle.json"),
            bundleJson);
    }

    private static void RegisterSecurityMasterWorkbenchServices(
        IServiceCollection services,
        StubSecurityMasterQueryService queryService,
        StubSecurityMasterConflictService conflictService,
        StubSecurityValidationService? validationService = null)
    {
        services.AddSingleton<ISecurityMasterQueryService>(queryService);
        services.AddSingleton<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>(queryService);
        services.AddSingleton<ISecurityValidationService>(validationService ?? new StubSecurityValidationService());
        services.AddSingleton<ISecurityMasterConflictService>(conflictService);
        services.AddSingleton<ISecurityMasterIngestStatusService>(new StubSecurityMasterIngestStatusService());
        RegisterRunReadServices(services);
        services.AddSingleton<ReportGenerationService>();
        services.AddSingleton<ISecurityMasterWorkbenchQueryService, SecurityMasterWorkbenchQueryService>();
    }

    private static SecuritySummaryDto CreateSecuritySummary(Guid securityId, string displayName, string primaryIdentifier)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            PrimaryIdentifier: primaryIdentifier,
            Currency: "USD",
            Version: 4);

    private static SecurityDetailDto CreateSecurityDetail(Guid securityId, string displayName, string primaryIdentifier)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            Currency: "USD",
            CommonTerms: CreateEmptyJson(),
            AssetSpecificTerms: CreateEmptyJson(),
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Ticker,
                    primaryIdentifier,
                    true,
                    new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
                    null,
                    null)
            ],
            Aliases: [],
            Version: 4,
            EffectiveFrom: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null);

    private static SecurityEconomicDefinitionRecord CreateEconomicDefinitionRecord(
        Guid securityId,
        string displayName,
        string primaryIdentifier,
        JsonElement provenance)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            AssetFamily: "Public Equity",
            SubType: "CommonStock",
            TypeName: "Common Stock",
            IssuerType: "Corporate",
            RiskCountry: "US",
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            Currency: "USD",
            Classification: CreateEmptyJson(),
            CommonTerms: CreateEmptyJson(),
            EconomicTerms: CreateEmptyJson(),
            Provenance: provenance,
            Version: 4,
            EffectiveFrom: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Ticker,
                    primaryIdentifier,
                    true,
                    new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
                    null,
                    null)
            ],
            LegacyAssetClass: null,
            LegacyAssetSpecificTerms: null);

    private static TradingParametersDto CreateTradingParameters(Guid securityId)
        => new(
            SecurityId: securityId,
            LotSize: 1m,
            TickSize: 0.01m,
            ContractMultiplier: null,
            MarginRequirementPct: null,
            TradingHoursUtc: "13:30-20:00",
            CircuitBreakerThresholdPct: null,
            AsOf: new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero));

    private static OrderState CreateExecutionOrderState(string orderId, string symbol, decimal quantity) => new()
    {
        OrderId = orderId,
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = Meridian.Execution.Sdk.OrderType.Market,
        Quantity = quantity,
        Status = Meridian.Execution.Sdk.OrderStatus.Accepted,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow
    };

    private static ExecutionReport CreateExecutionFill(string orderId, string symbol, decimal quantity, decimal fillPrice) => new()
    {
        OrderId = orderId,
        ReportType = ExecutionReportType.Fill,
        Symbol = symbol,
        Side = OrderSide.Buy,
        OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
        OrderQuantity = quantity,
        FilledQuantity = quantity,
        FillPrice = fillPrice,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static JsonElement CreateEmptyJson()
        => JsonDocument.Parse("{}").RootElement.Clone();

    private sealed class StubSecurityValidationService : ISecurityValidationService
    {
        private readonly Dictionary<Guid, SecurityValidationReportDto> _reports = new();

        public void RegisterReport(Guid securityId, SecurityValidationReportDto report)
            => _reports[securityId] = report;

        public Task<SecurityValidationReportDto> ValidateSecurityAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(
                _reports.TryGetValue(securityId, out var report)
                    ? report
                    : CreateReport(securityId, hasBlockingIssues: false, errorIssueCount: 0, issues: []));

        public Task<SecurityValidationReportDto> ValidateUniverseAsync(CancellationToken ct = default)
            => Task.FromResult(CreateReport(null, hasBlockingIssues: false, errorIssueCount: 0, issues: []));

        public SecurityValidationReportDto ValidateRecord(
            SecurityProjectionRecord record,
            IReadOnlyList<SecurityProjectionRecord> universe,
            DateTimeOffset? evaluatedAtUtc = null)
            => _reports.TryGetValue(record.SecurityId, out var report)
                ? report
                : CreateReport(record.SecurityId, hasBlockingIssues: false, errorIssueCount: 0, issues: []);

        private static SecurityValidationReportDto CreateReport(
            Guid? securityId,
            bool hasBlockingIssues,
            int errorIssueCount,
            IReadOnlyList<SecurityValidationIssueDto> issues)
            => new(
                SecurityId: securityId,
                Scope: securityId.HasValue ? "Security" : "Universe",
                EvaluatedAtUtc: new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
                HasBlockingIssues: hasBlockingIssues,
                CriticalIssueCount: 0,
                ErrorIssueCount: errorIssueCount,
                Issues: issues);
    }

    private sealed class RecordingReconciliationRunService : IReconciliationRunService
    {
        public int RunAsyncCallCount { get; private set; }
        public int GetLatestForRunCallCount { get; private set; }

        public Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default)
        {
            RunAsyncCallCount++;
            return Task.FromResult<ReconciliationRunDetail?>(null);
        }

        public Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default)
            => Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default)
        {
            GetLatestForRunCallCount++;
            return Task.FromResult<ReconciliationRunDetail?>(null);
        }

        public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>([]);
    }

    private sealed class StaticReconciliationRunService(ReconciliationRunDetail detail) : IReconciliationRunService
    {
        public ReconciliationRunRequest? LastRunRequest { get; private set; }

        public Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default)
        {
            LastRunRequest = request;
            return Task.FromResult<ReconciliationRunDetail?>(detail);
        }

        public Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default) =>
            string.Equals(reconciliationRunId, detail.Summary.ReconciliationRunId, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult<ReconciliationRunDetail?>(detail)
                : Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default) =>
            string.Equals(runId, detail.Summary.RunId, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult<ReconciliationRunDetail?>(detail)
                : Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>(
                string.Equals(runId, detail.Summary.RunId, StringComparison.OrdinalIgnoreCase)
                    ? [detail.Summary]
                    : []);
    }

    private static KernelObservabilityService CreateRecoveredKernelObservability()
    {
        var observability = new KernelObservabilityService();

        for (var index = 0; index < 30; index++)
        {
            var context = new ProviderRouteContext(
                ProviderCapabilityKind.HistoricalBars,
                Workspace: "data-ops",
                Symbol: $"baseline-{index}");
            RecordKernelObservation(
                observability,
                context,
                BuildKernelSuccessResult(context, "route-steady", ["healthy-route"]),
                score: 96);
        }

        for (var index = 0; index < 30; index++)
        {
            var context = new ProviderRouteContext(
                ProviderCapabilityKind.HistoricalBars,
                Workspace: "data-ops",
                Symbol: $"critical-{index}");
            RecordKernelObservation(
                observability,
                context,
                BuildKernelCriticalResult(context, "route-review", ["manual-review"]),
                score: 12);
        }

        for (var index = 0; index < 30; index++)
        {
            var context = new ProviderRouteContext(
                ProviderCapabilityKind.HistoricalBars,
                Workspace: "data-ops",
                Symbol: $"recovery-{index}");
            RecordKernelObservation(
                observability,
                context,
                BuildKernelSuccessResult(context, "route-steady", ["healthy-route"]),
                score: 97);
        }

        return observability;
    }

    private static void RecordKernelObservation(
        KernelObservabilityService observability,
        ProviderRouteContext context,
        ProviderRouteResult result,
        double score)
    {
        var scope = observability.BeginExecution(context);
        var healthByConnection = result.SelectedDecision is null
            ? new Dictionary<string, ProviderConnectionHealthSnapshot>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ProviderConnectionHealthSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [result.SelectedDecision.ConnectionId] = new(
                    result.SelectedDecision.ConnectionId,
                    result.SelectedDecision.ProviderFamilyId,
                    result.SelectedDecision.IsHealthy,
                    result.SelectedDecision.IsHealthy ? "healthy" : "degraded",
                    score,
                    DateTimeOffset.UtcNow)
            };

        observability.RecordResult(context, result, healthByConnection, scope);
    }

    private static ProviderRouteResult BuildKernelSuccessResult(
        ProviderRouteContext context,
        string connectionId,
        IReadOnlyList<string>? reasonCodes = null)
    {
        var selected = new ProviderRouteDecision(
            connectionId,
            "alpha",
            context.Capability,
            ProviderSafetyMode.HealthAwareFailover,
            ScopeRank: 0,
            Priority: 0,
            IsHealthy: true,
            ReasonCodes: reasonCodes ?? [],
            FallbackConnectionIds: []);

        return new ProviderRouteResult(
            context,
            selected,
            Candidates: [selected],
            SkippedCandidates: []);
    }

    private static ProviderRouteResult BuildKernelCriticalResult(
        ProviderRouteContext context,
        string connectionId,
        IReadOnlyList<string>? reasonCodes = null)
    {
        var selected = new ProviderRouteDecision(
            connectionId,
            "beta",
            context.Capability,
            ProviderSafetyMode.ManualApprovalRequired,
            ScopeRank: 0,
            Priority: 0,
            IsHealthy: true,
            ReasonCodes: reasonCodes ?? [],
            FallbackConnectionIds: []);

        return new ProviderRouteResult(
            context,
            selected,
            Candidates: [selected],
            SkippedCandidates: [],
            RequiresManualApproval: true);
    }

    private static string CamelCase(string propertyName) => JsonNamingPolicy.CamelCase.ConvertName(propertyName);

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static async Task<OperationsTransitionResultDto> PostTransitionAsync(HttpClient client, string path, object payload)
    {
        var response = await client.PostAsJsonAsync(path, payload, ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue(result.ErrorMessage);
        result.Workflow.Should().NotBeNull();
        return result;
    }

    private static IReadOnlyList<OperationsChecklistControlApprovalDto> RequiredOperationsChecklistControlApprovals() =>
    [
        new("close-gate-brokeringest", "operations-lead", new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero)),
        new("close-gate-securitymaster", "security-master-lead", new DateTimeOffset(2026, 5, 31, 12, 1, 0, TimeSpan.Zero)),
        new("close-gate-ledgerposting", "ledger-lead", new DateTimeOffset(2026, 5, 31, 12, 2, 0, TimeSpan.Zero)),
        new("close-gate-reconciliation", "reconciliation-lead", new DateTimeOffset(2026, 5, 31, 12, 3, 0, TimeSpan.Zero)),
        new("close-gate-approval", "controller", new DateTimeOffset(2026, 5, 31, 12, 4, 0, TimeSpan.Zero)),
        new("close-gate-approval", "fund-admin", new DateTimeOffset(2026, 5, 31, 12, 5, 0, TimeSpan.Zero))
    ];

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<T>(json, ServerJsonOptions);
        result.Should().NotBeNull($"expected a {typeof(T).Name} in response body, but got: {json}");
        return result!;
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload, ServerJsonOptions), Encoding.UTF8, "application/json");

    private static bool ContainsStringValue(JsonElement element, string expectedValue)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => string.Equals(element.GetString(), expectedValue, StringComparison.Ordinal),
            JsonValueKind.Object => element.EnumerateObject().Any(property => ContainsStringValue(property.Value, expectedValue)),
            JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsStringValue(item, expectedValue)),
            _ => false
        };
    }

    private static async Task<OperatorWorkflowHomeSummary> ReadWorkflowSummaryAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<OperatorWorkflowHomeSummary>(ServerJsonOptions);
        summary.Should().NotBeNull();
        return summary!;
    }

    private static WorkspaceWorkflowSummary GetWorkspace(OperatorWorkflowHomeSummary summary, string workspaceId)
        => summary.Workspaces.Single(workspace =>
            string.Equals(workspace.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase));

    private static StrategyRunEntry BuildRun(
        string runId,
        string strategyId,
        string strategyName,
        RunType runType,
        DateTimeOffset startedAt,
        string? datasetReference = null,
        string? feedReference = null,
        string? fundProfileId = null)
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
            AuditReference = $"audit-{runId}",
            FundProfileId = fundProfileId
        };
    }

    private static StrategyRunEntry BuildActivePaperRun(string runId, bool withBreaks)
    {
        if (withBreaks)
        {
            var mismatched = BuildReconciliationMismatchRun(runId);
            return mismatched with
            {
                RunType = RunType.Paper,
                EndedAt = mismatched.EndedAt,
                Engine = "BrokerPaper",
                TerminalStatus = StrategyRunStatus.Running,
                ParentRunId = "workflow-backtest-candidate",
                FundProfileId = "northwind-income",
                FundDisplayName = "Northwind Income",
                AuditReference = $"audit-{runId}"
            };
        }

        var startedAt = new DateTimeOffset(2026, 3, 22, 14, 0, 0, TimeSpan.Zero);
        var portfolioAsOf = startedAt.AddMinutes(30);
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m)
        };
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: BacktestDefaults.DefaultBrokerageAccountId,
            DisplayName: "Primary Brokerage",
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Simulated Broker",
            Cash: 600m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            Equity: 1_000m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: portfolioAsOf,
            Date: DateOnly.FromDateTime(portfolioAsOf.UtcDateTime),
            Cash: 600m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            TotalEquity: 1_000m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

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
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 0m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());
        var result = new BacktestResult(
            Request: new BacktestRequest(
                From: new DateOnly(2026, 3, 21),
                To: new DateOnly(2026, 3, 22),
                Symbols: ["AAPL"],
                InitialCash: 1_000m,
                DataRoot: "./data"),
            Universe: new HashSet<string>(["AAPL"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: CreateWorkflowBalancedLedger(startedAt, portfolioAsOf),
            ElapsedTime: TimeSpan.FromMinutes(30),
            TotalEventsProcessed: 42);

        return StrategyRunEntry.Start("workflow-paper-strategy", "Workflow Paper Strategy", RunType.Paper) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = portfolioAsOf,
            Metrics = result,
            DatasetReference = "dataset/us/equities",
            FeedReference = "synthetic:equities",
            PortfolioId = "workflow-paper-portfolio",
            LedgerReference = "workflow-paper-ledger",
            AuditReference = $"audit-{runId}",
            Engine = "BrokerPaper",
            TerminalStatus = StrategyRunStatus.Running,
            ParentRunId = "workflow-backtest-candidate",
            FundProfileId = "northwind-income",
            FundDisplayName = "Northwind Income"
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

    private static StrategyRunEntry BuildContinuityRun(string runId, bool includeCashFlows = true, bool includeFills = false, StrategyRunPromotionState promotionState = StrategyRunPromotionState.None)
    {
        var run = BuildReconciliationReadyRun(runId);
        var cashFlows = includeCashFlows
            ? new CashFlowEntry[]
            {
                new TradeCashFlow(run.StartedAt.AddMinutes(10), -400m, "AAPL", 10L, 40m),
                new CommissionCashFlow(run.StartedAt.AddMinutes(10), -1m, "AAPL", Guid.NewGuid()),
                new DividendCashFlow(run.StartedAt.AddDays(3), 25m, "AAPL", 10L, 2.5m)
            }
            : [];
        var fills = includeFills
            ? new FillEvent[]
            {
                new(
                    FillId: Guid.NewGuid(),
                    OrderId: Guid.NewGuid(),
                    Symbol: "AAPL",
                    FilledQuantity: 10L,
                    FillPrice: 40m,
                    Commission: 1m,
                    FilledAt: run.StartedAt.AddMinutes(10),
                    AccountId: "default-brokerage")
            }
            : [];

        var runType = promotionState is StrategyRunPromotionState.CandidateForLive or StrategyRunPromotionState.LiveManaged
            ? RunType.Paper
            : run.RunType;

        return run with
        {
            RunType = runType,
            Metrics = run.Metrics! with
            {
                CashFlows = cashFlows,
                Fills = fills
            },
            FundProfileId = "alpha-credit",
            FundDisplayName = "Alpha Credit"
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

    private static BacktestResult BuildBacktestResultWithOpenLots(
        string symbol,
        long quantity,
        decimal entryPrice,
        string accountId,
        string accountDisplayName)
    {
        var startedAt = new DateTimeOffset(2026, 5, 14, 14, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddDays(6);
        var openLot = new OpenLot(
            LotId: Guid.Parse("33333333-cccc-4444-dddd-333333333333"),
            Symbol: symbol,
            Quantity: quantity,
            EntryPrice: entryPrice,
            OpenedAt: startedAt,
            OpenFillId: Guid.Parse("44444444-dddd-4444-eeee-444444444444"),
            AccountId: accountId,
            Notes: "Trust remittance carry lot");
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = new(symbol, quantity, entryPrice, 12_500m, 0m, [openLot])
        };
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: accountId,
            DisplayName: accountDisplayName,
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Structured Credit Broker",
            Cash: 250_000m,
            MarginBalance: 0m,
            LongMarketValue: 982_500m,
            ShortMarketValue: 0m,
            Equity: 1_232_500m,
            Positions: positions,
            Rules: new FinancialAccountRules(),
            OpenLots: [openLot]);
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 250_000m,
            MarginBalance: 0m,
            LongMarketValue: 982_500m,
            ShortMarketValue: 0m,
            TotalEquity: 1_232_500m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

        var request = new BacktestRequest(
            From: new DateOnly(2026, 5, 14),
            To: new DateOnly(2026, 5, 20),
            Symbols: [symbol],
            InitialCash: 1_220_000m,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_220_000m,
            FinalEquity: 1_232_500m,
            GrossPnl: 12_500m,
            NetPnl: 12_500m,
            TotalReturn: 0.0102459016393442622950819672m,
            AnnualizedReturn: 0.0102459016393442622950819672m,
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
        PostBalancedEntry(ledger, startedAt, $"Buy {symbol}",
        [
            (LedgerAccounts.Securities(symbol), 970_000m, 0m),
            (LedgerAccounts.Cash, 0m, 970_000m)
        ]);

        return new BacktestResult(
            Request: request,
            Universe: new HashSet<string>([symbol], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(45),
            TotalEventsProcessed: 64);
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
                Severity: ReconciliationBreakSeverity.Medium,
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

    private static ReconciliationRunDetail BuildOperationsContinuityReconciliationDetail(
        string reconciliationRunId,
        string runId)
    {
        var createdAt = new DateTimeOffset(2026, 5, 20, 15, 30, 0, TimeSpan.Zero);
        var breaks = new[]
        {
            new ReconciliationBreakDto(
                CheckId: "bank-break-1",
                Label: "Bank cash missing ledger posting",
                Category: ReconciliationBreakCategory.MissingLedgerCoverage,
                Status: ReconciliationBreakStatus.Open,
                MissingSource: "ledger",
                ExpectedAmount: 250m,
                ActualAmount: null,
                Variance: 250m,
                Severity: ReconciliationBreakSeverity.Critical,
                Reason: "Normalized bank activity has no matching ledger posting.",
                ExpectedAsOf: createdAt,
                ActualAsOf: null)
        };
        var summary = new ReconciliationRunSummary(
            ReconciliationRunId: reconciliationRunId,
            RunId: runId,
            CreatedAt: createdAt,
            PortfolioAsOf: createdAt,
            LedgerAsOf: createdAt,
            MatchCount: 2,
            BreakCount: breaks.Length,
            OpenBreakCount: breaks.Length,
            HasTimingDrift: false,
            AmountTolerance: 0.05m,
            MaxAsOfDriftMinutes: 10,
            SecurityIssueCount: 1,
            HasSecurityCoverageIssues: true,
            BankTransactionCount: 2,
            BankBreakCount: 1,
            ExpectedAccountingEventCount: 3,
            ExpectedJournalPreviewCount: 2,
            SecurityMasterAccountingIssueCount: 1,
            HasSecurityMasterAccountingIssues: true);

        return new ReconciliationRunDetail(
            summary,
            Matches: [],
            Breaks: breaks,
            SecurityCoverageIssues:
            [
                new ReconciliationSecurityCoverageIssueDto(
                    Source: "ledger",
                    Symbol: "BOND1",
                    AccountName: "Fixed income",
                    Reason: "Security Master mapping requires review.",
                    Code: "SM_RECON_SECURITY_UNRESOLVED",
                    Severity: ReconciliationBreakSeverity.High,
                    EvidenceLink: "/workstation/data/security-master")
            ],
            SecurityMasterAccountingIssues:
            [
                new SecurityMasterAccountingIssueDto(
                    Code: "SM_ACCOUNTING_TERMS_INCOMPLETE",
                    Source: "security-master",
                    Symbol: "BOND1",
                    AccountId: "fund-account",
                    Reason: "Coupon day-count terms are incomplete.",
                    Severity: ReconciliationBreakSeverity.Critical,
                    EvidenceLink: "/workstation/data/security-master")
            ]);
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

    private static global::Meridian.Ledger.Ledger CreateWorkflowBalancedLedger(
        DateTimeOffset startedAt,
        DateTimeOffset portfolioAsOf)
    {
        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, startedAt, "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, portfolioAsOf, "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
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
        ISecurityMasterQueryService,
        Meridian.Application.SecurityMaster.ISecurityMasterQueryService
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
    }

    private sealed class RecordingLedgerJournalStore : ILedgerJournalStore
    {
        public List<LedgerJournalEntryWrite> Appended { get; } = [];

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.Entry.IsBalanced)
            {
                throw new LedgerValidationException("Journal entry must be balanced.");
            }

            Appended.Add(entry);
            return Task.CompletedTask;
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

        public Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
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

        public Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<StatementRunSummaryDto>>([StatementRun]);
        }

        public Task<StatementRunDto?> CreateStatementRunAsync(StatementRunCreateDto request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            CreatedRequests.Add(request);
            return Task.FromResult<StatementRunDto?>(BuildRunDto("created-statement-run", StatementRunStatus.Completed));
        }

        public Task<StatementRunDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(
                string.Equals(runId, StatementRun.RunId, StringComparison.OrdinalIgnoreCase)
                    ? BuildRunDto(StatementRun.RunId, StatementRun.Status)
                    : null);
        }

        public Task<StatementRunValidationDto?> GetStatementRunValidationAsync(string runId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<StatementRunValidationDto?>(
                string.Equals(runId, StatementRun.RunId, StringComparison.OrdinalIgnoreCase)
                    ? new StatementRunValidationDto(runId, [], IsBlocked: false)
                    : null);
        }

        public Task<IReadOnlyList<StatementRunBreakDto>?> ListStatementRunBreaksAsync(string runId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<StatementRunBreakDto>?>(
                string.Equals(runId, StatementRun.RunId, StringComparison.OrdinalIgnoreCase)
                    ? [BuildBreakDto()]
                    : null);
        }

        public Task<StatementRunDto?> ReconcileStatementRunAsync(string runId, StatementRunReconcileRequestDto request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ReconciledRunIds.Add(runId);
            return Task.FromResult<StatementRunDto?>(
                string.Equals(runId, StatementRun.RunId, StringComparison.OrdinalIgnoreCase)
                    ? BuildRunDto(runId, StatementRunStatus.ReviewRequired)
                    : null);
        }

        public Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
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
                BookAmount: 0m,
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

        public Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ReconciliationCaseSummaryDto>>([]);
        }

        public Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
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
