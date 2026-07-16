using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage;
using Meridian.Storage.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class AutomatedJournalScheduleTests
{
    private static readonly Guid BookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset DueAt = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MonthlyFeeSchedule_RunsAtConfiguredDueTime_AndDoesNotRunTwice()
    {
        var fixture = CreateFixture();
        var store = new InMemoryAutomatedJournalScheduleStore();
        var saved = await store.SaveAsync(FeeSchedule("fees-2026-07"));
        var worker = CreateWorker(store, fixture.Runner);

        saved.ScheduledForUtc.Should().Be(DueAt);
        (await worker.RunDueAsync(DueAt.AddTicks(-1))).Runs.Should().BeEmpty();

        using var services = new ServiceCollection()
            .AddSingleton(worker)
            .BuildServiceProvider();
        var hosted = new AutomatedJournalSchedulerHostedService(
            services,
            new FixedTimeProvider(DueAt),
            NullLogger<AutomatedJournalSchedulerHostedService>.Instance);
        var due = await hosted.RunOnceAsync();

        var run = due.Runs.Should().ContainSingle().Subject;
        run.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        run.JournalEntryIds.Should().HaveCount(2);
        run.NextPeriodId.Should().Be("2026-08");
        run.NextScheduledForUtc.Should().Be(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));
        (await worker.RunDueAsync(DueAt.AddMinutes(1))).Runs.Should().BeEmpty();
        var persisted = await store.GetAsync("fees-2026-07");
        persisted!.RunHistory.Should().ContainSingle();
        persisted.State.Should().Be(AutomatedJournalScheduleStateDto.Scheduled);
        persisted.PeriodId.Should().Be("2026-08");
        persisted.PeriodStart.Should().Be(new DateOnly(2026, 8, 1));
        persisted.PeriodEnd.Should().Be(new DateOnly(2026, 8, 31));
        persisted.ScheduledForUtc.Should().Be(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));
        persisted.CapitalAccountReconciliation.Should().BeNull("each recurring fee cycle requires a new reviewed tie-out");
        persisted.RunHistory.Single().State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        var workbench = await fixture.Workbench.GetWorkbenchAsync(
            "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha");
        workbench.Drafts.Should().HaveCount(2);
        workbench.Drafts.Should().OnlyContain(static draft =>
            draft.Status == ManualJournalEntryStatusDto.Draft &&
            draft.AccountingDate == new DateOnly(2026, 7, 31) &&
            draft.EvidenceLinks.Count > 0 &&
            draft.SubmittedAtUtc == null &&
            draft.ApprovedAtUtc == null &&
            draft.PostedAtUtc == null);
        var firstDraft = workbench.Drafts[0];
        var automatedSubmit = () => fixture.Workbench.SubmitApprovalAsync(
            new SubmitManualJournalEntryApprovalRequest(
                firstDraft.JournalEntryId,
                firstDraft.FundProfileId,
                "automated-journal-scheduler",
                firstDraft.Version,
                ActionOrigin: OperationsActionOriginDto.AssistantDraft,
                LedgerBookId: BookId,
                TenantId: "tenant-alpha",
                CompanyId: "company-alpha"));
        await automatedSubmit.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*human operator approval is required*");
    }

    [Fact]
    public async Task ScheduleDueTime_UsesConfiguredLocalTimeZone()
    {
        var store = new InMemoryAutomatedJournalScheduleStore();

        var saved = await store.SaveAsync(FeeSchedule("fees-phoenix-time") with
        {
            DueTimeLocal = new TimeOnly(9, 0),
            TimeZoneId = "America/Phoenix"
        });

        saved.ScheduledForUtc.Should().Be(new DateTimeOffset(2026, 8, 1, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task FeeSchedule_WithoutReviewedCapitalAccountEvidence_BlocksBeforeDraftCreation()
    {
        var fixture = CreateFixture();
        var store = new InMemoryAutomatedJournalScheduleStore();
        fixture.CapitalAccountResolver.Reconciliation = null;
        await store.SaveAsync(FeeSchedule("fees-missing-capital-evidence"));

        var result = await CreateWorker(store, fixture.Runner).RunDueAsync(DueAt);

        var run = result.Runs.Should().ContainSingle().Subject;
        run.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        run.Blockers.Should().Contain(item => item.Contains("capital-account reconciliation", StringComparison.OrdinalIgnoreCase));
        (await fixture.Workbench.GetWorkbenchAsync(
            "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha")).Drafts.Should().BeEmpty();
    }

    [Fact]
    public async Task FeeSchedule_LowConfidenceCapitalAccountEvidence_NeedsInvestigationAndCanBeRearmed()
    {
        var fixture = CreateFixture();
        var store = new InMemoryAutomatedJournalScheduleStore();
        fixture.CapitalAccountResolver.Reconciliation = Reconciliation(confidence: 0.60m);
        await store.SaveAsync(FeeSchedule("fees-low-confidence"));
        var worker = CreateWorker(store, fixture.Runner);

        var first = await worker.RunDueAsync(DueAt);

        first.Runs.Should().ContainSingle().Which.State.Should().Be(AutomatedJournalScheduleStateDto.NeedsInvestigation);
        (await fixture.Workbench.GetWorkbenchAsync(
            "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha")).Drafts.Should().BeEmpty();
        var retained = (await store.GetAsync("fees-low-confidence"))!;
        fixture.CapitalAccountResolver.Reconciliation = Reconciliation();
        await store.SaveAsync(retained with
        {
            State = AutomatedJournalScheduleStateDto.Scheduled,
            LastScheduledForUtc = null,
            Blockers = [],
            EvidenceLinks = []
        });

        var retry = await worker.RunDueAsync(DueAt.AddMinutes(1));

        retry.Runs.Should().ContainSingle().Which.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        var advanced = (await store.GetAsync("fees-low-confidence"))!;
        advanced.PeriodId.Should().Be("2026-08");
        advanced.RunHistory.Should().ContainSingle(history =>
            history.State == AutomatedJournalScheduleStateDto.DraftReady &&
            history.JournalEntryIds.Count == 2);
    }

    [Fact]
    public async Task DelayedFeeSchedule_EvaluatesServerEvidenceAtActualExecutionTime()
    {
        var fixture = CreateFixture();
        var reviewedAt = DueAt.AddMinutes(15);
        var executedAt = DueAt.AddHours(1);
        fixture.CapitalAccountResolver.Reconciliation = Reconciliation(reviewedAtUtc: reviewedAt);
        var store = new InMemoryAutomatedJournalScheduleStore();
        await store.SaveAsync(FeeSchedule("fees-delayed"));

        var result = await CreateWorker(store, fixture.Runner).RunDueAsync(executedAt);

        result.EvaluatedAtUtc.Should().Be(executedAt);
        result.Runs.Should().ContainSingle().Which.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        fixture.CapitalAccountResolver.Scopes.Should().ContainSingle().Which.EvaluatedAtUtc.Should().Be(executedAt);
        var history = (await store.GetAsync("fees-delayed"))!.RunHistory.Should().ContainSingle().Subject;
        history.ScheduledForUtc.Should().Be(DueAt);
        history.CompletedAtUtc.Should().Be(executedAt);
    }

    [Fact]
    public async Task RunDueForScope_ExecutesOnlyExactTenantAndCompany()
    {
        var fixture = CreateFixture();
        var store = new InMemoryAutomatedJournalScheduleStore();
        await store.SaveAsync(FeeSchedule("fees-tenant-a") with { TenantId = "tenant-alpha", CompanyId = "company-alpha" });
        await store.SaveAsync(FeeSchedule("fees-tenant-b") with { TenantId = "tenant-b", CompanyId = "company-b" });
        var worker = CreateWorker(store, fixture.Runner);

        var result = await worker.RunDueForScopeAsync(DueAt, "tenant-alpha", "company-alpha");

        result.Runs.Should().ContainSingle().Which.ScheduleId.Should().Be("fees-tenant-a");
        (await store.GetAsync("fees-tenant-a"))!.PeriodId.Should().Be("2026-08");
        (await store.GetAsync("fees-tenant-b"))!.State.Should().Be(AutomatedJournalScheduleStateDto.Scheduled);
    }

    [Fact]
    public async Task ScheduleStore_RejectsIdentityTakeover_ButAllowsNewHumanConfigurator()
    {
        var store = new InMemoryAutomatedJournalScheduleStore();
        var original = await store.SaveAsync(FeeSchedule("fees-owned") with
        {
            TenantId = "tenant-a",
            CompanyId = "company-a",
            Actor = "creator-a",
            CreatedBy = "creator-a"
        });

        var reconfigured = await store.SaveAsync(original with
        {
            Actor = "controller-b",
            LastConfiguredBy = "controller-b"
        });
        reconfigured.Actor.Should().Be("controller-b");
        reconfigured.CreatedBy.Should().Be("creator-a");

        var takeover = () => store.SaveAsync(original with
        {
            TenantId = "tenant-b",
            CompanyId = "company-b",
            Actor = "attacker",
            CreatedBy = "attacker"
        });
        await takeover.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different immutable identity scope*");
    }

    [Fact]
    public async Task ScheduleStore_RejectsStaleVersionWithOptimisticConcurrency()
    {
        var store = new InMemoryAutomatedJournalScheduleStore();
        var original = await store.SaveAsync(FeeSchedule("fees-cas"));
        var current = await store.SaveAsync(original with { LastConfiguredBy = "controller-a" });

        var staleWrite = () => store.SaveAsync(original with { LastConfiguredBy = "controller-b" });

        current.Version.Should().Be(original.Version + 1);
        await staleWrite.Should().ThrowAsync<AutomatedJournalScheduleConcurrencyException>()
            .WithMessage("*version*stale*");
    }

    [Fact]
    public async Task CompletedRunningClaim_IsNotResumed()
    {
        var fixture = CreateFixture();
        var store = new InMemoryAutomatedJournalScheduleStore();
        var original = await store.SaveAsync(FeeSchedule("fees-completed-claim"));
        var worker = CreateWorker(store, fixture.Runner);
        await worker.RunDueAsync(DueAt);
        var completed = (await store.GetAsync(original.ScheduleId))!;
        await store.SaveAsync(original with
        {
            Version = completed.Version,
            State = AutomatedJournalScheduleStateDto.Running,
            LastScheduledForUtc = DueAt,
            RunHistory = completed.RunHistory
        });

        var resumed = await worker.RunDueAsync(DueAt.AddMinutes(5));

        resumed.Runs.Should().BeEmpty("only a durable Running history row without completion may be resumed");
    }

    [Fact]
    public async Task PersistedRunningClaim_RestartsWithSameRunKey_AndDeduplicatesDraftsAndHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "automated-journal-schedule-tests", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(directory, "monthly-schedules.json");
        try
        {
            var fixture = await CreateFileFixtureAsync(directory, initializeConfiguration: true);
            var firstStore = new FileAutomatedJournalScheduleStore(snapshotPath);
            var original = await firstStore.SaveAsync(FeeSchedule("fees-restart-2026-07"));
            var firstRun = await CreateWorker(firstStore, fixture.Runner).RunDueAsync(DueAt);
            firstRun.Runs.Should().ContainSingle();
            var completed = (await firstStore.GetAsync("fees-restart-2026-07"))!;
            var runningHistory = completed.RunHistory.Single() with
            {
                State = AutomatedJournalScheduleStateDto.Running,
                CompletedAtUtc = null,
                Summary = "Simulated process termination after intake and before completion."
            };
            await firstStore.SaveAsync(original with
            {
                Version = completed.Version,
                State = AutomatedJournalScheduleStateDto.Running,
                LastRunAtUtc = null,
                LastScheduledForUtc = DueAt,
                RunHistory = [runningHistory]
            });

            var restartedStore = new FileAutomatedJournalScheduleStore(snapshotPath);
            var restartedFixture = await CreateFileFixtureAsync(directory, initializeConfiguration: false);
            var restarted = await CreateWorker(restartedStore, restartedFixture.Runner).RunDueAsync(DueAt.AddMinutes(5));

            var restartedRun = restarted.Runs.Should().ContainSingle().Subject;
            restartedRun.RunKey.Should().Be(firstRun.Runs.Single().RunKey);
            restartedRun.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
            var persisted = (await restartedStore.GetAsync("fees-restart-2026-07"))!;
            persisted.RunHistory.Should().ContainSingle("a restart replaces the durable record for the same run key");
            persisted.RunHistory.Single().JournalEntryIds.Should().BeEquivalentTo(firstRun.Runs.Single().JournalEntryIds);
            persisted.PeriodId.Should().Be("2026-08");
            (await restartedFixture.Workbench.GetWorkbenchAsync(
                "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha")).Drafts.Should().HaveCount(2,
                "a fully reconstructed process graph must load the durable drafts and deduplicate restart intake");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProviderDividendSchedule_CreatesDividendAndWithholdingDraftsWithEvidence()
    {
        var dividend = DividendAction(
            payDate: new DateOnly(2026, 7, 16),
            currency: "USD",
            recordDate: new DateOnly(2026, 7, 3));
        var fixture = CreateFixture([dividend]);
        var store = new InMemoryAutomatedJournalScheduleStore();
        await store.SaveAsync(DividendSchedule("dividends-2026-07", withholdingRate: 0.15m));

        var result = await CreateWorker(store, fixture.Runner).RunDueAsync(DueAt);

        result.Runs.Should().ContainSingle().Which.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        var drafts = (await fixture.Workbench.GetWorkbenchAsync(
            "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha")).Drafts;
        drafts.Should().HaveCount(2);
        drafts.Should().Contain(draft => draft.Memo.Contains("Dividend declared", StringComparison.OrdinalIgnoreCase) && draft.TotalDebits == 104m);
        drafts.Should().Contain(draft => draft.Memo.Contains("Withholding tax", StringComparison.OrdinalIgnoreCase) && draft.TotalDebits == 15.60m);
        drafts.Should().OnlyContain(static draft => draft.EvidenceLinks.Count > 0);
        drafts.Should().OnlyContain(static draft =>
            draft.AutomationEvidenceAssessment != null &&
            draft.AutomationEvidenceAssessment.Quality == AutomatedJournalEvidenceQualityDto.High &&
            !draft.AutomationEvidenceAssessment.RequiresInvestigation);
        drafts.Should().OnlyContain(static draft => draft.EvidenceLinks.Any(link =>
            link.Contains("position-snapshots", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task RecurringDividendSchedule_ResolvesEachCyclePositionAfterRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "automated-journal-dividend-restart-tests", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(directory, "monthly-schedules.json");
        var actions = new[]
        {
            DividendAction(new DateOnly(2026, 7, 2), 0.26m, new DateOnly(2026, 7, 16), "USD", new DateOnly(2026, 7, 3)),
            DividendAction(new DateOnly(2026, 8, 2), 0.30m, new DateOnly(2026, 8, 16), "USD", new DateOnly(2026, 8, 3))
        };
        try
        {
            var firstFixture = await CreateFileFixtureAsync(directory, initializeConfiguration: true, actions);
            var firstStore = new FileAutomatedJournalScheduleStore(snapshotPath);
            await firstStore.SaveAsync(DividendSchedule("dividend-cycle-restart"));

            var july = await CreateWorker(firstStore, firstFixture.Runner, new CycleDividendPositionResolver())
                .RunDueAsync(DueAt);

            july.Runs.Should().ContainSingle().Which.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
            (await firstStore.GetAsync("dividend-cycle-restart"))!.PeriodId.Should().Be("2026-08");

            var restartedStore = new FileAutomatedJournalScheduleStore(snapshotPath);
            var restartedFixture = await CreateFileFixtureAsync(directory, initializeConfiguration: false, actions);
            var august = await CreateWorker(restartedStore, restartedFixture.Runner, new CycleDividendPositionResolver())
                .RunDueAsync(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));

            august.Runs.Should().ContainSingle().Which.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
            var drafts = (await restartedFixture.Workbench.GetWorkbenchAsync(
                "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha")).Drafts;
            drafts.Should().Contain(draft => draft.AccountingDate == new DateOnly(2026, 7, 2) && draft.TotalDebits == 104m);
            drafts.Should().Contain(draft => draft.AccountingDate == new DateOnly(2026, 8, 2) && draft.TotalDebits == 75m);
            drafts.Should().NotContain(draft => draft.TotalDebits > 100_000m,
                "the config-time quantity is not an execution authority");
            drafts.Should().OnlyContain(static draft => draft.EvidenceLinks.Any(link =>
                link.Contains("position-snapshots", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PositionSnapshotResolver_ReconstructsExDateHoldingsAcrossChangesAndRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "automated-journal-position-history-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new StorageOptions { RootPath = directory };
            var firstStore = new JsonlPositionSnapshotStore(options, NullLogger<JsonlPositionSnapshotStore>.Instance);
            await firstStore.SaveSnapshotAsync(PositionSnapshot(new DateTimeOffset(2026, 6, 30, 20, 0, 0, TimeSpan.Zero), 400m));
            await firstStore.SaveSnapshotAsync(PositionSnapshot(new DateTimeOffset(2026, 7, 10, 20, 0, 0, TimeSpan.Zero), 200m));
            await firstStore.SaveSnapshotAsync(PositionSnapshot(new DateTimeOffset(2026, 7, 20, 20, 0, 0, TimeSpan.Zero), quantity: null));

            var restartedStore = new JsonlPositionSnapshotStore(options, NullLogger<JsonlPositionSnapshotStore>.Instance);
            var resolution = await new PositionSnapshotAutomatedJournalDividendPositionResolver(restartedStore)
                .ResolveAsync(DividendSchedule("dividend-authoritative-history"), DueAt);

            resolution.IsReady.Should().BeTrue();
            resolution.Positions.Should().Contain(position => position.Quantity == 400m);
            resolution.Positions.Should().Contain(position => position.Quantity == 200m);
            resolution.Positions.Should().Contain(position => position.Quantity == 0m,
                "an omitted symbol in a later authoritative snapshot is retained as a zero-quantity tombstone");
            resolution.Positions.Should().OnlyContain(static position =>
                position.PositionAsOfUtc.HasValue && position.PositionEvidenceReferences.Count == 1);

            var actions = new[]
            {
                DividendAction(new DateOnly(2026, 7, 2), 0.26m, new DateOnly(2026, 7, 16), "USD", new DateOnly(2026, 7, 3)),
                DividendAction(new DateOnly(2026, 7, 12), 0.26m, new DateOnly(2026, 7, 26), "USD", new DateOnly(2026, 7, 13)),
                DividendAction(new DateOnly(2026, 7, 25), 0.26m, new DateOnly(2026, 8, 8), "USD", new DateOnly(2026, 7, 26))
            };
            var production = await new CorporateActionDividendEventProducer(new FakeSecurityMasterQueryService(actions))
                .ProduceAsync(new CorporateActionDividendRequest(
                    resolution.Positions,
                    "USD",
                    new DateOnly(2026, 7, 1),
                    new DateOnly(2026, 7, 31),
                    DueAt,
                    MaximumPositionAgeDays: 7));

            production.Events.Should().HaveCount(2);
            production.Events.Select(static item => item.Amount).Should().BeEquivalentTo([104m, 52m]);
            production.Events.Should().OnlyContain(static item => item.EvidenceReferences.Any(reference =>
                reference.Kind == "position-snapshot"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DividendSchedule_WithNoEligibleProviderEvidence_IsVisiblyBlocked()
    {
        var fixture = CreateFixture([]);
        var store = new InMemoryAutomatedJournalScheduleStore();
        await store.SaveAsync(DividendSchedule("dividends-no-evidence-2026-07"));

        var result = await CreateWorker(store, fixture.Runner).RunDueAsync(DueAt);

        var run = result.Runs.Should().ContainSingle().Subject;
        run.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        run.Blockers.Should().ContainSingle(item =>
            item.Contains("No eligible corporate-action evidence", StringComparison.OrdinalIgnoreCase));
        (await fixture.Workbench.GetWorkbenchAsync(
            "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha")).Drafts.Should().BeEmpty();
        var persisted = await store.GetAsync("dividends-no-evidence-2026-07");
        persisted!.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        persisted.RunHistory.Should().ContainSingle(history =>
            history.State == AutomatedJournalScheduleStateDto.Blocked && history.Blockers.Count == 1);
    }

    [Fact]
    public async Task LowConfidenceDividend_RemainsNeedsFixWithInvestigationIssue_AndCannotSubmit()
    {
        var lowConfidence = DividendAction(payDate: null, currency: "USD", recordDate: null);
        var fixture = CreateFixture([lowConfidence]);
        var store = new InMemoryAutomatedJournalScheduleStore();
        await store.SaveAsync(DividendSchedule(
            "dividends-investigate-2026-07",
            withholdingRate: 0.15m,
            minimumConfidence: 0.90m));

        var result = await CreateWorker(store, fixture.Runner).RunDueAsync(DueAt);

        var run = result.Runs.Should().ContainSingle().Subject;
        run.State.Should().Be(AutomatedJournalScheduleStateDto.NeedsInvestigation);
        run.Blockers.Should().Contain(item => item.Contains("Needs investigation", StringComparison.OrdinalIgnoreCase));
        var drafts = (await fixture.Workbench.GetWorkbenchAsync(
            "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha")).Drafts;
        drafts.Should().HaveCount(2, "both dividend and withholding drafts inherit the same source-evidence grade");
        drafts.Should().OnlyContain(draft =>
            draft.Status == ManualJournalEntryStatusDto.NeedsFix &&
            draft.AutomationEvidenceAssessment != null &&
            draft.AutomationEvidenceAssessment.RequiresInvestigation &&
            draft.AutomationEvidenceAssessment.Quality == AutomatedJournalEvidenceQualityDto.High &&
            draft.AutomationEvidenceAssessment.ConfidenceScore == 0.85m &&
            draft.ValidationIssues.Any(issue =>
                issue.Code == "manual-je.automation-investigation-required" &&
                issue.Severity == AccountingConfigurationValidationSeverityDto.Critical));
        var draft = drafts[0];

        var submit = () => fixture.Workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            draft.JournalEntryId,
            draft.FundProfileId,
            "fund-controller",
            draft.Version,
            LedgerBookId: BookId,
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));
        await submit.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");
        var retained = (await fixture.Workbench.GetWorkbenchAsync(
            "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha")).Drafts;
        retained.Should().OnlyContain(static item =>
            item.Status == ManualJournalEntryStatusDto.NeedsFix &&
            item.SubmittedAtUtc == null &&
            item.ApprovedAtUtc == null &&
            item.PostedAtUtc == null);
    }

    [Fact]
    public async Task StatusProjection_ExposesCloseCountsEvidenceAndBlockedPosture()
    {
        var store = new InMemoryAutomatedJournalScheduleStore();
        await store.SaveAsync(FeeSchedule("fees-status") with
        {
            State = AutomatedJournalScheduleStateDto.DraftReady,
            JournalEntryIds = [Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
        });
        await store.SaveAsync(DividendSchedule("dividends-status") with
        {
            State = AutomatedJournalScheduleStateDto.NeedsInvestigation,
            Blockers = ["Corporate-action evidence needs investigation."],
            EvidenceLinks = [new OperationsEvidenceLinkDto("corp-act", "Corporate action", "/evidence/corp-act", "security-master", DueAt)]
        });

        var status = await store.GetStatusAsync(
            "fund-alpha", BookId, "2026-07", tenantId: "tenant-alpha", companyId: "company-alpha", entityId: "entity-alpha");

        status.ConfiguredCount.Should().Be(2);
        status.EnabledCount.Should().Be(2);
        status.FeeScheduleCount.Should().Be(1);
        status.DividendScheduleCount.Should().Be(1);
        status.DraftReadyCount.Should().Be(1);
        status.NeedsInvestigationCount.Should().Be(1);
        status.State.Should().Be(AutomatedJournalScheduleStateDto.NeedsInvestigation);
        status.EvidenceLinks.Should().ContainSingle();
        status.Blockers.Should().ContainSingle();
    }

    [Theory]
    [InlineData(AutomatedJournalScheduleStateDto.Scheduled)]
    [InlineData(AutomatedJournalScheduleStateDto.Running)]
    public async Task StatusProjection_ActiveCurrentCycleWinsSamePeriodCompletedHistory(
        AutomatedJournalScheduleStateDto currentState)
    {
        var oldDraftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var store = new InMemoryAutomatedJournalScheduleStore();
        await store.SaveAsync(FeeSchedule($"fees-current-{currentState}") with
        {
            State = currentState,
            LastScheduledForUtc = DueAt,
            JournalEntryIds = [],
            RunHistory =
            [
                new AutomatedJournalScheduleRunHistory(
                    "old-completed-run",
                    DueAt.AddDays(-1),
                    DueAt.AddDays(-1),
                    DueAt.AddDays(-1).AddMinutes(1),
                    AutomatedJournalScheduleStateDto.DraftReady,
                    "Older cycle produced a draft.",
                    JournalEntryIds: [oldDraftId],
                    PeriodId: "2026-07",
                    PeriodStart: new DateOnly(2026, 7, 1),
                    PeriodEnd: new DateOnly(2026, 7, 31))
            ]
        });

        var status = await store.GetStatusAsync(
            "fund-alpha", BookId, "2026-07", tenantId: "tenant-alpha", companyId: "company-alpha", entityId: "entity-alpha");

        status.State.Should().Be(currentState);
        status.JournalEntryIds.Should().BeEmpty();
        status.DraftReadyCount.Should().Be(0);
    }

    [Fact]
    public async Task StatusProjection_ScheduledCycleOutranksDraftReadyPeer()
    {
        var store = new InMemoryAutomatedJournalScheduleStore();
        await store.SaveAsync(FeeSchedule("fees-current-scheduled") with
        {
            State = AutomatedJournalScheduleStateDto.Scheduled,
            JournalEntryIds = []
        });
        await store.SaveAsync(DividendSchedule("dividends-draft-ready") with
        {
            State = AutomatedJournalScheduleStateDto.DraftReady,
            JournalEntryIds = [Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]
        });

        var status = await store.GetStatusAsync(
            "fund-alpha", BookId, "2026-07", tenantId: "tenant-alpha", companyId: "company-alpha", entityId: "entity-alpha");

        status.State.Should().Be(AutomatedJournalScheduleStateDto.Scheduled);
    }

    [Fact]
    public async Task StatusProjection_IsolatesTenantCompanyAndEntityScope()
    {
        var store = new InMemoryAutomatedJournalScheduleStore();
        await store.SaveAsync(FeeSchedule("fees-a-entity-a") with
        {
            TenantId = "tenant-a",
            CompanyId = "company-a",
            EntityId = "entity-a"
        });
        await store.SaveAsync(FeeSchedule("fees-a-entity-b") with
        {
            TenantId = "tenant-a",
            CompanyId = "company-a",
            EntityId = "entity-b"
        });
        await store.SaveAsync(FeeSchedule("fees-b-entity-a") with
        {
            TenantId = "tenant-b",
            CompanyId = "company-b",
            EntityId = "entity-a"
        });

        var status = await store.GetStatusAsync(
            "fund-alpha", BookId, "2026-07", tenantId: "tenant-a", companyId: "company-a", entityId: "entity-a");

        status.ConfiguredCount.Should().Be(1);
        status.TenantId.Should().Be("tenant-a");
        status.CompanyId.Should().Be("company-a");
        status.EntityId.Should().Be("entity-a");
    }

    private static AutomatedJournalScheduledWorker CreateWorker(
        IAutomatedJournalScheduleStore store,
        AutomatedJournalIntakeRunner runner,
        IAutomatedJournalDividendPositionResolver? positionResolver = null)
        => new(
            store,
            runner,
            NullLogger<AutomatedJournalScheduledWorker>.Instance,
            positionResolver ?? new CycleDividendPositionResolver());

    private static AutomatedJournalScheduleWorkItem FeeSchedule(string scheduleId)
        => new(
            ScheduleId: scheduleId,
            Kind: AutomatedJournalScheduleKind.FeeAccrual,
            FundProfileId: "fund-alpha",
            LedgerBookId: BookId,
            PeriodId: "2026-07",
            EntityId: "entity-alpha",
            Currency: "USD",
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: new DateOnly(2026, 7, 31),
            DueDate: new DateOnly(2026, 8, 1),
            DueTimeLocal: new TimeOnly(9, 0),
            TimeZoneId: "UTC",
            Actor: "automated-journal-scheduler",
            BeginningNav: 1_000_000m,
            EndingNavBeforeFees: 1_100_000m,
            HighWaterMark: 1_050_000m,
            ManagementFeeRate: 0.02m,
            PerformanceFeeRate: 0.20m,
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha",
            CapitalAccountReconciliation: Reconciliation());

    private static AutomatedJournalCapitalAccountReconciliationDto Reconciliation(
        decimal confidence = 0.98m,
        bool reconciled = true,
        decimal maximumVarianceTolerance = 0m,
        DateTimeOffset? reviewedAtUtc = null)
        => new(
            ReconciliationId: "capital-tie-out-2026-07",
            PeriodId: "2026-07",
            Currency: "USD",
            ReconciledBeginningNav: 1_000_000m,
            ReconciledEndingNavBeforeFees: 1_100_000m,
            ReconciledHighWaterMark: 1_050_000m,
            CapitalAccountOpeningBalance: 1_000_000m,
            CapitalAccountEndingBalanceBeforeFees: 1_100_000m,
            CapitalAccountHighWaterMark: 1_050_000m,
            MaximumVarianceTolerance: maximumVarianceTolerance,
            ConfidenceScore: confidence,
            IsReconciled: reconciled,
            SourceVersion: "capital-ledger:v42",
            ReviewedBy: "fund-controller",
            ReviewedAtUtc: reviewedAtUtc ?? DueAt.AddHours(-2),
            EvidenceLinks:
            [
                new OperationsEvidenceLinkDto(
                    "capital-tie-out-evidence-2026-07",
                    "Reviewed capital-account reconciliation",
                    "evidence://capital-accounts/fund-alpha/2026-07/v42",
                    "capital-account-subledger",
                    reviewedAtUtc ?? DueAt.AddHours(-2))
            ]);

    private static AutomatedJournalScheduleWorkItem DividendSchedule(
        string scheduleId,
        decimal withholdingRate = 0m,
        decimal minimumConfidence = 0.75m)
        => new(
            ScheduleId: scheduleId,
            Kind: AutomatedJournalScheduleKind.DividendCapture,
            FundProfileId: "fund-alpha",
            LedgerBookId: BookId,
            PeriodId: "2026-07",
            EntityId: "entity-alpha",
            Currency: "USD",
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: new DateOnly(2026, 7, 31),
            DueDate: new DateOnly(2026, 8, 1),
            DueTimeLocal: new TimeOnly(9, 0),
            TimeZoneId: "UTC",
            Actor: "automated-journal-scheduler",
            Positions: [new DividendAccrualPosition("AAPL", 999_999m)],
            WithholdingTaxRate: withholdingRate,
            MinimumCorporateActionConfidence: minimumConfidence,
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha",
            PositionSnapshotScopes: [new AutomatedJournalPositionSnapshotScope("run-alpha", "account-alpha")]);

    private static CorporateActionDto DividendAction(
        DateOnly? payDate,
        string? currency,
        DateOnly? recordDate)
        => DividendAction(new DateOnly(2026, 7, 2), 0.26m, payDate, currency, recordDate);

    private static CorporateActionDto DividendAction(
        DateOnly exDate,
        decimal dividendPerShare,
        DateOnly? payDate,
        string? currency,
        DateOnly? recordDate)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: SecurityId,
            EventType: CorporateActionEventTypes.Dividend,
            ExDate: exDate,
            PayDate: payDate,
            DividendPerShare: dividendPerShare,
            Currency: currency,
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null,
            RecordDate: recordDate);

    private static AccountSnapshotRecord PositionSnapshot(DateTimeOffset asOf, decimal? quantity)
        => new(
            "run-alpha",
            "account-alpha",
            "Account Alpha",
            "Brokerage",
            Cash: 0m,
            MarginBalance: 0m,
            UnrealisedPnl: 0m,
            RealisedPnl: 0m,
            Positions: quantity.HasValue
                ? [new PositionRecord("AAPL", quantity.Value, 100m, 0m, 0m)]
                : [],
            AsOf: asOf,
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha",
            FundProfileId: "fund-alpha",
            LedgerBookId: BookId,
            EntityId: "entity-alpha");

    private sealed record Fixture(
        AutomatedJournalIntakeRunner Runner,
        ManualJournalEntryWorkbenchService Workbench,
        StubCapitalAccountReconciliationResolver CapitalAccountResolver);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubCapitalAccountReconciliationResolver(
        AutomatedJournalCapitalAccountReconciliationDto? reconciliation)
        : IAutomatedJournalCapitalAccountReconciliationResolver
    {
        public AutomatedJournalCapitalAccountReconciliationDto? Reconciliation { get; set; } = reconciliation;

        public List<AutomatedJournalCapitalAccountReconciliationScope> Scopes { get; } = [];

        public Task<AutomatedJournalCapitalAccountReconciliationDto?> ResolveAsync(
            AutomatedJournalCapitalAccountReconciliationScope scope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Scopes.Add(scope);
            return Task.FromResult(Reconciliation);
        }
    }

    private sealed class CycleDividendPositionResolver : IAutomatedJournalDividendPositionResolver
    {
        public Task<AutomatedJournalDividendPositionResolution> ResolveAsync(
            AutomatedJournalScheduleWorkItem workItem,
            DateTimeOffset evaluatedAtUtc,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var quantity = workItem.PeriodStart.Month == 7 ? 400m : 250m;
            var asOf = new DateTimeOffset(workItem.PeriodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var route = $"evidence://position-snapshots/run-alpha/account-alpha?asOf={Uri.EscapeDataString(asOf.ToString("O"))}";
            var reference = new JournalEvidenceReference(
                $"position:{workItem.PeriodId}",
                route,
                "position-snapshot",
                "position-snapshot-store",
                asOf,
                "automated-journal",
                "account-alpha");
            return Task.FromResult(new AutomatedJournalDividendPositionResolution(
                [new DividendAccrualPosition(
                    "AAPL",
                    quantity,
                    FinancialAccountId: "account-alpha",
                    PositionAsOfUtc: asOf,
                    PositionEvidenceReferences: [reference])],
                [new OperationsEvidenceLinkDto(
                    reference.EvidenceId,
                    "Authoritative ex-date position snapshot",
                    route,
                    reference.SourceSystem,
                    asOf)],
                []));
        }
    }

    private static Fixture CreateFixture(IReadOnlyList<CorporateActionDto>? corporateActions = null)
    {
        var configurationStore = new InMemoryAccountingConfigurationStore();
        configurationStore.SaveAsync(ConfigurationWorkspace()).GetAwaiter().GetResult();
        var configurationService = new AccountingConfigurationService(
            configurationStore,
            new InMemoryAccountingActionAuditStore());
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configurationService,
            new InMemoryAccountingActionAuditStore());
        return CreateFixture(corporateActions, configurationService, draftStore, workbench);
    }

    private static async Task<Fixture> CreateFileFixtureAsync(
        string directory,
        bool initializeConfiguration,
        IReadOnlyList<CorporateActionDto>? corporateActions = null)
    {
        var configurationStore = new FileAccountingConfigurationStore(
            Path.Combine(directory, "accounting-configuration.json"));
        if (initializeConfiguration)
        {
            await configurationStore.SaveAsync(ConfigurationWorkspace());
        }

        var configurationService = new AccountingConfigurationService(configurationStore, configurationStore);
        var draftStore = new FileManualJournalEntryDraftStore(Path.Combine(directory, "manual-journal-drafts.json"));
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configurationService,
            configurationStore);
        return CreateFixture(corporateActions, configurationService, draftStore, workbench);
    }

    private static Fixture CreateFixture(
        IReadOnlyList<CorporateActionDto>? corporateActions,
        IAccountingConfigurationService configurationService,
        IManualJournalEntryDraftStore draftStore,
        ManualJournalEntryWorkbenchService workbench)
    {
        var intake = new AutomatedJournalDraftIntakeService(workbench, draftStore, configurationService);
        var securityMaster = corporateActions is null
            ? null
            : new FakeSecurityMasterQueryService(corporateActions);
        var capitalAccountResolver = new StubCapitalAccountReconciliationResolver(Reconciliation());
        return new Fixture(
            new AutomatedJournalIntakeRunner(
                intake,
                new FeeScheduleAccrualEventProducer(),
                securityMaster is null ? null : new CorporateActionDividendEventProducer(securityMaster),
                capitalAccountReconciliationResolver: capitalAccountResolver),
            workbench,
            capitalAccountResolver);
    }

    private static AccountingConfigurationWorkspaceDto ConfigurationWorkspace()
        => new(
            "fund-alpha",
            LedgerBookId: null,
            AccountingConfigurationStatusDto.Draft,
            "test",
            DateTimeOffset.UtcNow,
            LedgerBooks: [],
            ChartOfAccounts:
            [
                Node("Assets:Dividend Receivable", "Dividend Receivable", "Asset"),
                Node("Income:Dividend Income", "Dividend Income", "Revenue"),
                Node("Expenses:Withholding Tax Expense", "Withholding Tax Expense", "Expense"),
                Node("Liabilities:Withholding Tax Payable", "Withholding Tax Payable", "Liability"),
                Node("Expenses:Management Fee Expense", "Management Fee Expense", "Expense"),
                Node("Liabilities:Management Fee Payable", "Management Fee Payable", "Liability"),
                Node("Expenses:Performance Fee Expense", "Performance Fee Expense", "Expense"),
                Node("Liabilities:Performance Fee Payable", "Performance Fee Payable", "Liability")
            ],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues: [],
            AuditTrail: [],
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha");

    private static ChartOfAccountsNodeDto Node(string path, string name, string type)
        => new(NodeId: path, Path: path, AccountName: name, AccountType: type);

    private sealed class FakeSecurityMasterQueryService(IReadOnlyList<CorporateActionDto> corporateActions)
        : ISecurityMasterQueryService
    {
        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            CancellationToken ct = default,
            DateTimeOffset? asOfUtc = null)
            => Task.FromResult<SecurityDetailDto?>(
                identifierKind == SecurityIdentifierKind.Ticker &&
                string.Equals(identifierValue, "AAPL", StringComparison.OrdinalIgnoreCase)
                    ? Detail(SecurityId)
                    : null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>(
                corporateActions.Where(action => action.SecurityId == securityId).ToArray());

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(Detail(securityId));

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
            => Task.FromResult(Detail(securityId));

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
            => Task.FromResult<TradingParametersDto?>(null);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);

        private static SecurityDetailDto Detail(Guid securityId)
        {
            var emptyTerms = JsonDocument.Parse("{}").RootElement;
            return new SecurityDetailDto(
                securityId,
                "Equity",
                SecurityStatusDto.Active,
                "Test Security",
                "USD",
                emptyTerms,
                emptyTerms,
                Identifiers: [],
                Aliases: [],
                Version: 1,
                EffectiveFrom: DateTimeOffset.UtcNow.AddYears(-1),
                EffectiveTo: null);
        }
    }
}
