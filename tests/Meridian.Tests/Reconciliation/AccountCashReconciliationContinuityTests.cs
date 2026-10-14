using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Reconciliation;

public sealed class AccountCashReconciliationContinuityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-account-cash-{Guid.NewGuid():N}");
    private readonly AccountCashReconciliationScope _scope = new(
        "tenant-a", "company-a", "account-a", Guid.Parse("d403d4cf-8248-439a-85e7-bb7865b5490a"));
    private readonly DateTimeOffset _start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Restart_and_source_correction_update_same_case_with_owner_aging_and_original_evidence()
    {
        var service = await ServiceAsync();
        var request = Request("first", 0, 1000m, 990m);
        var firstRun = await service.RunAsync(request);
        var firstCase = Assert.Single(await service.GetCasesAsync(_scope));
        firstCase.State.Should().Be("Open");
        var assigned = await service.AssignAsync(_scope, firstCase.CaseId, firstCase.Version,
            "cash-ops", "supervisor", "Investigate custodian balance", _start.AddMinutes(30));

        service = await ServiceAsync();
        var correction = Request("corrected", 3, 1000m, 1000m) with { InitialOwner = "different-initial-owner" };
        var correctedRun = await service.RunAsync(correction);
        var retained = Assert.Single(await (await ServiceAsync()).GetCasesAsync(_scope));

        retained.CaseId.Should().Be(firstCase.CaseId);
        retained.Owner.Should().Be("cash-ops");
        retained.Assignments.Should().BeEquivalentTo(assigned.Assignments);
        retained.FirstObservedAtUtc.Should().Be(_start);
        retained.LastObservedAtUtc.Should().Be(_start.AddDays(3));
        retained.AgingDays.Should().Be(3);
        retained.State.Should().Be("SourceCleared");
        retained.ResolvedAtUtc.Should().Be(_start.AddDays(3));
        retained.ResolutionRunId.Should().Be("corrected");
        retained.History.Select(item => item.RunId).Should().Equal("first", "corrected");
        retained.History.Select(item => item.ResultHash).Should().Equal(firstRun.ResultHash, correctedRun.ResultHash);

        var replay = await service.ReplayAsync(_scope, "first");
        replay.Input.Left.Should().BeEquivalentTo(request.Left);
        replay.Input.Right.Should().BeEquivalentTo(request.Right);
        replay.Input.Tolerance.Should().BeEquivalentTo(request.Tolerance);
        replay.Should().BeEquivalentTo(firstRun, options => options.WithStrictOrdering());
        Assert.Single(replay.Input.Right.Rows).OriginalPayload.Should().Contain("990");
    }

    [Fact]
    public async Task Partial_correction_then_tolerance_match_preserve_case_and_observation_history()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var original = Assert.Single(await service.GetCasesAsync(_scope));

        await (await ServiceAsync()).RunAsync(Request("partial-correction", 2, 1000m, 995m));
        var partial = Assert.Single(await service.GetCasesAsync(_scope));
        partial.CaseId.Should().Be(original.CaseId);
        partial.State.Should().Be("Open");
        partial.ResolvedAtUtc.Should().BeNull();
        partial.ResolutionRunId.Should().BeNull();
        partial.AgingDays.Should().Be(2);

        var clearedRun = await service.RunAsync(Request("within-tolerance", 5, 1000m, 999.995m));
        var cleared = Assert.Single(await service.GetCasesAsync(_scope));
        cleared.CaseId.Should().Be(original.CaseId);
        cleared.History.Select(item => item.State).Should().Equal("Open", "Open", "SourceCleared");
        cleared.FirstObservedAtUtc.Should().Be(_start);
        cleared.AgingDays.Should().Be(5);
        Assert.Single(Assert.Single(clearedRun.Comparisons).Matches).Classification
            .Should().Be(BreakClassification.MatchedWithinTolerance);
    }

    [Fact]
    public async Task Replay_uses_retained_tolerance_and_source_rows_after_caller_mutation_and_later_policy_change()
    {
        var service = await ServiceAsync();
        var rules = new List<CashToleranceRule> { new("cash-rule-v7", 0.01m, null, TimeSpan.FromHours(1)) };
        var strict = new StatementToleranceProfile("account-close", 7, rules, [], []);
        var request = Request("strict", 0, 1000m, 1000.10m) with { Tolerance = strict };
        var sourceRows = request.Right.Rows.ToList();
        request = request with { Right = request.Right with { Rows = sourceRows } };
        var original = await service.RunAsync(request);
        var initialCase = Assert.Single(await service.GetCasesAsync(_scope));

        rules[0] = new("cash-rule-v7", 100m, null, TimeSpan.FromDays(1));
        sourceRows[0] = sourceRows[0] with { Row = sourceRows[0].Row with { Amount = 1000m, AmountBase = 1000m } };
        var loose = Profile(8, 0.5m);
        await service.RunAsync(Request("looser-policy", 1, 1000m, 1000.10m) with { Tolerance = loose });
        var cleared = Assert.Single(await service.GetCasesAsync(_scope));
        cleared.CaseId.Should().Be(initialCase.CaseId);
        cleared.State.Should().Be("SourceCleared");

        var replay = await (await ServiceAsync()).ReplayAsync(_scope, "strict");
        replay.Should().BeEquivalentTo(original, options => options.WithStrictOrdering());
        replay.Input.Tolerance.Version.Should().Be(7);
        Assert.Single(replay.Input.Tolerance.CashRules).AbsoluteCashTolerance.Should().Be(0.01m);
        Assert.Single(replay.Input.Right.Rows).Row.Amount.Should().Be(1000.10m);
        Assert.Single(replay.Comparisons).ObservationState.Should().Be("Open");
        Assert.Single(await service.GetCasesAsync(_scope)).History.Should().HaveCount(2);
    }

    [Fact]
    public async Task Foreign_account_rows_are_rejected_and_account_histories_never_match_or_clear_each_other()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("account-a-run", 0, 1000m, 990m));
        var first = Assert.Single(await service.GetCasesAsync(_scope));
        var foreign = _scope with { AccountId = "account-b" };

        await service.RunAsync(Request("account-b-clear", 1, 1000m, 1000m, foreign));
        (await service.GetCasesAsync(foreign)).Should().BeEmpty();
        (await service.GetCasesAsync(_scope)).Should().ContainSingle().Which.Should().BeEquivalentTo(first);
        await service.RunAsync(Request("account-b-break", 2, 1000m, 990m, foreign));
        var foreignCase = Assert.Single(await service.GetCasesAsync(foreign));
        foreignCase.CaseId.Should().NotBe(first.CaseId);
        foreignCase.Scope.Should().Be(foreign);

        var spoofed = Request("spoofed", 3, 1000m, 1000m) with { Scope = foreign };
        await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(spoofed));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReplayAsync(foreign, "account-a-run"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AssignAsync(foreign, first.CaseId, first.Version,
            "other-owner", "supervisor", "Foreign scope", _start.AddDays(3)));
        (await (await ServiceAsync()).GetCasesAsync(_scope)).Should().ContainSingle().Which.Should().BeEquivalentTo(first);
    }

    [Theory]
    [InlineData("incomplete-left")]
    [InlineData("incomplete-right")]
    [InlineData("duplicate-left")]
    [InlineData("duplicate-right")]
    public async Task Incomplete_or_duplicated_populations_cannot_clear_an_existing_case(string scenario)
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var before = Assert.Single(await service.GetCasesAsync(_scope));
        var invalid = Request("invalid", 1, 1000m, 1000m);
        invalid = scenario switch
        {
            "incomplete-left" => invalid with { Left = invalid.Left with { CompletePopulation = false } },
            "incomplete-right" => invalid with { Right = invalid.Right with { CompletePopulation = false } },
            "duplicate-left" => invalid with { Left = invalid.Left with { Rows = [invalid.Left.Rows[0], invalid.Left.Rows[0]] } },
            _ => invalid with { Right = invalid.Right with { Rows = [invalid.Right.Rows[0], invalid.Right.Rows[0]] } }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(invalid));
        Assert.Single(await (await ServiceAsync()).GetCasesAsync(_scope)).Should().BeEquivalentTo(before);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReplayAsync(_scope, "invalid"));
    }

    [Fact]
    public async Task Missing_balances_remain_visible_and_empty_populations_never_clear_a_prior_case()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var original = Assert.Single(await service.GetCasesAsync(_scope));
        var missing = Request("missing-left", 1, 1000m, 1000m);
        missing = missing with { Left = missing.Left with { Rows = [] } };
        var missingRun = await service.RunAsync(missing);
        Assert.Single(missingRun.Comparisons).ObservationState.Should().Be("MissingEvidence");
        var bothMissing = Request("both-missing", 2, 1000m, 1000m);
        bothMissing = bothMissing with { Left = bothMissing.Left with { Rows = [] }, Right = bothMissing.Right with { Rows = [] } };
        var emptyRun = await (await ServiceAsync()).RunAsync(bothMissing);

        Assert.Single(emptyRun.Comparisons).ObservationState.Should().Be("MissingEvidence");
        var retained = Assert.Single(await service.GetCasesAsync(_scope));
        retained.CaseId.Should().Be(original.CaseId);
        retained.State.Should().Be("MissingEvidence");
        retained.ResolvedAtUtc.Should().BeNull();
        retained.ResolutionRunId.Should().BeNull();
        retained.History.Select(item => item.State).Should().Equal("Open", "MissingEvidence", "MissingEvidence");
    }

    [Fact]
    public async Task Run_identity_is_immutable_and_exact_retry_is_idempotent_after_restart()
    {
        var service = await ServiceAsync();
        var request = Request("immutable-run", 0, 1000m, 990m);
        var firstRun = await service.RunAsync(request);
        var firstCase = Assert.Single(await service.GetCasesAsync(_scope));
        service = await ServiceAsync();

        (await service.RunAsync(request)).Should().BeEquivalentTo(firstRun, options => options.WithStrictOrdering());
        Assert.Single(await service.GetCasesAsync(_scope)).Should().BeEquivalentTo(firstCase);
        var correctedRows = request.Right.Rows.Select(item => item with
        {
            Row = item.Row with { Amount = 1000m, AmountBase = 1000m }
        }).ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(request with
        {
            Right = request.Right with { Rows = correctedRows }
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(request with { Tolerance = Profile(2, 20m) }));
        Assert.Single(await (await ServiceAsync()).GetCasesAsync(_scope)).Should().BeEquivalentTo(firstCase);
    }

    [Fact]
    public async Task Recurrence_reopens_same_case_and_preserves_prior_resolution_evidence_and_owner()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var original = Assert.Single(await service.GetCasesAsync(_scope));
        await service.AssignAsync(_scope, original.CaseId, original.Version, "cash-ops", "supervisor", "Own recurrence", _start);
        var clearedRun = await service.RunAsync(Request("cleared", 1, 1000m, 1000m));
        await (await ServiceAsync()).RunAsync(Request("recurring", 4, 1000m, 995m));
        var recurrence = Assert.Single(await service.GetCasesAsync(_scope));

        recurrence.CaseId.Should().Be(original.CaseId);
        recurrence.Owner.Should().Be("cash-ops");
        recurrence.FirstObservedAtUtc.Should().Be(_start);
        recurrence.AgingDays.Should().Be(4);
        recurrence.State.Should().Be("Open");
        recurrence.ResolvedAtUtc.Should().BeNull();
        recurrence.ResolutionRunId.Should().BeNull();
        recurrence.History.Select(item => item.State).Should().Equal("Open", "SourceCleared", "Open");
        recurrence.History[1].ResultHash.Should().Be(clearedRun.ResultHash);
        (await service.ReplayAsync(_scope, "cleared")).Should().BeEquivalentTo(clearedRun, options => options.WithStrictOrdering());

        await service.RunAsync(Request("cleared-again", 5, 1000m, 1000m));
        var clearedAgain = Assert.Single(await service.GetCasesAsync(_scope));
        clearedAgain.CaseId.Should().Be(original.CaseId);
        clearedAgain.ResolvedAtUtc.Should().Be(_start.AddDays(5));
        clearedAgain.ResolutionRunId.Should().Be("cleared-again");
    }

    [Fact]
    public async Task Source_and_row_order_do_not_change_retained_evidence_or_case_identity()
    {
        var service = await ServiceAsync();
        var first = Request("first", 0, 1000m, 990m);
        first = first with
        {
            Left = Source("accounting-ledger", ReconciliationSourceType.InternalLedger, _scope, _start, ("USD", 1000m), ("EUR", 2000m)),
            Right = Source("custodian", ReconciliationSourceType.Custodian, _scope, _start, ("EUR", 1990m), ("USD", 990m))
        };
        var originalRun = await service.RunAsync(first);
        var originalIds = (await service.GetCasesAsync(_scope)).Select(item => item.CaseId).Order(StringComparer.Ordinal).ToArray();
        originalIds.Should().HaveCount(2);
        var reordered = first with
        {
            Left = first.Right with { Rows = first.Right.Rows.Reverse().ToArray() },
            Right = first.Left with { Rows = first.Left.Rows.Reverse().ToArray() }
        };

        (await (await ServiceAsync()).RunAsync(reordered)).Should().BeEquivalentTo(originalRun, options => options.WithStrictOrdering());
        var next = Request("next", 1, 1000m, 990m);
        next = next with
        {
            Left = Source("custodian", ReconciliationSourceType.Custodian, _scope, _start.AddDays(1), ("USD", 990m), ("EUR", 1990m)),
            Right = Source("accounting-ledger", ReconciliationSourceType.InternalLedger, _scope, _start.AddDays(1), ("EUR", 2000m), ("USD", 1000m))
        };
        await service.RunAsync(next);
        var retained = await service.GetCasesAsync(_scope);
        retained.Select(item => item.CaseId).Order(StringComparer.Ordinal).Should().Equal(originalIds);
        retained.Should().OnlyContain(item => item.History.Count == 2 && item.FirstObservedAtUtc == _start);
    }

    [Fact]
    public async Task Retained_source_identity_cannot_be_rebound_to_another_type()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var before = Assert.Single(await service.GetCasesAsync(_scope));
        var rebound = Request("rebound", 1, 1000m, 1000m);
        rebound = rebound with { Right = rebound.Right with { SourceType = ReconciliationSourceType.Prime } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(rebound));
        Assert.Single(await (await ServiceAsync()).GetCasesAsync(_scope)).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData("shared-reference")]
    [InlineData("padded-reference")]
    [InlineData("copied-payload")]
    [InlineData("copied-source")]
    public async Task Relabeled_or_copied_evidence_cannot_clear_or_mutate_an_existing_case(string scenario)
    {
        var service = await ServiceAsync();
        var original = await service.RunAsync(Request("first", 0, 1000m, 990m));
        var before = Assert.Single(await service.GetCasesAsync(_scope));
        var statePath = Path.Combine(_root, "reconciliation", "account-cash-continuity", "state.json");
        var bytes = await File.ReadAllBytesAsync(statePath);
        var invalid = Request("copied", 1, 1000m, 1000m);
        invalid = scenario switch
        {
            "shared-reference" => invalid with { Right = invalid.Right with { EvidenceReference = invalid.Left.EvidenceReference } },
            "padded-reference" => invalid with { Right = invalid.Right with { EvidenceReference = " " + invalid.Left.EvidenceReference + " " } },
            "copied-payload" => invalid with
            {
                Right = invalid.Right with { Rows = [invalid.Right.Rows[0] with { OriginalPayload = " " + invalid.Left.Rows[0].OriginalPayload + " " }] }
            },
            _ => invalid with { Right = invalid.Left with { SourceId = invalid.Right.SourceId, SourceType = invalid.Right.SourceType } }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(invalid));
        (await File.ReadAllBytesAsync(statePath)).Should().Equal(bytes);
        service = await ServiceAsync();
        Assert.Single(await service.GetCasesAsync(_scope)).Should().BeEquivalentTo(before);
        (await service.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(original, options => options.WithStrictOrdering());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReplayAsync(_scope, "copied"));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("payload")]
    [InlineData("evidence-reference")]
    [InlineData("row-provenance")]
    [InlineData("capture-time")]
    [InlineData("omitted-row")]
    [InlineData("padded-version")]
    public async Task Retained_source_version_rejects_changed_content_without_mutating_history(string scenario)
    {
        var service = await ServiceAsync();
        var request = Request("first", 0, 1000m, 990m);
        var original = await service.RunAsync(request);
        var before = Assert.Single(await service.GetCasesAsync(_scope));
        var statePath = Path.Combine(_root, "reconciliation", "account-cash-continuity", "state.json");
        var bytes = await File.ReadAllBytesAsync(statePath);
        var source = request.Right;
        var row = source.Rows[0];
        var changed = scenario switch
        {
            "amount" => source with { Rows = [row with { Row = row.Row with { Amount = 1000m, AmountBase = 1000m } }] },
            "payload" => source with { Rows = [row with { OriginalPayload = row.OriginalPayload + " " }] },
            "evidence-reference" => source with { EvidenceReference = "evidence:replacement" },
            "row-provenance" => source with { Rows = [row with { Row = row.Row with { SourceReference = "rebound-row" } }] },
            "capture-time" => source with { CapturedAtUtc = source.CapturedAtUtc.AddMinutes(1) },
            "omitted-row" => source with { Rows = [] },
            _ => source with { Version = " " + source.Version + " ", EvidenceReference = "evidence:replacement" }
        };
        var invalid = request with { RunId = "rebound", ObservedAtUtc = _start.AddHours(1), Right = changed };

        service = await ServiceAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(invalid));
        error.Message.Should().Contain("retained source version");
        (await File.ReadAllBytesAsync(statePath)).Should().Equal(bytes);
        Assert.Single(await service.GetCasesAsync(_scope)).Should().BeEquivalentTo(before);
        (await service.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(original, options => options.WithStrictOrdering());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReplayAsync(_scope, "rebound"));
    }

    [Fact]
    public async Task Source_version_binding_survives_intervening_revision_and_different_comparison_pair()
    {
        var service = await ServiceAsync();
        var first = Request("first", 0, 1000m, 990m);
        await service.RunAsync(first);
        var correction = Request("revision-two", 0, 1000m, 995m) with { ObservedAtUtc = _start.AddHours(1) };
        await service.RunAsync(correction with { Right = correction.Right with { Version = "source-v2" } });
        var before = await service.GetCasesAsync(_scope);
        var rebound = Request("reintroduced", 0, 1000m, 1000m) with { ObservedAtUtc = _start.AddHours(2) };
        rebound = rebound with { Left = rebound.Left with { SourceId = "other-ledger" } };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await (await ServiceAsync()).RunAsync(rebound));
        error.Message.Should().Contain("retained source version");
        (await service.GetCasesAsync(_scope)).Should().BeEquivalentTo(before, options => options.WithStrictOrdering());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReplayAsync(_scope, "reintroduced"));
    }

    [Fact]
    public async Task Independent_equal_balances_and_unchanged_source_revisions_remain_valid()
    {
        var service = await ServiceAsync();
        var request = Request("first", 0, 1000m, 990m);
        await service.RunAsync(request);
        var firstCase = Assert.Single(await service.GetCasesAsync(_scope));
        var sameSources = request with { RunId = "repeat", ObservedAtUtc = _start.AddHours(1), Left = request.Right, Right = request.Left };
        await (await ServiceAsync()).RunAsync(sameSources);

        var corrected = Request("corrected", 0, 1000m, 1000m) with { ObservedAtUtc = _start.AddHours(2) };
        corrected = corrected with { Right = corrected.Right with { Version = "source-v2" } };
        var run = await service.RunAsync(corrected);
        Assert.Single(run.Comparisons).ObservationState.Should().Be("SourceCleared");
        var cleared = Assert.Single(await service.GetCasesAsync(_scope));
        cleared.CaseId.Should().Be(firstCase.CaseId);
        cleared.History.Select(item => item.RunId).Should().Equal("first", "repeat", "corrected");
        (await (await ServiceAsync()).RunAsync(corrected)).Should().BeEquivalentTo(run, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Source_version_bindings_are_isolated_by_exact_account_scope()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("account-a", 0, 1000m, 990m));
        var original = Assert.Single(await service.GetCasesAsync(_scope));
        var foreign = _scope with { AccountId = "account-b" };

        var run = await service.RunAsync(Request("account-b", 0, 2000m, 2000m, foreign));

        Assert.Single(run.Comparisons).ObservationState.Should().Be("SourceCleared");
        (await service.GetCasesAsync(foreign)).Should().BeEmpty();
        Assert.Single(await service.GetCasesAsync(_scope)).Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task Independent_instances_cannot_retain_conflicting_content_for_one_source_revision()
    {
        var first = await ServiceAsync();
        var second = await ServiceAsync();
        var request = Request("first", 0, 1000m, 990m);
        var conflicting = Request("second", 0, 1000m, 980m);
        conflicting = conflicting with { Left = conflicting.Left with { SourceId = "other-ledger" } };

        var results = await Task.WhenAll(Record.ExceptionAsync(() => first.RunAsync(request)),
            Record.ExceptionAsync(() => second.RunAsync(conflicting)));

        results.Should().ContainSingle(result => result == null);
        results.OfType<InvalidOperationException>().Should().ContainSingle().Which.Message.Should().Contain("retained source version");
        var service = await ServiceAsync();
        var retained = Assert.Single(await service.GetCasesAsync(_scope));
        var runId = results[0] is null ? "first" : "second";
        retained.History.Should().ContainSingle().Which.RunId.Should().Be(runId);
        var replay = await service.ReplayAsync(_scope, runId);
        new[] { replay.Input.Left, replay.Input.Right }.Single(source => source.SourceId == "custodian")
            .Rows[0].Row.Amount.Should().Be(results[0] is null ? 990m : 980m);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReplayAsync(_scope, results[0] is null ? "second" : "first"));
    }

    [Fact]
    public async Task Null_cash_tolerance_rule_is_rejected_before_existing_history_changes()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var before = Assert.Single(await service.GetCasesAsync(_scope));
        var invalid = Request("null-rule", 1, 1000m, 1000m);
        invalid = invalid with { Tolerance = invalid.Tolerance with { CashRules = [null!] } };

        await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(invalid));
        Assert.Single(await (await ServiceAsync()).GetCasesAsync(_scope)).Should().BeEquivalentTo(before);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReplayAsync(_scope, "null-rule"));
    }

    [Fact]
    public async Task Independent_instances_reject_stale_owner_assignment_without_overwriting_new_owner()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var original = Assert.Single(await service.GetCasesAsync(_scope));
        var other = await ServiceAsync();
        var assigned = await service.AssignAsync(_scope, original.CaseId, original.Version,
            "cash-ops", "supervisor", "Initial assignment", _start);

        await Assert.ThrowsAsync<InvalidOperationException>(() => other.AssignAsync(_scope, original.CaseId, original.Version,
            "stale-owner", "other-supervisor", "Stale assignment", _start.AddMinutes(1)));
        Assert.Single(await other.GetCasesAsync(_scope)).Should().BeEquivalentTo(assigned);
    }

    [Fact]
    public async Task Corrupt_or_missing_retained_state_fails_closed_instead_of_starting_new_history()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var statePath = Path.Combine(_root, "reconciliation", "account-cash-continuity", "state.json");
        await File.WriteAllTextAsync(statePath, "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetCasesAsync(_scope));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FileAccountCashReconciliationStore(_root).InitializeAsync());
        File.Delete(statePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(Request("new-run", 1, 1000m, 1000m)));
    }

    private async Task<AccountCashReconciliationService> ServiceAsync()
    {
        var store = new FileAccountCashReconciliationStore(_root);
        await store.InitializeAsync();
        return new AccountCashReconciliationService(store);
    }

    private AccountCashReconciliationRequest Request(string runId, int days, decimal left, decimal right,
        AccountCashReconciliationScope? scope = null)
    {
        var accountScope = scope ?? _scope;
        var observed = _start.AddDays(days);
        return new AccountCashReconciliationRequest(runId, accountScope, DateOnly.FromDateTime(observed.UtcDateTime), observed,
            Source("accounting-ledger", ReconciliationSourceType.InternalLedger, accountScope, observed, ("USD", left)),
            Source("custodian", ReconciliationSourceType.Custodian, accountScope, observed, ("USD", right)),
            Profile(1, 0.01m), "initial-owner");
    }

    private static StatementToleranceProfile Profile(int version, decimal absolute)
        => new("account-close", version,
            [new($"cash-rule-v{version}", absolute, null, TimeSpan.FromHours(1))],
            [new("retained-position-rule", 0.01m, 1m, 0.01m)],
            [new("retained-transaction-rule", 0.01m, TimeSpan.FromDays(1), 0.01m)]);

    private static AccountCashBalanceSource Source(string sourceId, ReconciliationSourceType type,
        AccountCashReconciliationScope scope, DateTimeOffset observed, params (string Currency, decimal Amount)[] balances)
    {
        var posted = observed.AddHours(-3);
        var date = DateOnly.FromDateTime(posted.UtcDateTime);
        var rows = balances.Select(balance => new AccountCashBalanceSourceRow(
            new NormalizedCashEntry($"{sourceId}:{balance.Currency}:balance", scope.AccountId, balance.Amount,
                balance.Currency, balance.Amount, balance.Currency, posted, date,
                $"{sourceId}:{date:yyyy-MM-dd}:{balance.Currency}", "settled-balance", "balance-check", "Original balance observation"),
            FormattableString.Invariant($"{{\"source\":\"{sourceId}\",\"account\":\"{scope.AccountId}\",\"currency\":\"{balance.Currency}\",\"settledCash\":{balance.Amount}}}")))
            .ToArray();
        return new(sourceId, type, $"source-v{date:yyyyMMdd}", $"evidence:{sourceId}:{date:yyyy-MM-dd}",
            observed.AddHours(-1), true, rows);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
