using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Strategies;

public sealed class ReconciliationBreakQueueRepositoryTests
{
    [Fact]
    public async Task ResolveAsync_rejects_transition_preconditions_and_failure_modes()
    {
        var repo = CreateRepository(out _);

        var invalidStatus = await repo.ResolveAsync(new ResolveReconciliationBreakRequest("missing", ReconciliationBreakQueueStatus.Open, "ops", "", "reason"));
        invalidStatus.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.InvalidTransition);

        var missingRationale = await repo.ResolveAsync(new ResolveReconciliationBreakRequest("missing", ReconciliationBreakQueueStatus.Resolved, "ops", "", " "));
        missingRationale.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.InvalidTransition);

        var notFound = await repo.ResolveAsync(new ResolveReconciliationBreakRequest("missing", ReconciliationBreakQueueStatus.Resolved, "ops", "done", "evidence"));
        notFound.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.NotFound);

        var openItem = CreateItem(status: ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(openItem);

        var wrongSource = await repo.ResolveAsync(new ResolveReconciliationBreakRequest(openItem.BreakId, ReconciliationBreakQueueStatus.Resolved, "ops", "done", "evidence"));
        wrongSource.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.InvalidTransition);
        wrongSource.Error.Should().Contain("Cannot move break from Open");
    }

    [Fact]
    public async Task StartReview_and_resolve_enforce_lifecycle_invariants_and_ordering()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(status: ReconciliationBreakQueueStatus.Open, severity: ReconciliationBreakSeverity.Critical, requiredSignoffRole: "controller");
        await repo.CreateIfMissingAsync(item);

        var review = await repo.StartReviewAsync(new ReviewReconciliationBreakRequest(item.BreakId, "alice", "alice", "triage"));
        review.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        review.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.InReview);

        var closed = await repo.ResolveAsync(new ResolveReconciliationBreakRequest(item.BreakId, ReconciliationBreakQueueStatus.Resolved, "bob", "resolved", "evidence packet #42"));
        closed.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        closed.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.AwaitingApproval);
        closed.Item.SignoffHistory.Should().NotBeNullOrEmpty();
        closed.Item.StateTransitions.Should().HaveCountGreaterThanOrEqualTo(2);

        var timestamps = closed.Item.StateTransitions!.Select(t => t.OccurredAt).ToArray();
        timestamps.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Audit_history_is_append_only_and_contains_required_fields_in_chronological_order()
    {
        var repo = CreateRepository(out var root);
        var item = CreateItem(status: ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        await repo.StartReviewAsync(new ReviewReconciliationBreakRequest(item.BreakId, "ops", "ops", "triage"));
        await repo.ResolveAsync(new ResolveReconciliationBreakRequest(item.BreakId, ReconciliationBreakQueueStatus.Resolved, "ops", "resolved", "packet evidence"));

        var history = await repo.GetAuditHistoryAsync(item.BreakId);
        history.Should().HaveCount(3);
        history.Select(x => x.OccurredAt).Should().BeInAscendingOrder();

        var auditPath = Path.Combine(root, "reconciliation-break-queue-audit.jsonl");
        var lines = await File.ReadAllLinesAsync(auditPath);
        lines.Length.Should().BeGreaterThanOrEqualTo(3);

        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            var rootEl = doc.RootElement;
            rootEl.GetProperty("eventId").GetString().Should().NotBeNullOrWhiteSpace();
            rootEl.GetProperty("breakId").GetString().Should().NotBeNullOrWhiteSpace();
            rootEl.GetProperty("eventType").GetString().Should().NotBeNullOrWhiteSpace();
            rootEl.GetProperty("newStatus").GetString().Should().NotBeNullOrWhiteSpace();
            rootEl.GetProperty("newLifecycleState").GetString().Should().NotBeNullOrWhiteSpace();
            rootEl.GetProperty("occurredAt").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Concurrent_review_commands_allow_only_one_successful_state_transition()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(status: ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);

        var first = repo.StartReviewAsync(new ReviewReconciliationBreakRequest(item.BreakId, "a", "a", "one"));
        var second = repo.StartReviewAsync(new ReviewReconciliationBreakRequest(item.BreakId, "b", "b", "two"));

        var results = await Task.WhenAll(first, second);
        results.Count(r => r.Status == ReconciliationBreakQueueTransitionStatus.Success).Should().Be(1);
        results.Count(r => r.Status == ReconciliationBreakQueueTransitionStatus.InvalidTransition).Should().Be(1);
    }

    [Fact]
    public async Task GetAllAsync_filters_by_status_for_read_route_behavior()
    {
        var repo = CreateRepository(out _);
        var open = CreateItem(status: ReconciliationBreakQueueStatus.Open);
        var review = CreateItem(status: ReconciliationBreakQueueStatus.InReview);
        await repo.CreateIfMissingAsync(open);
        await repo.CreateIfMissingAsync(review);

        var onlyOpen = await repo.GetAllAsync(ReconciliationBreakQueueStatus.Open);
        onlyOpen.Should().ContainSingle(i => i.BreakId == open.BreakId);
        onlyOpen.Should().NotContain(i => i.BreakId == review.BreakId);
    }


    [Fact]
    public async Task Casework_commands_enforce_guardrails_comments_audit_and_concurrency()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        var seeded = await repo.GetByIdAsync(item.BreakId);
        seeded.Should().NotBeNull();

        var investigateWithoutAssignee = await repo.ApplyCaseworkCommandAsync(Command(seeded!, ReconciliationCaseworkAction.TransitionStatus) with
        {
            Status = ReconciliationCaseLifecycleState.Investigating
        });
        investigateWithoutAssignee.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        investigateWithoutAssignee.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingActor);

        var assigned = await repo.ApplyCaseworkCommandAsync(Command(seeded!, ReconciliationCaseworkAction.Assign) with { Assignee = "controller-a" });
        assigned.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        assigned.Item!.Version.Should().Be(seeded!.Version + 1);

        var staleRetry = await repo.ApplyCaseworkCommandAsync(Command(seeded, ReconciliationCaseworkAction.ChangePriority) with { Priority = ReconciliationCasePriority.High });
        staleRetry.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Conflict);
        staleRetry.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.ConcurrencyConflict);

        var awaitingEvidence = await repo.ApplyCaseworkCommandAsync(Command(assigned.Item, ReconciliationCaseworkAction.TransitionStatus) with
        {
            Status = ReconciliationCaseLifecycleState.AwaitingEvidence
        });
        awaitingEvidence.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingEvidence);

        var comment = await repo.ApplyCaseworkCommandAsync(Command(assigned.Item, ReconciliationCaseworkAction.AddComment) with
        {
            Note = "Need custodian close packet.",
            Visibility = ReconciliationCaseCommentVisibility.CloseEvidence,
            EvidenceLinks = ["evidence://close/packet-1"]
        });
        comment.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        comment.Item!.CommentCount.Should().Be(1);
        comment.Item.EvidenceCount.Should().Be(1);

        var history = await repo.GetAuditHistoryAsync(item.BreakId);
        history.Should().Contain(e => e.EventType == "AssigneeChanged" && e.CommandId is not null && e.CorrelationId is not null && e.SchemaVersion == 1);
        history.Should().Contain(e => e.EventType == "CommentAdded" && e.AfterPayload is not null);
    }

    [Fact]
    public async Task Resolve_signoff_reopen_and_bulk_dry_run_follow_shared_casework_rules()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open) with { AssignedTo = "controller-a" };
        await repo.CreateIfMissingAsync(item);
        var current = (await repo.GetByIdAsync(item.BreakId))!;

        var missingTaxonomy = await repo.ApplyCaseworkCommandAsync(Command(current, ReconciliationCaseworkAction.Resolve));
        missingTaxonomy.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingRootCause);

        var rootCause = await repo.ApplyCaseworkCommandAsync(Command(current, ReconciliationCaseworkAction.SetRootCause) with { RootCauseCode = "BrokerCashTiming" });
        var resolution = await repo.ApplyCaseworkCommandAsync(Command(rootCause.Item!, ReconciliationCaseworkAction.SetResolution) with { ResolutionCode = "LedgerAdjusted", Note = "Adjusted ledger lot." });
        var resolved = await repo.ApplyCaseworkCommandAsync(Command(resolution.Item!, ReconciliationCaseworkAction.Resolve) with { Note = "Resolved with close packet." });
        resolved.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        resolved.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Resolved);

        var sameSigner = await repo.ApplyCaseworkCommandAsync(Command(resolved.Item, ReconciliationCaseworkAction.SignOff));
        sameSigner.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.ResolverSignerConflict);

        var signoff = await repo.ApplyCaseworkCommandAsync(Command(resolved.Item, ReconciliationCaseworkAction.SignOff) with { Actor = "controller-b", Note = "Independent review complete." });
        signoff.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        signoff.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.SignedOff);

        var reopenWithoutReason = await repo.ApplyCaseworkCommandAsync(Command(signoff.Item, ReconciliationCaseworkAction.Reopen) with { Actor = "controller-manager", Privileged = true });
        reopenWithoutReason.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingReason);

        var reopened = await repo.ApplyCaseworkCommandAsync(Command(signoff.Item, ReconciliationCaseworkAction.Reopen) with { Actor = "controller-manager", Privileged = true, Reason = "Late broker correction." });
        reopened.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        reopened.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);

        var dryRunRequest = new ReconciliationBulkCaseworkRequest(
            BreakIds: [item.BreakId, "missing"],
            Action: ReconciliationCaseworkAction.ChangePriority,
            Actor: "controller-manager",
            CommandId: "bulk-1",
            CorrelationId: "corr-bulk-1",
            Source: "test",
            IdempotencyKey: "idem-1",
            DryRun: true,
            AllowPartialSuccess: true,
            Priority: ReconciliationCasePriority.Critical);
        var dryRun = await repo.ApplyBulkCaseworkAsync(dryRunRequest);
        dryRun.DryRun.Should().BeTrue();
        dryRun.Results.Should().Contain(r => r.BreakId == item.BreakId && r.WouldSucceed);
        dryRun.Results.Should().Contain(r => r.BreakId == "missing" && !r.WouldSucceed);

        var retry = await repo.ApplyBulkCaseworkAsync(dryRunRequest with { Priority = ReconciliationCasePriority.Low });
        retry.Should().BeEquivalentTo(dryRun);
    }

    [Fact]
    public void Sla_calculator_uses_business_hours_pause_stop_and_reopen_state()
    {
        var detected = new DateTimeOffset(2026, 5, 22, 15, 0, 0, TimeSpan.Zero);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open, ReconciliationBreakSeverity.High) with { DetectedAt = detected, LastUpdatedAt = detected, Priority = ReconciliationCasePriority.High };
        var policy = ReconciliationSlaCalculator.DefaultPolicyFor(item);

        var warning = ReconciliationSlaCalculator.Compute(item, policy, new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero));
        warning.State.Should().Be(ReconciliationCaseSlaState.Warning);
        warning.DueAt.Should().Be(new DateTimeOffset(2026, 5, 25, 15, 0, 0, TimeSpan.Zero));

        var paused = ReconciliationSlaCalculator.Compute(item with
        {
            LifecycleState = ReconciliationCaseLifecycleState.AwaitingEvidence,
            EvidenceLinks = ["evidence://request/1"]
        }, policy, detected.AddDays(2));
        paused.State.Should().Be(ReconciliationCaseSlaState.Paused);

        var stopped = ReconciliationSlaCalculator.Compute(item with { LifecycleState = ReconciliationCaseLifecycleState.Resolved }, policy, detected.AddDays(2));
        stopped.State.Should().Be(ReconciliationCaseSlaState.Stopped);

        var reopened = ReconciliationSlaCalculator.Compute(item with { LifecycleState = ReconciliationCaseLifecycleState.Reopened }, policy, detected.AddDays(4));
        reopened.State.Should().Be(ReconciliationCaseSlaState.Breached);
    }

    private static ReconciliationCaseworkCommand Command(ReconciliationBreakQueueItem item, ReconciliationCaseworkAction action)
        => new(
            BreakId: item.BreakId,
            Action: action,
            Actor: "controller-a",
            CommandId: Guid.NewGuid().ToString("N"),
            CorrelationId: Guid.NewGuid().ToString("N"),
            Source: "unit-test",
            ExpectedVersion: item.Version,
            Reason: "unit test");

    private static FileReconciliationBreakQueueRepository CreateRepository(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), $"recon-break-repo-{Guid.NewGuid():N}");
        return new FileReconciliationBreakQueueRepository(root, NullLogger<FileReconciliationBreakQueueRepository>.Instance);
    }

    private static ReconciliationBreakQueueItem CreateItem(
        ReconciliationBreakQueueStatus status,
        ReconciliationBreakSeverity severity = ReconciliationBreakSeverity.Medium,
        string? requiredSignoffRole = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new ReconciliationBreakQueueItem(
            BreakId: Guid.NewGuid().ToString("N"),
            RunId: "run-1",
            StrategyName: "strat",
            Category: ReconciliationBreakCategory.CashMismatch,
            Status: status,
            Variance: 10m,
            Reason: "variance",
            AssignedTo: null,
            DetectedAt: now,
            LastUpdatedAt: now,
            Severity: severity,
            RequiredSignoffRole: requiredSignoffRole,
            SignoffStatus: "pending");
    }
}
