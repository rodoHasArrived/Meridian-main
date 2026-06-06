using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Workflow.Workflows;

namespace Meridian.Tests.Workflow;

public sealed class FundWorkflowCommandHandlerTests
{
    [Fact]
    public void Handle_ProgressesWorkflowThroughClose()
    {
        var handler = new FundWorkflowCommandHandler();
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator");

        handler.Handle(new StartWorkflow(workflowId, metadata));
        handler.Handle(new ImportBrokerData(workflowId, metadata));
        handler.Handle(new NormalizeBrokerTransactions(workflowId, metadata));
        handler.Handle(new ResolveSecurityMasterMappings(workflowId, metadata));
        handler.Handle(new ApproveSecurityMasterOverrides(workflowId, metadata));
        handler.Handle(new BuildLedgerDraft(workflowId, metadata));
        handler.Handle(new ValidateLedgerDraft(workflowId, metadata));
        handler.Handle(new PostLedgerEntries(workflowId, metadata));
        handler.Handle(new RunReconciliation(workflowId, metadata));
        handler.Handle(new ResolveBreakCase(workflowId, metadata));
        handler.Handle(new SubmitForApproval(workflowId, metadata));
        handler.Handle(new ApproveWorkflow(workflowId, metadata));

        var closed = handler.Handle(new CloseWorkflow(workflowId, metadata));

        closed.OverallStatus.Should().Be(FundWorkflowOverallStatus.Closed);
        closed.StageStatus.Values.Should().OnlyContain(x => x == FundWorkflowSubStatus.Completed);
        closed.LastActor.Should().Be("operator");
    }

    [Fact]
    public void Handle_PostLedgerEntriesRequiresPostingGate()
    {
        var handler = new FundWorkflowCommandHandler();
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator");

        handler.Handle(new StartWorkflow(workflowId, metadata));
        handler.Handle(new NormalizeBrokerTransactions(workflowId, metadata));
        handler.Handle(new ApproveSecurityMasterOverrides(workflowId, metadata));

        var act = () => handler.Handle(new PostLedgerEntries(workflowId, metadata));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Posting gate must be opened by ValidateLedgerDraft.");
    }

    [Fact]
    public void Handle_RoutesLedgerRejectionBackToLedger()
    {
        var handler = new FundWorkflowCommandHandler();
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator");

        MoveToApproval(handler, workflowId, metadata);

        var rejected = handler.Handle(new RejectWorkflow(workflowId, FundWorkflowRejectionReasonCode.LedgerMismatch, metadata));

        rejected.OverallStatus.Should().Be(FundWorkflowOverallStatus.Rejected);
        rejected.StageStatus[FundWorkflowStage.Approval].Should().Be(FundWorkflowSubStatus.Blocked);
        rejected.StageStatus[FundWorkflowStage.Ledger].Should().Be(FundWorkflowSubStatus.InProgress);
        rejected.StageStatus[FundWorkflowStage.Reconciliation].Should().Be(FundWorkflowSubStatus.NotStarted);
    }

    [Fact]
    public void Handle_ReopenRequiresElevatedRoleAndIncidentTicket()
    {
        var handler = new FundWorkflowCommandHandler();
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator");
        MoveToApproval(handler, workflowId, metadata);
        handler.Handle(new RejectWorkflow(workflowId, FundWorkflowRejectionReasonCode.DataQuality, metadata));

        var missingTicket = () => handler.Handle(new ReopenWorkflow(workflowId, "retry", Metadata("operator", role: "Administrator")));
        var basicUser = () => handler.Handle(new ReopenWorkflow(workflowId, "retry", Metadata("operator", role: "Analyst", incidentTicketId: "INC-1")));

        missingTicket.Should().Throw<InvalidOperationException>()
            .WithMessage("Incident/ticket ID is required for reopen.");
        basicUser.Should().Throw<InvalidOperationException>()
            .WithMessage("Reopen requires elevated role (Administrator or OperationsManager).");
    }

    [Fact]
    public void Handle_ReopenRejectedWorkflowReturnsToReconciliation()
    {
        var handler = new FundWorkflowCommandHandler();
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator");
        MoveToApproval(handler, workflowId, metadata);
        handler.Handle(new RejectWorkflow(workflowId, FundWorkflowRejectionReasonCode.DataQuality, metadata));

        var reopened = handler.Handle(new ReopenWorkflow(workflowId, "retry", Metadata("ops-lead", role: "OperationsManager", incidentTicketId: "INC-1")));

        reopened.OverallStatus.Should().Be(FundWorkflowOverallStatus.InProgress);
        reopened.StageStatus[FundWorkflowStage.Reconciliation].Should().Be(FundWorkflowSubStatus.InProgress);
        reopened.StageStatus[FundWorkflowStage.Approval].Should().Be(FundWorkflowSubStatus.NotStarted);
        reopened.ApprovalGateOpen.Should().BeFalse();
        reopened.LastActor.Should().Be("ops-lead");
    }

    [Fact]
    public void Handle_DuplicateRequestIdDoesNotApplyMutationTwice()
    {
        var handler = new FundWorkflowCommandHandler();
        var workflowId = Guid.NewGuid();
        var first = Metadata("first", requestId: "request-1");
        var duplicate = Metadata("duplicate", requestId: "request-1");

        var started = handler.Handle(new StartWorkflow(workflowId, first));
        var repeated = handler.Handle(new NormalizeBrokerTransactions(workflowId, duplicate));

        repeated.StageStatus.Should().Equal(started.StageStatus);
        repeated.LastActor.Should().Be("first");
    }

    private static void MoveToApproval(FundWorkflowCommandHandler handler, Guid workflowId, FundWorkflowCommandMetadata metadata)
    {
        handler.Handle(new StartWorkflow(workflowId, metadata));
        handler.Handle(new ImportBrokerData(workflowId, metadata));
        handler.Handle(new NormalizeBrokerTransactions(workflowId, metadata));
        handler.Handle(new ResolveSecurityMasterMappings(workflowId, metadata));
        handler.Handle(new ApproveSecurityMasterOverrides(workflowId, metadata));
        handler.Handle(new BuildLedgerDraft(workflowId, metadata));
        handler.Handle(new ValidateLedgerDraft(workflowId, metadata));
        handler.Handle(new PostLedgerEntries(workflowId, metadata));
        handler.Handle(new RunReconciliation(workflowId, metadata));
        handler.Handle(new ResolveBreakCase(workflowId, metadata));
        handler.Handle(new SubmitForApproval(workflowId, metadata));
    }

    private static FundWorkflowCommandMetadata Metadata(
        string actor,
        string role = "Analyst",
        string? requestId = null,
        string? incidentTicketId = null)
        => new(actor, role, requestId, CorrelationId: null, incidentTicketId);
}
