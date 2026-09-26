using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Strategies;

public sealed class FileReconciliationBreakQueueLineageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lineage-" + Guid.NewGuid().ToString("N"));
    private readonly ReconciliationRunObservationScope _scope = new("tenant-a", "company-a", "fund-a", "account-a",
        Guid.NewGuid(), Guid.NewGuid().ToString("D"), "custodian-feed", "custody-account");
    private readonly DateTimeOffset _start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private FileReconciliationBreakQueueRepository Repository() => new(_root, NullLogger<FileReconciliationBreakQueueRepository>.Instance);

    [Fact]
    public async Task Restart_retains_new_aging_cleared_and_distinct_recurring_occurrence()
    {
        var repo = Repository();
        await Add(repo, "one", 1m);
        await repo.ObserveCompletedRunAsync(Run("one", 0, new ReconciliationBreakObservation("one", "security-1")));
        var first = (await repo.GetByIdAsync("one"))!.Lineage!;
        first.ObservationState.Should().Be("New");

        repo = Repository();
        await Add(repo, "two", 9m);
        await repo.ObserveCompletedRunAsync(Run("two", 2, new ReconciliationBreakObservation("two", "security-1")));
        var aging = (await repo.GetByIdAsync("two"))!.Lineage!;
        aging.LineageId.Should().Be(first.LineageId);
        aging.OccurrenceId.Should().Be(first.OccurrenceId);
        aging.ObservationState.Should().Be("Aging");
        aging.FirstObservedAt.Should().Be(_start);
        (await repo.GetByIdAsync("one"))!.Lineage!.LastObservedRunId.Should().Be("two");

        await repo.ObserveCompletedRunAsync(Run("matched", 3));
        repo = Repository();
        var cleared = (await repo.GetByIdAsync("two"))!;
        cleared.Lineage!.ObservationState.Should().Be("Cleared");
        cleared.Lineage.ClearedByRunId.Should().Be("matched");
        cleared.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
        cleared.BlockedOutputs.Should().Contain("PeriodClose");
        await Add(repo, "three", 6m);
        await repo.ObserveCompletedRunAsync(Run("three", 4, new ReconciliationBreakObservation("three", "security-1")));
        var recurring = (await Repository().GetByIdAsync("three"))!.Lineage!;
        recurring.LineageId.Should().Be(first.LineageId);
        recurring.OccurrenceId.Should().NotBe(first.OccurrenceId);
        recurring.OccurrenceNumber.Should().Be(2);
        recurring.ObservationState.Should().Be("Recurring");
        recurring.FirstObservedAt.Should().Be(_start);
        recurring.OccurrenceFirstObservedAt.Should().Be(_start.AddDays(4));
    }

    [Fact]
    public async Task Failure_partial_and_older_runs_do_not_clear_prior_observations()
    {
        var repo = Repository();
        await Add(repo, "one");
        await repo.ObserveCompletedRunAsync(Run("one", 1, new ReconciliationBreakObservation("one", "subject")));
        foreach (var invalid in new[] { Run("failed", 2) with { Succeeded = false },
            Run("partial", 2) with { CompletePopulation = false } })
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ObserveCompletedRunAsync(invalid));
        await repo.ObserveCompletedRunAsync(Run("old", 0));
        await Repository().ObserveCompletedRunAsync(Run("old", 0));
        (await Repository().GetByIdAsync("one"))!.Lineage!.ClearedAt.Should().BeNull();
    }

    [Fact]
    public async Task Scope_and_subject_boundaries_never_clear_a_different_population()
    {
        var repo = Repository();
        await Add(repo, "one");
        await repo.ObserveCompletedRunAsync(Run("one", 0, new ReconciliationBreakObservation("one", "subject")));
        var foreignScopes = new[] { _scope with { TenantId = "tenant-b" }, _scope with { CompanyId = "company-b" },
            _scope with { FundProfileId = "fund-b" }, _scope with { FundAccountId = "account-b" },
            _scope with { LedgerBookId = Guid.NewGuid() }, _scope with { AccountingPeriodId = Guid.NewGuid().ToString("D") },
            _scope with { ExternalAccountId = "other" }, _scope with { SourceSystem = "other" } };
        foreach (var scope in foreignScopes)
            await repo.ObserveCompletedRunAsync(Run("other", 1) with { Scope = scope });
        (await repo.GetByIdAsync("one"))!.Lineage!.ClearedAt.Should().BeNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ObserveCompletedRunAsync(
            Run("spoof", 2, new ReconciliationBreakObservation("one", "subject")) with { Scope = _scope with { TenantId = "tenant-b" } }));
    }

    [Fact]
    public async Task Replay_is_idempotent_and_changed_replay_is_rejected()
    {
        var repo = Repository();
        await Add(repo, "one");
        var run = Run("one", 0, new ReconciliationBreakObservation("one", "subject"));
        await repo.ObserveCompletedRunAsync(run);
        var before = await repo.GetByIdAsync("one");
        await Repository().ObserveCompletedRunAsync(run);
        (await Repository().GetByIdAsync("one")).Should().BeEquivalentTo(before);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository().ObserveCompletedRunAsync(run with { Breaks = [] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository().ObserveCompletedRunAsync(
            run with { Scope = _scope with { ExternalAccountId = "different-source-account" } }));
    }

    [Fact]
    public async Task Snapshot_write_failure_preserves_prior_observation_and_can_retry_after_restart()
    {
        var repo = Repository();
        await Add(repo, "one");
        await repo.ObserveCompletedRunAsync(Run("one", 0, new ReconciliationBreakObservation("one", "subject")));
        var failing = new FileReconciliationBreakQueueRepository(_root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance,
            stateWriter: (_, _, _) => throw new IOException("injected"));
        await Assert.ThrowsAsync<IOException>(() => failing.ObserveCompletedRunAsync(Run("matched", 1)));
        (await Repository().GetByIdAsync("one"))!.Lineage!.ClearedAt.Should().BeNull();
        await Repository().ObserveCompletedRunAsync(Run("matched", 1));
        (await Repository().GetByIdAsync("one"))!.Lineage!.ClearedAt.Should().NotBeNull();
    }

    private ReconciliationCompletedRunObservation Run(string id, int day, params ReconciliationBreakObservation[] breaks)
        => new(id, _scope, _start.AddDays(day), true, true, breaks);

    private Task<bool> Add(FileReconciliationBreakQueueRepository repo, string id, decimal amount = 1m)
        => repo.CreateIfMissingAsync(new ReconciliationBreakQueueItem(id, id, "Statement reconciliation",
            ReconciliationBreakCategory.CashMismatch, ReconciliationBreakQueueStatus.Open, amount,
            "Variance", null, _start, _start, FundAccountId: _scope.FundAccountId,
            SourceType: "statement", SourceSystem: "statement-reconciliation", SourceImportId: id,
            LedgerBookId: _scope.LedgerBookId, AccountingPeriodId: _scope.AccountingPeriodId,
            AsOfDate: new DateOnly(2026, 9, 30), BlockedOutputs: ["PeriodClose"])
        { TenantId = _scope.TenantId, CompanyId = _scope.CompanyId, FundProfileId = _scope.FundProfileId });

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
