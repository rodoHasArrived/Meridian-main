using System.Text.Json;
using FluentAssertions;

namespace Meridian.Tests.Application;

public sealed partial class OperationsContinuityWorkflowServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadModels_PreserveCommandEvidenceAndRetainedState(bool reviewed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var service = CreateService(out var repository, out var auditStore);
        var workflow = reviewed
            ? await CreateReviewedApprovalReadyWorkflowAsync(service)
            : await CreateLedgerPostedWorkflowAsync(service);
        var retainedState = JsonSerializer.Serialize(await repository.GetAsync(workflow.WorkflowId, timeout.Token));
        var retainedAudit = JsonSerializer.Serialize(await auditStore.GetTimelineAsync(workflow.WorkflowId, timeout.Token));

        var detail = await service.GetAsync(workflow.WorkflowId, timeout.Token);
        var repeatedDetail = await service.GetAsync(workflow.WorkflowId, timeout.Token);
        var checklist = await service.GetChecklistAsync(workflow.WorkflowId, timeout.Token);
        var summary = (await service.ListAsync(workflow.FundAccountId, workflow.PeriodId,
            ct: timeout.Token, ledgerBookId: workflow.LedgerBookId)).Should().ContainSingle().Subject;

        detail.Should().BeEquivalentTo(workflow, options => options.WithStrictOrdering());
        repeatedDetail.Should().BeEquivalentTo(workflow, options => options.WithStrictOrdering());
        checklist.Should().BeEquivalentTo(workflow.CloseChecklist, options => options.WithStrictOrdering());
        summary.Status.Should().Be(workflow.Status);
        summary.Version.Should().Be(workflow.Version);
        summary.Gates.Should().BeEquivalentTo(workflow.Gates, options => options.WithStrictOrdering());
        summary.NextActions.Should().BeEquivalentTo(workflow.NextActions, options => options.WithStrictOrdering());
        JsonSerializer.Serialize(await repository.GetAsync(workflow.WorkflowId, timeout.Token)).Should().Be(retainedState);
        JsonSerializer.Serialize(await auditStore.GetTimelineAsync(workflow.WorkflowId, timeout.Token)).Should().Be(retainedAudit);
    }
}
