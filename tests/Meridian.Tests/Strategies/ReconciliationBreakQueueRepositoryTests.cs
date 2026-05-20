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
        review.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Triaged);

        var closed = await repo.ResolveAsync(new ResolveReconciliationBreakRequest(item.BreakId, ReconciliationBreakQueueStatus.Resolved, "bob", "resolved", "evidence packet #42"));
        closed.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        closed.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Closed);
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
