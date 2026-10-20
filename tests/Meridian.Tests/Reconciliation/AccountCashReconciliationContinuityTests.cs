using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrity;
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

    [Theory]
    [InlineData("cash-threshold")]
    [InlineData("cash-basis-points")]
    [InlineData("cash-settlement-window")]
    [InlineData("cash-rule-order")]
    [InlineData("position-rules")]
    [InlineData("transaction-rules")]
    public async Task Retained_tolerance_version_rejects_changed_rules_without_mutating_history(string scenario)
    {
        var service = await ServiceAsync();
        var request = Request("first", 0, 1000m, 990m);
        request = request with
        {
            Tolerance = request.Tolerance with
            {
                CashRules = [request.Tolerance.CashRules[0], new("second-rule", 0.02m, null, TimeSpan.FromHours(2))]
            }
        };
        var original = await service.RunAsync(request);
        var before = Assert.Single(await service.GetCasesAsync(_scope));
        var statePath = ScopeStatePath(_scope);
        var bytes = await File.ReadAllBytesAsync(statePath);
        var tolerance = request.Tolerance;
        var rule = tolerance.CashRules[0];
        var changed = scenario switch
        {
            "cash-threshold" => tolerance with { CashRules = [rule with { AbsoluteCashTolerance = 20m }, tolerance.CashRules[1]] },
            "cash-basis-points" => tolerance with { CashRules = [rule with { BasisPointCashTolerance = 200m }, tolerance.CashRules[1]] },
            "cash-settlement-window" => tolerance with { CashRules = [rule with { SettlementDateTolerance = TimeSpan.FromDays(1) }, tolerance.CashRules[1]] },
            "cash-rule-order" => tolerance with { CashRules = tolerance.CashRules.Reverse().ToArray() },
            "position-rules" => tolerance with { PositionRules = [new("changed-position", 1m, 1m, 1m)] },
            _ => tolerance with { TransactionRules = [new("changed-transaction", 1m, TimeSpan.FromDays(2), 1m)] }
        };
        var invalid = request with { RunId = "rebound-policy", ObservedAtUtc = _start.AddHours(1), Tolerance = changed };

        service = await ServiceAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(invalid));
        error.Message.Should().Contain("retained tolerance version");
        (await File.ReadAllBytesAsync(statePath)).Should().Equal(bytes);
        Assert.Single(await (await ServiceAsync()).GetCasesAsync(_scope)).Should().BeEquivalentTo(before);
        (await service.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(original, options => options.WithStrictOrdering());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReplayAsync(_scope, "rebound-policy"));
    }

    [Fact]
    public async Task Unchanged_tolerance_reuse_and_explicit_new_version_preserve_case_and_replay()
    {
        var service = await ServiceAsync();
        var request = Request("first", 0, 1000m, 990m);
        var original = await service.RunAsync(request);
        var originalCase = Assert.Single(await service.GetCasesAsync(_scope));
        await (await ServiceAsync()).RunAsync(request with { RunId = "unchanged-policy", ObservedAtUtc = _start.AddHours(1) });
        var revised = request with { RunId = "new-policy-version", ObservedAtUtc = _start.AddHours(2), Tolerance = Profile(2, 20m) };

        var result = await service.RunAsync(revised);

        Assert.Single(result.Comparisons).ObservationState.Should().Be("SourceCleared");
        var retained = Assert.Single(await (await ServiceAsync()).GetCasesAsync(_scope));
        retained.CaseId.Should().Be(originalCase.CaseId);
        retained.History.Select(item => item.RunId).Should().Equal("first", "unchanged-policy", "new-policy-version");
        (await service.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(original, options => options.WithStrictOrdering());
        (await service.RunAsync(revised)).Should().BeEquivalentTo(result, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Tolerance_binding_survives_intervening_version_and_different_source_pair()
    {
        var service = await ServiceAsync();
        var request = Request("first", 0, 1000m, 990m);
        await service.RunAsync(request);
        await service.RunAsync(request with { RunId = "version-two", ObservedAtUtc = _start.AddHours(1), Tolerance = Profile(2, 0.005m) });
        var before = await service.GetCasesAsync(_scope);
        var statePath = ScopeStatePath(_scope);
        var bytes = await File.ReadAllBytesAsync(statePath);
        var rebound = request with
        {
            RunId = "reintroduced-policy",
            ObservedAtUtc = _start.AddHours(2),
            Left = Source("other-ledger", ReconciliationSourceType.InternalLedger, _scope, _start, ("USD", 1000m)),
            Tolerance = Profile(1, 20m)
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await (await ServiceAsync()).RunAsync(rebound));

        error.Message.Should().Contain("retained tolerance version");
        (await File.ReadAllBytesAsync(statePath)).Should().Equal(bytes);
        (await service.GetCasesAsync(_scope)).Should().BeEquivalentTo(before, options => options.WithStrictOrdering());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReplayAsync(_scope, "reintroduced-policy"));
    }

    [Fact]
    public async Task Tolerance_version_bindings_are_isolated_by_exact_account_scope()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("account-a", 0, 1000m, 990m));
        var original = Assert.Single(await service.GetCasesAsync(_scope));
        var foreign = _scope with { AccountId = "account-b" };

        var result = await (await ServiceAsync()).RunAsync(Request("account-b", 0, 1000m, 990m, foreign) with { Tolerance = Profile(1, 20m) });

        Assert.Single(result.Comparisons).ObservationState.Should().Be("SourceCleared");
        (await service.GetCasesAsync(foreign)).Should().BeEmpty();
        Assert.Single(await service.GetCasesAsync(_scope)).Should().BeEquivalentTo(original);
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
        var statePath = ScopeStatePath(_scope);
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
        var statePath = ScopeStatePath(_scope);
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

    [Theory]
    [InlineData("before-first-observation")]
    [InlineData("before-latest-observation")]
    [InlineData("before-latest-assignment")]
    public async Task Backdated_assignment_cannot_change_owner_or_retained_history(string scenario)
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        await service.RunAsync(Request("latest", 1, 1000m, 990m));
        var before = Assert.Single(await service.GetCasesAsync(_scope));
        if (scenario == "before-latest-assignment")
            before = await service.AssignAsync(_scope, before.CaseId, before.Version,
                "cash-ops", "supervisor", "Current assignment", _start.AddDays(1).AddHours(1));
        var invalidAt = scenario switch
        {
            "before-first-observation" => _start.AddMinutes(-1),
            "before-latest-observation" => _start.AddDays(1).AddMinutes(-1),
            _ => _start.AddDays(1).AddMinutes(30)
        };
        var statePath = ScopeStatePath(_scope);
        var bytes = await File.ReadAllBytesAsync(statePath);
        service = await ServiceAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignAsync(_scope, before.CaseId, before.Version,
            "backdated-owner", "other-supervisor", "Backdated assignment", invalidAt));

        error.Message.Should().Contain("cannot precede");
        (await File.ReadAllBytesAsync(statePath)).Should().Equal(bytes);
        Assert.Single(await (await ServiceAsync()).GetCasesAsync(_scope)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Assignment_may_equal_latest_observation_and_previous_assignment_instant()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        await service.RunAsync(Request("latest", 1, 1000m, 990m));
        var original = Assert.Single(await service.GetCasesAsync(_scope));
        var assigned = await service.AssignAsync(_scope, original.CaseId, original.Version,
            "cash-ops", "supervisor", "First assignment", _start.AddDays(1));

        var reassigned = await (await ServiceAsync()).AssignAsync(_scope, assigned.CaseId, assigned.Version,
            "cash-supervision", "other-supervisor", "Same-instant handoff", _start.AddDays(1).ToOffset(TimeSpan.FromHours(2)));

        reassigned.Owner.Should().Be("cash-supervision");
        reassigned.Version.Should().Be(original.Version + 2);
        reassigned.Assignments.Should().HaveCount(2).And.OnlyContain(item => item.AssignedAtUtc == _start.AddDays(1));
        Assert.Single(await service.GetCasesAsync(_scope)).Should().BeEquivalentTo(reassigned);
    }

    [Fact]
    public async Task Corrupt_or_missing_retained_state_fails_closed_instead_of_starting_new_history()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var statePath = ScopeStatePath(_scope);
        await File.WriteAllTextAsync(statePath, "{}");

        // Corrupt or missing retained state is a restore-from-backup condition (503), not a command conflict.
        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetCasesAsync(_scope));
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileAccountCashReconciliationStore(_root).InitializeAsync());
        File.Delete(statePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(Request("new-run", 1, 1000m, 1000m)));
    }

    [Fact]
    public async Task Interrupted_provisioning_adopts_only_an_empty_unmarked_state()
    {
        var directory = Path.Combine(_root, "reconciliation", "account-cash-continuity");
        var markerPath = Path.Combine(directory, "initialized");
        var service = await ServiceAsync();

        // Simulate a crash between writing the empty state and its marker: provisioning recovers.
        File.Delete(markerPath);
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        File.Exists(markerPath).Should().BeTrue();
        (await service.GetCasesAsync(_scope)).Should().BeEmpty();

        // Retained history without its marker is never adopted by provisioning.
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        File.Delete(markerPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FileAccountCashReconciliationStore(_root).InitializeAsync());
        File.Exists(markerPath).Should().BeFalse();
    }

    [Fact]
    public async Task Reformatted_copied_json_observation_is_rejected_as_dependent_evidence()
        => await Copied_json_observation_is_rejected_as_dependent_evidence("whitespace");

    [Theory]
    [InlineData("property-order")]
    [InlineData("nested-property-order")]
    public async Task Reordered_copied_json_observation_is_rejected_as_dependent_evidence(string shape)
        => await Copied_json_observation_is_rejected_as_dependent_evidence(shape);

    [Theory]
    [InlineData("number-exponent")]
    [InlineData("number-fraction")]
    [InlineData("root-scalar")]
    [InlineData("wide-number")]
    [InlineData("wide-exponent")]
    [InlineData("zero-padded-exponent")]
    [InlineData("string-escape")]
    public async Task Respelled_number_in_copied_json_observation_is_rejected_as_dependent_evidence(string shape)
        => await Copied_json_observation_is_rejected_as_dependent_evidence(shape);

    [Theory]
    [InlineData("1e30", "1000000000000000000000000000001")]
    [InlineData("1e2147483648", "1e2147483647")]
    [InlineData("1e-2147483649", "2e-2147483649")]
    public async Task Distinct_wide_numbers_beyond_decimal_range_are_independent_observations(string left, string right)
    {
        var service = await ServiceAsync();
        var request = Request("first", 0, 1000m, 1000m);
        request = request with
        {
            Left = request.Left with { Rows = [request.Left.Rows[0] with { OriginalPayload = left }] },
            Right = request.Right with { Rows = [request.Right.Rows[0] with { OriginalPayload = right }] }
        };

        var run = await service.RunAsync(request);

        run.Input.RunId.Should().Be("first");
    }

    [Theory]
    [InlineData("""{"amount":0,"amount":100}""", "duplicate JSON property names")]
    [InlineData("1e1000000000000000000", "exponent wider than 18 digits")]
    public async Task Ambiguous_or_unbounded_json_observation_is_refused_as_input(string payload, string rejection)
    {
        var service = await ServiceAsync();
        var request = Request("first", 0, 1000m, 1000m);
        request = request with { Left = request.Left with { Rows = [request.Left.Rows[0] with { OriginalPayload = payload }] } };

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(request));

        failure.Message.Should().Contain(rejection);
        (await service.GetCasesAsync(_scope)).Should().BeEmpty();
    }

    [Fact]
    public async Task Over_depth_json_observation_is_refused_instead_of_compared_as_text()
    {
        var service = await ServiceAsync();
        var deep = new string('[', 65) + "1000" + new string(']', 65);
        var request = Request("first", 0, 1000m, 1000m);
        request = request with
        {
            Left = request.Left with { Rows = [request.Left.Rows[0] with { OriginalPayload = deep }] },
            Right = request.Right with { Rows = [request.Right.Rows[0] with { OriginalPayload = deep.Replace("1000", " 1e3 ") }] }
        };

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(request));

        failure.Message.Should().Contain("nested deeper than 64");
        (await service.GetCasesAsync(_scope)).Should().BeEmpty();
    }

    private async Task Copied_json_observation_is_rejected_as_dependent_evidence(string shape)
    {
        var service = await ServiceAsync();
        var request = Request("first", 0, 1000m, 1000m);
        var original = request.Left.Rows[0].OriginalPayload;
        string reformatted;
        switch (shape)
        {
            case "whitespace":
                reformatted = original.Replace(",", ",\n  ").Replace(":", ": ");
                break;
            case "property-order":
                reformatted = ReverseProperties(original);
                break;
            case "number-exponent":
                original = """{"balance":{"amount":1000,"currency":"USD"},"source":"ledger"}""";
                reformatted = """{"balance":{"amount":1e3,"currency":"USD"},"source":"ledger"}""";
                request = request with { Left = request.Left with { Rows = [request.Left.Rows[0] with { OriginalPayload = original }] } };
                break;
            case "wide-number":
                original = """{"id":1000000000000000000000000000000,"source":"ledger"}""";
                reformatted = """{"id":1e30,"source":"ledger"}""";
                request = request with { Left = request.Left with { Rows = [request.Left.Rows[0] with { OriginalPayload = original }] } };
                break;
            case "wide-exponent":
                original = "1e2147483648";
                reformatted = "10e2147483647";
                request = request with { Left = request.Left with { Rows = [request.Left.Rows[0] with { OriginalPayload = original }] } };
                break;
            case "zero-padded-exponent":
                original = "1000";
                reformatted = "1e+000000000000000000000000003";
                request = request with { Left = request.Left with { Rows = [request.Left.Rows[0] with { OriginalPayload = original }] } };
                break;
            case "string-escape":
                original = """{"balance":{"amount":1000,"currency":"USD"},"source":"ledger"}""";
                reformatted = """{"balance":{"amount":1000,"currency":"\u0055SD"},"source":"\u006cedger"}""";
                request = request with { Left = request.Left with { Rows = [request.Left.Rows[0] with { OriginalPayload = original }] } };
                break;
            case "root-scalar":
                original = "1000";
                reformatted = "1e3";
                request = request with { Left = request.Left with { Rows = [request.Left.Rows[0] with { OriginalPayload = original }] } };
                break;
            case "number-fraction":
                original = """{"balance":{"amount":1000,"currency":"USD"},"source":"ledger"}""";
                reformatted = """{"balance":{"amount":1000.00,"currency":"USD"},"source":"ledger"}""";
                request = request with { Left = request.Left with { Rows = [request.Left.Rows[0] with { OriginalPayload = original }] } };
                break;
            default:
                original = """{"balance":{"amount":1000,"currency":"USD"},"source":"ledger","tags":["a","b"]}""";
                reformatted = """{ "tags": ["a", "b"], "source": "ledger", "balance": { "currency": "USD", "amount": 1000 } }""";
                request = request with { Left = request.Left with { Rows = [request.Left.Rows[0] with { OriginalPayload = original }] } };
                break;
        }
        reformatted.Should().NotBe(original);
        var copied = request with { Right = request.Right with { Rows = [request.Right.Rows[0] with { OriginalPayload = reformatted }] } };

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(copied));

        failure.Message.Should().Contain("copied evidence is not independent");
        (await service.GetCasesAsync(_scope)).Should().BeEmpty();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReplayAsync(_scope, "first"));
    }

    [Fact]
    public async Task Backdated_business_date_cannot_replace_the_comparison_head()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("head", 2, 1000m, 990m));
        var before = Assert.Single(await service.GetCasesAsync(_scope));
        var earlierDay = _start;
        var backdated = new AccountCashReconciliationRequest("backdated", _scope, DateOnly.FromDateTime(earlierDay.UtcDateTime), _start.AddDays(3),
            Source("accounting-ledger", ReconciliationSourceType.InternalLedger, _scope, earlierDay, ("USD", 1000m)),
            Source("custodian", ReconciliationSourceType.Custodian, _scope, earlierDay, ("USD", 1000m)),
            Profile(1, 0.01m), "initial-owner");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(backdated));

        failure.Message.Should().Contain("earlier business date");
        Assert.Single(await (await ServiceAsync()).GetCasesAsync(_scope)).Should().BeEquivalentTo(before);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReplayAsync(_scope, "backdated"));
    }

    [Fact]
    public async Task Decimal_overflow_during_comparison_is_rejected_as_invalid_input()
    {
        var service = await ServiceAsync();
        var request = Request("overflow", 0, decimal.MaxValue, decimal.MinValue);

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(request));

        failure.Message.Should().Contain("decimal arithmetic");
        failure.InnerException.Should().BeOfType<OverflowException>();
        (await service.GetCasesAsync(_scope)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("position")]
    [InlineData("transaction")]
    public async Task Null_position_or_transaction_rule_is_rejected_before_history_changes(string collection)
    {
        var service = await ServiceAsync();
        var request = Request("first", 0, 1000m, 990m);
        request = collection == "position"
            ? request with { Tolerance = request.Tolerance with { PositionRules = [null!] } }
            : request with { Tolerance = request.Tolerance with { TransactionRules = [null!] } };

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(request));

        failure.Message.Should().Contain("null entries");
        (await service.GetCasesAsync(_scope)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("day/custodian")]
    [InlineData("run one")]
    [InlineData("run%2Fone")]
    [InlineData("run?replay=1")]
    [InlineData("run#1")]
    [InlineData("run\\one")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task Run_identity_that_cannot_round_trip_replay_routes_is_rejected(string runId)
    {
        var service = await ServiceAsync();

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(Request(runId, 0, 1000m, 990m)));

        failure.Message.Should().Contain("round-trip");
        (await service.GetCasesAsync(_scope)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("route-unsafe-run-id")]
    [InlineData("empty-cash-rules")]
    [InlineData("null-position-rule")]
    [InlineData("null-transaction-rule")]
    [InlineData("reformatted-copied-payload")]
    public async Task Legacy_retained_run_still_replays_and_resubmits_while_the_same_new_run_is_rejected(string legacyShape)
    {
        var store = new FileAccountCashReconciliationStore(_root);
        await store.InitializeAsync();
        var service = new AccountCashReconciliationService(store);
        var original = await service.RunAsync(Request("first", 0, 1000m, 990m));
        var (legacyRequest, expectedRejection) = LegacyRequest(legacyShape, "legacy", 0);
        // Retained before the admission rule existed: write the durable record directly, bypassing RunAsync.
        var legacy = service.EvaluateUnretained(legacyRequest);
        legacy.InputHash.Should().NotBe(original.InputHash);
        await store.WithStateAsync(_scope, document => (document with { Runs = [legacy] }, 0), true, CancellationToken.None);

        var replay = await (await ServiceAsync()).ReplayAsync(_scope, legacyRequest.RunId);
        replay.Should().BeEquivalentTo(legacy, options => options.WithStrictOrdering());
        replay.Comparisons.Should().ContainSingle().Which.ObservationState.Should().Be(legacyShape == "reformatted-copied-payload" ? "SourceCleared" : "Open");

        var resubmitted = await service.RunAsync(legacyRequest);
        resubmitted.Should().BeEquivalentTo(legacy, options => options.WithStrictOrdering());
        var rebind = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(legacyRequest with { InitialOwner = "someone-else" }));
        rebind.Message.Should().Contain("cannot be rebound");

        var (fresh, _) = LegacyRequest(legacyShape, "fresh", 1);
        var failure = await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(fresh));
        failure.Message.Should().Contain(expectedRejection);
        Assert.Single(await (await ServiceAsync()).GetCasesAsync(_scope)).History.Should().ContainSingle();
    }

    [Fact]
    public async Task Empty_cash_rule_set_cannot_clear_a_comparison()
    {
        var service = await ServiceAsync();
        var request = Request("first", 0, 1000m, 1000m);
        request = request with { Tolerance = request.Tolerance with { CashRules = [] } };

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(request));

        failure.Message.Should().Contain("At least one executed cash tolerance rule");
        (await service.GetCasesAsync(_scope)).Should().BeEmpty();
    }

    [Fact]
    public async Task Padded_profile_identity_with_identical_rules_is_the_same_retained_tolerance_version()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var padded = Request("padded", 1, 1000m, 990m);
        padded = padded with { Tolerance = padded.Tolerance with { ProfileId = " " + padded.Tolerance.ProfileId + " " } };

        var run = await service.RunAsync(padded);

        run.Input.RunId.Should().Be("padded");
        Assert.Single(await service.GetCasesAsync(_scope)).History.Select(item => item.RunId).Should().Equal("first", "padded");
    }

    [Fact]
    public async Task Padded_profile_identity_cannot_rebind_a_retained_tolerance_version()
    {
        var service = await ServiceAsync();
        var original = await service.RunAsync(Request("first", 0, 1000m, 990m));
        var padded = Request("padded", 1, 1000m, 990m);
        padded = padded with { Tolerance = Profile(1, 20m) with { ProfileId = " " + padded.Tolerance.ProfileId + " " } };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(padded));

        failure.Message.Should().Contain("retained tolerance version");
        Assert.Single(await service.GetCasesAsync(_scope)).History.Select(item => item.RunId).Should().Equal("first");
        (await service.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(original, options => options.WithStrictOrdering());
    }

    private (AccountCashReconciliationRequest Request, string Rejection) LegacyRequest(string shape, string runId, int days)
    {
        var request = Request(runId, days, 1000m, 990m);
        switch (shape)
        {
            case "route-unsafe-run-id":
                return (request with { RunId = runId + " run/2026%09" }, "round-trip");
            case "empty-cash-rules":
                return (request with { Tolerance = request.Tolerance with { CashRules = [] } }, "At least one executed cash tolerance rule");
            case "null-position-rule":
                return (request with { Tolerance = request.Tolerance with { PositionRules = [null!] } }, "null entries");
            case "null-transaction-rule":
                return (request with { Tolerance = request.Tolerance with { TransactionRules = [null!] } }, "null entries");
            default:
                request = Request(runId, days, 1000m, 1000m);
                var reformatted = request.Left.Rows[0].OriginalPayload.Replace(",", ",\n  ").Replace(":", ": ");
                return (request with { Right = request.Right with { Rows = [request.Right.Rows[0] with { OriginalPayload = reformatted }] } }, "copied evidence is not independent");
        }
    }

    private static string ReverseProperties(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var properties = document.RootElement.EnumerateObject().Reverse().ToArray();
        properties.Length.Should().BeGreaterThan(1);
        return "{" + string.Join(",", properties.Select(property => System.Text.Json.JsonSerializer.Serialize(property.Name) + ":" + property.Value.GetRawText())) + "}";
    }

    [Fact]
    public async Task Corrupt_scope_file_blocks_only_its_own_scope()
    {
        var service = await ServiceAsync();
        var otherScope = _scope with { AccountId = "account-b" };
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var otherRun = await service.RunAsync(Request("other-first", 0, 500m, 500m, otherScope));
        await File.WriteAllTextAsync(ScopeStatePath(_scope), "{}");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetCasesAsync(_scope));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(Request("second", 1, 1000m, 1000m)));
        (await service.GetCasesAsync(otherScope)).Should().BeEmpty();
        (await service.ReplayAsync(otherScope, "other-first")).Should().BeEquivalentTo(otherRun, options => options.WithStrictOrdering());
        var appended = await service.RunAsync(Request("other-second", 1, 500m, 480m, otherScope));
        Assert.Single(await (await ServiceAsyncWithoutProvisioning()).GetCasesAsync(otherScope)).History.Should().ContainSingle()
            .Which.RunId.Should().Be(appended.Input.RunId);
        File.Exists(ScopeLockPath(_scope)).Should().BeTrue();
        File.Exists(ScopeLockPath(otherScope)).Should().BeTrue();
        File.Exists(Path.Combine(StoreDirectory, "state.json")).Should().BeFalse();
    }

    [Fact]
    public async Task Legacy_single_file_state_is_split_per_scope_losslessly_and_renamed_not_deleted()
    {
        var otherScope = _scope with { AccountId = "account-b" };
        var service = await ServiceAsync();
        var firstRun = await service.RunAsync(Request("first", 0, 1000m, 990m));
        var firstCase = Assert.Single(await service.GetCasesAsync(_scope));
        var assigned = await service.AssignAsync(_scope, firstCase.CaseId, firstCase.Version, "cash-ops", "supervisor", "Investigate", _start.AddMinutes(30));
        var otherRun = await service.RunAsync(Request("other-first", 1, 500m, 480m, otherScope));
        var otherCase = Assert.Single(await service.GetCasesAsync(otherScope));
        var legacyBytes = await LegacyStateBytesAsync(_scope, otherScope);
        var legacyRoot = Path.Combine(_root, "legacy-root");
        var legacyDirectory = Path.Combine(legacyRoot, "reconciliation", "account-cash-continuity");
        Directory.CreateDirectory(legacyDirectory);
        await File.WriteAllBytesAsync(Path.Combine(legacyDirectory, "state.json"), legacyBytes);
        await File.WriteAllTextAsync(Path.Combine(legacyDirectory, "initialized"), "meridian-account-cash-reconciliation-v1");

        var store = new FileAccountCashReconciliationStore(legacyRoot);
        await store.InitializeAsync();
        var migrated = new AccountCashReconciliationService(store);

        File.Exists(Path.Combine(legacyDirectory, "state.json")).Should().BeFalse();
        (await File.ReadAllBytesAsync(Path.Combine(legacyDirectory, "state.json.migrated"))).Should().Equal(legacyBytes);
        Directory.EnumerateFiles(Path.Combine(legacyDirectory, "scopes"), "*.json").Should().HaveCount(2);
        Assert.Single(await migrated.GetCasesAsync(_scope)).Should().BeEquivalentTo(assigned);
        Assert.Single(await migrated.GetCasesAsync(otherScope)).Should().BeEquivalentTo(otherCase);
        (await migrated.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(firstRun, options => options.WithStrictOrdering());
        (await migrated.ReplayAsync(otherScope, "other-first")).Should().BeEquivalentTo(otherRun, options => options.WithStrictOrdering());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => migrated.ReplayAsync(otherScope, "first"));
        // Re-initializing after the split neither re-splits nor disturbs the migrated copy.
        await new FileAccountCashReconciliationStore(legacyRoot).InitializeAsync();
        (await File.ReadAllBytesAsync(Path.Combine(legacyDirectory, "state.json.migrated"))).Should().Equal(legacyBytes);
    }

    [Fact]
    public async Task Legacy_single_file_state_is_split_on_first_scoped_use_without_reprovisioning()
    {
        var service = await ServiceAsync();
        var firstRun = await service.RunAsync(Request("first", 0, 1000m, 990m));
        var legacyBytes = await LegacyStateBytesAsync(_scope);
        var legacyRoot = Path.Combine(_root, "legacy-root");
        var legacyDirectory = Path.Combine(legacyRoot, "reconciliation", "account-cash-continuity");
        Directory.CreateDirectory(legacyDirectory);
        await File.WriteAllBytesAsync(Path.Combine(legacyDirectory, "state.json"), legacyBytes);
        await File.WriteAllTextAsync(Path.Combine(legacyDirectory, "initialized"), "meridian-account-cash-reconciliation-v1");

        var migrated = new AccountCashReconciliationService(new FileAccountCashReconciliationStore(legacyRoot));
        (await migrated.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(firstRun, options => options.WithStrictOrdering());

        File.Exists(Path.Combine(legacyDirectory, "state.json.migrated")).Should().BeTrue();
        File.Exists(Path.Combine(legacyDirectory, "state.json")).Should().BeFalse();
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("unprovisioned")]
    public async Task Corrupt_or_unprovisioned_legacy_state_fails_closed_without_splitting(string scenario)
    {
        var legacyDirectory = Path.Combine(_root, "reconciliation", "account-cash-continuity");
        Directory.CreateDirectory(legacyDirectory);
        var legacyPath = Path.Combine(legacyDirectory, "state.json");
        var legacyBytes = scenario == "corrupt" ? "{}"u8.ToArray() : await LegacyBytesFromSeparateRootAsync();
        await File.WriteAllBytesAsync(legacyPath, legacyBytes);
        if (scenario == "corrupt")
            await File.WriteAllTextAsync(Path.Combine(legacyDirectory, "initialized"), "meridian-account-cash-reconciliation-v1");
        var service = new AccountCashReconciliationService(new FileAccountCashReconciliationStore(_root));

        // A corrupt legacy file is a restore condition (InvalidDataException -> 503); unprovisioned retained
        // history is a provisioning conflict (InvalidOperationException -> 409). Neither is split or adopted.
        var corrupt = scenario == "corrupt";
        var caught = new[]
        {
            await Record.ExceptionAsync(() => service.GetCasesAsync(_scope)),
            await Record.ExceptionAsync(() => service.RunAsync(Request("new-run", 1, 1000m, 1000m))),
            await Record.ExceptionAsync(() => new FileAccountCashReconciliationStore(_root).InitializeAsync()),
        };
        if (corrupt)
            caught.Should().AllBeOfType<InvalidDataException>();
        else
            caught.Should().AllBeOfType<InvalidOperationException>();
        (await File.ReadAllBytesAsync(legacyPath)).Should().Equal(legacyBytes);
        File.Exists(legacyPath + ".migrated").Should().BeFalse();
        File.Exists(Path.Combine(legacyDirectory, "scope-index.json")).Should().BeFalse();
        Directory.Exists(Path.Combine(legacyDirectory, "scopes")).Should().BeFalse();
    }

    private async Task<byte[]> LegacyBytesFromSeparateRootAsync()
    {
        var separateRoot = Path.Combine(_root, "separate-root");
        var store = new FileAccountCashReconciliationStore(separateRoot);
        await store.InitializeAsync();
        await new AccountCashReconciliationService(store).RunAsync(Request("first", 0, 1000m, 990m));
        return await LegacyStateBytesAsync(separateRoot, _scope);
    }

    private Task<byte[]> LegacyStateBytesAsync(params AccountCashReconciliationScope[] scopes) => LegacyStateBytesAsync(_root, scopes);

    /// <summary>Recomposes the pre-partition single-file envelope from retained scope files.</summary>
    private static async Task<byte[]> LegacyStateBytesAsync(string root, params AccountCashReconciliationScope[] scopes)
    {
        var runs = new List<AccountCashReconciliationRun>();
        var cases = new List<AccountCashReconciliationCase>();
        foreach (var scope in scopes)
        {
            var path = Path.Combine(root, "reconciliation", "account-cash-continuity", "scopes", FileAccountCashReconciliationStore.ScopeFileName(scope) + ".json");
            var envelope = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(path), AccountCashReconciliationJsonContext.Default.AccountCashReconciliationScopeEnvelope)!;
            runs.AddRange(envelope.Document.Runs);
            cases.AddRange(envelope.Document.Cases);
        }
        var document = new AccountCashReconciliationDocument(1, runs, cases.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray());
        var hash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(document, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationDocument));
        return JsonSerializer.SerializeToUtf8Bytes(new AccountCashReconciliationEnvelope(hash, document), AccountCashReconciliationJsonContext.Default.AccountCashReconciliationEnvelope);
    }

    [Theory]
    [InlineData("layout-lost")]
    [InlineData("empty-index-left")]
    [InlineData("corrupt-migrated")]
    public async Task Migrated_legacy_state_is_never_reprovisioned_as_empty(string remains)
    {
        // Upgrade path: retained single-file state is split and renamed on provisioning.
        var service = await ServiceAsync();
        var original = await service.RunAsync(Request("first", 0, 1000m, 990m));
        var legacyBytes = await LegacyStateBytesAsync(_scope);
        var legacyPath = Path.Combine(StoreDirectory, "state.json");
        var migratedPath = legacyPath + ".migrated";
        var markerPath = Path.Combine(StoreDirectory, "initialized");
        var indexPath = Path.Combine(StoreDirectory, "scope-index.json");
        Directory.Delete(Path.Combine(StoreDirectory, "scopes"), recursive: true);
        File.Delete(indexPath);
        await File.WriteAllBytesAsync(legacyPath, legacyBytes);
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        var backupScope = await File.ReadAllBytesAsync(ScopeStatePath(_scope));
        var backupIndex = await File.ReadAllBytesAsync(indexPath);
        (await File.ReadAllBytesAsync(migratedPath)).Should().Equal(legacyBytes);

        // A partial restore keeps the migrated evidence but loses the marker, the scope files and,
        // except for an empty index copied from a fresh store, the index.
        File.Delete(markerPath);
        Directory.Delete(Path.Combine(StoreDirectory, "scopes"), recursive: true);
        File.Delete(indexPath);
        if (remains == "empty-index-left")
        {
            var separateRoot = Path.Combine(Path.GetTempPath(), $"meridian-account-cash-{Guid.NewGuid():N}");
            await new FileAccountCashReconciliationStore(separateRoot).InitializeAsync();
            File.Copy(Path.Combine(separateRoot, "reconciliation", "account-cash-continuity", "scope-index.json"), indexPath);
            Directory.Delete(separateRoot, recursive: true);
        }
        if (remains == "corrupt-migrated")
            await File.WriteAllTextAsync(migratedPath, "{}");
        var migratedBytes = await File.ReadAllBytesAsync(migratedPath);

        // Retained evidence is a recovery condition (409), a corrupt file a restore condition (503); neither restarts history.
        var reprovision = await Record.ExceptionAsync(() => new FileAccountCashReconciliationStore(_root).InitializeAsync());
        reprovision.Should().BeOfType(remains == "corrupt-migrated" ? typeof(InvalidDataException) : typeof(InvalidOperationException));
        File.Exists(markerPath).Should().BeFalse();
        File.Exists(indexPath).Should().Be(remains == "empty-index-left");
        (await File.ReadAllBytesAsync(migratedPath)).Should().Equal(migratedBytes);
        var unprovisioned = await ServiceAsyncWithoutProvisioning();
        await Assert.ThrowsAsync<InvalidOperationException>(() => unprovisioned.GetCasesAsync(_scope));
        if (remains == "corrupt-migrated")
            return;

        // Restoring the complete scope layout and marker from the same backup set recovers the history without a reset.
        Directory.CreateDirectory(Path.Combine(StoreDirectory, "scopes"));
        await File.WriteAllBytesAsync(ScopeStatePath(_scope), backupScope);
        await File.WriteAllBytesAsync(indexPath, backupIndex);
        await File.WriteAllTextAsync(markerPath, "meridian-account-cash-reconciliation-v1");
        var restored = await ServiceAsync();
        (await restored.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(original, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Cancellation_after_the_commit_decision_still_registers_the_scope_with_its_file()
    {
        var store = new FileAccountCashReconciliationStore(_root);
        await store.InitializeAsync();
        var service = new AccountCashReconciliationService(store);
        var run = service.EvaluateUnretained(Request("first", 0, 1000m, 990m));
        using var cts = new CancellationTokenSource();

        // The request is abandoned once the mutation has been decided: the first scope-file write must
        // still follow its registration, or the indexed scope would fail closed on every later read.
        await store.WithStateAsync(_scope, document =>
        {
            cts.Cancel();
            return (document with { Runs = [run] }, 0);
        }, true, cts.Token);

        File.Exists(ScopeStatePath(_scope)).Should().BeTrue();
        var reopened = await ServiceAsyncWithoutProvisioning();
        (await reopened.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(run, options => options.WithStrictOrdering());
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
    }

    [Fact]
    public async Task Provisioning_concurrent_with_first_scope_writes_never_misreads_a_registered_scope()
    {
        var store = new FileAccountCashReconciliationStore(_root);
        await store.InitializeAsync();
        var service = new AccountCashReconciliationService(store);
        var scopes = Enumerable.Range(0, 12).Select(index => _scope with { AccountId = $"account-{index}" }).ToArray();

        // Each first write registers its scope and writes its file under one store lease, so a concurrent
        // provisioning pass validating every registered scope never sees an indexed scope without its file.
        var writes = scopes.Select(scope => (Task)service.RunAsync(Request($"first-{scope.AccountId}", 0, 1000m, 990m, scope)));
        var provisions = scopes.Select(_ => new FileAccountCashReconciliationStore(_root).InitializeAsync());
        await Task.WhenAll(writes.Concat(provisions));

        foreach (var scope in scopes)
            (await service.GetCasesAsync(scope)).Should().ContainSingle();
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
    }

    [Fact]
    public async Task Migrated_history_scope_cannot_be_treated_as_new_when_its_file_is_lost()
    {
        var service = await ServiceAsync();
        var original = await service.RunAsync(Request("first", 0, 1000m, 990m));
        var legacyBytes = await LegacyStateBytesAsync(_scope);
        var legacyPath = Path.Combine(StoreDirectory, "state.json");
        var migratedPath = legacyPath + ".migrated";
        var indexPath = Path.Combine(StoreDirectory, "scope-index.json");
        Directory.Delete(Path.Combine(StoreDirectory, "scopes"), recursive: true);
        File.Delete(indexPath);
        await File.WriteAllBytesAsync(legacyPath, legacyBytes);
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        var backupScope = await File.ReadAllBytesAsync(ScopeStatePath(_scope));
        var backupIndex = await File.ReadAllBytesAsync(indexPath);
        (await File.ReadAllBytesAsync(migratedPath)).Should().Equal(legacyBytes);

        // A partial restore keeps the marker and a valid empty index but loses the scope files.
        var separateRoot = Path.Combine(Path.GetTempPath(), $"meridian-account-cash-{Guid.NewGuid():N}");
        await new FileAccountCashReconciliationStore(separateRoot).InitializeAsync();
        File.Copy(Path.Combine(separateRoot, "reconciliation", "account-cash-continuity", "scope-index.json"), indexPath, overwrite: true);
        Directory.Delete(separateRoot, recursive: true);
        Directory.Delete(Path.Combine(StoreDirectory, "scopes"), recursive: true);

        // The migrated scope is retained evidence: it is never treated as new, and provisioning fails closed.
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileAccountCashReconciliationStore(_root).InitializeAsync());
        var reopened = await ServiceAsyncWithoutProvisioning();
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.GetCasesAsync(_scope));
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.RunAsync(Request("second", 1, 1000m, 1000m)));
        (await File.ReadAllBytesAsync(migratedPath)).Should().Equal(legacyBytes);
        var otherScope = _scope with { AccountId = "account-b" };
        await reopened.RunAsync(Request("other-first", 0, 500m, 500m, otherScope));

        // Restoring the complete scope layout from the backup set recovers the history without a reset.
        Directory.CreateDirectory(Path.Combine(StoreDirectory, "scopes"));
        await File.WriteAllBytesAsync(ScopeStatePath(_scope), backupScope);
        await File.WriteAllBytesAsync(indexPath, backupIndex);
        var restored = await ServiceAsync();
        (await restored.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(original, options => options.WithStrictOrdering());
        (await restored.ReplayAsync(otherScope, "other-first")).Input.RunId.Should().Be("other-first");
    }


    [Theory]
    [InlineData("provision")]
    [InlineData("read")]
    [InlineData("write")]
    public async Task Migration_refuses_missing_registered_file_instead_of_rolling_back_appends(string use)
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var legacy = await LegacyStateBytesAsync(_scope);
        var legacyPath = Path.Combine(StoreDirectory, "state.json");
        await File.WriteAllBytesAsync(legacyPath, legacy);
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        var second = await service.RunAsync(Request("second", 1, 1000m, 980m));
        var complete = await File.ReadAllBytesAsync(ScopeStatePath(_scope));
        var index = await File.ReadAllBytesAsync(Path.Combine(StoreDirectory, "scope-index.json"));
        File.Move(legacyPath + ".migrated", legacyPath);
        File.Delete(ScopeStatePath(_scope));

        var error = await Record.ExceptionAsync(async () =>
        {
            if (use == "provision")
                await new FileAccountCashReconciliationStore(_root).InitializeAsync();
            else if (use == "read")
                await service.GetCasesAsync(_scope);
            else
                await service.RunAsync(Request("third", 2, 1000m, 970m));
        });
        error.Should().BeOfType<InvalidDataException>();
        File.Exists(ScopeStatePath(_scope)).Should().BeFalse();
        (await File.ReadAllBytesAsync(legacyPath)).Should().Equal(legacy);
        (await File.ReadAllBytesAsync(Path.Combine(StoreDirectory, "scope-index.json"))).Should().Equal(index);

        await File.WriteAllBytesAsync(ScopeStatePath(_scope), complete);
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        (await service.ReplayAsync(_scope, "second")).Should().BeEquivalentTo(second, options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData("missing-run")]
    [InlineData("conflicting-run")]
    [InlineData("missing-case")]
    [InlineData("observation")]
    [InlineData("assignment")]
    [InlineData("case-metadata")]
    [InlineData("unregistered")]
    public async Task Migrated_history_must_remain_a_prefix_of_valid_replacement(string defect)
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var item = Assert.Single(await service.GetCasesAsync(_scope));
        await service.AssignAsync(_scope, item.CaseId, item.Version, "reviewer", "operator", "Review discrepancy", _start.AddHours(1));
        await service.RunAsync(Request("second", 1, 1000m, 980m));
        var legacy = await LegacyStateBytesAsync(_scope);
        var legacyPath = Path.Combine(StoreDirectory, "state.json");
        await File.WriteAllBytesAsync(legacyPath, legacy);
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        var complete = await File.ReadAllBytesAsync(ScopeStatePath(_scope));
        var envelope = JsonSerializer.Deserialize(complete, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationScopeEnvelope)!;
        var document = envelope.Document;
        var originalCase = document.Cases.Single();
        document = defect switch
        {
            "missing-run" or "unregistered" => document with { Runs = document.Runs.Take(1).ToArray() },
            "conflicting-run" => document with
            {
                Runs = document.Runs.Select(run =>
                {
                    var input = run.Input with { InitialOwner = "different-owner" };
                    return run with
                    {
                        Input = input,
                        InputHash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(input,
                            AccountCashReconciliationJsonContext.Default.AccountCashReconciliationRequest))
                    };
                }).ToArray()
            },
            "missing-case" => document with { Cases = [] },
            "observation" => document with { Cases = [originalCase with { History = originalCase.History.Skip(1).ToArray() }] },
            "assignment" => document with { Cases = [originalCase with { Assignments = [originalCase.Assignments[0] with { Reason = "Changed history" }] }] },
            _ => document with { Cases = [originalCase with { FirstObservedAtUtc = originalCase.FirstObservedAtUtc.AddMinutes(1) }] }
        };
        var hash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(document, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationDocument));
        var invalid = JsonSerializer.SerializeToUtf8Bytes(envelope with { Document = document, ContentHash = hash },
            AccountCashReconciliationJsonContext.Default.AccountCashReconciliationScopeEnvelope);
        await File.WriteAllBytesAsync(ScopeStatePath(_scope), invalid);
        if (defect == "unregistered")
        {
            var emptyRoot = Path.Combine(_root, "empty-layout");
            await new FileAccountCashReconciliationStore(emptyRoot).InitializeAsync();
            File.Copy(Path.Combine(emptyRoot, "reconciliation", "account-cash-continuity", "scope-index.json"),
                Path.Combine(StoreDirectory, "scope-index.json"), overwrite: true);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => new FileAccountCashReconciliationStore(_root).InitializeAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetCasesAsync(_scope));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(Request("third", 2, 1000m, 970m)));
        (await File.ReadAllBytesAsync(ScopeStatePath(_scope))).Should().Equal(invalid);
        (await File.ReadAllBytesAsync(legacyPath + ".migrated")).Should().Equal(legacy);
        (await service.GetCasesAsync(_scope with { TenantId = "unrelated-tenant" })).Should().BeEmpty();

        await File.WriteAllBytesAsync(ScopeStatePath(_scope), complete);
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        (await service.GetCasesAsync(_scope)).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migrated_prefix_allows_later_observations_assignments_resolution_and_reopening(bool restoreLegacy)
    {
        var service = await ServiceAsync();
        var first = await service.RunAsync(Request("first", 0, 1000m, 990m));
        var item = Assert.Single(await service.GetCasesAsync(_scope));
        await service.AssignAsync(_scope, item.CaseId, item.Version, "reviewer", "operator", "Review discrepancy", _start.AddHours(1));
        var legacy = await LegacyStateBytesAsync(_scope);
        var legacyPath = Path.Combine(StoreDirectory, "state.json");
        await File.WriteAllBytesAsync(legacyPath, legacy);
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        await service.RunAsync(Request("cleared", 1, 1000m, 1000m));
        item = Assert.Single(await service.GetCasesAsync(_scope));
        await service.AssignAsync(_scope, item.CaseId, item.Version, "next-owner", "operator", "Follow-up", _start.AddDays(1).AddHours(1));
        await service.RunAsync(Request("reopened", 2, 1000m, 980m));
        var before = await File.ReadAllBytesAsync(ScopeStatePath(_scope));
        if (restoreLegacy)
            File.Move(legacyPath + ".migrated", legacyPath);
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        (await service.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
        item = Assert.Single(await service.GetCasesAsync(_scope));
        item.History.Should().HaveCount(3);
        item.Assignments.Should().HaveCount(2);
        item.Owner.Should().Be("next-owner");
        item.State.Should().Be("Open");
        (await File.ReadAllBytesAsync(ScopeStatePath(_scope))).Should().Equal(before);
        (await File.ReadAllBytesAsync(legacyPath + ".migrated")).Should().Equal(legacy);
    }

    [Theory]
    [InlineData("global-corrupt")]
    [InlineData("baseline-corrupt")]
    [InlineData("bootstrap")]
    [InlineData("bootstrap-conflict")]
    [InlineData("baseline-only-loss")]
    [InlineData("baseline-only-empty-legacy")]
    [InlineData("lost-index-and-scope")]
    public async Task Per_scope_migration_evidence_preserves_isolation_and_recovery(string scenario)
    {
        var service = await ServiceAsync();
        var first = await service.RunAsync(Request("first", 0, 1000m, 990m));
        var otherScope = _scope with { AccountId = "account-b" };
        await service.RunAsync(Request("other-first", 0, 500m, 500m, otherScope));
        var legacyPath = Path.Combine(StoreDirectory, "state.json");
        await File.WriteAllBytesAsync(legacyPath, await LegacyStateBytesAsync(_scope, otherScope));
        var store = new FileAccountCashReconciliationStore(_root);
        await store.InitializeAsync();
        var baselineDirectory = Path.Combine(StoreDirectory, "migration-baselines");
        var baselinePath = Path.Combine(baselineDirectory, FileAccountCashReconciliationStore.ScopeFileName(_scope) + ".json");
        var baseline = await File.ReadAllBytesAsync(baselinePath);
        var indexPath = Path.Combine(StoreDirectory, "scope-index.json");
        var markerPath = Path.Combine(StoreDirectory, "initialized");
        var index = await File.ReadAllBytesAsync(indexPath);
        var complete = await File.ReadAllBytesAsync(ScopeStatePath(_scope));
        var otherComplete = await File.ReadAllBytesAsync(ScopeStatePath(otherScope));
        if (scenario == "lost-index-and-scope")
        {
            await service.RunAsync(Request("second", 1, 1000m, 980m));
            var later = await File.ReadAllBytesAsync(ScopeStatePath(_scope));
            File.Move(legacyPath + ".migrated", legacyPath);
            File.Delete(indexPath);
            File.Delete(ScopeStatePath(_scope));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.InitializeAsync());
            File.Exists(indexPath).Should().BeFalse();
            File.Exists(ScopeStatePath(_scope)).Should().BeFalse();
            File.Exists(legacyPath).Should().BeTrue();
            await File.WriteAllBytesAsync(ScopeStatePath(_scope), later);
            await File.WriteAllBytesAsync(indexPath, index);
            await store.InitializeAsync();
            Assert.Single(await service.GetCasesAsync(_scope)).History.Should().HaveCount(2);
            return;
        }
        if (scenario.StartsWith("bootstrap", StringComparison.Ordinal))
        {
            Directory.Delete(baselineDirectory, recursive: true);
            if (scenario == "bootstrap-conflict")
            {
                var envelope = JsonSerializer.Deserialize(complete, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationScopeEnvelope)!;
                var document = envelope.Document with { Runs = [], Cases = [] };
                var hash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(document, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationDocument));
                await File.WriteAllBytesAsync(ScopeStatePath(_scope), JsonSerializer.SerializeToUtf8Bytes(envelope with { Document = document, ContentHash = hash },
                    AccountCashReconciliationJsonContext.Default.AccountCashReconciliationScopeEnvelope));
                await Assert.ThrowsAsync<InvalidDataException>(() => service.GetCasesAsync(_scope));
                File.Exists(baselinePath).Should().BeFalse();
                return;
            }
            (await service.GetCasesAsync(_scope)).Should().ContainSingle();
            (await File.ReadAllBytesAsync(baselinePath)).Should().Equal(baseline);
        }
        if (scenario == "baseline-corrupt")
        {
            await File.WriteAllTextAsync(baselinePath, "{}");
            await Assert.ThrowsAsync<InvalidDataException>(() => service.GetCasesAsync(_scope));
            (await service.ReplayAsync(otherScope, "other-first")).Input.RunId.Should().Be("other-first");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.InitializeAsync());
            return;
        }
        if (scenario.StartsWith("baseline-only", StringComparison.Ordinal))
        {
            File.Delete(legacyPath + ".migrated");
            File.Delete(markerPath);
            File.Delete(indexPath);
            Directory.Delete(Path.Combine(StoreDirectory, "scopes"), recursive: true);
            if (scenario == "baseline-only-empty-legacy")
            {
                await File.WriteAllBytesAsync(legacyPath, await LegacyStateBytesAsync());
                await Assert.ThrowsAsync<InvalidDataException>(() => store.InitializeAsync());
                File.Delete(legacyPath);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
            }
            File.Exists(markerPath).Should().BeFalse();
            File.Exists(indexPath).Should().BeFalse();
            Directory.CreateDirectory(Path.Combine(StoreDirectory, "scopes"));
            await File.WriteAllBytesAsync(ScopeStatePath(_scope), complete);
            await File.WriteAllBytesAsync(ScopeStatePath(otherScope), otherComplete);
            await File.WriteAllBytesAsync(indexPath, index);
            await File.WriteAllTextAsync(markerPath, "meridian-account-cash-reconciliation-v1");
            await store.InitializeAsync();
        }
        else
        {
            await File.WriteAllTextAsync(legacyPath + ".migrated", "{}");
        }
        (await service.ReplayAsync(_scope, "first")).Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
        if (scenario != "bootstrap")
            (await service.ReplayAsync(otherScope, "other-first")).Input.RunId.Should().Be("other-first");
        await service.RunAsync(Request("later", 1, 1000m, 980m));
        Assert.Single(await service.GetCasesAsync(_scope)).History.Should().HaveCount(2);
        if (!scenario.StartsWith("baseline-only", StringComparison.Ordinal))
            await Assert.ThrowsAsync<InvalidDataException>(() => store.InitializeAsync());
    }

    [Theory]
    [InlineData("read")]
    [InlineData("provision")]
    [InlineData("write")]
    public async Task Interrupted_first_registration_is_completed_on_next_use(string use)
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var otherScope = _scope with { AccountId = "account-b" };

        // A first commit writes the scope file before registering it. Simulate a crash in between by
        // retaining the other scope's committed file without its index entry.
        var separateRoot = Path.Combine(Path.GetTempPath(), $"meridian-account-cash-{Guid.NewGuid():N}");
        var separate = new FileAccountCashReconciliationStore(separateRoot);
        await separate.InitializeAsync();
        var otherRun = await new AccountCashReconciliationService(separate).RunAsync(Request("other-first", 0, 500m, 500m, otherScope));
        Directory.CreateDirectory(Path.Combine(StoreDirectory, "scopes"));
        File.Copy(Path.Combine(separateRoot, "reconciliation", "account-cash-continuity", "scopes", FileAccountCashReconciliationStore.ScopeFileName(otherScope) + ".json"), ScopeStatePath(otherScope));
        Directory.Delete(separateRoot, recursive: true);

        switch (use)
        {
            case "read":
                (await service.ReplayAsync(otherScope, "other-first")).Should().BeEquivalentTo(otherRun, options => options.WithStrictOrdering());
                break;
            case "provision":
                await new FileAccountCashReconciliationStore(_root).InitializeAsync();
                break;
            default:
                await service.RunAsync(Request("other-second", 1, 500m, 480m, otherScope));
                break;
        }

        // The scope is registered: a fresh store replays it, provisioning validates it, and the retained
        // file is now protected like any other registered scope.
        var reopened = await ServiceAsyncWithoutProvisioning();
        (await reopened.ReplayAsync(otherScope, "other-first")).Should().BeEquivalentTo(otherRun, options => options.WithStrictOrdering());
        // The matched first run opens no case; only the later mismatch does.
        var cases = await reopened.GetCasesAsync(otherScope);
        if (use == "write")
            Assert.Single(cases).History.Should().ContainSingle();
        else
            cases.Should().BeEmpty();
        (await reopened.GetCasesAsync(_scope)).Should().ContainSingle();
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        File.Delete(ScopeStatePath(otherScope));
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.GetCasesAsync(otherScope));
    }

    [Fact]
    public async Task Unregistered_scope_file_that_does_not_name_its_own_scope_is_refused()
    {
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        var otherScope = _scope with { AccountId = "account-b" };
        File.Copy(ScopeStatePath(_scope), ScopeStatePath(otherScope));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetCasesAsync(otherScope));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(Request("other-first", 0, 500m, 500m, otherScope)));
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileAccountCashReconciliationStore(_root).InitializeAsync());
        (await service.GetCasesAsync(_scope)).Should().ContainSingle();
        Assert.Single(await (await ServiceAsyncWithoutProvisioning()).GetCasesAsync(_scope));
    }

    [Theory]
    [InlineData("layout-lost")]
    [InlineData("empty-index-left")]
    public async Task Empty_migration_baseline_beside_a_lost_layout_refuses_provisioning(string remains)
    {
        // An empty pre-partition store migrates to an empty .migrated file; a scope first used after
        // that records an empty baseline, but only once its active file exists.
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        var markerPath = Path.Combine(StoreDirectory, "initialized");
        var indexPath = Path.Combine(StoreDirectory, "scope-index.json");
        var legacyPath = Path.Combine(StoreDirectory, "state.json");
        var baselinePath = Path.Combine(StoreDirectory, "migration-baselines", FileAccountCashReconciliationStore.ScopeFileName(_scope) + ".json");
        File.Delete(markerPath);
        await File.WriteAllBytesAsync(legacyPath, await LegacyStateBytesAsync());
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        File.Exists(legacyPath + ".migrated").Should().BeTrue();
        var emptyIndex = await File.ReadAllBytesAsync(indexPath);
        var service = await ServiceAsync();
        (await service.GetCasesAsync(_scope)).Should().BeEmpty();
        File.Exists(baselinePath).Should().BeFalse("a queried scope without an active file leaves no baseline");
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        File.Exists(baselinePath).Should().BeTrue("the first commit persists the baseline beside the scope file before it returns");
        var baseline = await File.ReadAllBytesAsync(baselinePath);
        var backupScope = await File.ReadAllBytesAsync(ScopeStatePath(_scope));
        var backupIndex = await File.ReadAllBytesAsync(indexPath);

        // A partial restore keeps the empty migrated file and the empty baseline but loses the scope
        // file, with or without the marker and index; the baseline alone proves later history existed.
        Directory.Delete(Path.Combine(StoreDirectory, "scopes"), recursive: true);
        if (remains == "layout-lost")
        {
            File.Delete(markerPath);
            File.Delete(indexPath);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new FileAccountCashReconciliationStore(_root).InitializeAsync());
            File.Exists(markerPath).Should().BeFalse();
            File.Exists(indexPath).Should().BeFalse();
            var unprovisioned = await ServiceAsyncWithoutProvisioning();
            await Assert.ThrowsAsync<InvalidOperationException>(() => unprovisioned.GetCasesAsync(_scope));
        }
        else
        {
            await File.WriteAllBytesAsync(indexPath, emptyIndex);
            await Assert.ThrowsAsync<InvalidDataException>(() => new FileAccountCashReconciliationStore(_root).InitializeAsync());
            (await File.ReadAllBytesAsync(indexPath)).Should().Equal(emptyIndex);
            Directory.Exists(Path.Combine(StoreDirectory, "scopes")).Should().BeFalse();
        }
        (await File.ReadAllBytesAsync(baselinePath)).Should().Equal(baseline);

        // Restoring the complete scope layout and marker from the same backup set recovers the history without a reset.
        Directory.CreateDirectory(Path.Combine(StoreDirectory, "scopes"));
        await File.WriteAllBytesAsync(ScopeStatePath(_scope), backupScope);
        await File.WriteAllBytesAsync(indexPath, backupIndex);
        await File.WriteAllTextAsync(markerPath, "meridian-account-cash-reconciliation-v1");
        var restored = await ServiceAsync();
        (await restored.ReplayAsync(_scope, "first")).Input.RunId.Should().Be("first");
        (await restored.GetCasesAsync(_scope)).Should().ContainSingle();
    }

    [Fact]
    public async Task Interrupted_empty_legacy_split_resumes_on_the_next_provisioning()
    {
        // Provisioning an empty pre-partition store writes its empty index before renaming the legacy
        // file; a crash between the two leaves an empty unmarked legacy file beside an empty index.
        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        var markerPath = Path.Combine(StoreDirectory, "initialized");
        var legacyPath = Path.Combine(StoreDirectory, "state.json");
        File.Delete(markerPath);
        await File.WriteAllBytesAsync(legacyPath, await LegacyStateBytesAsync());

        await new FileAccountCashReconciliationStore(_root).InitializeAsync();
        File.Exists(markerPath).Should().BeTrue();
        File.Exists(legacyPath).Should().BeFalse();
        File.Exists(legacyPath + ".migrated").Should().BeTrue();
        var service = await ServiceAsync();
        await service.RunAsync(Request("first", 0, 1000m, 990m));
        (await service.GetCasesAsync(_scope)).Should().ContainSingle();

        // Retained history beside an empty index is still a recovery condition, not an interrupted split.
        var separateRoot = Path.Combine(Path.GetTempPath(), $"meridian-account-cash-{Guid.NewGuid():N}");
        await new FileAccountCashReconciliationStore(separateRoot).InitializeAsync();
        var separateDirectory = Path.Combine(separateRoot, "reconciliation", "account-cash-continuity");
        File.Delete(Path.Combine(separateDirectory, "initialized"));
        await File.WriteAllBytesAsync(Path.Combine(separateDirectory, "state.json"), await LegacyBytesFromSeparateRootAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FileAccountCashReconciliationStore(separateRoot).InitializeAsync());
        File.Exists(Path.Combine(separateDirectory, "initialized")).Should().BeFalse();
        Directory.Delete(separateRoot, recursive: true);
    }

    private string StoreDirectory => Path.Combine(_root, "reconciliation", "account-cash-continuity");
    private string ScopeStatePath(AccountCashReconciliationScope scope)
        => Path.Combine(StoreDirectory, "scopes", FileAccountCashReconciliationStore.ScopeFileName(scope) + ".json");
    private string ScopeLockPath(AccountCashReconciliationScope scope)
        => Path.Combine(StoreDirectory, "scopes", FileAccountCashReconciliationStore.ScopeFileName(scope) + ".lock");

    private Task<AccountCashReconciliationService> ServiceAsyncWithoutProvisioning()
        => Task.FromResult(new AccountCashReconciliationService(new FileAccountCashReconciliationStore(_root)));

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
