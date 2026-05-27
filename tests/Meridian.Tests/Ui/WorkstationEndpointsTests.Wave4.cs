using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Microsoft.AspNetCore.TestHost;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
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
                string.Equals(code, "OPERATIONS_PREREQUISITE_GATES_NOT_PASSED", StringComparison.OrdinalIgnoreCase));

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
}
