using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Application;

public sealed partial class OperationsContinuityWorkflowServiceTests
{
    [Fact]
    public async Task StartPreparedWorkflowAsync_CreatesFreshControlsWithoutSourceEvidence()
    {
        var service = CreateService(out _, out var auditStore);
        var workflowId = Guid.NewGuid();
        var request = PreparedStartRequest() with
        {
            SecurityMasterSnapshotId = Guid.NewGuid(),
            EvidenceLinks = [new("september-reviewed-evidence", "September evidence", null, "source-period", DateTimeOffset.UtcNow)]
        };

        var result = await service.StartPreparedWorkflowAsync(request, workflowId);

        result.Success.Should().BeTrue();
        var workflow = result.Workflow!;
        workflow.WorkflowId.Should().Be(workflowId);
        workflow.PeriodId.Should().Be("2026-10");
        workflow.LedgerBookId.Should().Be(request.LedgerBookId);
        workflow.SecurityMasterSnapshotId.Should().BeNull();
        workflow.ApprovalState.Should().Be(OperationsApprovalStateDto.Pending);
        workflow.Approvals.Should().BeEmpty();
        workflow.EvidenceLinks.Should().BeEmpty();
        workflow.ClosePackage.Should().BeNull();
        workflow.LedgerPreview.Should().BeNull();
        workflow.CloseChecklist.Should().OnlyContain(task => task.AcknowledgedAtUtc == null && task.AcknowledgedBy == null);
        workflow.Gates.Where(gate => gate.GateKey != OperationsGateKeyDto.BrokerIngest)
            .Should().OnlyContain(gate => gate.Status == OperationsGateStatusDto.NotStarted);
        var audit = await auditStore.GetTimelineAsync(workflowId);
        audit.Should().ContainSingle(entry => entry.EventType == "prepared-workflow-started");
        audit[0].References.Should().BeEmpty();
    }

    [Fact]
    public async Task StartPreparedWorkflowAsync_RetryPreservesSubsequentWorkAndOneCreationReceipt()
    {
        var service = CreateService(out _, out var auditStore);
        var workflowId = Guid.NewGuid();
        var request = PreparedStartRequest();
        var started = await service.StartPreparedWorkflowAsync(request, workflowId);
        var imported = await service.ImportBrokerDataAsync(workflowId,
            new OperationsTransitionRequestDto(started.Workflow!.Version, "operator", "October activity received"));
        imported.Success.Should().BeTrue();

        var retried = await service.StartPreparedWorkflowAsync(request, workflowId);

        retried.Success.Should().BeTrue();
        retried.Workflow!.Version.Should().Be(imported.Workflow!.Version);
        retried.Workflow.BrokerIntakeState.Should().Be(imported.Workflow.BrokerIntakeState);
        (await service.ListAsync(request.FundAccountId, request.PeriodId)).Should().ContainSingle();
        (await auditStore.GetTimelineAsync(workflowId)).Should().ContainSingle(entry => entry.EventType == "prepared-workflow-started");
    }

    [Fact]
    public async Task StartPreparedWorkflowAsync_RejectsExistingScopeAndChangedScopeForRetainedIdentity()
    {
        var service = CreateService(out _, out _);
        var request = PreparedStartRequest();
        var workflowId = Guid.NewGuid();
        (await service.StartPreparedWorkflowAsync(request, workflowId)).Success.Should().BeTrue();

        var duplicateScope = await service.StartPreparedWorkflowAsync(request, Guid.NewGuid());
        var changedScope = await service.StartPreparedWorkflowAsync(request with { PeriodId = "2026-11" }, workflowId);

        duplicateScope.Success.Should().BeFalse();
        duplicateScope.ErrorCode.Should().Be("WORKFLOW_ALREADY_EXISTS");
        changedScope.Success.Should().BeFalse();
        changedScope.ErrorCode.Should().Be("PREPARED_WORKFLOW_ID_CONFLICT");
        (await service.ListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task StartPreparedWorkflowAsync_DoesNotAdoptAnOrdinaryExistingWorkflow()
    {
        var service = CreateService(out _, out _);
        var request = PreparedStartRequest();
        var ordinary = await service.StartWorkflowAsync(request);

        var result = await service.StartPreparedWorkflowAsync(request, ordinary.Workflow!.WorkflowId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("PREPARED_WORKFLOW_ID_CONFLICT");
    }

    [Fact]
    public async Task StartPreparedWorkflowAsync_RefusesMissingAtomicStoreWithoutWriting()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var service = new OperationsContinuityWorkflowService(repository,
            new ThrowingAuditStore("prepared-workflow-started"), derivation);

        var result = await service.StartPreparedWorkflowAsync(PreparedStartRequest(), Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ATOMIC_WORKFLOW_START_UNAVAILABLE");
        (await repository.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task StartPreparedWorkflowAsync_RequiresRetainedIdentityAndExplicitBook()
    {
        var service = CreateService(out var repository, out _);

        var result = await service.StartPreparedWorkflowAsync(PreparedStartRequest() with { LedgerBookId = null }, Guid.Empty);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().Contain(blocker => blocker.Code == "WORKFLOW_ID_REQUIRED");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_BOOK_REQUIRED");
        (await repository.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task StartPreparedWorkflowAsync_RecoversAcrossFileStoreRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meridian-prepared-start-{Guid.NewGuid():N}");
        try
        {
            var derivation = new OperationsStatusDerivationService();
            OperationsContinuityWorkflowService OpenService() => new(
                new FileOperationsContinuityRepository(directory, derivation, NullLogger<FileOperationsContinuityRepository>.Instance),
                new FileOperationsWorkflowAuditStore(directory, NullLogger<FileOperationsWorkflowAuditStore>.Instance),
                derivation);
            var request = PreparedStartRequest();
            var workflowId = Guid.NewGuid();
            var started = await OpenService().StartPreparedWorkflowAsync(request, workflowId);
            started.Success.Should().BeTrue();

            var restarted = OpenService();
            var recovered = await restarted.StartPreparedWorkflowAsync(request, workflowId);

            recovered.Success.Should().BeTrue();
            recovered.Workflow!.CreatedAtUtc.Should().Be(started.Workflow!.CreatedAtUtc);
            recovered.Workflow.Version.Should().Be(started.Workflow.Version);
            (await restarted.ListAsync()).Should().ContainSingle();
            (await restarted.GetTimelineAsync(workflowId)).Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static OperationsStartWorkflowRequestDto PreparedStartRequest() =>
        new(Guid.NewGuid(), "2026-10", null, "custodian", "close-operator", LedgerBookId: Guid.NewGuid());
}
