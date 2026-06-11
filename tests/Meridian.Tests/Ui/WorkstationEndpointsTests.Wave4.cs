using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.Workstation;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task OperationsContinuityEndpoints_ApprovalPolicyMatrix_ExposesServerOwnedRules()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(UiApiRoutes.OperationsContinuityApprovalPolicyMatrix);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var matrix = await response.Content.ReadFromJsonAsync<OperationsApprovalPolicyMatrixDto>(ServerJsonOptions);
        matrix.Should().NotBeNull();
        matrix!.Rows.Should().Contain(row =>
            row.PolicyKey == "operations-continuity.approve" &&
            row.Gate == OperationsGateKeyDto.Approval &&
            row.RequiresIndependentReviewer &&
            row.RequiredDistinctApprovals == 2 &&
            row.RequiresReportPack &&
            row.RequiresChecklistControlApprovals &&
            row.AuditEventType == "approval-approved");
        matrix.Rows.Should().Contain(row =>
            row.PolicyKey == "operations-continuity.reopen" &&
            row.RequiredPermission == nameof(UserPermission.AdminMaintenance));
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ApprovalPolicyRuleUpsert_UpdatesMatrixWithAuditEvidence()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityApprovalPolicyRules,
            new OperationsApprovalPolicyRuleUpsertRequestDto(
                PolicyKey: "operations-continuity.approve",
                WorkflowArea: "Operations close",
                Action: "Approve submitted workflow",
                Gate: OperationsGateKeyDto.Approval,
                Trigger: "Workflow is submitted and assigned reviewer evidence is present.",
                RequiredPermission: nameof(UserPermission.AdminMaintenance),
                SubmitterRole: "Accounting operator",
                ReviewerRole: "Controller",
                RequiredDistinctApprovals: 3,
                RequiresIndependentReviewer: true,
                RequiresReportPack: true,
                RequiresChecklistControlApprovals: true,
                EvidenceRequirement: "Controller packet plus three distinct approval-gate control approvals.",
                AuditEventType: "approval-approved",
                Route: UiApiRoutes.OperationsContinuityApprovalApprove,
                Severity: "Critical",
                RequestedBy: "untrusted-browser-user",
                Rationale: "Tighten close approval governance for controller review.",
                CorrelationId: "approval-policy-test"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OperationsApprovalPolicyRuleUpsertResultDto>(ServerJsonOptions);
        result.Should().NotBeNull();
        result!.Rule.PolicyKey.Should().Be("operations-continuity.approve");
        result.Rule.RequiredDistinctApprovals.Should().Be(3);
        result.AuditEvent.Actor.Should().Be("ops-user");
        result.AuditEvent.CorrelationId.Should().Be("approval-policy-test");
        result.Matrix.Rows.Should().Contain(row =>
            row.PolicyKey == "operations-continuity.approve" &&
            row.RequiredDistinctApprovals == 3 &&
            row.ReviewerRole == "Controller");
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ApprovalPolicyRuleUpsert_WithoutAdminMaintenance_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityApprovalPolicyRules,
            new OperationsApprovalPolicyRuleUpsertRequestDto(
                PolicyKey: "operations-continuity.approve",
                WorkflowArea: "Operations close",
                Action: "Approve submitted workflow",
                Gate: OperationsGateKeyDto.Approval,
                Trigger: "Workflow is submitted.",
                RequiredPermission: nameof(UserPermission.AdminMaintenance),
                SubmitterRole: "Accounting operator",
                ReviewerRole: "Controller",
                RequiredDistinctApprovals: 2,
                RequiresIndependentReviewer: true,
                RequiresReportPack: true,
                RequiresChecklistControlApprovals: true,
                EvidenceRequirement: "Controller packet.",
                AuditEventType: "approval-approved",
                Route: UiApiRoutes.OperationsContinuityApprovalApprove,
                Severity: "Critical",
                RequestedBy: "ops-user",
                Rationale: "Should be rejected."));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ApprovalPolicyRuleUpsert_InvalidApprovalCount_ReturnsBadRequest()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityApprovalPolicyRules,
            new OperationsApprovalPolicyRuleUpsertRequestDto(
                PolicyKey: "operations-continuity.approve",
                WorkflowArea: "Operations close",
                Action: "Approve submitted workflow",
                Gate: OperationsGateKeyDto.Approval,
                Trigger: "Workflow is submitted.",
                RequiredPermission: nameof(UserPermission.AdminMaintenance),
                SubmitterRole: "Accounting operator",
                ReviewerRole: "Controller",
                RequiredDistinctApprovals: 0,
                RequiresIndependentReviewer: true,
                RequiresReportPack: true,
                RequiresChecklistControlApprovals: true,
                EvidenceRequirement: "Controller packet.",
                AuditEventType: "approval-approved",
                Route: UiApiRoutes.OperationsContinuityApprovalApprove,
                Severity: "Critical",
                RequestedBy: "ops-user",
                Rationale: "Invalid count."));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_CloseCalendar_ExposesWorkflowDuePosture()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                fundAccountId,
                "2026-07",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor"));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);

        using var calendarResponse = await client.GetAsync($"{UiApiRoutes.OperationsContinuityCloseCalendar}?fundAccountId={fundAccountId}&periodId=2026-07");

        calendarResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var calendar = await calendarResponse.Content.ReadFromJsonAsync<OperationsCloseCalendarDto>(ServerJsonOptions);
        calendar.Should().NotBeNull();
        var calendarItem = calendar!.Items.Should().ContainSingle(item =>
            item.WorkflowId == start!.Workflow!.WorkflowId &&
            item.FundAccountId == fundAccountId &&
            item.PeriodId == "2026-07" &&
            item.Status == OperationsWorkflowStatusDto.CollectingBrokerData &&
            item.NextDueTaskId == "close-gate-brokeringest" &&
            item.OpenChecklistCount > 0 &&
            item.RequiredApprovalCount > 0 &&
            item.Route.Contains(start.Workflow.WorkflowId.ToString(), StringComparison.OrdinalIgnoreCase)).Subject;
        calendarItem.ReadinessScore.Should().BeLessThan(100);
        calendarItem.ReadinessComponents.Should().NotBeNull();
        calendarItem.ReadinessComponents!.Select(static component => component.Key)
            .Should()
            .BeEquivalentTo(
            [
                "security-master",
                "provider-freshness",
                "positions",
                "cash",
                "ledger",
                "pricing",
                "reconciliation",
                "reports",
                "approvals"
            ]);
        calendarItem.ReadinessBlockers.Should().NotBeNullOrEmpty();
        calendarItem.ReadinessBlockers!.Select(static blocker => blocker.Code)
            .Should()
            .Contain(
            [
                "SECURITY_MASTER_RESOLUTION_REQUIRED",
                "BROKER_SYNC_STALE",
                "BROKER_CASH_COVERAGE_INCOMPLETE",
                "LEDGER_POSTING_REQUIRED",
                "RECONCILIATION_CRITICAL_BREAKS_OPEN",
                "REPORT_PACK_REQUIRED",
                "APPROVAL_REQUIRED"
            ]);
        calendarItem.ReadinessNextActions.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_PrivateCapitalCloseCockpit_ReturnsSharedCloseAggregate()
    {
        var captured = new List<(string? FundProfileId, Guid? LedgerBookId, Guid? FundAccountId, string? PeriodId, string? EntityId)>();
        var ledgerBookId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var fundAccountId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IPrivateCapitalCloseCockpitService>(
                new StubPrivateCapitalCloseCockpitService(
                    BuildPrivateCapitalCloseCockpit(ledgerBookId, fundAccountId),
                    captured));
        });
        var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"{UiApiRoutes.OperationsPrivateCapitalCloseCockpit}?fundProfileId=fund-alpha&ledgerBookId={ledgerBookId:D}&fundAccountId={fundAccountId:D}&periodId=2026-06&entityId=entity-master");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cockpit = await response.Content.ReadFromJsonAsync<PrivateCapitalCloseCockpitDto>(ServerJsonOptions);
        cockpit.Should().NotBeNull();
        cockpit!.OverallStatus.Should().Be(EvidenceStatusDto.Ready);
        cockpit.Lanes.Should().ContainSingle(lane => lane.LaneId == "period-lock" && lane.IsReady);
        captured.Should().ContainSingle(call =>
            call.FundProfileId == "fund-alpha" &&
            call.LedgerBookId == ledgerBookId &&
            call.FundAccountId == fundAccountId &&
            call.PeriodId == "2026-06" &&
            call.EntityId == "entity-master");
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_PrivateCapitalCloseCockpit_WithoutReadPermission_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IPrivateCapitalCloseCockpitService>(
                new StubPrivateCapitalCloseCockpitService(BuildPrivateCapitalCloseCockpit(Guid.NewGuid(), Guid.NewGuid())));
        }, currentUserPermissions: UserPermission.ViewStrategies);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(UiApiRoutes.OperationsPrivateCapitalCloseCockpit);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_CloseCalendarItemUpsert_UpdatesDueOwnerWithAuditEvidence()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                fundAccountId,
                "2026-08",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor"));
        var startBody = await startResponse.Content.ReadAsStringAsync();
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK, startBody);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        var workflowId = start!.Workflow!.WorkflowId;

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityCloseCalendarItems,
            new OperationsCloseCalendarItemUpsertRequestDto(
                WorkflowId: workflowId,
                TaskId: "close-gate-brokeringest",
                DueDate: new DateOnly(2026, 8, 3),
                Owner: "Controller",
                RequestedBy: "untrusted-browser-user",
                Rationale: "Move broker intake close task to controller queue.",
                CorrelationId: "close-calendar-test"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OperationsCloseCalendarItemUpsertResultDto>(ServerJsonOptions);
        result.Should().NotBeNull();
        result!.Item.WorkflowId.Should().Be(workflowId);
        result.Item.NextDueTaskId.Should().Be("close-gate-brokeringest");
        result.Item.NextDueDate.Should().Be(new DateOnly(2026, 8, 3));
        result.Item.NextDueOwner.Should().Be("Controller");
        result.AuditEvent.Actor.Should().Be("ops-user");
        result.AuditEvent.CorrelationId.Should().Be("close-calendar-test");
        result.Calendar.Items.Should().Contain(item =>
            item.WorkflowId == workflowId &&
            item.NextDueDate == new DateOnly(2026, 8, 3) &&
            item.NextDueOwner == "Controller");
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_CloseCalendarItemUpsert_WithoutAdminMaintenance_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityCloseCalendarItems,
            new OperationsCloseCalendarItemUpsertRequestDto(
                WorkflowId: Guid.NewGuid(),
                TaskId: "close-gate-brokeringest",
                DueDate: new DateOnly(2026, 8, 3),
                Owner: "Controller",
                RequestedBy: "ops-user",
                Rationale: "Should be rejected."));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_CloseCalendarItemUpsert_InvalidTask_ReturnsBadRequest()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                fundAccountId,
                "2026-09",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor"));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityCloseCalendarItems,
            new OperationsCloseCalendarItemUpsertRequestDto(
                WorkflowId: start!.Workflow!.WorkflowId,
                TaskId: "missing-task",
                DueDate: new DateOnly(2026, 9, 3),
                Owner: "Controller",
                RequestedBy: "ops-user",
                Rationale: "Invalid task."));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_CloseReadiness_ExposesControllerScore()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                FundAccountId: Guid.NewGuid(),
                PeriodId: "2026-10",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor"));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        var workflowId = start!.Workflow!.WorkflowId;

        using var readinessResponse = await client.GetAsync(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityCloseReadiness, "workflowId", workflowId.ToString("D")));

        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await readinessResponse.Content.ReadFromJsonAsync<OperationsCloseReadinessDto>(ServerJsonOptions);
        readiness.Should().NotBeNull();
        readiness!.IsReadyToClose.Should().BeFalse();
        readiness.Score.Should().BeLessThan(100);
        readiness.Components.Select(static component => component.Key)
            .Should()
            .BeEquivalentTo(
            [
                "security-master",
                "provider-freshness",
                "positions",
                "cash",
                "ledger",
                "pricing",
                "reconciliation",
                "reports",
                "approvals"
            ]);
        readiness.Blockers.Select(static blocker => blocker.Code)
            .Should()
            .Contain(
            [
                "SECURITY_MASTER_RESOLUTION_REQUIRED",
                "BROKER_SYNC_STALE",
                "LEDGER_POSTING_REQUIRED",
                "RECONCILIATION_CRITICAL_BREAKS_OPEN",
                "REPORT_PACK_REQUIRED",
                "APPROVAL_REQUIRED"
            ]);
        readiness.NextActions.Should().OnlyContain(action => !string.IsNullOrWhiteSpace(action.Route));
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ListDetailTimeline_ExposeSharedLifecycleEvidence()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            RegisterOperationsContinuityServices(services);
        });
        var client = app.GetTestClient();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                FundAccountId: Guid.NewGuid(),
                PeriodId: "2026-05",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor",
                EvidenceLinks:
                [
                    new OperationsEvidenceLinkDto(
                        EvidenceId: "ops-start-evidence",
                        Label: "Start packet",
                        Route: "/workstation/evidence/subjects/reconciliation-review/fund-1",
                        Source: "integration-test",
                        CapturedAtUtc: DateTimeOffset.UtcNow)
                ]));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        start.Should().NotBeNull();
        start!.Success.Should().BeTrue();
        var workflowId = start.Workflow!.WorkflowId;

        using var listResponse = await client.GetAsync(UiApiRoutes.OperationsContinuity);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await listResponse.Content.ReadFromJsonAsync<List<OperationsContinuityWorkflowSummaryDto>>(ServerJsonOptions);
        listed.Should().Contain(item => item.WorkflowId == workflowId);

        using var detailResponse = await client.GetAsync(UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityById, "workflowId", workflowId.ToString()));
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<OperationsContinuityWorkflowDto>(ServerJsonOptions);
        detail.Should().NotBeNull();
        detail!.EvidenceLinks.Should().Contain(link => link.EvidenceId == "ops-start-evidence");

        using var timelineResponse = await client.GetAsync(UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityTimeline, "workflowId", workflowId.ToString()));
        timelineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var timeline = await timelineResponse.Content.ReadFromJsonAsync<List<OperationsTimelineEntryDto>>(ServerJsonOptions);
        timeline.Should().NotBeNullOrEmpty();
        timeline!.SelectMany(static entry => entry.References).Should().Contain(reference => reference.EvidenceId == "ops-start-evidence");
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ApprovalAndClose_ExposeSharedCloseBlockers()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            RegisterOperationsContinuityServices(services);
        });
        var client = app.GetTestClient();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                Guid.NewGuid(),
                "2026-06",
                null,
                "custodian",
                "local-actor",
                EvidenceLinks: []));
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        var workflowId = start!.Workflow!.WorkflowId;

        using var submitResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityApprovalSubmit, "workflowId", workflowId.ToString()),
            new OperationsSubmitApprovalRequestDto(
                ExpectedVersion: start.Workflow.Version,
                Actor: "ops-user",
                Reviewer: "controller",
                Rationale: "Submitting from endpoint test",
                ReportPackId: "rp-test"));
        submitResponse.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        var submit = await submitResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        submit.Should().NotBeNull();
        submit!.Blockers.Should().NotBeEmpty();

        using var closeResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityClose, "workflowId", workflowId.ToString()),
            new OperationsCloseWorkflowRequestDto(
                ExpectedVersion: start.Workflow.Version,
                Actor: "ops-user",
                Rationale: "Attempt close for blocker validation",
                ReportPackId: "rp-test"));
        closeResponse.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        var close = await closeResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        close.Should().NotBeNull();
        close!.Blockers.Select(static blocker => blocker.Code)
            .Should()
            .Contain(code =>
                string.Equals(code, "OPERATIONS_GATES_NOT_PASSED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "OPERATIONS_PREREQUISITE_GATES_NOT_PASSED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "APPROVAL_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "LEDGER_POSTING_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "REPORT_PACK_REQUIRED", StringComparison.OrdinalIgnoreCase));

        using var timelineResponse = await client.GetAsync(UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityTimeline, "workflowId", workflowId.ToString()));
        timelineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var timelineJson = await timelineResponse.Content.ReadAsStringAsync();
        using var timelineDoc = JsonDocument.Parse(timelineJson);
        timelineDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ChecklistAndAcknowledgement_ShouldRequireEvidenceBackedCompletion()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            RegisterOperationsContinuityServices(services);
        });
        var client = app.GetTestClient();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(Guid.NewGuid(), "2026-07", null, "custodian", "local-actor", EvidenceLinks: []));
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        var workflowId = start!.Workflow!.WorkflowId;

        using var checklistResponse = await client.GetAsync(UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityChecklist, "workflowId", workflowId.ToString()));
        checklistResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var checklist = await checklistResponse.Content.ReadFromJsonAsync<List<OperationsCloseChecklistTaskDto>>(ServerJsonOptions);
        checklist.Should().NotBeNullOrEmpty();

        var firstTask = checklist![0];
        using var ackResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityChecklistAcknowledge, "workflowId", workflowId.ToString()), "taskId", firstTask.TaskId),
            new OperationsChecklistAcknowledgeRequestDto(start.Workflow.Version, "ops-user", "ack"));
        ackResponse.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
    }

    private static PrivateCapitalCloseCockpitDto BuildPrivateCapitalCloseCockpit(Guid ledgerBookId, Guid fundAccountId)
        => new(
            FundProfileId: "fund-alpha",
            LedgerBookId: ledgerBookId,
            FundAccountId: fundAccountId,
            PeriodId: "2026-06",
            EntityId: "entity-master",
            ProjectedAtUtc: new DateTimeOffset(2026, 6, 30, 18, 0, 0, TimeSpan.Zero),
            CockpitRoute: UiApiRoutes.OperationsPrivateCapitalCloseCockpit,
            OverallStatus: EvidenceStatusDto.Ready,
            IsReadyToClose: true,
            ReadinessScore: 100,
            WorkflowCount: 1,
            FundEventCount: 1,
            CapitalAccountCount: 1,
            ReportOutputCount: 1,
            DeliveredReportOutputCount: 1,
            ReadyLaneCount: 1,
            BlockedLaneCount: 0,
            Lanes:
            [
                new PrivateCapitalCloseCockpitLaneDto(
                    "period-lock",
                    "Period lock",
                    EvidenceStatusDto.Ready,
                    true,
                    "Period lock evidence retained.",
                    "/api/workstation/operations/continuity/22222222-2222-2222-2222-222222222222",
                    0,
                    [])
            ],
            Workflows:
            [
                new PrivateCapitalCloseCockpitWorkflowDto(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    fundAccountId,
                    "2026-06",
                    OperationsWorkflowStatusDto.Closed,
                    100,
                    true,
                    "/api/workstation/operations/continuity/22222222-2222-2222-2222-222222222222",
                    "close-package-fund-alpha-2026-06",
                    "/operations-continuity/close-package/manifest-fund-alpha-2026-06",
                    0,
                    0,
                    new DateTimeOffset(2026, 6, 30, 18, 0, 0, TimeSpan.Zero))
            ],
            Blockers: [],
            NextActions: [],
            LiveCapabilities: ["Fund/book/period close lane projection"],
            PlannedCapabilities: ["Live payment release"]);

    private sealed class StubPrivateCapitalCloseCockpitService : IPrivateCapitalCloseCockpitService
    {
        private readonly PrivateCapitalCloseCockpitDto _cockpit;
        private readonly List<(string? FundProfileId, Guid? LedgerBookId, Guid? FundAccountId, string? PeriodId, string? EntityId)>? _captured;

        public StubPrivateCapitalCloseCockpitService(
            PrivateCapitalCloseCockpitDto cockpit,
            List<(string? FundProfileId, Guid? LedgerBookId, Guid? FundAccountId, string? PeriodId, string? EntityId)>? captured = null)
        {
            _cockpit = cockpit;
            _captured = captured;
        }

        public Task<PrivateCapitalCloseCockpitDto> GetCockpitAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            Guid? fundAccountId = null,
            string? periodId = null,
            string? entityId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _captured?.Add((fundProfileId, ledgerBookId, fundAccountId, periodId, entityId));
            return Task.FromResult(_cockpit);
        }
    }
}
