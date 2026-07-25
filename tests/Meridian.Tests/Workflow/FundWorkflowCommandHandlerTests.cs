using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Operations;
using Meridian.Workflow.Workflows;

namespace Meridian.Tests.Workflow;

public sealed class FundWorkflowCommandHandlerTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "Meridian.Tests",
        "FundWorkflowCaseHistory",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HandleAsync_ProgressesWorkflowThroughCloseWithVerifiedOutcome()
    {
        var store = Store();
        var handler = new FundWorkflowCommandHandler(store);
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator", approvalReference: "approval-1");

        await handler.HandleAsync(new StartWorkflow(workflowId, metadata));
        await handler.HandleAsync(new ImportBrokerData(workflowId, metadata));
        await handler.HandleAsync(new NormalizeBrokerTransactions(workflowId, metadata));
        await handler.HandleAsync(new ResolveSecurityMasterMappings(workflowId, metadata));
        await handler.HandleAsync(new ApproveSecurityMasterOverrides(workflowId, metadata));
        await handler.HandleAsync(new BuildLedgerDraft(workflowId, metadata));
        await handler.HandleAsync(new ValidateLedgerDraft(workflowId, metadata));
        await handler.HandleAsync(new PostLedgerEntries(workflowId, metadata));
        await handler.HandleAsync(new RunReconciliation(workflowId, metadata));
        await handler.HandleAsync(new ResolveBreakCase(workflowId, metadata));
        await handler.HandleAsync(new SubmitForApproval(workflowId, metadata));
        await handler.HandleAsync(new ApproveWorkflow(workflowId, metadata));

        var closed = await handler.HandleAsync(new CloseWorkflow(workflowId, metadata));

        closed.OverallStatus.Should().Be(FundWorkflowOverallStatus.Closed);
        closed.StageStatus.Values.Should().OnlyContain(status => status == FundWorkflowSubStatus.Completed);
        closed.LastActor.Should().Be("operator");
        closed.Outcome.Should().NotBeNull();
        closed.Outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(closed.Outcome).Should().BeEmpty();
        var history = await store.ReadAsync(new OperationalCaseHistoryQuery { CaseId = workflowId.ToString("D") });
        history.Should().HaveCount(13);
        history.SelectMany(record => record.Approvals).Should().Contain(approval => approval.ApprovalId == "approval-1");
    }

    [Theory]
    [InlineData("", "Controller", "approval-1", "Approval actor is required.")]
    [InlineData("operator", "Trader", "approval-1", "Approval requires an authorized role")]
    [InlineData("operator", "", "approval-1", "Approval requires an authorized role")]
    [InlineData("operator", "Controller", null, "Approval reference is required.")]
    public async Task HandleAsync_ApprovalMetadataIsInvalid_FailsClosedWithoutRetainingApproval(
        string actor,
        string role,
        string? approvalReference,
        string expectedMessage)
    {
        var store = Store();
        var handler = new FundWorkflowCommandHandler(store);
        var workflowId = Guid.NewGuid();
        await handler.HandleAsync(new StartWorkflow(workflowId, Metadata("operator")));
        await handler.HandleAsync(new NormalizeBrokerTransactions(workflowId, Metadata("operator")));

        var blocked = await handler.HandleAsync(new ApproveSecurityMasterOverrides(
            workflowId,
            Metadata(actor, role: role, approvalReference: approvalReference)));

        blocked.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        blocked.Outcome.Issues.Should().ContainSingle(issue =>
            issue.IsBlocking && issue.Message.Contains(expectedMessage, StringComparison.Ordinal));
        blocked.StageStatus[FundWorkflowStage.SecurityMaster].Should().Be(FundWorkflowSubStatus.InProgress);
        VerifiedOperationOutcomeValidator.Validate(blocked.Outcome).Should().BeEmpty();
        var history = await store.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = workflowId.ToString("D")
        });
        history[^1].EventType.Should().EndWith(".rejected");
        history[^1].Approvals.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_FinalApprovalWithoutReference_RemainsAwaitingApproval()
    {
        var store = Store();
        var handler = new FundWorkflowCommandHandler(store);
        var workflowId = Guid.NewGuid();
        await MoveToApprovalAsync(handler, workflowId, Metadata("operator"));

        var blocked = await handler.HandleAsync(new ApproveWorkflow(
            workflowId,
            Metadata("controller", approvalReference: null)));

        blocked.OverallStatus.Should().Be(FundWorkflowOverallStatus.AwaitingApproval);
        blocked.StageStatus[FundWorkflowStage.Approval].Should().Be(FundWorkflowSubStatus.InProgress);
        blocked.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        blocked.Outcome.Issues.Should().ContainSingle(issue =>
            issue.Message == "Approval reference is required.");
        var history = await store.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = workflowId.ToString("D")
        });
        history[^1].Approvals.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_DelimiterCollisionInputs_DoNotReplayAsTheSameRequest()
    {
        var store = Store();
        var handler = new FundWorkflowCommandHandler(store);
        var workflowId = Guid.NewGuid();
        const string requestId = "collision-request";
        var firstMetadata = Metadata("alpha|beta", role: "gamma", requestId: requestId);
        var collidingUnderLegacyHash = Metadata("alpha", role: "beta|gamma", requestId: requestId);

        var accepted = await handler.HandleAsync(new StartWorkflow(workflowId, firstMetadata));
        var blocked = await handler.HandleAsync(new StartWorkflow(workflowId, collidingUnderLegacyHash));

        accepted.Outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        blocked.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        blocked.Outcome.Issues.Should().ContainSingle(issue =>
            issue.Message.Contains("already used", StringComparison.Ordinal));
        var history = await store.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = workflowId.ToString("D")
        });
        history.Should().HaveCount(2);
        history[0].InputHashSha256.Should().NotBe(history[1].InputHashSha256);
    }

    [Fact]
    public async Task HandleAsync_CaseHistoryReadFailure_ReturnsValidatedBlockedOutcome()
    {
        var handler = new FundWorkflowCommandHandler(new FaultingCaseHistoryStore(
            readException: new IOException("Case history is unavailable.")));
        var workflowId = Guid.NewGuid();

        var blocked = await handler.HandleAsync(new StartWorkflow(
            workflowId,
            Metadata("operator", requestId: "read-failure")));

        blocked.OverallStatus.Should().Be(FundWorkflowOverallStatus.Draft);
        blocked.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        blocked.Outcome.Issues.Should().ContainSingle(issue =>
            issue.Code == "workflow-case-history-read-failed" && issue.IsBlocking);
        blocked.Outcome.Recovery.Should().ContainSingle(action =>
            action.ActionId == "recover-workflow-case-history-read" && action.Retryable);
        VerifiedOperationOutcomeValidator.Validate(blocked.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_CaseHistoryAppendFailure_ReturnsValidatedFailedOutcome()
    {
        var handler = new FundWorkflowCommandHandler(new FaultingCaseHistoryStore(
            appendException: new IOException("Case history append failed.")));
        var workflowId = Guid.NewGuid();

        var failed = await handler.HandleAsync(new StartWorkflow(
            workflowId,
            Metadata("operator", requestId: "append-failure")));

        failed.OverallStatus.Should().Be(FundWorkflowOverallStatus.Draft);
        failed.Outcome!.State.Should().Be(OperationTerminalState.Failed);
        failed.Outcome.Issues.Should().ContainSingle(issue =>
            issue.Code == "workflow-case-history-persistence-failed" && !issue.IsBlocking);
        failed.Outcome.Recovery.Should().ContainSingle(action =>
            action.ActionId == "recover-workflow-case-history-persistence" &&
            action.Guidance.Contains("may already be committed", StringComparison.Ordinal));
        VerifiedOperationOutcomeValidator.Validate(failed.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_PostLedgerEntriesRequiresPostingGateAndReturnsRetainedBlockedOutcome()
    {
        var store = Store();
        var handler = new FundWorkflowCommandHandler(store);
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator", requestId: "request-post");

        await handler.HandleAsync(new StartWorkflow(workflowId, Metadata("operator")));
        await handler.HandleAsync(new NormalizeBrokerTransactions(workflowId, Metadata("operator")));
        await handler.HandleAsync(new ApproveSecurityMasterOverrides(workflowId, Metadata("operator")));

        var blocked = await handler.HandleAsync(new PostLedgerEntries(workflowId, metadata));
        var replayed = await handler.HandleAsync(new PostLedgerEntries(workflowId, metadata));
        var restarted = new FundWorkflowCommandHandler(new FileOperationalCaseHistoryStore(_dataRoot));
        var replayedAfterRestart = await restarted.HandleAsync(new PostLedgerEntries(workflowId, metadata));

        blocked.Outcome.Should().NotBeNull();
        blocked.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        blocked.Outcome.Issues.Should().ContainSingle(issue =>
            issue.IsBlocking &&
            issue.Message == "Posting gate must be opened by ValidateLedgerDraft.");
        VerifiedOperationOutcomeValidator.Validate(blocked.Outcome).Should().BeEmpty();
        replayed.Outcome.Should().BeEquivalentTo(blocked.Outcome);
        replayedAfterRestart.Outcome.Should().BeEquivalentTo(blocked.Outcome);
        replayed.Outcome.OperationId.Should().Be(blocked.Outcome.OperationId);
        replayedAfterRestart.Outcome.OperationId.Should().Be(blocked.Outcome.OperationId);
        var history = await store.ReadAsync(new OperationalCaseHistoryQuery { CaseId = workflowId.ToString("D") });
        var rejected = history.Should().ContainSingle(record => record.EventType.EndsWith(".rejected", StringComparison.Ordinal)).Which;
        rejected.Data["requestId"].Should().Be("request-post");
        rejected.Exceptions.Should().ContainSingle(exception => exception.Message.Contains("Posting gate", StringComparison.Ordinal));
        rejected.TerminalOutcome!.State.Should().Be(OperationTerminalState.Blocked);
        VerifiedOperationOutcomeValidator.Validate(rejected.TerminalOutcome).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_RoutesLedgerRejectionBackToLedger()
    {
        var handler = new FundWorkflowCommandHandler(Store());
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator");
        await MoveToApprovalAsync(handler, workflowId, metadata);

        var rejected = await handler.HandleAsync(new RejectWorkflow(
            workflowId,
            FundWorkflowRejectionReasonCode.LedgerMismatch,
            metadata));

        rejected.OverallStatus.Should().Be(FundWorkflowOverallStatus.Rejected);
        rejected.StageStatus[FundWorkflowStage.Approval].Should().Be(FundWorkflowSubStatus.Blocked);
        rejected.StageStatus[FundWorkflowStage.Ledger].Should().Be(FundWorkflowSubStatus.InProgress);
        rejected.StageStatus[FundWorkflowStage.Reconciliation].Should().Be(FundWorkflowSubStatus.NotStarted);
        rejected.Outcome!.State.Should().Be(OperationTerminalState.Succeeded);
    }

    [Fact]
    public async Task HandleAsync_ReopenRequiresElevatedRoleAndIncidentTicket()
    {
        var handler = new FundWorkflowCommandHandler(Store());
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator");
        await MoveToApprovalAsync(handler, workflowId, metadata);
        await handler.HandleAsync(new RejectWorkflow(workflowId, FundWorkflowRejectionReasonCode.DataQuality, metadata));

        var missingTicket = await handler.HandleAsync(new ReopenWorkflow(
            workflowId,
            "retry",
            Metadata("operator", role: "Administrator")));
        var basicUser = await handler.HandleAsync(new ReopenWorkflow(
            workflowId,
            "retry",
            Metadata("operator", role: "Analyst", incidentTicketId: "INC-1")));

        missingTicket.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        missingTicket.Outcome.Issues.Should().ContainSingle(issue =>
            issue.IsBlocking && issue.Message == "Incident/ticket ID is required for reopen.");
        basicUser.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        basicUser.Outcome.Issues.Should().ContainSingle(issue =>
            issue.IsBlocking && issue.Message == "Reopen requires elevated role (Administrator or OperationsManager).");
        VerifiedOperationOutcomeValidator.Validate(missingTicket.Outcome).Should().BeEmpty();
        VerifiedOperationOutcomeValidator.Validate(basicUser.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ReopenRejectedWorkflowReturnsToReconciliationAndRetainsRecoveryAttempt()
    {
        var store = Store();
        var handler = new FundWorkflowCommandHandler(store);
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator");
        await MoveToApprovalAsync(handler, workflowId, metadata);
        await handler.HandleAsync(new RejectWorkflow(workflowId, FundWorkflowRejectionReasonCode.DataQuality, metadata));

        var reopened = await handler.HandleAsync(new ReopenWorkflow(
            workflowId,
            "retry",
            Metadata("ops-lead", role: "OperationsManager", incidentTicketId: "INC-1")));

        reopened.OverallStatus.Should().Be(FundWorkflowOverallStatus.InProgress);
        reopened.StageStatus[FundWorkflowStage.Reconciliation].Should().Be(FundWorkflowSubStatus.InProgress);
        reopened.StageStatus[FundWorkflowStage.Approval].Should().Be(FundWorkflowSubStatus.NotStarted);
        reopened.ApprovalGateOpen.Should().BeFalse();
        reopened.LastActor.Should().Be("ops-lead");
        var history = await store.ReadAsync(new OperationalCaseHistoryQuery { CaseId = workflowId.ToString("D") });
        history[^1].RecoveryAttempts.Should().ContainSingle(attempt => attempt.RecoveryActionId == "INC-1");
    }

    [Fact]
    public async Task HandleAsync_DuplicateRequestIdRetainsReceiptWithoutApplyingMutationTwice()
    {
        var store = Store();
        var handler = new FundWorkflowCommandHandler(store);
        var workflowId = Guid.NewGuid();
        var first = Metadata("first", requestId: "request-1");

        var started = await handler.HandleAsync(new StartWorkflow(workflowId, first));
        var repeated = await handler.HandleAsync(new StartWorkflow(workflowId, first));

        repeated.StageStatus.Should().Equal(started.StageStatus);
        repeated.LastActor.Should().Be("first");
        repeated.Outcome.Should().BeEquivalentTo(started.Outcome);
        repeated.Outcome!.OperationId.Should().Be(started.Outcome!.OperationId);
        var history = await store.ReadAsync(new OperationalCaseHistoryQuery { CaseId = workflowId.ToString("D") });
        history.Should().ContainSingle();
        history[0].EventType.Should().EndWith(".accepted");
        history[0].Data["requestId"].Should().Be("request-1");
    }

    [Fact]
    public async Task HandleAsync_AfterRestart_ExactReplayReturnsRetainedReceiptButRequestIdConflictIsBlocked()
    {
        var store = Store();
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator", requestId: "request-1");
        var firstHandler = new FundWorkflowCommandHandler(store);
        var started = await firstHandler.HandleAsync(new StartWorkflow(workflowId, metadata));

        var restarted = new FundWorkflowCommandHandler(new FileOperationalCaseHistoryStore(_dataRoot));
        var replayed = await restarted.HandleAsync(new StartWorkflow(workflowId, metadata));
        var conflicted = await restarted.HandleAsync(new NormalizeBrokerTransactions(workflowId, metadata));

        replayed.StageStatus.Should().Equal(started.StageStatus);
        replayed.Outcome.Should().BeEquivalentTo(started.Outcome);
        replayed.Outcome!.OperationId.Should().Be(started.Outcome!.OperationId);
        conflicted.StageStatus.Should().Equal(started.StageStatus);
        conflicted.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        conflicted.Outcome.Issues.Should().ContainSingle(issue =>
            issue.IsBlocking && issue.Message.Contains("already used", StringComparison.Ordinal));
        VerifiedOperationOutcomeValidator.Validate(conflicted.Outcome).Should().BeEmpty();
        var history = await store.ReadAsync(new OperationalCaseHistoryQuery { CaseId = workflowId.ToString("D") });
        history.Select(record => record.EventType).Should().Equal(
            "fund-workflow.StartWorkflow.accepted",
            "fund-workflow.NormalizeBrokerTransactions.idempotency-conflict");
    }

    [Fact]
    public async Task HandleAsync_NewHandlerReplaysDurableHistoryBeforeApplyingNextTransition()
    {
        var store = Store();
        var workflowId = Guid.NewGuid();
        var metadata = Metadata("operator");
        var firstHandler = new FundWorkflowCommandHandler(store);
        await firstHandler.HandleAsync(new StartWorkflow(workflowId, metadata));
        await firstHandler.HandleAsync(new NormalizeBrokerTransactions(workflowId, metadata));
        await firstHandler.HandleAsync(new ApproveSecurityMasterOverrides(workflowId, metadata));

        var restartedHandler = new FundWorkflowCommandHandler(new FileOperationalCaseHistoryStore(_dataRoot));
        var restored = await restartedHandler.HandleAsync(new BuildLedgerDraft(workflowId, Metadata("after-restart")));

        restored.OverallStatus.Should().Be(FundWorkflowOverallStatus.InProgress);
        restored.StageStatus[FundWorkflowStage.Ledger].Should().Be(FundWorkflowSubStatus.InProgress);
        restored.LastActor.Should().Be("after-restart");
        restored.Outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        var replayedHistory = await store.ReadAsync(new OperationalCaseHistoryQuery { CaseId = workflowId.ToString("D") });
        replayedHistory.Should().HaveCount(4);
    }

    [Fact]
    public async Task HandleAsync_StaleHandlerReturnsBlockedOutcomeAndRefreshesBeforeFurtherWork()
    {
        var store = Store();
        var workflowId = Guid.NewGuid();
        var startMetadata = Metadata("operator-a", requestId: "start-request");
        var winner = new FundWorkflowCommandHandler(store);
        await winner.HandleAsync(new StartWorkflow(workflowId, startMetadata));

        var stale = new FundWorkflowCommandHandler(new FileOperationalCaseHistoryStore(_dataRoot));
        var hydratedReplay = await stale.HandleAsync(new StartWorkflow(workflowId, startMetadata));
        hydratedReplay.Outcome!.State.Should().Be(OperationTerminalState.Succeeded);

        await winner.HandleAsync(new NormalizeBrokerTransactions(
            workflowId,
            Metadata("operator-a", requestId: "winner-normalize")));
        var blocked = await stale.HandleAsync(new ImportBrokerData(
            workflowId,
            Metadata("operator-b", requestId: "stale-import")));

        blocked.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        blocked.Outcome.Issues.Should().ContainSingle(issue =>
            issue.Code == "workflow-history-concurrency-conflict" && issue.IsBlocking);
        blocked.Outcome.Recovery.Should().ContainSingle(action =>
            action.ActionId == "reconcile-retained-workflow-state" && action.Retryable);
        VerifiedOperationOutcomeValidator.Validate(blocked.Outcome).Should().BeEmpty();
        blocked.StageStatus[FundWorkflowStage.BrokerImport].Should().Be(FundWorkflowSubStatus.Completed);
        blocked.StageStatus[FundWorkflowStage.SecurityMaster].Should().Be(FundWorkflowSubStatus.InProgress);
        blocked.LastActor.Should().Be("operator-a");

        var retainedAfterConflict = await store.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = workflowId.ToString("D"),
            CaseType = "fund-workflow"
        });
        retainedAfterConflict.Should().HaveCount(3);
        var retainedConflict = retainedAfterConflict.Should().ContainSingle(record =>
            record.EventType == "fund-workflow.ImportBrokerData.concurrency-conflict").Which;
        RequestId(retainedConflict).Should().Be("stale-import");
        retainedConflict.Exceptions.Should().ContainSingle(exception =>
            exception.Message.Contains("expected predecessor sequence", StringComparison.OrdinalIgnoreCase));
        retainedConflict.TerminalOutcome.Should().BeEquivalentTo(blocked.Outcome);
        retainedAfterConflict.Should().NotContain(record =>
            record.EventType == "fund-workflow.ImportBrokerData.accepted" && RequestId(record) == "stale-import");

        var replayedAfterRestart = await new FundWorkflowCommandHandler(
                new FileOperationalCaseHistoryStore(_dataRoot))
            .HandleAsync(new ImportBrokerData(
                workflowId,
                Metadata("operator-b", requestId: "stale-import")));
        replayedAfterRestart.Outcome.Should().BeEquivalentTo(blocked.Outcome);

        var resumed = await stale.HandleAsync(new ResolveSecurityMasterMappings(
            workflowId,
            Metadata("operator-b", requestId: "after-refresh")));
        resumed.Outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        (await store.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = workflowId.ToString("D"),
            CaseType = "fund-workflow"
        })).Should().HaveCount(4);
    }

    [Fact]
    public async Task HandleAsync_ReplaysAssigneeAndEmitsExactPredecessorAcrossRestartedReassignment()
    {
        var store = Store();
        var workflowId = Guid.NewGuid();
        var firstHandler = new FundWorkflowCommandHandler(store);
        await firstHandler.HandleAsync(new StartWorkflow(
            workflowId,
            Metadata("operator-a", requestId: "assign-a", assigneeId: "analyst-a")));
        await firstHandler.HandleAsync(new NormalizeBrokerTransactions(
            workflowId,
            Metadata("operator-a", requestId: "assign-b", assigneeId: "analyst-b")));

        var restarted = new FundWorkflowCommandHandler(new FileOperationalCaseHistoryStore(_dataRoot));
        var reassigned = await restarted.HandleAsync(new ResolveSecurityMasterMappings(
            workflowId,
            Metadata("operator-b", requestId: "assign-c", assigneeId: "analyst-c")));

        reassigned.Outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        var history = await store.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = workflowId.ToString("D"),
            CaseType = "fund-workflow"
        });
        history.Should().HaveCount(3);
        history[0].Data["expectedPreviousCaseSequence"].Should().Be("0");
        history[0].Data.Should().NotContainKey("expectedPreviousCaseRecordHashSha256");
        history[0].Assignment!.PreviousAssigneeId.Should().BeNull();
        history[0].Assignment.AssigneeId.Should().Be("analyst-a");
        history[1].Data["expectedPreviousCaseSequence"].Should().Be(history[0].Sequence.ToString());
        history[1].Data["expectedPreviousCaseRecordHashSha256"].Should().Be(history[0].RecordHashSha256);
        history[1].Assignment!.PreviousAssigneeId.Should().Be("analyst-a");
        history[1].Assignment.AssigneeId.Should().Be("analyst-b");
        history[2].Data["expectedPreviousCaseSequence"].Should().Be(history[1].Sequence.ToString());
        history[2].Data["expectedPreviousCaseRecordHashSha256"].Should().Be(history[1].RecordHashSha256);
        history[2].Assignment!.PreviousAssigneeId.Should().Be("analyst-b");
        history[2].Assignment.AssigneeId.Should().Be("analyst-c");
    }

    private FileOperationalCaseHistoryStore Store() => new(_dataRoot);

    private static string? RequestId(OperationalCaseHistoryRecord record) =>
        record.Data.TryGetValue("requestId", out var requestId) ? requestId : null;

    private static async Task MoveToApprovalAsync(
        FundWorkflowCommandHandler handler,
        Guid workflowId,
        FundWorkflowCommandMetadata metadata)
    {
        await handler.HandleAsync(new StartWorkflow(workflowId, metadata));
        await handler.HandleAsync(new ImportBrokerData(workflowId, metadata));
        await handler.HandleAsync(new NormalizeBrokerTransactions(workflowId, metadata));
        await handler.HandleAsync(new ResolveSecurityMasterMappings(workflowId, metadata));
        await handler.HandleAsync(new ApproveSecurityMasterOverrides(workflowId, metadata));
        await handler.HandleAsync(new BuildLedgerDraft(workflowId, metadata));
        await handler.HandleAsync(new ValidateLedgerDraft(workflowId, metadata));
        await handler.HandleAsync(new PostLedgerEntries(workflowId, metadata));
        await handler.HandleAsync(new RunReconciliation(workflowId, metadata));
        await handler.HandleAsync(new ResolveBreakCase(workflowId, metadata));
        await handler.HandleAsync(new SubmitForApproval(workflowId, metadata));
    }

    private static FundWorkflowCommandMetadata Metadata(
        string actor,
        string role = "Controller",
        string? requestId = null,
        string? incidentTicketId = null,
        string? approvalReference = "approval-default",
        string? assigneeId = null) =>
        new(actor, role, requestId, CorrelationId: null, incidentTicketId, approvalReference, assigneeId);

    private sealed class FaultingCaseHistoryStore(
        Exception? readException = null,
        Exception? appendException = null) : IOperationalCaseHistoryStore
    {
        public ValueTask<OperationalCaseHistoryRecord> AppendAsync(
            OperationalCaseHistoryAppendRequest request,
            CancellationToken cancellationToken = default) =>
            appendException is null
                ? throw new InvalidOperationException("The test store requires an append exception.")
                : ValueTask.FromException<OperationalCaseHistoryRecord>(appendException);

        public ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadAsync(
            OperationalCaseHistoryQuery query,
            CancellationToken cancellationToken = default) =>
            readException is null
                ? ValueTask.FromResult<IReadOnlyList<OperationalCaseHistoryRecord>>([])
                : ValueTask.FromException<IReadOnlyList<OperationalCaseHistoryRecord>>(readException);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }
}
