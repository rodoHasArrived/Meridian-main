using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

/// <summary>
/// Scenario: Accounting close triage requires shared reconciliation casework guardrails before a break can be resolved or signed off.
/// </summary>
public sealed class ReconciliationCaseworkHardeningTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-recon-casework-" + Guid.NewGuid().ToString("N"));
    private readonly FileReconciliationBreakQueueRepository _repository;

    public ReconciliationCaseworkHardeningTests()
    {
        _repository = new FileReconciliationBreakQueueRepository(_directory, NullLogger<FileReconciliationBreakQueueRepository>.Instance);
    }

    [Fact]
    public async Task Scenario_AccountingClose_CaseRequiresAssignmentTaxonomyAndDualSignOff()
    {
        var item = BuildCase();
        await _repository.CreateIfMissingAsync(item);

        var unassignedInvestigation = await _repository.TransitionStatusAsync(
            new ReconciliationStatusTransitionRequest(item.BreakId, ReconciliationCaseLifecycleState.Investigating, "Start investigation.", ["review-note"]),
            "ops.reviewer");

        unassignedInvestigation.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        unassignedInvestigation.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingAssignee);

        (await _repository.AssignAsync(new ReconciliationAssignRequest(item.BreakId, "ops.reviewer", "Ops Reviewer", "Own cash variance."), "ops.lead"))
            .Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        (await _repository.TransitionStatusAsync(new ReconciliationStatusTransitionRequest(item.BreakId, ReconciliationCaseLifecycleState.Investigating, "Investigating cash variance.", ["review-note"]), "ops.reviewer"))
            .Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);

        var unresolved = await _repository.TransitionStatusAsync(
            new ReconciliationStatusTransitionRequest(item.BreakId, ReconciliationCaseLifecycleState.Resolved, "Resolve after bank support.", ["evidence://bank-ticket"]),
            "ops.reviewer");
        unresolved.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingRootCause);

        (await _repository.SetRootCauseAsync(new ReconciliationTaxonomyRequest(item.BreakId, "BankTiming", null, "Bank timing root cause."), "ops.reviewer"))
            .Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        (await _repository.SetResolutionAsync(new ReconciliationTaxonomyRequest(item.BreakId, "AdjustedLedger", "Posted adjustment.", "Resolution selected."), "ops.reviewer"))
            .Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        var resolved = await _repository.TransitionStatusAsync(
            new ReconciliationStatusTransitionRequest(item.BreakId, ReconciliationCaseLifecycleState.Resolved, "Resolve after bank support.", ["evidence://bank-ticket"]),
            "ops.reviewer");
        resolved.Item.Should().NotBeNull();
        resolved.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Resolved);
        resolved.Item.Version.Should().BeGreaterThan(1);

        var sameSigner = await _repository.SignOffAsync(new ReconciliationSignOffRequest(item.BreakId, "Approve close evidence."), "ops.reviewer");
        sameSigner.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.DualReviewRequired);

        var signed = await _repository.SignOffAsync(new ReconciliationSignOffRequest(item.BreakId, "Approve close evidence."), "fund.controller");
        signed.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        signed.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.SignedOff);
        signed.Item.SignOffBy.Should().Be("fund.controller");
    }

    [Fact]
    public async Task Scenario_AccountingClose_CommentsBulkRetryAndAuditAreSharedAndIdempotent()
    {
        var item = BuildCase("BRK-threaded");
        await _repository.CreateIfMissingAsync(item);
        await _repository.AssignAsync(new ReconciliationAssignRequest(item.BreakId, "ops.owner", "Ops Owner", "Assign for evidence."), "ops.lead");

        var comment = await _repository.AddCommentAsync(
            new ReconciliationCommentMutationRequest(item.BreakId, null, null, "Need close evidence from custodian.", ReconciliationCommentVisibility.CloseEvidence, ["evidence://request-1"]),
            "ops.owner",
            "Ops Owner");
        comment.Item!.Comments.Should().ContainSingle(c => c.Visibility == ReconciliationCommentVisibility.CloseEvidence);
        comment.Item.SlaState.Should().BeOneOf(ReconciliationSlaState.Running, ReconciliationSlaState.Warning, ReconciliationSlaState.Paused);

        var stale = await _repository.ChangePriorityAsync(new ReconciliationPriorityRequest(item.BreakId, ReconciliationCasePriority.Critical, "Escalate.", ExpectedVersion: 1), "ops.owner");
        stale.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Conflict);

        var bulk = new ReconciliationBulkActionRequest([item.BreakId, "missing"], ReconciliationBulkActionType.ChangePriority, "idem-1", DryRun: false, AllowPartialSuccess: true, Priority: ReconciliationCasePriority.High, Reason: "Bulk triage.");
        var result = await _repository.BulkActionAsync(bulk, "ops.lead");
        var retry = await _repository.BulkActionAsync(bulk, "ops.lead");
        retry.Should().BeEquivalentTo(result);
        result.SuccessCount.Should().Be(1);
        result.FailureCount.Should().Be(1);

        var audit = await _repository.GetAuditHistoryAsync(item.BreakId);
        audit.Should().Contain(e => e.EventType == "CommentAdded" && e.Actor == "ops.owner" && e.SchemaVersion == 1);
        audit.Should().Contain(e => e.EventType == "BulkActionCaseSucceeded" && e.CommandId is null);
    }

    [Fact]
    public async Task Scenario_AccountingClose_BulkDryRunDoesNotConsumeExecuteIdempotencyKey()
    {
        var item = BuildCase("BRK-bulk-idempotency");
        await _repository.CreateIfMissingAsync(item);

        var preview = new ReconciliationBulkActionRequest(
            [item.BreakId],
            ReconciliationBulkActionType.ChangePriority,
            "idem-preview-commit",
            DryRun: true,
            AllowPartialSuccess: true,
            Priority: ReconciliationCasePriority.High,
            Reason: "Preview priority escalation.");

        var dryRun = await _repository.BulkActionAsync(preview, "ops.lead");
        dryRun.DryRun.Should().BeTrue();
        dryRun.SuccessCount.Should().Be(1);
        (await _repository.GetByIdAsync(item.BreakId))!.Priority.Should().Be(ReconciliationCasePriority.Normal);

        var execute = await _repository.BulkActionAsync(preview with { DryRun = false }, "ops.lead");
        execute.DryRun.Should().BeFalse();
        execute.SuccessCount.Should().Be(1);
        execute.BulkActionId.Should().NotBe(dryRun.BulkActionId);
        (await _repository.GetByIdAsync(item.BreakId))!.Priority.Should().Be(ReconciliationCasePriority.High);

        var lookup = await _repository.GetBulkActionResultAsync("idem-preview-commit");
        lookup.Should().NotBeNull();
        lookup!.DryRun.Should().BeFalse();
        lookup.BulkActionId.Should().Be(execute.BulkActionId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static ReconciliationBreakQueueItem BuildCase(string breakId = "BRK-close-001")
        => new(
            BreakId: breakId,
            RunId: "run-close-001",
            StrategyName: "Fund Accounting Close",
            Category: ReconciliationBreakCategory.AmountMismatch,
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: 1250m,
            Reason: "Bank cash variance exceeds tolerance.",
            AssignedTo: null,
            DetectedAt: DateTimeOffset.UtcNow.AddHours(-2),
            LastUpdatedAt: DateTimeOffset.UtcNow.AddHours(-2),
            Severity: ReconciliationBreakSeverity.High,
            FundAccountId: "fund-001");
}
