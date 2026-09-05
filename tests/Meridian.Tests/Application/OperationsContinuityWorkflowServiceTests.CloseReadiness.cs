using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Application;

public sealed partial class OperationsContinuityWorkflowServiceTests
{
    internal static OperationsContinuityWorkflowService CreateGuardedService(
        IClosePublicationReadinessGuard guard, out InMemoryOperationsContinuityRepository repository,
        out InMemoryOperationsWorkflowAuditStore auditStore)
        => CreateService(out repository, out auditStore, closeReadinessGuard: guard);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CloseWorkflowAsync_DirectCallerCannotOmitScopeOrAuthority(bool missingScope)
    {
        var service = CreateService(out var repository, out _, registerCloseReadinessGuard: false);
        var submitted = await CreateApprovalSubmittedWorkflowAsync(service);
        var approved = await service.ApproveWorkflowAsync(submitted.WorkflowId, new(
            submitted.Version, "ops-user", "reviewer", "Approve prerequisite evidence", "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var request = new OperationsCloseWorkflowRequestDto(approved.Workflow!.Version, "ops-user", "Close", "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals(),
            CloseScope: missingScope ? null : await StateMachineCloseScopeAsync(service, submitted.WorkflowId));
        var result = await service.CloseWorkflowAsync(submitted.WorkflowId, request);
        result.Success.Should().BeFalse();
        result.Blockers.Should().Contain(blocker => blocker.Code == (missingScope ? "CLOSE_SCOPE_REQUIRED" : "CLOSE_READINESS_UNAVAILABLE"));
        (await repository.GetAsync(submitted.WorkflowId))!.IsClosed.Should().BeFalse();
    }

    private static async Task<CloseReadinessScopeDto> StateMachineCloseScopeAsync(
        OperationsContinuityWorkflowService service, Guid workflowId)
    {
        var workflow = (await service.GetAsync(workflowId))!;
        return new("fund-alpha", workflow.LedgerBookId ?? Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            workflow.FundAccountId, "entity-alpha", workflow.PeriodId);
    }

    // Legacy unit scenarios isolate aggregate transitions (hashes, approvals, reopen, and audit
    // persistence). Shared evidence authorization has separate integration tests using the real
    // ClosePublicationReadinessGuard and FinancialOperationsCommandCenterReadService.
    private sealed class StateMachineCloseReadinessFixture : IClosePublicationReadinessGuard
    {
        public Task<IReadOnlyList<OperationsWorkflowBlockerDto>> ValidateAsync(Guid workflowId, long expectedVersion,
            CloseReadinessScopeDto? scope, string? tenantId = null, string? companyId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OperationsWorkflowBlockerDto>>(scope is null || workflowId == Guid.Empty || expectedVersion <= 0
                ? [new("CLOSE_SCOPE_REQUIRED", "The state-machine fixture requires explicit close identity.", null, "Critical", [])] : []);
    }
}
