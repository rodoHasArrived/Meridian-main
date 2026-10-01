using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;
using Npgsql;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>Retained occurrence recovery through the real journal-intake and file-backed workbench.</summary>
public sealed class RecurringJournalRunnerTests : IDisposable
{
    private const string Fund = "fund-recurring";
    private const string Tenant = "tenant-recurring";
    private const string Company = "company-recurring";
    private const string Entity = "entity-recurring";
    private const string ScheduleId = "monthly-fee";
    private const string Period = "8e867b27-4a59-4c3b-86b3-ec2b7f1b2347";
    private const string CorrectionPeriod = "a6288311-5728-47e6-a795-e9bf42e89312";
    private static readonly Guid Book = Guid.Parse("caeeebac-ecb3-4e76-8391-a615d9ab2345");
    private static readonly DateOnly Date = new(2026, 7, 1);
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly RecurringJournalScope Scope = new(Fund, Book, Entity, "USD", Tenant, Company);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-recurring-runner-" + Guid.NewGuid().ToString("N"));
    private string StateDirectory => Path.Combine(_root, "recurring");

    [Fact]
    public async Task RunDue_RestartAfterClaim_RecoversOneDraftWithExactDefinitionsAndEvidence()
    {
        var store = await SeedAsync();
        RecurringOccurrenceRecord claimed;
        await using (var session = await store.OpenSessionAsync())
            claimed = await session.ClaimAsync(ScheduleId, Date, 1, 1, Now);

        var restarted = await CreateFixtureAsync();
        await restarted.Runner.RunDueAsync(Now);

        var retained = (await restarted.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        retained.JournalEntryId.Should().Be(claimed.DraftId);
        retained.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        retained.ApprovalId.Should().BeNull();
        retained.ApprovedBy.Should().BeNull();
        retained.PostedAtUtc.Should().BeNull();
        retained.TotalDebits.Should().Be(125m);
        retained.TotalCredits.Should().Be(125m);
        retained.EvidenceLinks.Should().Contain(Source().Uri);
        RecurringJournalEvidenceGuard.Validate(retained).Should().BeNull();
        var evidence = JsonSerializer.Deserialize<RecurringJournalEvidence>(retained.RecurringJournalEvidenceJson!)!;
        evidence.ScheduleVersion.Should().Be(1);
        evidence.TemplateVersion.Should().Be(1);
        evidence.SourceEvidence.Should().BeEquivalentTo(new[] { Source() });
        evidence.ScheduleDefinitionJson.Should().Contain(ScheduleId);
        evidence.TemplateDefinitionJson.Should().Contain("fee-template");

        var again = await CreateFixtureAsync();
        await again.Runner.RunDueAsync(Now.AddMinutes(1));
        (await again.Drafts.ListAsync(Fund, Book)).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(retained);
        (await again.Audit.ListAsync(Fund, Book)).Should().ContainSingle(item => item.Action == "manual-je.save-draft");
        await using var recovered = await new FileRecurringJournalStore(StateDirectory).OpenSessionAsync();
        recovered.Occurrences.Should().ContainSingle().Which.State.Should().Be(RecurringOccurrenceState.Drafted);
    }

    [Fact]
    public async Task RunDue_IntakeCommittedBeforeOccurrenceCompletionFailure_RetryRetainsSameDraftAndAudit()
    {
        var store = await SeedAsync();
        var broken = await CreateFixtureAsync(new FailDraftCompletionStore(store));
        // The worker may turn the injected failure into a blocked result; the retained draft is
        // authoritative in either case and must survive independently of the occurrence update.
        _ = await Record.ExceptionAsync(() => broken.Runner.RunDueAsync(Now));
        var first = (await broken.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        await using (var interrupted = await store.OpenSessionAsync())
            interrupted.Occurrences.Should().ContainSingle().Which.State.Should().NotBe(RecurringOccurrenceState.Drafted);

        var restarted = await CreateFixtureAsync();
        await restarted.Runner.RunDueAsync(Now.AddMinutes(1));
        var retained = (await restarted.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        retained.Should().BeEquivalentTo(first);
        (await restarted.Audit.ListAsync(Fund, Book)).Should().ContainSingle(item => item.Action == "manual-je.save-draft");
        await using var complete = await store.OpenSessionAsync();
        complete.Occurrences.Should().ContainSingle().Which.State.Should().Be(RecurringOccurrenceState.Drafted);
    }

    [Fact]
    public async Task RunDue_ConcurrentIndependentRunners_ClaimOneRetainedApprovalDraft()
    {
        await SeedAsync();
        var runners = new List<Fixture>();
        for (var index = 0; index < 6; index++)
            runners.Add(await CreateFixtureAsync());

        await Task.WhenAll(runners.Select(fixture => fixture.Runner.RunDueAsync(Now)));

        var restarted = await CreateFixtureAsync();
        var draft = (await restarted.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        draft.JournalEntryId.Should().Be(RecurringJournalIdentity.DraftId(RecurringJournalIdentity.OccurrenceKey(ScheduleId, Date)));
        (await restarted.Audit.ListAsync(Fund, Book)).Should().ContainSingle(item => item.Action == "manual-je.save-draft");
        await using var session = await new FileRecurringJournalStore(StateDirectory).OpenSessionAsync();
        session.Occurrences.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunDue_DefinitionChangedAfterDraft_BlocksWithoutReplacingRetainedDraft(bool changeTemplate)
    {
        var store = await SeedAsync();
        var firstRunner = await CreateFixtureAsync();
        await firstRunner.Runner.RunDueAsync(Now);
        var original = (await firstRunner.Drafts.ListAsync(Fund, Book)).Single();
        await using (var session = await store.OpenSessionAsync())
        {
            if (changeTemplate)
                await session.RegisterTemplateAsync(Template(factor: 2m), "controller", 1, Now.AddMinutes(1));
            else
                await session.RegisterScheduleAsync(Schedule(amount: 250m), Scope, [Source()], "controller", 1, Now.AddMinutes(1));
        }

        var restarted = await CreateFixtureAsync();
        await restarted.Runner.RunDueAsync(Now.AddMinutes(2));

        (await restarted.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Which.Should().BeEquivalentTo(original);
        var queue = await QueueAsync(restarted);
        var item = queue.Occurrences.Should().ContainSingle().Subject;
        item.State.Should().Be(nameof(RecurringOccurrenceState.DefinitionChanged));
        item.Blockers.Should().NotBeEmpty();
        item.ScheduleVersion.Should().Be(1);
        item.TemplateVersion.Should().Be(1);
        item.JournalEntryId.Should().Be(original.JournalEntryId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunDue_MissingOrUnversionedEvidence_RetainsVisibleBlockWithoutDraft(bool incomplete)
    {
        await SeedAsync(incomplete ? [Source() with { ContentHash = null, EvidenceVersion = null }] : []);
        var fixture = await CreateFixtureAsync();

        await fixture.Runner.RunDueAsync(Now);

        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
        var item = (await QueueAsync(fixture)).Occurrences.Should().ContainSingle().Subject;
        item.State.Should().Be(nameof(RecurringOccurrenceState.Blocked));
        item.Blockers.Should().Contain(reason => reason.Contains("evidence", StringComparison.OrdinalIgnoreCase));
        item.ApprovalStatus.Should().BeNull();
    }

    [Fact]
    public async Task RunDue_LockedPeriod_RetainsOwnerAndGovernedReopenThenRetriesSameOccurrence()
    {
        await SeedAsync();
        var locked = await CreateFixtureAsync(authority: new PeriodAuthority(new(Period, 2, false,
            "close-controller", "/api/ledger/close-management/periods/reopen", "Period is locked.")));
        await locked.Runner.RunDueAsync(Now);

        (await locked.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
        var item = (await QueueAsync(locked)).Occurrences.Should().ContainSingle().Subject;
        item.State.Should().Be(nameof(RecurringOccurrenceState.Blocked));
        item.PeriodLockOwner.Should().Be("close-controller");
        item.GovernedReopenPath.Should().Be("/api/ledger/close-management/periods/reopen");
        item.PeriodId.Should().Be(Period);

        var reopened = await CreateFixtureAsync(authority: new PeriodAuthority(new(Period, 3, true, null, null, null)));
        await reopened.Runner.RunDueAsync(Now.AddMinutes(1));
        var draft = (await reopened.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        await using var session = await new FileRecurringJournalStore(StateDirectory).OpenSessionAsync();
        var occurrence = session.Occurrences.Should().ContainSingle().Subject;
        occurrence.DraftId.Should().Be(draft.JournalEntryId);
        occurrence.History.Should().Contain(transition => transition.LockOwner == "close-controller");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunDue_UnavailablePeriodAuthority_BlocksAndRecoversSameRetainedOccurrence(bool providerFailure)
    {
        await SeedAsync();
        Exception failure = providerFailure
            ? new NpgsqlException("Durable period authority is unavailable.", new IOException("Connection interrupted."))
            : new InvalidOperationException("Durable period authority is unavailable.");
        var fixture = await CreateFixtureAsync(authority: new UnavailablePeriodAuthority(failure));

        await fixture.Runner.RunDueAsync(Now);

        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
        var item = (await QueueAsync(fixture)).Occurrences.Should().ContainSingle().Subject;
        item.State.Should().Be(nameof(RecurringOccurrenceState.Blocked));
        item.Blockers.Should().Contain(reason => reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        item.JournalEntryId.Should().BeNull();
        Guid retainedDraftId;
        await using (var session = await new FileRecurringJournalStore(StateDirectory).OpenSessionAsync())
        {
            var occurrence = session.Occurrences.Should().ContainSingle().Subject;
            occurrence.State.Should().Be(RecurringOccurrenceState.Blocked);
            retainedDraftId = occurrence.DraftId;
        }

        var recovered = await CreateFixtureAsync();
        await recovered.Runner.RunDueAsync(Now.AddMinutes(1));
        await recovered.Runner.RunDueAsync(Now.AddMinutes(2));

        var draft = (await recovered.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        draft.JournalEntryId.Should().Be(retainedDraftId);
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        var recoveredItem = (await QueueAsync(recovered)).Occurrences.Should().ContainSingle().Subject;
        recoveredItem.OccurrenceId.Should().Be(item.OccurrenceId);
        recoveredItem.JournalEntryId.Should().Be(retainedDraftId);
        recoveredItem.State.Should().Be(nameof(RecurringOccurrenceState.Drafted));
        recoveredItem.Blockers.Should().BeEmpty();
        await using var retained = await new FileRecurringJournalStore(StateDirectory).OpenSessionAsync();
        retained.Occurrences.Should().ContainSingle().Subject.History.Should().Contain(transition =>
            transition.State == RecurringOccurrenceState.Blocked && transition.Reason == failure.Message);
    }

    [Fact]
    public async Task RunDue_MissingRetainedState_FailsClosedAndDoesNotReinitialize()
    {
        await SeedAsync();
        File.Delete(Path.Combine(StateDirectory, "recurring-journals.json"));
        var fixture = await CreateFixtureAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runner.RunDueAsync(Now));

        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
        File.Exists(Path.Combine(StateDirectory, "recurring-journals.json")).Should().BeFalse();
        await Assert.ThrowsAsync<InvalidOperationException>(() => QueueAsync(fixture));
    }

    [Fact]
    public async Task Queue_ExactScopeAndDraftStatus_AreReadFromRetainedAuthority()
    {
        await SeedAsync();
        var fixture = await CreateFixtureAsync();
        await fixture.Runner.RunDueAsync(Now, "different-tenant", Company, scopeSpecified: true);
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
        await fixture.Runner.RunDueAsync(Now, Tenant, Company, scopeSpecified: true);
        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Single();
        await fixture.Drafts.SaveAsync(draft with { Status = ManualJournalEntryStatusDto.Submitted, Version = draft.Version + 1 });

        var restarted = await CreateFixtureAsync();
        var item = (await QueueAsync(restarted)).Occurrences.Should().ContainSingle().Subject;
        item.ApprovalStatus.Should().Be(nameof(ManualJournalEntryStatusDto.Submitted));
        item.SourceEvidenceReferences.Should().Contain(Source().Uri);
        var other = await restarted.Runner.GetQueueAsync(Fund, Book, Entity, tenantId: "different-tenant", companyId: Company);
        other.Occurrences.Should().BeEmpty();
    }

    [Fact]
    public async Task Workbench_ClientResave_CannotReplaceOrClearRecurringProvenance()
    {
        await SeedAsync();
        var fixture = await CreateFixtureAsync();
        await fixture.Runner.RunDueAsync(Now);
        var original = (await fixture.Drafts.ListAsync(Fund, Book)).Single();
        var changed = original with
        {
            RecurringJournalEvidenceJson = "{}",
            RecurringJournalEvidenceDigest = new string('f', 64),
            RequiresRecurringJournalEvidence = false,
            TreasuryContext = original.TreasuryContext! with { IdempotencyKey = "client-replacement" }
        };

        var saved = await fixture.Workbench.SaveDraftAsync(new(changed, "preparer",
            LedgerBookId: Book, TenantId: Tenant, CompanyId: Company));

        saved.RecurringJournalEvidenceJson.Should().Be(original.RecurringJournalEvidenceJson);
        saved.RecurringJournalEvidenceDigest.Should().Be(original.RecurringJournalEvidenceDigest);
        saved.RequiresRecurringJournalEvidence.Should().BeTrue();
        saved.TreasuryContext!.IdempotencyKey.Should().Be(original.TreasuryContext!.IdempotencyKey);
        RecurringJournalEvidenceGuard.Validate(saved).Should().BeNull();
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Submit)]
    [InlineData(JournalEntryLifecycleActionDto.Approve)]
    public async Task Workbench_MissingRetainedSourceLink_BlocksSubmissionAndApproval(JournalEntryLifecycleActionDto action)
    {
        await SeedAsync();
        var fixture = await CreateFixtureAsync();
        await fixture.Runner.RunDueAsync(Now);
        var original = (await fixture.Drafts.ListAsync(Fund, Book)).Single();
        var damaged = original with
        {
            EvidenceLinks = [],
            Status = action == JournalEntryLifecycleActionDto.Submit ? ManualJournalEntryStatusDto.Draft : ManualJournalEntryStatusDto.Submitted,
            Version = original.Version + 1
        };
        await fixture.Drafts.SaveAsync(damaged);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.ApplyLifecycleActionAsync(new(
            damaged.JournalEntryId, Fund, action, "independent-reviewer", damaged.Version,
            Notes: "Reviewed retained fee terms.", EvidenceLinks: [Source().Uri], LedgerBookId: Book,
            TenantId: Tenant, CompanyId: Company)));

        var retained = (await fixture.Drafts.ListAsync(Fund, Book)).Single();
        retained.ApprovedAtUtc.Should().BeNull();
        retained.PostedAtUtc.Should().BeNull();
        retained.RecurringJournalEvidenceJson.Should().Be(original.RecurringJournalEvidenceJson);
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Reverse)]
    [InlineData(JournalEntryLifecycleActionDto.Rebook)]
    public async Task Workbench_GovernedCorrection_RetainsSourceEvidenceAndDistinctKeyAcrossPeriodChangeAndApproval(
        JournalEntryLifecycleActionDto correctionAction)
    {
        await SeedAsync();
        var fixture = await CreateFixtureAsync(includeCorrectionPeriod: true);
        await fixture.Runner.RunDueAsync(Now);
        var occurrenceDraft = (await fixture.Drafts.ListAsync(Fund, Book)).Single();
        // Seed only the historical posting status. This regression exercises the real governed
        // correction/save/approval path; ledger posting persistence has its own integration coverage.
        var posted = occurrenceDraft with
        {
            Status = ManualJournalEntryStatusDto.Posted,
            PostedAtUtc = Now,
            PostedBy = "posting-controller",
            Version = occurrenceDraft.Version + 1
        };
        await fixture.Drafts.SaveAsync(posted);
        var correctionRequest = new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntryId, Fund, correctionAction, "correction-preparer", posted.Version,
            Notes: "Move the governed correction into the next open adjustment period.",
            CorrelationId: $"recurring-{correctionAction}-correction",
            EvidenceLinks: [LifecycleEvidence("correction", posted.JournalEntryId)],
            LedgerBookId: Book, TenantId: Tenant, CompanyId: Company);

        var result = await fixture.Workbench.ApplyLifecycleActionAsync(correctionRequest);
        var correction = result.GeneratedJournalEntries.Should().ContainSingle().Which;

        correction.JournalEntryId.Should().NotBe(posted.JournalEntryId);
        correction.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        correction.ApprovedAtUtc.Should().BeNull();
        correction.RecurringJournalEvidenceJson.Should().Be(posted.RecurringJournalEvidenceJson);
        correction.RecurringJournalEvidenceDigest.Should().Be(posted.RecurringJournalEvidenceDigest);
        correction.EvidenceLinks.Should().Contain(Source().Uri);
        correction.TreasuryContext!.IdempotencyKey.Should().NotBe(posted.TreasuryContext!.IdempotencyKey)
            .And.Contain(correction.JournalEntryId.ToString("N"));
        RecurringJournalEvidenceGuard.Validate(correction).Should().BeNull();

        // A retry must recover the same correction, not mint another draft or posting identity.
        var replay = await fixture.Workbench.ApplyLifecycleActionAsync(correctionRequest);
        replay.GeneratedJournalEntries.Should().ContainSingle().Which.Should().BeEquivalentTo(correction);

        var changed = correction with
        {
            AccountingDate = Date.AddMonths(1),
            PeriodId = CorrectionPeriod,
            ReversalOfJournalEntryId = null,
            RebookedFromJournalEntryId = null,
            Reversal = null,
            Rebook = null,
            TreasuryContext = correction.TreasuryContext! with { IdempotencyKey = "forged-correction-key" }
        };
        var saved = await fixture.Workbench.SaveDraftAsync(new(changed, "correction-preparer",
            LedgerBookId: Book, TenantId: Tenant, CompanyId: Company));

        saved.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        saved.AccountingDate.Should().Be(Date.AddMonths(1));
        saved.PeriodId.Should().Be(CorrectionPeriod);
        saved.ReversalOfJournalEntryId.Should().Be(correction.ReversalOfJournalEntryId);
        saved.RebookedFromJournalEntryId.Should().Be(correction.RebookedFromJournalEntryId);
        saved.Reversal.Should().BeEquivalentTo(correction.Reversal);
        saved.Rebook.Should().BeEquivalentTo(correction.Rebook);
        saved.TreasuryContext!.IdempotencyKey.Should().Be(correction.TreasuryContext.IdempotencyKey);
        saved.RecurringJournalEvidenceJson.Should().Be(posted.RecurringJournalEvidenceJson);
        saved.RecurringJournalEvidenceDigest.Should().Be(posted.RecurringJournalEvidenceDigest);
        RecurringJournalEvidenceGuard.Validate(saved).Should().BeNull();
        var retainedSource = RecurringJournalEvidenceGuard.Deserialize(saved.RecurringJournalEvidenceJson!)!;
        retainedSource.JournalEntryId.Should().Be(posted.JournalEntryId);
        retainedSource.EffectiveDate.Should().Be(Date);
        retainedSource.PeriodId.Should().Be(Period);
        retainedSource.SourceEvidence.Should().BeEquivalentTo(new[] { Source() });

        var submitted = await fixture.Workbench.ApplyLifecycleActionAsync(new(
            saved.JournalEntryId, Fund, JournalEntryLifecycleActionDto.Submit, "correction-preparer", saved.Version,
            Notes: "Submit retained correction support for independent review.",
            EvidenceLinks: [LifecycleEvidence("review", saved.JournalEntryId)],
            LedgerBookId: Book, TenantId: Tenant, CompanyId: Company));
        var approved = await fixture.Workbench.ApplyLifecycleActionAsync(new(
            saved.JournalEntryId, Fund, JournalEntryLifecycleActionDto.Approve, "independent-controller", submitted.JournalEntry.Version,
            Notes: "Approve the correction in its open adjustment period with original source evidence retained.",
            EvidenceLinks: [LifecycleEvidence("approval", saved.JournalEntryId)],
            LedgerBookId: Book, TenantId: Tenant, CompanyId: Company));

        approved.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
        approved.JournalEntry.AccountingDate.Should().Be(Date.AddMonths(1));
        approved.JournalEntry.PeriodId.Should().Be(CorrectionPeriod);
        approved.JournalEntry.RecurringJournalEvidenceJson.Should().Be(posted.RecurringJournalEvidenceJson);
        approved.JournalEntry.RecurringJournalEvidenceDigest.Should().Be(posted.RecurringJournalEvidenceDigest);
        approved.JournalEntry.TreasuryContext!.IdempotencyKey.Should().Be(correction.TreasuryContext.IdempotencyKey);
        var retainedDrafts = await fixture.Drafts.ListAsync(Fund, Book);
        retainedDrafts.Should().HaveCount(2);
        var retainedOriginal = retainedDrafts.Single(draft => draft.JournalEntryId == posted.JournalEntryId);
        retainedOriginal.AccountingDate.Should().Be(Date);
        retainedOriginal.PeriodId.Should().Be(Period);
        retainedOriginal.RecurringJournalEvidenceJson.Should().Be(posted.RecurringJournalEvidenceJson);
    }

    [Fact]
    public async Task Workbench_ForgedCorrectionLinkOnOriginal_CannotAuthorizeAnotherDateOrPeriod()
    {
        await SeedAsync();
        var fixture = await CreateFixtureAsync(includeCorrectionPeriod: true);
        await fixture.Runner.RunDueAsync(Now);
        var original = (await fixture.Drafts.ListAsync(Fund, Book)).Single();
        var inventedSourceId = Guid.NewGuid();
        var forged = original with
        {
            AccountingDate = Date.AddMonths(1),
            PeriodId = CorrectionPeriod,
            RebookedFromJournalEntryId = inventedSourceId,
            Rebook = new(inventedSourceId, original.JournalEntryId, "Invented client correction", Now, "client"),
            TreasuryContext = original.TreasuryContext! with { IdempotencyKey = "invented-correction-key" }
        };

        var saved = await fixture.Workbench.SaveDraftAsync(new(forged, "preparer",
            LedgerBookId: Book, TenantId: Tenant, CompanyId: Company));

        saved.ReversalOfJournalEntryId.Should().BeNull();
        saved.RebookedFromJournalEntryId.Should().BeNull();
        saved.Reversal.Should().BeNull();
        saved.Rebook.Should().BeNull();
        saved.TreasuryContext!.IdempotencyKey.Should().Be(original.TreasuryContext!.IdempotencyKey);
        saved.RecurringJournalEvidenceJson.Should().Be(original.RecurringJournalEvidenceJson);
        saved.RecurringJournalEvidenceDigest.Should().Be(original.RecurringJournalEvidenceDigest);
        saved.Status.Should().Be(ManualJournalEntryStatusDto.NeedsFix);
        saved.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.recurring-source-required"
            && issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.ApplyLifecycleActionAsync(new(
            saved.JournalEntryId, Fund, JournalEntryLifecycleActionDto.Submit, "preparer", saved.Version,
            Notes: "Client claims a correction exists.", EvidenceLinks: [LifecycleEvidence("review", saved.JournalEntryId)],
            LedgerBookId: Book, TenantId: Tenant, CompanyId: Company)));
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Which.ApprovedAtUtc.Should().BeNull();
    }

    private static string LifecycleEvidence(string intent, Guid journalEntryId)
        => $"evidence://{intent}/journal/{journalEntryId:D}/ledger-book/{Book:D}/tenant/{Tenant}/company/{Company}";

    private async Task<FileRecurringJournalStore> SeedAsync(IReadOnlyList<JournalEvidenceReference>? evidence = null)
    {
        var store = new FileRecurringJournalStore(StateDirectory);
        await store.InitializeAsync();
        await using var session = await store.OpenSessionAsync();
        await session.RegisterTemplateAsync(Template(), "controller", 0, Now);
        await session.RegisterScheduleAsync(Schedule(), Scope, evidence ?? [Source()], "controller", 0, Now);
        return store;
    }

    private async Task<Fixture> CreateFixtureAsync(IRecurringJournalStore? store = null, IRecurringJournalPeriodAuthority? authority = null,
        bool includeCorrectionPeriod = false)
    {
        var configuration = new InMemoryAccountingConfigurationStore();
        await configuration.SaveAsync(new AccountingConfigurationWorkspaceDto(Fund, Book,
            AccountingConfigurationStatusDto.Draft, "test", Now, [],
            [new("expense", "Expenses:Management Fee Expense", "Management Fee Expense", "Expense"),
             new("payable", "Liabilities:Management Fee Payable", "Management Fee Payable", "Liability")],
            [], [], [], [], TenantId: Tenant, CompanyId: Company));
        var audit = new FileAccountingConfigurationStore(Path.Combine(_root, "audit.json"));
        var configurationService = new AccountingConfigurationService(configuration, audit);
        var drafts = new FileManualJournalEntryDraftStore(Path.Combine(_root, "drafts.json"));
        var ledger = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        ledger.Setup(value => value.GetPeriodAsync(Guid.Parse(Period), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedgerAccountingPeriod(Guid.Parse(Period), Book, 2026, 7, "July 2026",
                Date, Date.AddMonths(1).AddDays(-1), "Open", Now, null, 1));
        if (includeCorrectionPeriod)
            ledger.Setup(value => value.GetPeriodAsync(Guid.Parse(CorrectionPeriod), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerAccountingPeriod(Guid.Parse(CorrectionPeriod), Book, 2026, 8, "August 2026",
                    Date.AddMonths(1), Date.AddMonths(2).AddDays(-1), "Open", Now, null, 1));
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configurationService, audit, journalStore: ledger.Object,
            recurringStore: store ?? new FileRecurringJournalStore(StateDirectory));
        var intake = new AutomatedJournalDraftIntakeService(workbench, drafts, configurationService);
        return new(new RecurringJournalRunner(store ?? new FileRecurringJournalStore(StateDirectory), intake, drafts,
            authority ?? new PeriodAuthority(new(Period, 1, true, null, null, null))), drafts, audit, workbench);
    }

    private static Task<RecurringJournalQueueDto> QueueAsync(Fixture fixture)
        => fixture.Runner.GetQueueAsync(Fund, Book, Entity, tenantId: Tenant, companyId: Company);

    private static JournalTemplate Template(decimal factor = 1m) => new("fee-template", "Management fee", "Monthly retained fee terms",
        [new(LedgerAccounts.ManagementFeeExpenseFor(Fund), JournalTemplateSide.Debit, "amount", Factor: factor),
         new(LedgerAccounts.ManagementFeePayableFor(Fund), JournalTemplateSide.Credit, "amount", Factor: factor)]);

    private static RecurringJournalSchedule Schedule(decimal amount = 125m) => new(ScheduleId, "fee-template",
        new LedgerBookKey(Fund, "Fund"), RecurringJournalCadence.Monthly, Date, "controller", Now,
        new Dictionary<string, decimal> { ["amount"] = amount }, endsOn: Date);

    private static JournalEvidenceReference Source() => new("fee-terms-2026", "evidence://fee-terms-2026",
        "fee-terms", "document-vault", Now.AddDays(-1), "records-controller", ContentHash: new string('a', 64), EvidenceVersion: 3);

    private sealed record Fixture(RecurringJournalRunner Runner, FileManualJournalEntryDraftStore Drafts,
        FileAccountingConfigurationStore Audit, ManualJournalEntryWorkbenchService Workbench);

    private sealed class PeriodAuthority(RecurringJournalPeriodState state) : IRecurringJournalPeriodAuthority
    {
        public Task<RecurringJournalPeriodState> ResolveAsync(RecurringJournalScope scope, DateOnly date, CancellationToken ct)
            => Task.FromResult(state);
    }

    private sealed class UnavailablePeriodAuthority(Exception failure) : IRecurringJournalPeriodAuthority
    {
        public Task<RecurringJournalPeriodState> ResolveAsync(RecurringJournalScope scope, DateOnly date, CancellationToken ct)
            => Task.FromException<RecurringJournalPeriodState>(failure);
    }

    private sealed class FailDraftCompletionStore(IRecurringJournalStore inner) : IRecurringJournalStore
    {
        public async Task<IRecurringJournalSession> OpenSessionAsync(CancellationToken ct = default)
            => new FailDraftCompletionSession(await inner.OpenSessionAsync(ct));
    }

    private sealed class FailDraftCompletionSession(IRecurringJournalSession inner) : IRecurringJournalSession
    {
        public IReadOnlyList<RecurringTemplateDefinition> Templates => inner.Templates;
        public IReadOnlyList<RecurringScheduleDefinition> Schedules => inner.Schedules;
        public IReadOnlyList<RecurringScheduleDefinition> CurrentSchedules => inner.CurrentSchedules;
        public IReadOnlyList<RecurringOccurrenceRecord> Occurrences => inner.Occurrences;
        public IReadOnlyList<RecurringDefinitionActivation> Activations => inner.Activations;
        public RecurringScheduleDefinition GetCurrentSchedule(string scheduleId) => inner.GetCurrentSchedule(scheduleId);
        public RecurringTemplateDefinition GetCurrentTemplate(string templateId) => inner.GetCurrentTemplate(templateId);
        public Task ActivateDefinitionsAsync(string scheduleId, int scheduleVersion, int templateVersion, string actor, string reason, DateTimeOffset now, CancellationToken ct = default)
            => inner.ActivateDefinitionsAsync(scheduleId, scheduleVersion, templateVersion, actor, reason, now, ct);
        public Task<RecurringScheduleDefinition> ConfigureAsync(RecurringJournalSchedule schedule, JournalTemplate template,
            RecurringJournalScope scope, IReadOnlyList<JournalEvidenceReference> evidence, string actor,
            int expectedScheduleVersion, int expectedTemplateVersion, DateTimeOffset now, CancellationToken ct = default)
            => inner.ConfigureAsync(schedule, template, scope, evidence, actor, expectedScheduleVersion, expectedTemplateVersion, now, ct);
        public Task<RecurringTemplateDefinition> RegisterTemplateAsync(JournalTemplate template, string actor, int expectedVersion, DateTimeOffset now, CancellationToken ct = default, RecurringJournalScope? scope = null)
            => inner.RegisterTemplateAsync(template, actor, expectedVersion, now, ct, scope);
        public Task<RecurringScheduleDefinition> RegisterScheduleAsync(RecurringJournalSchedule schedule, RecurringJournalScope scope, IReadOnlyList<JournalEvidenceReference> evidence, string actor, int expectedVersion, DateTimeOffset now, CancellationToken ct = default)
            => inner.RegisterScheduleAsync(schedule, scope, evidence, actor, expectedVersion, now, ct);
        public Task<RecurringOccurrenceRecord> ClaimAsync(string scheduleId, DateOnly date, int expectedScheduleVersion, int expectedTemplateVersion, DateTimeOffset now, CancellationToken ct = default)
            => inner.ClaimAsync(scheduleId, date, expectedScheduleVersion, expectedTemplateVersion, now, ct);
        public Task<RecurringOccurrenceRecord> SetOutcomeAsync(string key, RecurringOccurrenceState state, string? reason, string? periodId, string? lockOwner, string? reopenPath, DateTimeOffset now, CancellationToken ct = default)
            => state == RecurringOccurrenceState.Drafted
                ? Task.FromException<RecurringOccurrenceRecord>(new IOException("Injected failure after draft intake and before occurrence completion."))
                : inner.SetOutcomeAsync(key, state, reason, periodId, lockOwner, reopenPath, now, ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
