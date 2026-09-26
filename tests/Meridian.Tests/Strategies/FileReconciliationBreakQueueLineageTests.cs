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

    [Fact]
    public async Task Policy_changes_preserve_occurrence_but_absence_under_another_policy_cannot_clear()
    {
        var repo = Repository();
        await Add(repo, "one");
        await repo.ObserveCompletedRunAsync(PolicyRun("one", 0, "a", new ReconciliationBreakObservation("one", "cash")));
        var first = (await repo.GetByIdAsync("one"))!.Lineage!;
        await Repository().ObserveCompletedRunAsync(PolicyRun("empty-b", 1, "b"));
        (await Repository().GetByIdAsync("one"))!.Lineage.Should().BeEquivalentTo(first);
        await Add(repo, "two", 20m);
        await Repository().ObserveCompletedRunAsync(PolicyRun("two", 2, "b", new ReconciliationBreakObservation("two", "cash")));
        var current = (await Repository().GetByIdAsync("two"))!.Lineage!;
        current.LineageId.Should().Be(first.LineageId);
        current.OccurrenceId.Should().Be(first.OccurrenceId);
        current.OccurrenceFirstObservedAt.Should().Be(first.OccurrenceFirstObservedAt);
        current.ComparisonScopeId.Should().NotBe(first.ComparisonScopeId);
        await Repository().ObserveCompletedRunAsync(PolicyRun("clear-b", 3, "b"));
        var cleared = (await Repository().GetByIdAsync("one"))!;
        cleared.Lineage!.ClearedByRunId.Should().Be("clear-b");
        cleared.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
        cleared.BlockedOutputs.Should().Contain("PeriodClose");
        await Add(repo, "three");
        await Repository().ObserveCompletedRunAsync(PolicyRun("three", 4, "c", new ReconciliationBreakObservation("three", "cash")));
        var recurring = (await Repository().GetByIdAsync("three"))!.Lineage!;
        recurring.LineageId.Should().Be(first.LineageId);
        recurring.OccurrenceId.Should().NotBe(first.OccurrenceId);
        recurring.OccurrenceNumber.Should().Be(2);
        recurring.OccurrenceFirstObservedAt.Should().Be(_start.AddDays(4));
        (await Repository().GetByIdAsync("one"))!.Lineage.Should().BeEquivalentTo(cleared.Lineage);
    }

    [Fact]
    public async Task Policy_changes_cannot_rebind_a_completed_run_or_replace_a_newer_head()
    {
        var repo = Repository();
        await Add(repo, "newer");
        await repo.ObserveCompletedRunAsync(PolicyRun("newer", 3, "b", new ReconciliationBreakObservation("newer", "cash")));
        await Repository().ObserveCompletedRunAsync(PolicyRun("older", 1, "a"));
        (await Repository().GetByIdAsync("newer"))!.Lineage!.ClearedAt.Should().BeNull();
        var original = PolicyRun("empty", 4, "b");
        await Repository().ObserveCompletedRunAsync(original);
        await Repository().ObserveCompletedRunAsync(original);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository().ObserveCompletedRunAsync(PolicyRun("empty", 4, "c")));
    }

    [Fact]
    public async Task Legacy_identity_is_preserved_and_policy_forks_are_not_silently_merged()
    {
        var repo = Repository();
        await Add(repo, "legacy");
        var legacy = Run("legacy", 0, new ReconciliationBreakObservation("legacy", "cash")) with
        { Scope = _scope with { SourceSystem = LegacySource("a") } };
        await repo.ObserveCompletedRunAsync(legacy);
        var original = (await repo.GetByIdAsync("legacy"))!.Lineage!;
        original.IdentityScopeId.Should().BeNull();
        await Add(repo, "current");
        await Repository().ObserveCompletedRunAsync(PolicyRun("current", 2, "b", new ReconciliationBreakObservation("current", "cash")));
        var current = (await Repository().GetByIdAsync("current"))!.Lineage!;
        current.LineageId.Should().Be(original.LineageId);
        current.OccurrenceId.Should().Be(original.OccurrenceId);
        await Repository().ObserveCompletedRunAsync(legacy);
        await Add(repo, "fork");
        await Repository().ObserveCompletedRunAsync(Run("fork", 1, new ReconciliationBreakObservation("fork", "cash")) with
        { Scope = _scope with { SourceSystem = LegacySource("c") } });
        await Add(repo, "ambiguous");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository().ObserveCompletedRunAsync(
            PolicyRun("ambiguous", 4, "d", new ReconciliationBreakObservation("ambiguous", "cash"))));
    }

    [Fact]
    public async Task Legacy_upgrade_replay_accepts_only_the_original_payload()
    {
        var original = Run("legacy", 1) with { Scope = _scope with { SourceSystem = LegacySource("a") } };
        await Repository().ObserveCompletedRunAsync(original);
        var upgraded = original with { Scope = original.Scope with { LineageSourceIdentity = "custodian" } };
        await Repository().ObserveCompletedRunAsync(upgraded);
        foreach (var changed in new[] { upgraded with { ObservedAt = _start.AddDays(2) },
            upgraded with { Scope = upgraded.Scope with { SourceSystem = LegacySource("b") } },
            upgraded with { Breaks = [new ReconciliationBreakObservation("unexpected", "cash")] } })
            await Assert.ThrowsAsync<InvalidOperationException>(() => Repository().ObserveCompletedRunAsync(changed));
    }

    [Fact]
    public async Task Legacy_publication_sequence_cannot_clear_a_later_observation()
    {
        var repo = Repository();
        await Add(repo, "latest");
        await repo.ObserveCompletedRunAsync(Run("latest", 5, new ReconciliationBreakObservation("latest", "cash")) with
        { Scope = _scope with { SourceSystem = LegacySource("a") } });
        await repo.ObserveCompletedRunAsync(Run("older", 2) with { Scope = _scope with { SourceSystem = LegacySource("b") } });
        await repo.ObserveCompletedRunAsync(Run("middle", 3) with
        { Scope = _scope with { SourceSystem = LegacySource("a"), LineageSourceIdentity = "custodian" } });
        (await Repository().GetByIdAsync("latest"))!.Lineage!.ClearedAt.Should().BeNull();
    }

    [Fact]
    public async Task Different_institution_cannot_inherit_or_clear_another_occurrence()
    {
        var repo = Repository();
        await Add(repo, "one");
        await repo.ObserveCompletedRunAsync(PolicyRun("one", 0, "a", new ReconciliationBreakObservation("one", "cash")));
        var first = (await repo.GetByIdAsync("one"))!.Lineage!;
        await Add(repo, "two");
        var foreign = PolicyRun("two", 1, "a", new ReconciliationBreakObservation("two", "cash"));
        await repo.ObserveCompletedRunAsync(foreign with { Scope = foreign.Scope with { LineageSourceIdentity = "other" } });
        (await repo.GetByIdAsync("two"))!.Lineage!.LineageId.Should().NotBe(first.LineageId);
        (await repo.GetByIdAsync("one"))!.Lineage.Should().BeEquivalentTo(first);
    }

    private static string LegacySource(string policy) => System.Text.Json.JsonSerializer.Serialize(new
    {
        institution = "custodian", MappingProfileId = policy, ToleranceProfileId = policy,
        SourceComparisonPolicyFingerprint = new string(policy[0], 64), SourceComparisonPopulationKinds = new[] { "cash" }
    });

    private ReconciliationCompletedRunObservation PolicyRun(string id, int day, string policy, params ReconciliationBreakObservation[] breaks)
        => Run(id, day, breaks) with { Scope = _scope with { SourceSystem = policy, LineageSourceIdentity = "custodian" } };

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
