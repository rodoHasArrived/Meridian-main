using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
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
        var repo = CreateRepository(out var root);
        var item = CreateItem(status: ReconciliationBreakQueueStatus.Open, severity: ReconciliationBreakSeverity.Critical, requiredSignoffRole: "controller") with
        {
            BlockedOutputs = ["FinalReport", "PeriodClose"]
        };
        await repo.CreateIfMissingAsync(item);
        var reviewRequest = new ReviewReconciliationBreakRequest(item.BreakId, "alice", "alice", "triage");

        var review = await repo.StartReviewAsync(reviewRequest);
        var reviewReplay = await repo.StartReviewAsync(reviewRequest);
        var restartedAfterReview = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var restartedReviewReplay = await restartedAfterReview.StartReviewAsync(reviewRequest);

        review.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        review.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Investigating);
        review.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        review.Outcome.Evidence.Should().ContainSingle(evidence => evidence.Kind == "reconciliation-audit-event");
        VerifiedOperationOutcomeValidator.Validate(review.Outcome).Should().BeEmpty();
        reviewReplay.Should().BeEquivalentTo(review);
        restartedReviewReplay.Should().BeEquivalentTo(review);

        var resolveRequest = new ResolveReconciliationBreakRequest(
            item.BreakId,
            ReconciliationBreakQueueStatus.Resolved,
            "bob",
            "resolved",
            "evidence packet #42");
        var closed = await repo.ResolveAsync(resolveRequest);
        var resolveReplay = await repo.ResolveAsync(resolveRequest);
        var restartedAfterResolve = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var restartedResolveReplay = await restartedAfterResolve.ResolveAsync(resolveRequest);

        closed.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        closed.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Resolved);
        closed.Item.Disposition.Should().Be(ReconciliationBreakDispositionDto.Resolved);
        closed.Item.DispositionEvidenceHash.Should().HaveLength(64);
        closed.Item.EvidenceLinks.Should().Contain($"urn:sha256:{closed.Item.DispositionEvidenceHash}");
        closed.Item.BlockedOutputs.Should().BeEmpty();
        closed.Item.ResolutionCode.Should().Be("LegacyResolved");
        closed.Item.SignoffHistory.Should().NotBeNullOrEmpty();
        closed.Item.StateTransitions.Should().HaveCountGreaterThanOrEqualTo(2);
        closed.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        closed.Outcome.Evidence.Should().ContainSingle(evidence => evidence.Kind == "reconciliation-audit-event");
        VerifiedOperationOutcomeValidator.Validate(closed.Outcome).Should().BeEmpty();
        resolveReplay.Should().BeEquivalentTo(closed);
        restartedResolveReplay.Should().BeEquivalentTo(closed);
        (await restartedAfterResolve.CreateIfMissingAsync(item)).Should().BeFalse(
            "deterministic source seeding must compare the retained creation input, not resolved mutable state");

        var timestamps = closed.Item.StateTransitions!.Select(t => t.OccurredAt).ToArray();
        timestamps.Should().BeInAscendingOrder();
        var history = await restartedAfterResolve.GetAuditHistoryAsync(item.BreakId);
        history.Count(entry => entry.EventType == "ReviewStarted").Should().Be(1);
        history.Count(entry => entry.EventType == "Resolved").Should().Be(1);
        history.Count(entry => entry.EventType == "CaseworkReplayAccepted").Should().Be(4);
        history.Where(entry => entry.EventType is "ReviewStarted" or "Resolved")
            .Should().OnlyContain(entry =>
                !string.IsNullOrWhiteSpace(entry.CommandId) &&
                !string.IsNullOrWhiteSpace(entry.CorrelationId));
    }

    [Fact]
    public async Task ResolveAsync_WhenReviewedAutomationOrigin_RejectsBeforeMutationAndAppendsDenialAudit()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(status: ReconciliationBreakQueueStatus.Open, severity: ReconciliationBreakSeverity.Critical);
        await repo.CreateIfMissingAsync(item);
        await repo.StartReviewAsync(new ReviewReconciliationBreakRequest(item.BreakId, "controller-a", "controller-a", "triage"));

        var denied = await repo.ResolveAsync(new ResolveReconciliationBreakRequest(
            item.BreakId,
            ReconciliationBreakQueueStatus.Resolved,
            "assistant",
            "Automation suggested resolution.",
            "Automation should remain a draft.",
            ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        denied.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        denied.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MaterialActionRequiresHumanOperator);
        denied.Validation!.MissingFields.Should().Contain("actionOrigin");
        var retained = await repo.GetByIdAsync(item.BreakId);
        retained!.Status.Should().Be(ReconciliationBreakQueueStatus.InReview);
        retained.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Investigating);
        retained.ResolvedBy.Should().BeNull();
        var history = await repo.GetAuditHistoryAsync(item.BreakId);
        history.Should().ContainSingle(entry =>
            entry.EventType == "MaterialActionDenied" &&
            entry.Actor == "assistant" &&
            entry.PreviousStatus == ReconciliationBreakQueueStatus.InReview &&
            entry.NewStatus == ReconciliationBreakQueueStatus.InReview);
    }

    [Fact]
    public async Task Legacy_states_are_migrated_to_shared_casework_lifecycle()
    {
        var repo = CreateRepository(out _);
        var inReview = CreateItem(status: ReconciliationBreakQueueStatus.InReview) with
        {
            LifecycleState = ReconciliationCaseLifecycleState.InReview,
            AssignedTo = "ops-a"
        };
        var dismissed = CreateItem(status: ReconciliationBreakQueueStatus.Dismissed) with
        {
            LifecycleState = ReconciliationCaseLifecycleState.Superseded
        };

        await repo.CreateIfMissingAsync(inReview);
        await repo.CreateIfMissingAsync(dismissed);

        var migratedReview = await repo.GetByIdAsync(inReview.BreakId);
        migratedReview!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Investigating);
        migratedReview.Status.Should().Be(ReconciliationBreakQueueStatus.InReview);

        var migratedDismissed = await repo.GetByIdAsync(dismissed.BreakId);
        migratedDismissed!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Resolved);
        migratedDismissed.Status.Should().Be(ReconciliationBreakQueueStatus.Resolved);
        migratedDismissed.RootCauseCode.Should().Be("DismissedFalsePositive");
        migratedDismissed.ResolutionCode.Should().Be("DismissedFalsePositive");
    }

    [Fact]
    public async Task CreateOrMigrateAsync_rekeys_superseded_break_id_and_preserves_casework_lineage()
    {
        var repo = CreateRepository(out _);
        var legacy = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "statement:old-fingerprint",
            SourceType = "statement",
            SourceBreakId = "upstream-break-1",
            SourceFingerprint = "old-fingerprint",
            AssignedTo = "controller-a"
        };
        await repo.CreateIfMissingAsync(legacy);
        // Human casework on the legacy id whose lineage must survive the re-key.
        await repo.StartReviewAsync(new ReviewReconciliationBreakRequest(legacy.BreakId, "controller-a", "controller-a", "triage"));

        var ledgerBookId = Guid.NewGuid();
        var accountingPeriodId = Guid.NewGuid();
        var asOfDate = new DateOnly(2026, 7, 31);
        var reseeded = legacy with
        {
            BreakId = "statement:new-fingerprint",
            SourceFingerprint = "new-fingerprint",
            LedgerBookId = ledgerBookId,
            AccountingPeriodId = accountingPeriodId.ToString("D"),
            AsOfDate = asOfDate,
            BlockedOutputs = ["FinalReport", "PeriodClose"],
            FundProfileId = "fund-alpha"
        };
        var created = await repo.CreateOrMigrateAsync(reseeded, previousBreakId: "statement:old-fingerprint");

        created.Should().BeFalse(); // a migration, not a brand-new case
        (await repo.GetByIdAsync("statement:old-fingerprint")).Should().BeNull();

        var migrated = await repo.GetByIdAsync("statement:new-fingerprint");
        migrated.Should().NotBeNull();
        migrated!.AssignedTo.Should().Be("controller-a");
        migrated.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Investigating);
        migrated.SourceFingerprint.Should().Be("new-fingerprint");
        migrated.FundProfileId.Should().Be("fund-alpha");
        migrated.LedgerBookId.Should().Be(ledgerBookId);
        migrated.AccountingPeriodId.Should().Be(accountingPeriodId.ToString("D"));
        migrated.AsOfDate.Should().Be(asOfDate);
        migrated.BlockedOutputs.Should().Equal("FinalReport", "PeriodClose");

        // The full audit trail follows the re-key: the migration event plus the pre-migration rows
        // that remain stored under the superseded id are all returned under the new id.
        var history = await repo.GetAuditHistoryAsync("statement:new-fingerprint");
        history.Should().Contain(e => e.EventType == "BreakIdMigrated" && e.Note!.Contains("statement:old-fingerprint"));
        history.Should().Contain(e => e.EventType == "CaseCreated");
        history.Should().Contain(e => e.EventType == "ReviewStarted");
        history.Select(e => e.Sequence).Should().BeInAscendingOrder();

        // Rebuild from the linked trail still resolves to the current, re-keyed case.
        var rebuilt = await repo.RebuildSnapshotFromAuditAsync("statement:new-fingerprint");
        rebuilt!.BreakId.Should().Be("statement:new-fingerprint");
    }

    [Fact]
    public async Task CreateOrMigrateAsync_does_not_rekey_when_current_break_id_already_exists()
    {
        var repo = CreateRepository(out _);
        var legacy = CreateItem(ReconciliationBreakQueueStatus.Open) with { BreakId = "statement:old", AssignedTo = "controller-a" };
        var current = CreateItem(ReconciliationBreakQueueStatus.Open) with { BreakId = "statement:new" };
        await repo.CreateIfMissingAsync(legacy);
        await repo.CreateIfMissingAsync(current);

        var conflict = () => repo.CreateOrMigrateAsync(current with { SourceFingerprint = "new" }, previousBreakId: "statement:old");

        await conflict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different source or scope input*");
        // The already-present current case wins; the legacy case is left untouched (no destructive merge).
        (await repo.GetByIdAsync("statement:old")).Should().NotBeNull();
        (await repo.GetByIdAsync("statement:new")).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateOrMigrateAsync_rejects_conflicting_retained_accounting_scope()
    {
        var repo = CreateRepository(out _);
        var legacy = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "statement:scoped-old",
            SourceType = "statement",
            SourceBreakId = "upstream-break-scoped",
            SourceFingerprint = "scoped-old",
            LedgerBookId = Guid.NewGuid(),
            AccountingPeriodId = Guid.NewGuid().ToString("D"),
            AsOfDate = new DateOnly(2026, 6, 30),
            FundProfileId = "fund-alpha"
        };
        await repo.CreateIfMissingAsync(legacy);
        var incoming = legacy with
        {
            BreakId = "statement:scoped-new",
            SourceFingerprint = "scoped-new",
            LedgerBookId = Guid.NewGuid()
        };

        var migrate = () => repo.CreateOrMigrateAsync(incoming, legacy.BreakId);

        await migrate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different tenant, company, or accounting close scope*");
        (await repo.GetByIdAsync(legacy.BreakId)).Should().NotBeNull();
        (await repo.GetByIdAsync(incoming.BreakId)).Should().BeNull();
    }

    [Fact]
    public async Task CreateIfMissing_exact_retry_is_idempotent_but_changed_source_or_scope_is_audited_conflict()
    {
        var repo = CreateRepository(out var root);
        var ledgerBookId = Guid.NewGuid();
        var item = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "create-bound-break",
            SourceType = "provider-ledger",
            SourceFingerprint = "fingerprint-a",
            LedgerBookId = ledgerBookId,
            AccountingPeriodId = "2026-07",
            AsOfDate = new DateOnly(2026, 7, 19)
        };

        (await repo.CreateIfMissingAsync(item)).Should().BeTrue();
        (await repo.CreateIfMissingAsync(item with { RunId = "later-run", LastUpdatedAt = DateTimeOffset.UtcNow.AddMinutes(1) })).Should().BeFalse();
        var conflict = () => repo.CreateIfMissingAsync(item with { SourceFingerprint = "fingerprint-b" });
        await conflict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different source or scope input*");

        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await restarted.GetByIdAsync(item.BreakId))!.SourceFingerprint.Should().Be("fingerprint-a");
        (await restarted.RebuildSnapshotFromAuditAsync(item.BreakId))!.SourceFingerprint.Should().Be("fingerprint-a");
        var history = await restarted.GetAuditHistoryAsync(item.BreakId);
        history.Should().ContainSingle(entry => entry.EventType == "CreateReplayAccepted");
        history.Should().ContainSingle(entry => entry.EventType == "CreateConflict");
    }

    [Fact]
    public async Task CreateOrMigrateAsync_does_not_rekey_a_legacy_case_owned_by_a_different_source_break()
    {
        var repo = CreateRepository(out _);
        // A legacy Delta==null case seeded under a shared fingerprint id, owned by upstream break B.
        var legacy = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "statement:shared-legacy",
            SourceType = "statement",
            SourceBreakId = "upstream-break-B",
            SourceFingerprint = "shared-legacy",
            AssignedTo = "controller-b"
        };
        await repo.CreateIfMissingAsync(legacy);

        // A different upstream break A that collides on the same legacy fingerprint id.
        var breakA = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "statement:new-A",
            SourceType = "statement",
            SourceBreakId = "upstream-break-A",
            SourceFingerprint = "new-A"
        };

        var createdA = await repo.CreateOrMigrateAsync(breakA, previousBreakId: "statement:shared-legacy");

        // Break A must not steal break B's case: it is created fresh, and the legacy case is untouched.
        createdA.Should().BeTrue();
        (await repo.GetByIdAsync("statement:shared-legacy")).Should().NotBeNull();
        (await repo.GetByIdAsync("statement:new-A"))!.AssignedTo.Should().BeNull();

        // Break B, sharing the legacy source identity, correctly re-keys its own case.
        var breakB = legacy with { BreakId = "statement:new-B", SourceFingerprint = "new-B" };
        var migratedB = await repo.CreateOrMigrateAsync(breakB, previousBreakId: "statement:shared-legacy");
        migratedB.Should().BeFalse();
        (await repo.GetByIdAsync("statement:shared-legacy")).Should().BeNull();
        (await repo.GetByIdAsync("statement:new-B"))!.AssignedTo.Should().Be("controller-b");
    }

    [Fact]
    public async Task CreateOrMigrateAsync_creates_a_new_case_when_no_superseded_case_exists()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open) with { BreakId = "statement:new" };

        var created = await repo.CreateOrMigrateAsync(item, previousBreakId: "statement:absent-old");

        created.Should().BeTrue();
        (await repo.GetByIdAsync("statement:new")).Should().NotBeNull();
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
        history.Should().HaveCount(4);
        history.Select(x => x.OccurredAt).Should().BeInAscendingOrder();
        history.Select(x => x.EventType).Should().Contain("CaseCreated");
        history.Select(x => x.EventType).Should().Contain("Assigned");
        history.Select(x => x.EventType).Should().Contain("ReviewStarted");
        history.Select(x => x.EventType).Should().Contain("Resolved");

        history.Select(x => x.Sequence).Should().Equal(1, 2, 3, 4);
        history.Should().OnlyContain(x =>
            !string.IsNullOrWhiteSpace(x.EventId) &&
            !string.IsNullOrWhiteSpace(x.AfterPayloadHash));

        var snapshotPath = Path.Combine(root, "reconciliation-break-queue.json");
        using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(snapshotPath)))
        {
            var snapshot = doc.RootElement;
            snapshot.GetProperty("schemaVersion").GetInt32().Should().Be(5);
            snapshot.GetProperty("contentHashSha256").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
            var retainedEvents = snapshot.GetProperty("auditEvents");
            retainedEvents.GetArrayLength().Should().Be(history.Count);
            foreach (var retainedEvent in retainedEvents.EnumerateArray())
            {
                retainedEvent.GetProperty("eventId").GetString().Should().NotBeNullOrWhiteSpace();
                retainedEvent.GetProperty("breakId").GetString().Should().NotBeNullOrWhiteSpace();
                retainedEvent.GetProperty("eventType").GetString().Should().NotBeNullOrWhiteSpace();
                retainedEvent.GetProperty("newStatus").GetString().Should().NotBeNullOrWhiteSpace();
                retainedEvent.GetProperty("newLifecycleState").GetString().Should().NotBeNullOrWhiteSpace();
                retainedEvent.GetProperty("occurredAt").GetString().Should().NotBeNullOrWhiteSpace();
                retainedEvent.GetProperty("sequence").GetInt64().Should().BeGreaterThan(0);
                retainedEvent.GetProperty("afterPayloadHash").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
            }
        }

        var rebuilt = await repo.RebuildSnapshotFromAuditAsync(item.BreakId);
        rebuilt.Should().NotBeNull();
        rebuilt!.BreakId.Should().Be(item.BreakId);
        rebuilt.Version.Should().Be((await repo.GetByIdAsync(item.BreakId))!.Version);
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
    public async Task ScopedQueueOperations_TwoTenantMonthEndCasework_PreventCrossTenantVisibilityAndMutation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        var repo = CreateRepository(out var root);
        var tenantA = new ReconciliationBreakQueueScope("tenant-alpha", "company-alpha");
        var tenantB = new ReconciliationBreakQueueScope("tenant-beta", "company-beta");
        var itemA = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "tenant-isolation:case-0001",
            TenantId = tenantA.TenantId,
            CompanyId = tenantA.CompanyId
        };
        var itemB = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "tenant-isolation:case-0002",
            TenantId = tenantB.TenantId,
            CompanyId = tenantB.CompanyId
        };
        var legacyUnscoped = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "tenant-isolation:case-legacy"
        };

        try
        {
            (await repo.CreateIfMissingAsync(tenantA, itemA, ct)).Should().BeTrue();
            (await repo.CreateIfMissingAsync(tenantB, itemB, ct)).Should().BeTrue();
            (await repo.CreateIfMissingAsync(legacyUnscoped, ct)).Should().BeTrue();

            (await repo.GetAllAsync(tenantA, ct: ct))
                .Should().ContainSingle()
                .Which.BreakId.Should().Be(itemA.BreakId);
            (await repo.GetAllAsync(tenantB, ct: ct))
                .Should().ContainSingle()
                .Which.BreakId.Should().Be(itemB.BreakId);

            var retainedA = await repo.GetByIdAsync(tenantA, itemA.BreakId, ct);
            var retainedB = await repo.GetByIdAsync(tenantB, itemB.BreakId, ct);
            retainedA.Should().NotBeNull();
            retainedB.Should().NotBeNull();
            (await repo.GetByIdAsync(tenantA, itemB.BreakId, ct)).Should().BeNull();
            (await repo.GetByIdAsync(tenantB, itemA.BreakId, ct)).Should().BeNull();
            (await repo.GetByIdAsync(tenantA, legacyUnscoped.BreakId, ct)).Should().BeNull();
            (await repo.GetByIdAsync(tenantB, legacyUnscoped.BreakId, ct)).Should().BeNull();

            var tenantAAudit = await repo.GetAuditHistoryAsync(tenantA, itemA.BreakId, ct);
            tenantAAudit.Should().ContainSingle(entry => entry.EventType == "CaseCreated");
            tenantAAudit.Should().OnlyContain(entry =>
                entry.TenantId == tenantA.TenantId && entry.CompanyId == tenantA.CompanyId);
            (await repo.GetAuditHistoryAsync(tenantB, itemA.BreakId, ct)).Should().BeEmpty();
            (await repo.GetAuditHistoryAsync(tenantA, itemB.BreakId, ct)).Should().BeEmpty();
            (await repo.GetAuditHistoryAsync(tenantA, legacyUnscoped.BreakId, ct)).Should().BeEmpty();

            var foreignSingleCommand = Command(retainedA!, ReconciliationCaseworkAction.Assign) with
            {
                CommandId = "tenant-beta-cross-tenant-single",
                CorrelationId = "tenant-beta-cross-tenant-single",
                Actor = "tenant-beta-operator",
                Assignee = "tenant-beta-operator"
            };
            var foreignSingle = await repo.ApplyCaseworkCommandAsync(
                tenantB,
                foreignSingleCommand,
                ct);
            foreignSingle.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.NotFound);
            foreignSingle.Item.Should().BeNull();
            (await repo.GetByIdAsync(tenantA, itemA.BreakId, ct))!.AssignedTo.Should().BeNull();

            var tenantASingleCommand = Command(retainedA!, ReconciliationCaseworkAction.Assign) with
            {
                CommandId = "tenant-alpha-own-single",
                CorrelationId = "tenant-alpha-own-single",
                Actor = "tenant-alpha-operator",
                Assignee = "tenant-alpha-operator"
            };
            var tenantASingle = await repo.ApplyCaseworkCommandAsync(
                tenantA,
                tenantASingleCommand,
                ct);
            tenantASingle.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
            tenantASingle.Item!.AssignedTo.Should().Be("tenant-alpha-operator");

            var bulkRequest = new ReconciliationBulkCaseworkRequest(
                BreakIds: [itemA.BreakId, itemB.BreakId],
                Action: ReconciliationCaseworkAction.ChangePriority,
                Actor: "tenant-alpha-operator",
                CommandId: "tenant-alpha-mixed-scope-bulk",
                CorrelationId: "tenant-alpha-mixed-scope-bulk",
                Source: "tenant-isolation-test",
                IdempotencyKey: "tenant-alpha-mixed-scope-bulk",
                DryRun: false,
                AllowPartialSuccess: true,
                Reason: "Escalate the tenant-alpha month-end break.",
                Priority: ReconciliationCasePriority.Critical);
            var bulk = await repo.ApplyBulkCaseworkAsync(tenantA, bulkRequest, ct);

            bulk.SucceededCount.Should().Be(1);
            bulk.FailedCount.Should().Be(1);
            bulk.Results.Single(result => result.BreakId == itemA.BreakId)
                .Should().Match<ReconciliationBulkCaseworkCaseResult>(
                    result => result.Succeeded
                              && result.Item != null
                              && result.Item.Priority == ReconciliationCasePriority.Critical);
            bulk.Results.Single(result => result.BreakId == itemB.BreakId)
                .Should().Match<ReconciliationBulkCaseworkCaseResult>(
                    result => !result.Succeeded && result.Item == null);

            (await repo.GetByIdAsync(tenantA, itemA.BreakId, ct))!.Priority
                .Should().Be(ReconciliationCasePriority.Critical);
            var tenantBFinal = await repo.GetByIdAsync(tenantB, itemB.BreakId, ct);
            tenantBFinal!.Priority.Should().Be(ReconciliationCasePriority.Normal);
            tenantBFinal.AssignedTo.Should().BeNull();
            (await repo.GetByIdAsync(tenantB, itemA.BreakId, ct)).Should().BeNull();

            (await repo.GetBulkCaseworkResultAsync(tenantA, bulkRequest.CommandId, ct))
                .Should().NotBeNull();
            (await repo.GetBulkCaseworkResultAsync(tenantB, bulkRequest.CommandId, ct))
                .Should().BeNull();
            (await repo.GetAuditHistoryAsync(tenantB, itemB.BreakId, ct))
                .Should().NotContain(entry =>
                    entry.CommandId == foreignSingleCommand.CommandId
                    || entry.CommandId == bulkRequest.CommandId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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

        var investigating = await repo.ApplyCaseworkCommandAsync(Command(assigned.Item, ReconciliationCaseworkAction.TransitionStatus) with
        {
            Status = ReconciliationCaseLifecycleState.Investigating
        });
        investigating.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);

        var addCommentCommand = Command(investigating.Item!, ReconciliationCaseworkAction.AddComment) with
        {
            CommentId = "comment-1",
            Note = "Need custodian close packet.",
            Visibility = ReconciliationCaseCommentVisibility.CloseEvidence,
            EvidenceLinks = ["evidence://close/packet-1"],
            Mentions = ["@custody"],
            StatusTransition = ReconciliationCaseLifecycleState.AwaitingEvidence
        };
        var comment = await repo.ApplyCaseworkCommandAsync(addCommentCommand);
        comment.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        comment.Item!.CommentCount.Should().Be(1);
        comment.Item.EvidenceCount.Should().Be(1);
        comment.Item.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.AwaitingEvidence);
        comment.Item.Comments![0].Mentions.Should().Contain("@custody");

        var wrongCaseCommentId = await repo.ApplyCaseworkCommandAsync(Command(comment.Item, ReconciliationCaseworkAction.EditComment) with
        {
            CommentId = "Comment-1",
            Note = "Must not match a different case-sensitive comment id."
        });
        wrongCaseCommentId.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);

        var unauthorizedEdit = await repo.ApplyCaseworkCommandAsync(Command(comment.Item, ReconciliationCaseworkAction.EditComment) with
        {
            Actor = "controller-b",
            CommentId = "comment-1",
            Note = "Another operator must not rewrite this comment."
        });
        unauthorizedEdit.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        unauthorizedEdit.Error.Should().Contain("author");

        var privilegedEdit = await repo.ApplyCaseworkCommandAsync(Command(comment.Item, ReconciliationCaseworkAction.EditComment) with
        {
            Actor = "controller-b",
            Privileged = true,
            CommentId = "comment-1",
            Note = "Privileged correction with retained audit evidence.",
            Reason = "Governed correction."
        });
        privilegedEdit.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);

        var edited = await repo.ApplyCaseworkCommandAsync(Command(privilegedEdit.Item!, ReconciliationCaseworkAction.EditComment) with
        {
            CommentId = "comment-1",
            Note = "Need custodian close packet and provider-record:abc.",
            Reason = "Clarify requested source."
        });
        edited.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        edited.Item!.Comments![0].PreviousTextHash.Should().NotBeNullOrWhiteSpace();

        var deletedWithoutReason = await repo.ApplyCaseworkCommandAsync(Command(edited.Item, ReconciliationCaseworkAction.DeleteComment) with { CommentId = "comment-1", Reason = " " });
        deletedWithoutReason.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingReason);

        var deleted = await repo.ApplyCaseworkCommandAsync(Command(edited.Item, ReconciliationCaseworkAction.DeleteComment) with { CommentId = "comment-1", Reason = "Duplicate request." });
        deleted.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        deleted.Item!.Comments![0].DeleteReason.Should().Be("Duplicate request.");

        var repeatedDelete = await repo.ApplyCaseworkCommandAsync(Command(deleted.Item, ReconciliationCaseworkAction.DeleteComment) with { CommentId = "comment-1", Reason = "Repeated deletion must fail." });
        repeatedDelete.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);

        var history = await repo.GetAuditHistoryAsync(item.BreakId);
        history.Should().Contain(e => e.EventType == "Assigned" && e.CommandId != null && e.CorrelationId != null && e.SchemaVersion == 1 && e.Sequence > 0);
        history.Should().Contain(e => e.EventType == "CommentAdded" && e.AfterPayload != null && e.AfterPayloadHash != null);

        var retried = await repo.ApplyCaseworkCommandAsync(addCommentCommand);
        retried.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        retried.Item!.Version.Should().Be(comment.Item.Version);
    }

    [Fact]
    public async Task Terminal_cases_reject_mutation_until_the_dedicated_governed_transition()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open) with { AssignedTo = "controller-a" };
        await repo.CreateIfMissingAsync(item);
        var current = (await repo.GetByIdAsync(item.BreakId))!;
        var investigating = await repo.ApplyCaseworkCommandAsync(Command(current, ReconciliationCaseworkAction.TransitionStatus) with
        {
            Status = ReconciliationCaseLifecycleState.Investigating
        });
        var resolved = await repo.ApplyCaseworkCommandAsync(Command(investigating.Item!, ReconciliationCaseworkAction.Resolve) with
        {
            RootCauseCode = "BrokerCashTiming",
            ResolutionCode = "LedgerAdjusted",
            Note = "Corrected from retained ledger evidence.",
            EvidenceLinks = ["ledger-event:terminal-case"]
        });

        var resolvedMutation = await repo.ApplyCaseworkCommandAsync(Command(resolved.Item!, ReconciliationCaseworkAction.Assign) with
        {
            Assignee = "controller-c"
        });
        resolvedMutation.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.IllegalTransition);
        resolvedMutation.Outcome.State.Should().Be(OperationTerminalState.Blocked);

        var signedOff = await repo.ApplyCaseworkCommandAsync(Command(resolved.Item!, ReconciliationCaseworkAction.SignOff) with
        {
            Actor = "controller-b",
            Note = "Independent review complete."
        });
        signedOff.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);

        var signedOffMutation = await repo.ApplyCaseworkCommandAsync(Command(signedOff.Item!, ReconciliationCaseworkAction.AddComment) with
        {
            CommentId = "late-comment",
            Note = "Must be added only after governed reopen."
        });
        signedOffMutation.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.IllegalTransition);

        var reopened = await repo.ApplyCaseworkCommandAsync(Command(signedOff.Item!, ReconciliationCaseworkAction.Reopen) with
        {
            Actor = "controller-manager",
            Privileged = true,
            Reason = "Late custodian correction requires renewed investigation."
        });
        reopened.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        var reopenedMutation = await repo.ApplyCaseworkCommandAsync(Command(reopened.Item!, ReconciliationCaseworkAction.Assign) with
        {
            Assignee = "controller-c"
        });
        reopenedMutation.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);

        var supersededSource = CreateItem(ReconciliationBreakQueueStatus.Open) with { BreakId = "terminal-superseded-source" };
        var successor = CreateItem(ReconciliationBreakQueueStatus.Open) with { BreakId = "terminal-superseded-successor" };
        await repo.CreateIfMissingAsync(supersededSource);
        await repo.CreateIfMissingAsync(successor);
        var sourceCurrent = (await repo.GetByIdAsync(supersededSource.BreakId))!;
        var superseded = await repo.ApplyCaseworkCommandAsync(Command(sourceCurrent, ReconciliationCaseworkAction.Supersede) with
        {
            Reason = "Corrected source created a replacement break.",
            EvidenceLinks = ["evidence:terminal-supersession"],
            ApprovalActor = "controller-b",
            ApprovalReference = "approval:terminal-supersession",
            SupersedingBreakId = successor.BreakId
        });
        superseded.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        var supersededMutation = await repo.ApplyCaseworkCommandAsync(Command(superseded.Item!, ReconciliationCaseworkAction.ChangePriority) with
        {
            Priority = ReconciliationCasePriority.Critical
        });
        supersededMutation.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.IllegalTransition);
    }

    [Fact]
    public async Task Resolve_signoff_reopen_and_bulk_dry_run_follow_shared_casework_rules()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open) with { AssignedTo = "controller-a" };
        await repo.CreateIfMissingAsync(item);
        var current = (await repo.GetByIdAsync(item.BreakId))!;
        var investigating = await repo.ApplyCaseworkCommandAsync(Command(current, ReconciliationCaseworkAction.TransitionStatus) with
        {
            Status = ReconciliationCaseLifecycleState.Investigating
        });
        investigating.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);

        var missingTaxonomy = await repo.ApplyCaseworkCommandAsync(Command(investigating.Item!, ReconciliationCaseworkAction.Resolve));
        missingTaxonomy.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingRootCause);

        var invalidTaxonomy = await repo.ApplyCaseworkCommandAsync(Command(investigating.Item!, ReconciliationCaseworkAction.Resolve) with
        {
            RootCauseCode = "UnknownBrokerExcuse",
            ResolutionCode = "LedgerAdjusted",
            Note = "Invalid taxonomy should not close."
        });
        invalidTaxonomy.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy);

        var rootCause = await repo.ApplyCaseworkCommandAsync(Command(investigating.Item!, ReconciliationCaseworkAction.SetRootCause) with { RootCauseCode = "BrokerCashTiming" });
        var resolution = await repo.ApplyCaseworkCommandAsync(Command(rootCause.Item!, ReconciliationCaseworkAction.SetResolution) with { ResolutionCode = "LedgerAdjusted", Note = "Adjusted ledger lot." });
        var missingDispositionReason = await repo.ApplyCaseworkCommandAsync(Command(resolution.Item!, ReconciliationCaseworkAction.Resolve) with
        {
            Reason = " ",
            Note = null,
            EvidenceLinks = ["ledger-event:close-without-reason"]
        });
        missingDispositionReason.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingReason);
        missingDispositionReason.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        var missingResolutionEvidence = await repo.ApplyCaseworkCommandAsync(Command(resolution.Item!, ReconciliationCaseworkAction.Resolve) with { Note = "Resolved with close packet." });
        missingResolutionEvidence.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingEvidence);
        missingResolutionEvidence.Validation!.MissingFields.Should().Contain("evidenceLinks");

        var automationResolve = await repo.ApplyCaseworkCommandAsync(Command(resolution.Item!, ReconciliationCaseworkAction.Resolve) with
        {
            Actor = "assistant",
            Note = "Automation suggested resolution.",
            EvidenceLinks = ["ledger-event:automation-close"],
            ActionOrigin = OperationsActionOriginDto.AutomationAssistant
        });
        automationResolve.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        automationResolve.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MaterialActionRequiresHumanOperator);
        (await repo.GetByIdAsync(item.BreakId))!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Investigating);
        (await repo.GetAuditHistoryAsync(item.BreakId)).Should().Contain(entry =>
            entry.EventType == "MaterialActionDenied" &&
            entry.Actor == "assistant" &&
            entry.Reason!.Contains("Reviewed automation", StringComparison.OrdinalIgnoreCase));

        var resolved = await repo.ApplyCaseworkCommandAsync(Command(resolution.Item!, ReconciliationCaseworkAction.Resolve) with
        {
            Note = "Resolved with close packet.",
            EvidenceLinks = ["ledger-event:close-1"]
        });
        resolved.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        resolved.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Resolved);

        var sameSigner = await repo.ApplyCaseworkCommandAsync(Command(resolved.Item, ReconciliationCaseworkAction.SignOff));
        sameSigner.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.ResolverSignerConflict);

        var privilegedSameSigner = await repo.ApplyCaseworkCommandAsync(Command(resolved.Item, ReconciliationCaseworkAction.SignOff) with
        {
            Privileged = true,
            Reason = "Emergency close override."
        });
        privilegedSameSigner.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);

        var reopenedForIndependentSignoff = await repo.ApplyCaseworkCommandAsync(Command(privilegedSameSigner.Item!, ReconciliationCaseworkAction.Reopen) with
        {
            Actor = "controller-manager",
            Privileged = true,
            Reason = "Route to independent signer after override test."
        });
        var investigatingAgain = await repo.ApplyCaseworkCommandAsync(Command(reopenedForIndependentSignoff.Item!, ReconciliationCaseworkAction.TransitionStatus) with { Status = ReconciliationCaseLifecycleState.Investigating });
        var resolvedAgain = await repo.ApplyCaseworkCommandAsync(Command(investigatingAgain.Item!, ReconciliationCaseworkAction.Resolve) with
        {
            RootCauseCode = "BrokerCashTiming",
            ResolutionCode = "LedgerAdjusted",
            EvidenceLinks = ["ledger-event:close-2"],
            Note = "Resolved again."
        });

        var signoff = await repo.ApplyCaseworkCommandAsync(Command(resolvedAgain.Item!, ReconciliationCaseworkAction.SignOff) with { Actor = "controller-b", Note = "Independent review complete." });
        signoff.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        signoff.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.SignedOff);
        (await repo.GetAuditHistoryAsync(item.BreakId)).Should().Contain(e => e.EventType == "SignedOff");

        var reopenWithoutReason = await repo.ApplyCaseworkCommandAsync(Command(signoff.Item, ReconciliationCaseworkAction.Reopen) with { Actor = "controller-manager", Privileged = true, Reason = " " });
        reopenWithoutReason.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingReason);

        var transitionReopenCommand = Command(signoff.Item, ReconciliationCaseworkAction.TransitionStatus) with
        {
            Actor = "controller-manager",
            Status = ReconciliationCaseLifecycleState.Reopened,
            Privileged = true,
            Reason = " "
        };
        var transitionReopenWithoutReason = await repo.ApplyCaseworkCommandAsync(transitionReopenCommand);
        transitionReopenWithoutReason.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        transitionReopenWithoutReason.Validation!.MissingFields.Should().Contain("action");
        (await repo.GetAuditHistoryAsync(item.BreakId)).Should().Contain(entry =>
            entry.CommandId == transitionReopenCommand.CommandId &&
            entry.EventType == "CaseworkRejected");

        var reopened = await repo.ApplyCaseworkCommandAsync(Command(signoff.Item, ReconciliationCaseworkAction.Reopen) with { Actor = "controller-manager", Privileged = true, Reason = "Late broker correction." });
        reopened.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        reopened.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);

        var automationBulkResolve = await repo.ApplyBulkCaseworkAsync(new ReconciliationBulkCaseworkRequest(
            BreakIds: [item.BreakId],
            Action: ReconciliationCaseworkAction.Resolve,
            Actor: "assistant",
            CommandId: "bulk-automation",
            CorrelationId: "corr-bulk-automation",
            Source: "test",
            IdempotencyKey: "idem-automation",
            DryRun: false,
            AllowPartialSuccess: true,
            Note: "Automation suggested reopened-case resolution.",
            RootCauseCode: "BrokerCashTiming",
            ResolutionCode: "LedgerAdjusted",
            ActionOrigin: OperationsActionOriginDto.AutomationAssistant));
        automationBulkResolve.FailedCount.Should().Be(1);
        automationBulkResolve.Results.Should().ContainSingle(result =>
            result.BreakId == item.BreakId &&
            !result.Succeeded &&
            result.Error!.Contains("Reviewed automation", StringComparison.OrdinalIgnoreCase));
        (await repo.GetByIdAsync(item.BreakId))!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);

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

        var conflictingRetry = await repo.ApplyBulkCaseworkAsync(dryRunRequest with { Priority = ReconciliationCasePriority.Low });
        conflictingRetry.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        conflictingRetry.Outcome.Issues.Should().ContainSingle(issue =>
            issue.Code == ReconciliationBreakQueueTransitionErrorCode.IdempotencyConflict.ToString());
        VerifiedOperationOutcomeValidator.Validate(conflictingRetry.Outcome).Should().BeEmpty();

        var cachedByBulkId = await repo.GetBulkCaseworkResultAsync(dryRun.BulkActionId);
        cachedByBulkId.Should().BeEquivalentTo(dryRun);

        var cachedByIdempotencyKey = await repo.GetBulkCaseworkResultAsync(dryRun.IdempotencyKey);
        cachedByIdempotencyKey.Should().BeEquivalentTo(dryRun);
    }

    [Fact]
    public async Task Resolve_signoff_reopen_clears_terminal_snapshot_and_projects_as_exact_open_reporting_evidence()
    {
        var repo = CreateRepository(out _);
        var ledgerBookId = Guid.NewGuid();
        var accountingPeriodId = Guid.NewGuid();
        var asOfDate = new DateOnly(2026, 7, 19);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open, ReconciliationBreakSeverity.High) with
        {
            AssignedTo = "controller-a",
            FundAccountId = "fund-1",
            FundProfileId = "fund-1",
            ExternalAccountId = "account-1",
            LedgerBookId = ledgerBookId,
            AccountingPeriodId = accountingPeriodId.ToString("D"),
            AsOfDate = asOfDate,
            SourceFingerprint = "source-fingerprint-1",
            Measures =
            [
                new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.Value, 100m, 112m, 12m, 1m, "USD"),
                new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.Quantity, 10m, 11m, 1m, 0m, "units"),
                new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.CostBasis, 80m, 84m, 4m, 1m, "USD")
            ],
            BlockedOutputs = ["FinalReport", "PeriodClose"]
        };
        await repo.CreateIfMissingAsync(item);
        var current = (await repo.GetByIdAsync(item.BreakId))!;
        var investigating = await repo.ApplyCaseworkCommandAsync(
            Command(current, ReconciliationCaseworkAction.TransitionStatus) with
            {
                Status = ReconciliationCaseLifecycleState.Investigating
            });
        var resolved = await repo.ApplyCaseworkCommandAsync(
            Command(investigating.Item!, ReconciliationCaseworkAction.Resolve) with
            {
                RootCauseCode = "BrokerCashTiming",
                ResolutionCode = "LedgerAdjusted",
                Note = "Resolved against the retained close packet.",
                EvidenceLinks = ["ledger-event:close-reopen-1"]
            });
        var signedOff = await repo.ApplyCaseworkCommandAsync(
            Command(resolved.Item!, ReconciliationCaseworkAction.SignOff) with
            {
                Actor = "controller-b",
                Note = "Independent sign-off complete."
            });
        var reopenCommand = Command(signedOff.Item!, ReconciliationCaseworkAction.Reopen) with
        {
            Actor = "controller-manager",
            CommandId = "reopen-clears-terminal-snapshot",
            Privileged = true,
            Reason = "Late broker correction requires a new governed resolution."
        };

        var reopened = await repo.ApplyCaseworkCommandAsync(reopenCommand);
        var exactReplay = await repo.ApplyCaseworkCommandAsync(reopenCommand);

        reopened.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        exactReplay.Should().BeEquivalentTo(reopened);
        reopened.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);
        reopened.Item.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
        reopened.Item.RootCauseCode.Should().Be("BrokerCashTiming");
        reopened.Item.ResolutionCode.Should().BeNull();
        reopened.Item.ResolvedBy.Should().BeNull();
        reopened.Item.ResolvedAt.Should().BeNull();
        reopened.Item.ResolutionNote.Should().BeNull();
        reopened.Item.SignedOffBy.Should().BeNull();
        reopened.Item.SignedOffAt.Should().BeNull();
        reopened.Item.SignOffNote.Should().BeNull();
        reopened.Item.SignoffStatus.Should().BeNull();
        reopened.Item.Disposition.Should().BeNull();
        reopened.Item.DispositionReason.Should().BeNull();
        reopened.Item.SupersedingBreakId.Should().BeNull();
        reopened.Item.DispositionApprovedBy.Should().BeNull();
        reopened.Item.DispositionApprovalReference.Should().BeNull();
        reopened.Item.DispositionEvidenceHash.Should().BeNull();
        reopened.Item.DisposedAt.Should().BeNull();
        reopened.Item.BlockedOutputs.Should().BeEquivalentTo("FinalReport", "PeriodClose");

        var exactEvidence = AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [reopened.Item],
            "fund-1",
            ledgerBookId,
            accountingPeriodId,
            asOfDate,
            expectedOpenBreakCount: 1);
        exactEvidence.Should().ContainSingle();
        exactEvidence[0].Disposition.Should().BeNull();
        exactEvidence[0].DispositionActor.Should().BeNull();
        exactEvidence[0].BlockedOutputs.Should().BeEquivalentTo("FinalReport", "PeriodClose");
    }

    public static IEnumerable<object[]> GovernedLifecycleBypassCases()
    {
        var genericActions = new[]
        {
            ReconciliationCaseworkAction.TransitionStatus,
            ReconciliationCaseworkAction.AddComment
        };
        var governedTargets = new[]
        {
            ReconciliationCaseLifecycleState.Resolved,
            ReconciliationCaseLifecycleState.SignedOff,
            ReconciliationCaseLifecycleState.Reopened,
            ReconciliationCaseLifecycleState.Superseded
        };
        var origins = new[]
        {
            OperationsActionOriginDto.HumanOperator,
            OperationsActionOriginDto.AutomationAssistant
        };

        return genericActions.SelectMany(action =>
            governedTargets.SelectMany(target =>
                origins.Select(origin => new object[] { action, target, origin })));
    }

    [Theory]
    [MemberData(nameof(GovernedLifecycleBypassCases))]
    public async Task Generic_casework_paths_cannot_bypass_governed_terminal_actions(
        ReconciliationCaseworkAction action,
        ReconciliationCaseLifecycleState target,
        OperationsActionOriginDto origin)
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open) with { AssignedTo = "controller-a" };
        await repo.CreateIfMissingAsync(item);
        var current = (await repo.GetByIdAsync(item.BreakId))!;
        var command = Command(current, action) with
        {
            Actor = origin == OperationsActionOriginDto.HumanOperator ? "controller-a" : "assistant",
            Status = action == ReconciliationCaseworkAction.TransitionStatus ? target : null,
            StatusTransition = action == ReconciliationCaseworkAction.AddComment ? target : null,
            Note = "Attempted generic lifecycle shortcut.",
            RootCauseCode = "BrokerCashTiming",
            ResolutionCode = "LedgerAdjusted",
            EvidenceLinks = ["ledger-event:generic-shortcut"],
            Privileged = true,
            ActionOrigin = origin,
            ApprovalActor = "controller-b",
            ApprovalReference = "approval:generic-shortcut",
            SupersedingBreakId = "successor-break"
        };

        var rejected = await repo.ApplyCaseworkCommandAsync(command);

        rejected.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        rejected.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        rejected.Validation!.MissingFields.Should().Contain("action");
        rejected.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        VerifiedOperationOutcomeValidator.Validate(rejected.Outcome).Should().BeEmpty();
        (await repo.GetByIdAsync(item.BreakId))!.Version.Should().Be(current.Version);
        (await repo.GetAuditHistoryAsync(item.BreakId)).Should().ContainSingle(entry =>
            entry.CommandId == command.CommandId &&
            entry.EventType == "CaseworkRejected" &&
            entry.PreviousLifecycleState == current.LifecycleState &&
            entry.NewLifecycleState == current.LifecycleState);
    }

    [Fact]
    public async Task Typed_measures_and_governed_waive_supersede_dispositions_survive_restart_with_audit_lineage()
    {
        var repo = CreateRepository(out var root);
        var measures = new[]
        {
            new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.Value, 100m, 112m, 12m, 1m, "USD"),
            new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.Quantity, 10m, 11m, 1m, 0m, "units"),
            new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.CostBasis, 80m, 84m, 4m, 1m, "USD")
        };
        var material = CreateItem(ReconciliationBreakQueueStatus.Open, ReconciliationBreakSeverity.High) with
        {
            Measures = measures,
            BlockedOutputs = ["FinalReport", "PeriodClose"]
        };
        await repo.CreateIfMissingAsync(material);
        var current = (await repo.GetByIdAsync(material.BreakId))!;

        var missingApproval = await repo.ApplyCaseworkCommandAsync(Command(current, ReconciliationCaseworkAction.Waive) with
        {
            Reason = "Documented immaterial timing exception.",
            EvidenceLinks = ["evidence:waiver-support"]
        });
        missingApproval.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingApproval);

        var selfApproval = await repo.ApplyCaseworkCommandAsync(Command(current, ReconciliationCaseworkAction.Waive) with
        {
            Reason = "Documented immaterial timing exception.",
            EvidenceLinks = ["evidence:waiver-support"],
            ApprovalActor = "controller-a",
            ApprovalReference = "approval:waiver-1"
        });
        selfApproval.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.SelfApprovalNotAllowed);

        var waiverCommand = Command(current, ReconciliationCaseworkAction.Waive) with
        {
            CommandId = Guid.NewGuid().ToString("N"),
            Reason = "Documented immaterial timing exception.",
            EvidenceLinks = ["evidence:waiver-support", "approval:waiver-1"],
            ApprovalActor = "controller-b",
            ApprovalReference = "approval:waiver-1"
        };
        var waived = await repo.ApplyCaseworkCommandAsync(waiverCommand);
        waived.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        waived.Item!.Disposition.Should().Be(ReconciliationBreakDispositionDto.Waived);
        waived.Item.DispositionEvidenceHash.Should().MatchRegex("^[0-9a-f]{64}$");
        waived.Item.Measures.Should().BeEquivalentTo(measures);

        var idempotentRetry = await repo.ApplyCaseworkCommandAsync(waiverCommand);
        idempotentRetry.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        idempotentRetry.Item!.Version.Should().Be(waived.Item.Version);

        var successor = CreateItem(ReconciliationBreakQueueStatus.Open, ReconciliationBreakSeverity.High) with
        {
            Measures = measures,
            BlockedOutputs = ["FinalReport"]
        };
        await repo.CreateIfMissingAsync(successor);
        await repo.CreateIfMissingAsync(CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "replacement-break-1",
            Measures = measures,
            BlockedOutputs = ["FinalReport"]
        });
        var successorCurrent = (await repo.GetByIdAsync(successor.BreakId))!;
        var superseded = await repo.ApplyCaseworkCommandAsync(Command(successorCurrent, ReconciliationCaseworkAction.Supersede) with
        {
            Reason = "Upstream correction created a replacement case.",
            EvidenceLinks = ["evidence:replacement", "approval:supersede-1"],
            ApprovalActor = "controller-b",
            ApprovalReference = "approval:supersede-1",
            SupersedingBreakId = "replacement-break-1"
        });
        superseded.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        superseded.Item!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Superseded);
        superseded.Item.Disposition.Should().Be(ReconciliationBreakDispositionDto.Superseded);
        superseded.Item.SupersedingBreakId.Should().Be("replacement-break-1");

        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var retainedWaiver = await restarted.GetByIdAsync(material.BreakId);
        retainedWaiver!.Disposition.Should().Be(ReconciliationBreakDispositionDto.Waived);
        retainedWaiver.Measures.Should().HaveCount(3);
        var retainedSupersede = await restarted.GetByIdAsync(successor.BreakId);
        retainedSupersede!.Disposition.Should().Be(ReconciliationBreakDispositionDto.Superseded);
        retainedSupersede.SlaState.Should().Be(ReconciliationCaseSlaState.Stopped);

        (await restarted.GetAuditHistoryAsync(material.BreakId)).Should().ContainSingle(entry => entry.EventType == "Waived");
        (await restarted.GetAuditHistoryAsync(successor.BreakId)).Should().ContainSingle(entry => entry.EventType == "Superseded");
    }

    [Fact]
    public async Task Waive_and_supersede_require_independent_approval_for_every_disposition()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open, ReconciliationBreakSeverity.Medium);
        var successor = CreateItem(ReconciliationBreakQueueStatus.Open) with { BreakId = "replacement-nonmaterial-break" };
        await repo.CreateIfMissingAsync(item);
        await repo.CreateIfMissingAsync(successor);
        var current = (await repo.GetByIdAsync(item.BreakId))!;

        var waived = await repo.ApplyCaseworkCommandAsync(Command(current, ReconciliationCaseworkAction.Waive) with
        {
            Reason = "Documented timing exception.",
            EvidenceLinks = ["evidence:waiver-support"]
        });
        var superseded = await repo.ApplyCaseworkCommandAsync(Command(current, ReconciliationCaseworkAction.Supersede) with
        {
            Reason = "Upstream correction created a replacement case.",
            EvidenceLinks = ["evidence:replacement"],
            SupersedingBreakId = successor.BreakId
        });

        waived.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingApproval);
        superseded.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingApproval);
        waived.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        superseded.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        (await repo.GetByIdAsync(item.BreakId))!.Version.Should().Be(current.Version);
    }

    [Fact]
    public async Task Casework_commit_failure_rolls_back_state_and_audit_as_one_atomic_unit()
    {
        var repo = CreateRepository(out var root);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        var current = (await repo.GetByIdAsync(item.BreakId))!;
        var failing = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance,
            stateWriter: (_, _, _) => throw new IOException("injected atomic commit failure"));
        var command = Command(current, ReconciliationCaseworkAction.Assign) with
        {
            Assignee = "controller-b",
            CommandId = "atomic-command-1"
        };

        var result = await failing.ApplyCaseworkCommandAsync(command);

        result.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Failed);
        result.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.PersistenceFailed);
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Outcome.Issues.Should().ContainSingle(issue => issue.ExceptionType == typeof(IOException).FullName);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
        (await failing.GetByIdAsync(item.BreakId))!.AssignedTo.Should().BeNull();
        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await restarted.GetByIdAsync(item.BreakId))!.AssignedTo.Should().BeNull();
        (await restarted.GetAuditHistoryAsync(item.BreakId)).Should().NotContain(entry => entry.CommandId == "atomic-command-1");
    }

    [Fact]
    public async Task Bulk_commit_failure_rolls_back_all_case_mutations_and_success_audits()
    {
        var repo = CreateRepository(out var root);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        var failing = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance,
            stateWriter: (_, _, _) => throw new IOException("injected bulk commit failure"));
        var request = new ReconciliationBulkCaseworkRequest(
            [item.BreakId],
            ReconciliationCaseworkAction.ChangePriority,
            "controller-a",
            "bulk-atomic-1",
            "bulk-atomic-correlation",
            "unit-test",
            "bulk-atomic-idempotency",
            DryRun: false,
            AllowPartialSuccess: false,
            Priority: ReconciliationCasePriority.Critical);

        var result = await failing.ApplyBulkCaseworkAsync(request);

        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.SucceededCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Outcome.Issues.Should().ContainSingle(issue => issue.ExceptionType == typeof(IOException).FullName);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
        (await failing.GetByIdAsync(item.BreakId))!.Priority.Should().Be(ReconciliationCasePriority.Normal);
        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await restarted.GetByIdAsync(item.BreakId))!.Priority.Should().Be(ReconciliationCasePriority.Normal);
        (await restarted.GetAuditHistoryAsync(item.BreakId)).Should().NotContain(entry => entry.EventType == "BulkActionCaseSucceeded");
        (await restarted.GetBulkCaseworkResultAsync(request.IdempotencyKey)).Should().BeNull();
    }

    [Fact]
    public async Task All_queue_mutators_restore_in_memory_and_durable_state_when_atomic_snapshot_write_fails()
    {
        var createRoot = Path.Combine(Path.GetTempPath(), $"recon-break-repo-{Guid.NewGuid():N}");
        var createFailing = new FileReconciliationBreakQueueRepository(
            createRoot,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance,
            stateWriter: (_, _, _) => throw new IOException("create commit failed"));
        var createItem = CreateItem(ReconciliationBreakQueueStatus.Open);
        var create = () => createFailing.CreateIfMissingAsync(createItem);
        await create.Should().ThrowAsync<IOException>().WithMessage("*create commit failed*");
        (await createFailing.GetAllAsync()).Should().BeEmpty();

        var repo = CreateRepository(out var root);
        var original = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "atomic-original",
            SourceType = "statement",
            SourceBreakId = "source-break-atomic",
            SourceFingerprint = "old-fingerprint"
        };
        await repo.CreateIfMissingAsync(original);
        var failing = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance,
            stateWriter: (_, _, _) => throw new IOException("atomic mutation commit failed"));

        var migrate = () => failing.CreateOrMigrateAsync(
            original with { BreakId = "atomic-migrated", SourceFingerprint = "new-fingerprint" },
            original.BreakId);
        await migrate.Should().ThrowAsync<IOException>();
        (await failing.GetByIdAsync(original.BreakId)).Should().NotBeNull();
        (await failing.GetByIdAsync("atomic-migrated")).Should().BeNull();

        var save = () => failing.SaveAsync(original with { Priority = ReconciliationCasePriority.Critical });
        await save.Should().ThrowAsync<IOException>();
        (await failing.GetByIdAsync(original.BreakId))!.Priority.Should().Be(ReconciliationCasePriority.Normal);

        var delete = () => failing.DeleteAsync(original.BreakId);
        await delete.Should().ThrowAsync<IOException>();
        (await failing.GetByIdAsync(original.BreakId)).Should().NotBeNull();

        var review = await failing.StartReviewAsync(new ReviewReconciliationBreakRequest(
            original.BreakId,
            "controller-a",
            "controller-a",
            "triage"));
        review.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Failed);
        review.Outcome.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(review.Outcome).Should().BeEmpty();
        (await failing.GetByIdAsync(original.BreakId))!.Status.Should().Be(ReconciliationBreakQueueStatus.Open);

        var reviewed = await repo.StartReviewAsync(new ReviewReconciliationBreakRequest(
            original.BreakId,
            "controller-a",
            "controller-a",
            "triage"));
        reviewed.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        var resolveFailing = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance,
            stateWriter: (_, _, _) => throw new IOException("resolve commit failed"));
        var resolve = await resolveFailing.ResolveAsync(new ResolveReconciliationBreakRequest(
            original.BreakId,
            ReconciliationBreakQueueStatus.Resolved,
            "controller-b",
            "resolved",
            "retained evidence"));
        resolve.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Failed);
        resolve.Outcome.Issues.Should().ContainSingle(issue => issue.ExceptionType == typeof(IOException).FullName);
        (await resolveFailing.GetByIdAsync(original.BreakId))!.Status.Should().Be(ReconciliationBreakQueueStatus.InReview);

        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await restarted.GetByIdAsync(original.BreakId))!.Status.Should().Be(ReconciliationBreakQueueStatus.InReview);
        (await restarted.GetAuditHistoryAsync(original.BreakId)).Should().NotContain(entry =>
            entry.EventType == "CaseSaved" ||
            entry.EventType == "CaseDeleted" ||
            entry.EventType == "BreakIdMigrated" ||
            entry.EventType == "Resolved");
    }

    [Fact]
    public async Task Independent_repository_instances_serialize_mutations_without_lost_updates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"recon-break-repo-{Guid.NewGuid():N}");
        var first = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var second = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var firstItem = CreateItem(ReconciliationBreakQueueStatus.Open) with { BreakId = "concurrent-break-a" };
        var secondItem = CreateItem(ReconciliationBreakQueueStatus.Open) with { BreakId = "concurrent-break-b" };

        await Task.WhenAll(
            first.CreateIfMissingAsync(firstItem),
            second.CreateIfMissingAsync(secondItem));

        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await restarted.GetAllAsync()).Select(item => item.BreakId)
            .Should().BeEquivalentTo(new[] { firstItem.BreakId, secondItem.BreakId });
        (await restarted.GetAuditHistoryAsync(firstItem.BreakId)).Should().ContainSingle(entry => entry.EventType == "CaseCreated");
        (await restarted.GetAuditHistoryAsync(secondItem.BreakId)).Should().ContainSingle(entry => entry.EventType == "CaseCreated");
    }

    [Fact]
    public async Task Bulk_result_and_idempotency_replay_survive_restart_without_reapplying_mutations()
    {
        var repo = CreateRepository(out var root);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        var request = new ReconciliationBulkCaseworkRequest(
            [item.BreakId],
            ReconciliationCaseworkAction.ChangePriority,
            "controller-a",
            "bulk-restart-1",
            "bulk-correlation-1",
            "unit-test",
            "bulk-idempotency-1",
            DryRun: false,
            AllowPartialSuccess: false,
            Priority: ReconciliationCasePriority.Critical);
        var first = await repo.ApplyBulkCaseworkAsync(request);
        var firstVersion = first.Results.Single().Item!.Version;

        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await restarted.GetBulkCaseworkResultAsync(request.IdempotencyKey)).Should().BeEquivalentTo(first);
        var replay = await restarted.ApplyBulkCaseworkAsync(request);

        replay.Should().BeEquivalentTo(first);
        (await restarted.GetByIdAsync(item.BreakId))!.Version.Should().Be(firstVersion);
        (await restarted.GetAuditHistoryAsync(item.BreakId)).Count(entry => entry.EventType == "BulkActionCaseSucceeded")
            .Should().Be(1);
        first.InputHashSha256.Should().Be(first.Outcome.InputHashSha256);
        first.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(first.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task Command_id_replay_is_bound_to_break_action_and_exact_input_hash()
    {
        var repo = CreateRepository(out var root);
        var first = CreateItem(ReconciliationBreakQueueStatus.Open);
        var second = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(first);
        await repo.CreateIfMissingAsync(second);
        var currentFirst = (await repo.GetByIdAsync(first.BreakId))!;
        var currentSecond = (await repo.GetByIdAsync(second.BreakId))!;
        var command = Command(currentFirst, ReconciliationCaseworkAction.Assign) with
        {
            CommandId = "bound-command-id",
            Assignee = "controller-b"
        };
        var applied = await repo.ApplyCaseworkCommandAsync(command);
        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);

        var exactReplay = await restarted.ApplyCaseworkCommandAsync(command);
        exactReplay.Item.Should().BeEquivalentTo(applied.Item);
        exactReplay.Outcome.Should().BeEquivalentTo(applied.Outcome);
        applied.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        applied.Outcome.InputHashSha256.Should().HaveLength(64);
        VerifiedOperationOutcomeValidator.Validate(applied.Outcome).Should().BeEmpty();
        var differentInput = await restarted.ApplyCaseworkCommandAsync(command with { Assignee = "controller-c" });
        var differentBreak = await restarted.ApplyCaseworkCommandAsync(command with
        {
            BreakId = currentSecond.BreakId,
            ExpectedVersion = currentSecond.Version
        });

        differentInput.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Conflict);
        differentInput.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.CommandIdConflict);
        differentInput.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        VerifiedOperationOutcomeValidator.Validate(differentInput.Outcome).Should().BeEmpty();
        differentBreak.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Conflict);
        differentBreak.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.CommandIdConflict);
    }

    [Fact]
    public async Task Undefined_casework_action_is_blocked_without_mutating_or_retaining_a_success_receipt()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        var current = (await repo.GetByIdAsync(item.BreakId))!;
        var command = Command(current, (ReconciliationCaseworkAction)byte.MaxValue);

        var rejected = await repo.ApplyCaseworkCommandAsync(command);

        rejected.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        rejected.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        rejected.Validation!.MissingFields.Should().Contain("action");
        rejected.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        VerifiedOperationOutcomeValidator.Validate(rejected.Outcome).Should().BeEmpty();
        (await repo.GetByIdAsync(item.BreakId))!.Version.Should().Be(current.Version);
        (await repo.GetAuditHistoryAsync(item.BreakId)).Should().ContainSingle(entry =>
            entry.CommandId == command.CommandId &&
            entry.EventType == "CaseworkRejected" &&
            entry.PreviousLifecycleState == current.LifecycleState &&
            entry.NewLifecycleState == current.LifecycleState);
    }

    [Fact]
    public async Task Bulk_idempotency_and_command_ids_reject_changed_inputs_after_restart()
    {
        var repo = CreateRepository(out var root);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        var request = new ReconciliationBulkCaseworkRequest(
            [item.BreakId],
            ReconciliationCaseworkAction.ChangePriority,
            "controller-a",
            "bulk-bound-command",
            "bulk-bound-correlation",
            "unit-test",
            "bulk-bound-idempotency",
            DryRun: false,
            AllowPartialSuccess: false,
            Priority: ReconciliationCasePriority.High);
        var first = await repo.ApplyBulkCaseworkAsync(request);
        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);

        var changedPayload = await restarted.ApplyBulkCaseworkAsync(request with { Priority = ReconciliationCasePriority.Critical });
        var changedIdempotency = await restarted.ApplyBulkCaseworkAsync(request with { IdempotencyKey = "different-idempotency" });
        var changedCommand = await restarted.ApplyBulkCaseworkAsync(request with { CommandId = "different-command" });

        first.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        changedPayload.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        changedIdempotency.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        changedCommand.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        changedPayload.Outcome.Issues.Should().ContainSingle(issue => issue.Code == ReconciliationBreakQueueTransitionErrorCode.IdempotencyConflict.ToString());
        VerifiedOperationOutcomeValidator.Validate(changedPayload.Outcome).Should().BeEmpty();
        (await restarted.GetByIdAsync(item.BreakId))!.Priority.Should().Be(ReconciliationCasePriority.High);
    }

    [Fact]
    public async Task Bulk_request_rejects_empty_duplicate_and_over_limit_ids_without_truncation_or_receipt()
    {
        var repo = CreateRepository(out _);
        var baseline = new ReconciliationBulkCaseworkRequest(
            ["break-1"],
            ReconciliationCaseworkAction.ChangePriority,
            "controller-a",
            "bulk-invalid",
            "bulk-invalid-correlation",
            "unit-test",
            "bulk-invalid-idempotency",
            DryRun: true,
            AllowPartialSuccess: false,
            Priority: ReconciliationCasePriority.High);
        var requests = new[]
        {
            baseline with { BreakIds = ["break-1", "break-1"], CommandId = "bulk-duplicate", IdempotencyKey = "idem-duplicate" },
            baseline with { BreakIds = [" "], CommandId = "bulk-empty", IdempotencyKey = "idem-empty" },
            baseline with { MaxCaseCount = 0, CommandId = "bulk-zero-limit", IdempotencyKey = "idem-zero-limit" },
            baseline with { MaxCaseCount = 101, CommandId = "bulk-client-limit", IdempotencyKey = "idem-client-limit" },
            baseline with
            {
                BreakIds = Enumerable.Range(0, 101).Select(index => $"break-{index}").ToArray(),
                MaxCaseCount = 100,
                CommandId = "bulk-over-limit",
                IdempotencyKey = "idem-over-limit"
            }
        };

        foreach (var request in requests)
        {
            var result = await repo.ApplyBulkCaseworkAsync(request);
            result.RequestedCount.Should().Be(request.BreakIds.Count);
            result.SucceededCount.Should().Be(0);
            result.FailedCount.Should().Be(request.BreakIds.Count);
            result.Outcome.State.Should().Be(OperationTerminalState.Blocked);
            VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
            (await repo.GetBulkCaseworkResultAsync(request.CommandId)).Should().BeNull();
        }
    }

    [Fact]
    public async Task Bulk_without_partial_success_rolls_back_valid_cases_when_any_case_is_blocked()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        var result = await repo.ApplyBulkCaseworkAsync(new ReconciliationBulkCaseworkRequest(
            [item.BreakId, "missing-break"],
            ReconciliationCaseworkAction.ChangePriority,
            "controller-a",
            "bulk-all-or-none",
            "bulk-all-or-none-correlation",
            "unit-test",
            "bulk-all-or-none-idempotency",
            DryRun: false,
            AllowPartialSuccess: false,
            Priority: ReconciliationCasePriority.Critical));

        result.Results.Should().HaveCount(2);
        result.SucceededCount.Should().Be(0);
        result.FailedCount.Should().Be(2);
        result.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        (await repo.GetByIdAsync(item.BreakId))!.Priority.Should().Be(ReconciliationCasePriority.Normal);
        (await repo.GetAuditHistoryAsync(item.BreakId)).Should().ContainSingle(entry => entry.EventType == "BulkActionCaseBlocked");
        (await repo.GetAuditHistoryAsync(item.BreakId)).Should().NotContain(entry => entry.EventType == "BulkActionCaseSucceeded");
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task Casework_requires_command_id_and_returns_valid_blocked_outcome()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        var current = (await repo.GetByIdAsync(item.BreakId))!;

        var result = await repo.ApplyCaseworkCommandAsync(Command(current, ReconciliationCaseworkAction.Assign) with
        {
            CommandId = " ",
            Assignee = "controller-b"
        });

        result.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        result.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        result.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
        (await repo.GetByIdAsync(item.BreakId))!.AssignedTo.Should().BeNull();
    }

    [Fact]
    public async Task Supersede_rejects_self_or_nonexistent_successor_before_state_change()
    {
        var repo = CreateRepository(out _);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        var current = (await repo.GetByIdAsync(item.BreakId))!;
        var baseline = Command(current, ReconciliationCaseworkAction.Supersede) with
        {
            Reason = "Corrected source will create a successor.",
            EvidenceLinks = ["evidence:successor"]
        };

        var self = await repo.ApplyCaseworkCommandAsync(baseline with { SupersedingBreakId = current.BreakId });
        var missing = await repo.ApplyCaseworkCommandAsync(baseline with { SupersedingBreakId = "missing-successor" });

        self.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingSuccessor);
        missing.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingSuccessor);
        (await repo.GetByIdAsync(item.BreakId))!.Disposition.Should().BeNull();
    }

    [Fact]
    public async Task Supersede_rejects_cross_scope_successor_in_single_and_bulk_paths()
    {
        var repo = CreateRepository(out _);
        var ledgerBookId = Guid.NewGuid();
        var accountingPeriodId = Guid.NewGuid().ToString("D");
        var asOfDate = new DateOnly(2026, 7, 19);
        var source = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "scope-source-single",
            FundAccountId = "fund-1",
            ExternalAccountId = "account-1",
            LedgerBookId = ledgerBookId,
            AccountingPeriodId = accountingPeriodId,
            AsOfDate = asOfDate
        };
        var bulkSource = source with { BreakId = "scope-source-bulk" };
        var crossScopeSuccessor = source with
        {
            BreakId = "scope-successor-other-fund",
            FundAccountId = "fund-2"
        };
        await repo.CreateIfMissingAsync(source);
        await repo.CreateIfMissingAsync(bulkSource);
        await repo.CreateIfMissingAsync(crossScopeSuccessor);
        var current = (await repo.GetByIdAsync(source.BreakId))!;

        var single = await repo.ApplyCaseworkCommandAsync(Command(current, ReconciliationCaseworkAction.Supersede) with
        {
            Reason = "Upstream correction created a replacement case.",
            EvidenceLinks = ["evidence:cross-scope-successor"],
            ApprovalActor = "controller-b",
            ApprovalReference = "approval:cross-scope-successor",
            SupersedingBreakId = crossScopeSuccessor.BreakId
        });
        var bulk = await repo.ApplyBulkCaseworkAsync(new ReconciliationBulkCaseworkRequest(
            BreakIds: [bulkSource.BreakId],
            Action: ReconciliationCaseworkAction.Supersede,
            Actor: "controller-a",
            CommandId: "bulk-cross-scope-successor",
            CorrelationId: "bulk-cross-scope-successor-correlation",
            Source: "unit-test",
            IdempotencyKey: "bulk-cross-scope-successor-idempotency",
            DryRun: false,
            AllowPartialSuccess: false,
            Reason: "Upstream correction created a replacement case.",
            EvidenceLinks: ["evidence:cross-scope-successor"],
            ApprovalActor: "controller-b",
            ApprovalReference: "approval:cross-scope-successor",
            SupersedingBreakId: crossScopeSuccessor.BreakId));

        single.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingSuccessor);
        single.Error.Should().Contain("complete reporting scope");
        single.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        bulk.SucceededCount.Should().Be(0);
        bulk.FailedCount.Should().Be(1);
        bulk.Results.Should().ContainSingle(result =>
            result.BreakId == bulkSource.BreakId &&
            result.Error!.Contains("complete reporting scope", StringComparison.OrdinalIgnoreCase));
        bulk.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        (await repo.GetByIdAsync(source.BreakId))!.Disposition.Should().BeNull();
        (await repo.GetByIdAsync(bulkSource.BreakId))!.Disposition.Should().BeNull();
    }

    [Fact]
    public async Task Supersede_rejects_cycles_and_disposed_successors_in_single_and_bulk_paths()
    {
        var repo = CreateRepository(out _);
        var ledgerBookId = Guid.NewGuid();
        var accountingPeriodId = Guid.NewGuid().ToString("D");
        var asOfDate = new DateOnly(2026, 7, 19);
        var first = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "cycle-a",
            FundAccountId = "fund-1",
            ExternalAccountId = "account-1",
            LedgerBookId = ledgerBookId,
            AccountingPeriodId = accountingPeriodId,
            AsOfDate = asOfDate
        };
        var second = first with { BreakId = "cycle-b" };
        var disposed = first with { BreakId = "disposed-successor" };
        var disposedSource = first with { BreakId = "disposed-source" };
        await repo.CreateIfMissingAsync(first);
        await repo.CreateIfMissingAsync(second);
        await repo.CreateIfMissingAsync(disposed);
        await repo.CreateIfMissingAsync(disposedSource);

        var firstCurrent = (await repo.GetByIdAsync(first.BreakId))!;
        var firstToSecond = await repo.ApplyCaseworkCommandAsync(Command(firstCurrent, ReconciliationCaseworkAction.Supersede) with
        {
            Reason = "First case was replaced by the second case.",
            EvidenceLinks = ["evidence:first-to-second"],
            ApprovalActor = "controller-b",
            ApprovalReference = "approval:first-to-second",
            SupersedingBreakId = second.BreakId
        });
        var secondCurrent = (await repo.GetByIdAsync(second.BreakId))!;
        var cycle = await repo.ApplyCaseworkCommandAsync(Command(secondCurrent, ReconciliationCaseworkAction.Supersede) with
        {
            Reason = "Attempt to point the successor back at its predecessor.",
            EvidenceLinks = ["evidence:cycle"],
            ApprovalActor = "controller-b",
            ApprovalReference = "approval:cycle",
            SupersedingBreakId = first.BreakId
        });

        var disposedCurrent = (await repo.GetByIdAsync(disposed.BreakId))!;
        var waiver = await repo.ApplyCaseworkCommandAsync(Command(disposedCurrent, ReconciliationCaseworkAction.Waive) with
        {
            Reason = "Approved timing exception disposed this candidate.",
            EvidenceLinks = ["evidence:disposed-successor"],
            ApprovalActor = "controller-b",
            ApprovalReference = "approval:disposed-successor"
        });
        var disposedSourceCurrent = (await repo.GetByIdAsync(disposedSource.BreakId))!;
        var disposedSingle = await repo.ApplyCaseworkCommandAsync(Command(disposedSourceCurrent, ReconciliationCaseworkAction.Supersede) with
        {
            Reason = "Attempt to use an already disposed successor.",
            EvidenceLinks = ["evidence:disposed-target"],
            ApprovalActor = "controller-b",
            ApprovalReference = "approval:disposed-target",
            SupersedingBreakId = disposed.BreakId
        });
        var disposedBulk = await repo.ApplyBulkCaseworkAsync(new ReconciliationBulkCaseworkRequest(
            BreakIds: [disposedSource.BreakId],
            Action: ReconciliationCaseworkAction.Supersede,
            Actor: "controller-a",
            CommandId: "bulk-disposed-successor",
            CorrelationId: "bulk-disposed-successor-correlation",
            Source: "unit-test",
            IdempotencyKey: "bulk-disposed-successor-idempotency",
            DryRun: false,
            AllowPartialSuccess: false,
            Reason: "Attempt to use an already disposed successor.",
            EvidenceLinks: ["evidence:disposed-target"],
            ApprovalActor: "controller-b",
            ApprovalReference: "approval:disposed-target",
            SupersedingBreakId: disposed.BreakId));

        firstToSecond.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        cycle.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        cycle.Error.Should().Contain("cycle");
        cycle.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        waiver.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        disposedSingle.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingSuccessor);
        disposedSingle.Error.Should().Contain("active");
        disposedBulk.SucceededCount.Should().Be(0);
        disposedBulk.Results.Should().ContainSingle(result =>
            result.Error!.Contains("active", StringComparison.OrdinalIgnoreCase));
        disposedBulk.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        (await repo.GetByIdAsync(second.BreakId))!.Disposition.Should().BeNull();
        (await repo.GetByIdAsync(disposedSource.BreakId))!.Disposition.Should().BeNull();
    }

    [Fact]
    public async Task Restart_fails_closed_on_malformed_or_hash_tampered_audit_evidence()
    {
        var repo = CreateRepository(out var root);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        var snapshotPath = Path.Combine(root, "reconciliation-break-queue.json");
        await RewriteSnapshotWithValidEnvelopeHashAsync(snapshotPath, document =>
        {
            document["auditEvents"]!.AsArray()[0]!["afterPayload"] = "{\"breakId\":\"tampered\"}";
        });
        var tampered = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);

        var tamperedRead = () => tampered.GetByIdAsync(item.BreakId);
        await tamperedRead.Should().ThrowAsync<InvalidDataException>().WithMessage("*payload hash verification*");

        var malformedRoot = Path.Combine(Path.GetTempPath(), $"recon-break-repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(malformedRoot);
        await File.WriteAllTextAsync(Path.Combine(malformedRoot, "reconciliation-break-queue-audit.jsonl"), "{not-json}");
        var malformed = new FileReconciliationBreakQueueRepository(
            malformedRoot,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var malformedRead = () => malformed.GetAllAsync();
        await malformedRead.Should().ThrowAsync<InvalidDataException>().WithMessage("*malformed*");
    }

    [Fact]
    public async Task Restart_fails_closed_when_legacy_success_receipt_is_detached_from_its_audit_event()
    {
        var repo = CreateRepository(out var root);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        await repo.StartReviewAsync(new ReviewReconciliationBreakRequest(
            item.BreakId,
            "controller-a",
            "controller-a",
            "triage"));
        var snapshotPath = Path.Combine(root, "reconciliation-break-queue.json");
        await RewriteSnapshotWithValidEnvelopeHashAsync(snapshotPath, document =>
        {
            var reviewAudit = document["auditEvents"]!.AsArray().Single(node =>
                node!["eventType"]!.GetValue<string>() == "ReviewStarted");
            reviewAudit!["commandId"] = "detached-command";
        });
        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);

        var read = () => restarted.GetAllAsync();

        await read.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*not bound to its retained audit evidence*");
    }

    [Fact]
    public async Task Restart_fails_closed_on_audit_sequence_identity_and_missing_payload_hash_tampering()
    {
        var sequenceRepo = CreateRepository(out var sequenceRoot);
        var sequenceItem = CreateItem(ReconciliationBreakQueueStatus.Open);
        await sequenceRepo.CreateIfMissingAsync(sequenceItem);
        var sequencePath = Path.Combine(sequenceRoot, "reconciliation-break-queue.json");
        await RewriteSnapshotWithValidEnvelopeHashAsync(sequencePath, document =>
        {
            document["auditEvents"]!.AsArray()[0]!["sequence"] = 2;
        });
        var sequenceTampered = new FileReconciliationBreakQueueRepository(
            sequenceRoot,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var sequenceRead = () => sequenceTampered.GetAllAsync();
        await sequenceRead.Should().ThrowAsync<InvalidDataException>().WithMessage("*sequence*");

        var identityRepo = CreateRepository(out var identityRoot);
        var identityItem = CreateItem(ReconciliationBreakQueueStatus.Open);
        await identityRepo.CreateIfMissingAsync(identityItem);
        var current = (await identityRepo.GetByIdAsync(identityItem.BreakId))!;
        await identityRepo.ApplyCaseworkCommandAsync(Command(current, ReconciliationCaseworkAction.Assign) with { Assignee = "controller-b" });
        var identityPath = Path.Combine(identityRoot, "reconciliation-break-queue.json");
        await RewriteSnapshotWithValidEnvelopeHashAsync(identityPath, document =>
        {
            var events = document["auditEvents"]!.AsArray();
            events[1]!["eventId"] = events[0]!["eventId"]!.GetValue<string>();
        });
        var identityTampered = new FileReconciliationBreakQueueRepository(
            identityRoot,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var identityRead = () => identityTampered.GetAllAsync();
        await identityRead.Should().ThrowAsync<InvalidDataException>().WithMessage("*Duplicate*event id*");

        var hashRepo = CreateRepository(out var hashRoot);
        var hashItem = CreateItem(ReconciliationBreakQueueStatus.Open);
        await hashRepo.CreateIfMissingAsync(hashItem);
        var hashPath = Path.Combine(hashRoot, "reconciliation-break-queue.json");
        await RewriteSnapshotWithValidEnvelopeHashAsync(hashPath, document =>
        {
            document["auditEvents"]!.AsArray()[0]!["afterPayloadHash"] = null;
        });
        var hashTampered = new FileReconciliationBreakQueueRepository(
            hashRoot,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var hashRead = () => hashTampered.GetAllAsync();
        await hashRead.Should().ThrowAsync<InvalidDataException>().WithMessage("*payload hash verification*");
    }

    [Fact]
    public async Task Version_one_snapshot_is_migrated_once_but_legacy_bulk_receipt_remains_blocked_and_non_replayable()
    {
        var repo = CreateRepository(out var root);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await repo.CreateIfMissingAsync(item);
        var request = new ReconciliationBulkCaseworkRequest(
            [item.BreakId],
            ReconciliationCaseworkAction.ChangePriority,
            "controller-a",
            "legacy-bulk-command",
            "legacy-bulk-correlation",
            "unit-test",
            "legacy-bulk-idempotency",
            DryRun: true,
            AllowPartialSuccess: false,
            Priority: ReconciliationCasePriority.High);
        await repo.ApplyBulkCaseworkAsync(request);
        var snapshotPath = Path.Combine(root, "reconciliation-break-queue.json");
        var legacy = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!.AsObject();
        legacy["schemaVersion"] = 1;
        legacy["contentHashSha256"] = null;
        legacy.Remove("bulkReceipts");
        foreach (var audit in legacy["auditEvents"]!.AsArray())
        {
            audit!["sequence"] = 0;
            audit["beforePayloadHash"] = null;
            audit["afterPayloadHash"] = null;
        }
        foreach (var result in legacy["bulkResults"]!.AsArray())
        {
            result!.AsObject().Remove("inputHashSha256");
            result.AsObject().Remove("outcome");
        }
        await File.WriteAllTextAsync(snapshotPath, legacy.ToJsonString());

        var migrated = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await migrated.GetAllAsync()).Should().ContainSingle(entry => entry.BreakId == item.BreakId);
        var retained = await migrated.GetBulkCaseworkResultAsync(request.IdempotencyKey);
        retained.Should().NotBeNull();
        retained!.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        retained.Outcome.Issues.Should().ContainSingle(issue => issue.Code == "legacy-unverified");
        VerifiedOperationOutcomeValidator.Validate(retained.Outcome).Should().BeEmpty();

        var replay = await migrated.ApplyBulkCaseworkAsync(request);
        replay.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        replay.Outcome.Issues.Should().ContainSingle(issue =>
            issue.Code == ReconciliationBreakQueueTransitionErrorCode.IdempotencyConflict.ToString());

        var upgraded = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!.AsObject();
        upgraded["schemaVersion"]!.GetValue<int>().Should().Be(5);
        upgraded["contentHashSha256"]!.GetValue<string>().Should().HaveLength(64);
        upgraded["auditEvents"]!.AsArray().Select(node => node!["sequence"]!.GetValue<long>())
            .Should().Equal(Enumerable.Range(1, upgraded["auditEvents"]!.AsArray().Count).Select(static value => (long)value));
    }

    [Fact]
    public async Task Audit_only_legacy_migration_survives_repeated_restarts_with_the_sidecar_preserved()
    {
        var source = CreateRepository(out var root);
        var item = CreateItem(ReconciliationBreakQueueStatus.Open);
        await source.CreateIfMissingAsync(item);
        var snapshotPath = Path.Combine(root, "reconciliation-break-queue.json");
        var snapshot = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!.AsObject();
        var legacyAudit = snapshot["auditEvents"]!.AsArray()[0]!.DeepClone().AsObject();
        legacyAudit["sequence"] = 0;
        legacyAudit["beforePayloadHash"] = null;
        legacyAudit["afterPayloadHash"] = null;
        var auditPath = Path.Combine(root, "reconciliation-break-queue-audit.jsonl");
        await File.WriteAllTextAsync(auditPath, legacyAudit.ToJsonString() + Environment.NewLine);
        File.Delete(snapshotPath);

        var migrated = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await migrated.GetAuditHistoryAsync(item.BreakId)).Should().ContainSingle();

        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await restarted.GetAuditHistoryAsync(item.BreakId)).Should().ContainSingle();

        var restartedAgain = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var retained = await restartedAgain.GetAuditHistoryAsync(item.BreakId);
        retained.Should().ContainSingle();
        retained[0].Sequence.Should().Be(1);
        retained[0].AfterPayloadHash.Should().MatchRegex("^[0-9a-f]{64}$");
        File.Exists(auditPath).Should().BeTrue("legacy evidence remains preserved after migration");
    }

    [Fact]
    public async Task Hard_close_checkpoint_recovers_after_seal_failure_and_restart_without_rereading_mutable_state()
    {
        var root = Path.Combine(Path.GetTempPath(), $"recon-break-repo-{Guid.NewGuid():N}");
        var failNextWrite = false;
        async Task WriteStateAsync(string path, string content, CancellationToken ct)
        {
            if (failNextWrite)
            {
                failNextWrite = false;
                throw new IOException("Injected checkpoint seal failure.");
            }

            await File.WriteAllTextAsync(path, content, ct);
        }

        var repo = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance,
            stateWriter: WriteStateAsync);
        var scope = new ReconciliationCloseScope(
            "fund-alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 7, 31));
        var retained = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "statement:checkpoint-retained",
            SourceType = "statement",
            LedgerBookId = scope.LedgerBookId,
            AccountingPeriodId = scope.AccountingPeriodId.ToString("D"),
            AsOfDate = scope.AsOfDate,
            BlockedOutputs = ["FinalReport", "PeriodClose"],
            FundProfileId = scope.FundProfileId
        };
        await repo.CreateIfMissingAsync(retained);

        var lease = await repo.AcquireCloseScopeLeaseAsync(scope);
        var checkpointHash = lease.CheckpointHashSha256;
        lease.Items.Should().ContainSingle(item => item.BreakId == retained.BreakId);
        failNextWrite = true;
        var seal = () => lease.CommitHardCloseAsync();
        await seal.Should().ThrowAsync<IOException>().WithMessage("*seal failure*");
        await lease.DisposeAsync();

        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var postCloseCreate = () => restarted.CreateIfMissingAsync(retained with
        {
            BreakId = "statement:post-close-before-recovery"
        });
        await postCloseCreate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*being hard-closed*");

        var recovered = await restarted.RecoverHardClosedScopeCheckpointAsync(scope);
        recovered.CheckpointHashSha256.Should().Be(checkpointHash);
        recovered.Items.Should().ContainSingle(item => item.BreakId == retained.BreakId);
        var idempotentRetry = await restarted.RecoverHardClosedScopeCheckpointAsync(scope);
        idempotentRetry.Should().BeEquivalentTo(recovered);

        var reacquire = () => restarted.AcquireCloseScopeLeaseAsync(scope);
        await reacquire.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already sealed*");
    }

    [Fact]
    public async Task Closing_checkpoint_owner_loss_reacquires_exact_checkpoint_and_explicit_precommit_abandon_reopens_casework()
    {
        var repo = CreateRepository(out var root);
        var scope = new ReconciliationCloseScope(
            "fund-alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 7, 31));
        var retained = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "statement:crashed-close-owner",
            SourceType = "statement",
            SourceBreakId = "source-crashed-close-owner",
            SourceFingerprint = "fingerprint-crashed-close-owner",
            LedgerBookId = scope.LedgerBookId,
            AccountingPeriodId = scope.AccountingPeriodId.ToString("D"),
            AsOfDate = scope.AsOfDate,
            BlockedOutputs = ["FinalReport", "PeriodClose"],
            FundProfileId = scope.FundProfileId
        };
        await repo.CreateIfMissingAsync(retained);

        var lostOwner = await repo.AcquireCloseScopeLeaseAsync(scope);
        var checkpointHash = lostOwner.CheckpointHashSha256;
        var snapshotPath = Path.Combine(root, "reconciliation-break-queue.json");
        var firstToken = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!
            ["closeScopeLocks"]!.AsArray()[0]!["token"]!.GetValue<string>();

        // Disposing without an explicit pre-commit abandon models process ownership loss:
        // the OS file lock is released, but the durable ambiguous checkpoint must remain.
        await lostOwner.DisposeAsync();
        var afterCrashCreate = () => new FileReconciliationBreakQueueRepository(
                root,
                NullLogger<FileReconciliationBreakQueueRepository>.Instance)
            .CreateIfMissingAsync(retained with
            {
                BreakId = "statement:blocked-until-ledger-verified",
                SourceBreakId = "source-blocked-until-ledger-verified"
            });
        await afterCrashCreate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*being hard-closed*");

        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        await using (var recoveredOwner = await restarted.AcquireCloseScopeLeaseAsync(scope))
        {
            recoveredOwner.CheckpointHashSha256.Should().Be(checkpointHash);
            recoveredOwner.Items.Should().ContainSingle(item => item.BreakId == retained.BreakId);
            var secondToken = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!
                ["closeScopeLocks"]!.AsArray()[0]!["token"]!.GetValue<string>();
            secondToken.Should().NotBe(firstToken, "reacquisition fences the lost process owner");

            // The bridge may invoke this only after its authoritative ledger read still reports
            // the period as not hard-closed.
            await recoveredOwner.AbandonBeforeLedgerCommitAsync();
        }

        var replacement = retained with
        {
            BreakId = "statement:post-abandon-casework",
            SourceBreakId = "source-post-abandon-casework",
            SourceFingerprint = "fingerprint-post-abandon-casework"
        };
        (await restarted.CreateIfMissingAsync(replacement)).Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_close_owner_waits_for_file_fence_and_cannot_replace_a_committed_checkpoint()
    {
        var repo = CreateRepository(out var root);
        var scope = new ReconciliationCloseScope(
            "fund-alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 7, 31));
        var firstOwner = await repo.AcquireCloseScopeLeaseAsync(scope);
        var competingRepository = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var competingOwner = competingRepository.AcquireCloseScopeLeaseAsync(scope, timeout.Token);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            competingOwner.IsCompleted.Should().BeFalse(
                "the first owner retains the exclusive cross-process mutation fence");

            await firstOwner.CommitHardCloseAsync();
        }
        finally
        {
            await firstOwner.DisposeAsync();
        }

        Func<Task> competingResult = async () => { await competingOwner; };
        await competingResult.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already sealed*");
    }

    [Fact]
    public async Task Hard_closed_scope_blocks_every_queue_mutation_route_but_not_an_unrelated_scope()
    {
        var repo = CreateRepository(out _);
        var scope = new ReconciliationCloseScope(
            "fund-alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 7, 31));
        var scoped = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "statement:hard-closed",
            SourceType = "statement",
            SourceBreakId = "source-hard-closed",
            SourceFingerprint = "fingerprint-hard-closed",
            LedgerBookId = scope.LedgerBookId,
            AccountingPeriodId = scope.AccountingPeriodId.ToString("D"),
            AsOfDate = scope.AsOfDate,
            BlockedOutputs = ["FinalReport", "PeriodClose"],
            FundProfileId = scope.FundProfileId
        };
        await repo.CreateIfMissingAsync(scoped);
        await using (var lease = await repo.AcquireCloseScopeLeaseAsync(scope))
        {
            await lease.CommitHardCloseAsync();
        }

        var create = () => repo.CreateIfMissingAsync(scoped with
        {
            BreakId = "statement:post-close-create",
            SourceBreakId = "source-post-close-create"
        });
        var migrate = () => repo.CreateOrMigrateAsync(scoped with
        {
            BreakId = "statement:post-close-migrate",
            SourceFingerprint = "fingerprint-post-close-migrate"
        }, scoped.BreakId);
        var save = () => repo.SaveAsync(scoped with { ResolutionNote = "post-close save" });
        var delete = () => repo.DeleteAsync(scoped.BreakId);

        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("*hard-closed*");
        await migrate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*hard-closed*");
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*hard-closed*");
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*hard-closed*");

        var legacyReview = await repo.StartReviewAsync(new ReviewReconciliationBreakRequest(
            scoped.BreakId,
            "controller-a",
            "controller-a",
            "post-close review"));
        legacyReview.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        legacyReview.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.AccountingPeriodHardClosed);
        var legacyResolve = await repo.ResolveAsync(new ResolveReconciliationBreakRequest(
            scoped.BreakId,
            ReconciliationBreakQueueStatus.Resolved,
            "controller-a",
            "post-close resolve",
            "post-close rationale"));
        legacyResolve.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        legacyResolve.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.AccountingPeriodHardClosed);

        var modern = await repo.ApplyCaseworkCommandAsync(
            Command(scoped, ReconciliationCaseworkAction.AddComment) with
            {
                Note = "post-close comment"
            });
        modern.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        modern.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.AccountingPeriodHardClosed);

        var unrelated = scoped with
        {
            BreakId = "statement:unrelated-scope",
            SourceBreakId = "source-unrelated-scope",
            SourceFingerprint = "fingerprint-unrelated-scope",
            AccountingPeriodId = Guid.NewGuid().ToString("D")
        };
        (await repo.CreateIfMissingAsync(unrelated)).Should().BeTrue();
    }

    [Fact]
    public async Task Restart_rejects_a_checkpoint_payload_changed_under_a_valid_snapshot_envelope()
    {
        var repo = CreateRepository(out var root);
        var scope = new ReconciliationCloseScope(
            "fund-alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 7, 31));
        var item = CreateItem(ReconciliationBreakQueueStatus.Open) with
        {
            BreakId = "statement:checkpoint-tamper",
            SourceType = "statement",
            LedgerBookId = scope.LedgerBookId,
            AccountingPeriodId = scope.AccountingPeriodId.ToString("D"),
            AsOfDate = scope.AsOfDate,
            FundProfileId = scope.FundProfileId
        };
        await repo.CreateIfMissingAsync(item);
        await using (var lease = await repo.AcquireCloseScopeLeaseAsync(scope))
        {
            await lease.CommitHardCloseAsync();
        }

        var snapshotPath = Path.Combine(root, "reconciliation-break-queue.json");
        await RewriteSnapshotWithValidEnvelopeHashAsync(snapshotPath, document =>
        {
            document["closeScopeLocks"]!.AsArray()[0]!["checkpointItems"]!
                .AsArray()[0]!["reason"] = "tampered after close";
        });
        var restarted = new FileReconciliationBreakQueueRepository(
            root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);

        var recover = () => restarted.RecoverHardClosedScopeCheckpointAsync(scope);

        await recover.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*checkpoint hash verification*");
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

    private static async Task RewriteSnapshotWithValidEnvelopeHashAsync(
        string snapshotPath,
        Action<JsonObject> mutate)
    {
        var document = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!.AsObject();
        mutate(document);
        document["contentHashSha256"] = null;
        var canonical = document.ToJsonString();
        document["contentHashSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        await File.WriteAllTextAsync(snapshotPath, document.ToJsonString());
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
