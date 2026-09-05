using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.OperationsContinuity;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.AccountingClose;

public sealed partial class AccountingCloseServicesTests
{
    [Theory]
    [InlineData("Missing")]
    [InlineData("Stale")]
    [InlineData("ScopeMismatch")]
    [InlineData("AuthorityUnavailable")]
    public async Task SharedCloseEvidence_BlocksBeforeLedgerMutation_AndRepairAllowsSameScope(string failure)
    {
        var workflow = BuildCloseWorkflow(Guid.NewGuid(), firstTaskStatus: "Done", secondTaskStatus: "Done");
        var bookId = workflow.LedgerBookId!.Value;
        var scope = new CloseReadinessScopeDto("fund-alpha", bookId, workflow.FundAccountId, "entity-alpha", workflow.PeriodId);
        var workflows = Substitute.For<IOperationsContinuityWorkflowService>();
        workflows.GetAsync(workflow.WorkflowId, Arg.Any<CancellationToken>()).Returns(workflow);
        var lockedWorkflow = BuildLockedCloseWorkflow(workflow, workflow.Version + 1);
        workflows.CloseWorkflowAsync(workflow.WorkflowId, Arg.Any<OperationsCloseWorkflowRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new OperationsTransitionResultDto(true, null, null, lockedWorkflow, [], [], lockedWorkflow.Version));
        var posting = CreateMutationGatedPostingWorkbench();
        var readyGate = new ClosePostingGateDto("closing-entries", "Closing entries", ClosePostingGateStateDto.Posted,
            true, 0m, 0, "Retained closing entries clear temporary balances.");
        posting.EvaluateAsync(Arg.Any<AccountingClosePostingContext>(), Arg.Any<CancellationToken>()).Returns(readyGate);
        posting.EnsureClosingDraftQueuedAsync(Arg.Any<AccountingClosePostingContext>(), Arg.Any<AccountingClosePostingCommand>(), Arg.Any<CancellationToken>())
            .Returns(readyGate);
        posting.FinalizeHardCloseAsync(Arg.Any<AccountingClosePostingContext>(), Arg.Any<AccountingClosePostingCommand>(), Arg.Any<CancellationToken>())
            .Returns(new LedgerPeriodDto(Guid.NewGuid(), bookId, 2026, 3, workflow.PeriodId,
                new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), LedgerPeriodStatusDto.HardClosed,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 2));
        IReadOnlyList<OperationsWorkflowBlockerDto> blockers =
            [new($"CLOSE_EVIDENCE_{failure}", $"Retained close evidence is {failure}.", null, "Critical", [])];
        var guard = Substitute.For<IClosePublicationReadinessGuard>();
        guard.ValidateAsync(workflow.WorkflowId, workflow.Version, scope, "tenant-alpha", "company-alpha", Arg.Any<CancellationToken>())
            .Returns(_ => blockers);
        var service = new AccountingCloseManagementService(workflows, posting,
            failure == "AuthorityUnavailable" ? null : guard);
        await ApproveRequiredCloseTasksAsync(service, workflow.WorkflowId, bookId);
        var request = new LockClosePeriodRequestDto(workflow.WorkflowId, workflow.Version, "controller-reviewer",
            "Close after retained evidence review.", "report-pack-2026-03",
            [$"evidence:close-package:{workflow.WorkflowId:D}:2026-03:book:{bookId:D}:period-lock"],
            ControllerRole: "Controller", CloseScope: scope);

        var refused = await LockClosePeriodScopedAsync(service, request, "controller-reviewer");
        refused!.IsLocked.Should().BeFalse();
        refused.Issues.Should().Contain(issue => issue.Code == (failure == "AuthorityUnavailable"
            ? "CLOSE_READINESS_REQUIRED" : $"CLOSE_EVIDENCE_{failure}"));
        await posting.DidNotReceive().FinalizeHardCloseAsync(Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(), Arg.Any<CancellationToken>());
        await workflows.DidNotReceive().CloseWorkflowAsync(workflow.WorkflowId, Arg.Any<OperationsCloseWorkflowRequestDto>(), Arg.Any<CancellationToken>());

        blockers = [];
        if (failure == "AuthorityUnavailable")
        {
            service = new AccountingCloseManagementService(workflows, posting, guard);
            await ApproveRequiredCloseTasksAsync(service, workflow.WorkflowId, bookId);
        }
        var repaired = await LockClosePeriodScopedAsync(service, request, "controller-reviewer");
        repaired!.IsLocked.Should().BeTrue();
        await guard.Received().ValidateAsync(workflow.WorkflowId, workflow.Version, scope,
            "tenant-alpha", "company-alpha", Arg.Any<CancellationToken>());
        await workflows.Received(1).CloseWorkflowAsync(workflow.WorkflowId,
            Arg.Is<OperationsCloseWorkflowRequestDto>(close => close.CloseScope == scope && close.ExpectedVersion == workflow.Version),
            Arg.Any<CancellationToken>());
        await posting.Received(2).FinalizeHardCloseAsync(Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(), Arg.Any<CancellationToken>());
    }

    // These existing scenarios isolate the ledger/workflow retry protocol. The authority's
    // evidence semantics are exercised by shared read-service and direct publication integration tests.
    private static IClosePublicationReadinessGuard CreateApprovedCloseReadinessGuard()
    {
        var guard = Substitute.For<IClosePublicationReadinessGuard>();
        guard.ValidateAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CloseReadinessScopeDto?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OperationsWorkflowBlockerDto>());
        return guard;
    }
}
