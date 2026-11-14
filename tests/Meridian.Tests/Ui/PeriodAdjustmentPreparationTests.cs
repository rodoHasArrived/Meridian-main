using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class PeriodAdjustmentPreparationTests
{
    private const string Fund = "synthetic-adjustments";
    private const string Tenant = "synthetic-tenant";
    private const string Company = "synthetic-company";
    private static readonly DateTimeOffset At = new(2026, 5, 31, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Preparation_PreservesClosedSource_QueuesCurrentPeriodInverse_AndReplaysExactly()
    {
        var fixture = await FixtureAsync();
        var result = await fixture.Service.PrepareAsync(fixture.Request, "preparer", Tenant, Company);
        var draft = result.Created.Should().ContainSingle().Subject;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        draft.AccountingDate.Should().Be(new DateOnly(2026, 6, 1));
        draft.PeriodId.Should().Be(fixture.Target.PeriodId.ToString("D"));
        draft.ReversalOfJournalEntryId.Should().Be(fixture.Source.Entry.JournalEntryId);
        draft.PriorPeriodAdjustmentSource!.OriginalEffectiveDate.Should().Be(new DateOnly(2026, 5, 31));
        draft.Lines.Should().Contain(x => x.AccountPath == "Assets:Cash" && x.Side == AccountingTemplateLineSideDto.Credit && x.Amount == 100m);
        draft.Lines.Should().Contain(x => x.AccountPath == "Equity:Capital Account" && x.Side == AccountingTemplateLineSideDto.Debit && x.Amount == 100m);
        draft.ApprovalId.Should().BeNull();
        fixture.SourcePeriod.Status.Should().Be("HardClosed");
        fixture.Source.Entry.Lines[0].Debit.Should().Be(100m);
        var replay = await fixture.Service.PrepareAsync(fixture.Request, "preparer", Tenant, Company);
        replay.Created.Should().BeEmpty();
        replay.Skipped.Should().ContainSingle().Which.IsReadyDuplicate.Should().BeTrue();
        fixture.Store.Verify(x => x.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Store.Verify(x => x.SavePeriodAsync(It.IsAny<LedgerAccountingPeriod>(), It.IsAny<long>(), It.IsAny<PeriodCloseEventRecord?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Preparation_RejectsChangedIntentTermsAndWrongBookOrDate()
    {
        var fixture = await FixtureAsync();
        await fixture.Service.PrepareAsync(fixture.Request, "preparer", Tenant, Company);
        var collision = () => fixture.Service.PrepareAsync(fixture.Request with { Reason = "Different explanation" }, "preparer", Tenant, Company);
        await collision.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different source, target, policy or reason*");
        var date = () => fixture.Service.PrepareAsync(fixture.Request with { AccountingDate = new DateOnly(2026, 7, 1) }, "preparer", Tenant, Company);
        await date.Should().ThrowAsync<InvalidOperationException>().WithMessage("*valid target accounting date*");
        fixture.Store.Setup(x => x.GetPeriodAsync(fixture.Target.PeriodId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Target with { LedgerBookId = Guid.NewGuid() });
        var book = () => fixture.Service.PrepareAsync(fixture.Request, "preparer", Tenant, Company);
        await book.Should().ThrowAsync<InvalidOperationException>().WithMessage("*same book*");
    }

    [Fact]
    public async Task MarkReversal_IsExplicit_AndOnlyAtNextPeriodStart()
    {
        var fixture = await FixtureAsync(mark: true);
        var result = await fixture.Service.PrepareAsync(fixture.Request with { ReversePeriodEndMark = true }, "preparer", Tenant, Company);
        result.Created.Should().ContainSingle().Which.PriorPeriodAdjustmentSource!.ReversePeriodEndMark.Should().BeTrue();
        var later = () => fixture.Service.PrepareAsync(fixture.Request with
        { IntentId = "late-mark", ReversePeriodEndMark = true, AccountingDate = new DateOnly(2026, 6, 2) }, "preparer", Tenant, Company);
        await later.Should().ThrowAsync<InvalidOperationException>().WithMessage("*first day*");
        var other = await FixtureAsync();
        var unsupported = () => other.Service.PrepareAsync(other.Request with { ReversePeriodEndMark = true }, "preparer", Tenant, Company);
        await unsupported.Should().ThrowAsync<InvalidOperationException>().WithMessage("*period-end fair-value mark*");
    }

    [Fact]
    public async Task PreparedSourceAuthority_CannotBeClearedOrChangeInverseLines()
    {
        var fixture = await FixtureAsync();
        var original = (await fixture.Service.PrepareAsync(fixture.Request, "preparer", Tenant, Company)).Created.Single();
        var changed = original with
        {
            PriorPeriodAdjustmentSource = null,
            ReversalOfJournalEntryId = Guid.NewGuid(),
            Lines = original.Lines.Select(line => line with { Amount = line.Amount + 1m }).ToArray()
        };
        var saved = await fixture.Workbench.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(changed,
            "preparer", LedgerBookId: fixture.Request.LedgerBookId, TenantId: Tenant, CompanyId: Company));
        saved.PriorPeriodAdjustmentSource.Should().BeEquivalentTo(original.PriorPeriodAdjustmentSource);
        saved.ReversalOfJournalEntryId.Should().Be(original.ReversalOfJournalEntryId);
        saved.Status.Should().Be(ManualJournalEntryStatusDto.NeedsFix);
        saved.ValidationIssues.Should().Contain(x => x.Code == "manual-je.prior-period-authority-required");
    }

    [Fact]
    public async Task ScheduledMarkReversal_RetainsPolicyBeforeDue_AndRecoversDraftAcceptanceAfterRestart()
    {
        var fixture = await FixtureAsync(mark: true);
        var directory = Path.Combine(Path.GetTempPath(), "meridian-period-reversal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "schedules.json");
            var schedules = new FileAutomatedJournalScheduleStore(path);
            var service = new PeriodAdjustmentPreparationService(fixture.Store.Object, fixture.Intake, schedules);
            var request = fixture.Request with { ReversePeriodEndMark = true };
            var scheduled = await service.ScheduleMarkReversalAsync(request, "preparer", Tenant, Company);
            scheduled.PeriodAdjustment.Should().Be(request);
            scheduled.RecurrenceEnabled.Should().BeFalse();
            var worker = Worker(schedules);
            var due = scheduled.ScheduledForUtc!.Value;
            (await worker.RunDueAsync(due.AddTicks(-1))).Runs.Should().BeEmpty();
            (await worker.RunDueAsync(due)).Runs.Should().ContainSingle().Which.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
            var retained = (await schedules.GetAsync(scheduled.ScheduleId))!;
            retained.JournalEntryIds.Should().ContainSingle();

            // Stand in for losing the final acknowledgement after durable draft acceptance.
            await schedules.SaveAsync(retained with
            {
                State = AutomatedJournalScheduleStateDto.Running,
                RunHistory = retained.RunHistory.Select(history => history with
                { State = AutomatedJournalScheduleStateDto.Running, CompletedAtUtc = null }).ToArray()
            });
            var restarted = new FileAutomatedJournalScheduleStore(path);
            var recovered = await Worker(restarted).RunDueAsync(due.AddMinutes(1));
            recovered.Runs.Should().ContainSingle().Which.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
            (await restarted.GetAsync(scheduled.ScheduleId))!.JournalEntryIds.Should().Equal(retained.JournalEntryIds);
            (await Worker(restarted).RunDueAsync(due.AddMinutes(2))).Runs.Should().BeEmpty();
            fixture.Store.Verify(x => x.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);

            AutomatedJournalScheduledWorker Worker(IAutomatedJournalScheduleStore store) => new(store,
                new AutomatedJournalIntakeRunner(fixture.Intake, new FeeScheduleAccrualEventProducer()),
                NullLogger<AutomatedJournalScheduledWorker>.Instance,
                periodAdjustmentJournalStore: fixture.Store.Object, periodAdjustmentIntake: fixture.Intake);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FailedMarkReversalSchedule_CanRetryOnlyThroughSameValidatedPolicy()
    {
        var fixture = await FixtureAsync(mark: true);
        var schedules = new InMemoryAutomatedJournalScheduleStore();
        var service = new PeriodAdjustmentPreparationService(fixture.Store.Object, fixture.Intake, schedules);
        var request = fixture.Request with { ReversePeriodEndMark = true };
        var scheduled = await service.ScheduleMarkReversalAsync(request, "preparer", Tenant, Company);
        fixture.Store.Setup(x => x.GetPeriodAsync(fixture.Target.PeriodId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Injected period source outage"));
        var worker = new AutomatedJournalScheduledWorker(schedules,
            new AutomatedJournalIntakeRunner(fixture.Intake, new FeeScheduleAccrualEventProducer()),
            NullLogger<AutomatedJournalScheduledWorker>.Instance,
            periodAdjustmentJournalStore: fixture.Store.Object, periodAdjustmentIntake: fixture.Intake);
        (await worker.RunDueAsync(scheduled.ScheduledForUtc!.Value)).Runs.Should().ContainSingle()
            .Which.State.Should().Be(AutomatedJournalScheduleStateDto.Failed);
        fixture.Store.Setup(x => x.GetPeriodAsync(fixture.Target.PeriodId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Target);
        var rearmed = await service.ScheduleMarkReversalAsync(request, "retrying-controller", Tenant, Company);
        rearmed.State.Should().Be(AutomatedJournalScheduleStateDto.Scheduled);
        rearmed.CreatedBy.Should().Be("preparer");
        rearmed.LastConfiguredBy.Should().Be("retrying-controller");
        rearmed.RunHistory.Should().Contain(history => history.State == AutomatedJournalScheduleStateDto.Failed
            && history.HistoryKind == AutomatedJournalScheduleHistoryKind.Rearm && history.Actor == "retrying-controller");
        (await worker.RunDueAsync(scheduled.ScheduledForUtc.Value.AddMinutes(1))).Runs.Should().ContainSingle()
            .Which.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        var completed = (await schedules.GetAsync(scheduled.ScheduleId))!;
        completed.RunHistory.Should().Contain(history => history.State == AutomatedJournalScheduleStateDto.Failed
            && history.HistoryKind == AutomatedJournalScheduleHistoryKind.Rearm
            && history.Summary.Contains("Injected period source outage")
            && history.Blockers.Any(blocker => blocker.Contains("Injected period source outage"))
            && history.CompletedAtUtc.HasValue);
        completed.RunHistory.Should().Contain(history => history.State == AutomatedJournalScheduleStateDto.DraftReady
            && history.HistoryKind == AutomatedJournalScheduleHistoryKind.Execution);
        var altered = () => service.ScheduleMarkReversalAsync(request with { Reason = "Altered policy" }, "preparer", Tenant, Company);
        await altered.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different source, target, scope or policy terms*");
    }

    [Fact]
    public async Task ForeignCurrencySource_FailsClosedBeforeIntake()
    {
        var fixture = await FixtureAsync(foreign: true);
        var attempt = () => fixture.Service.PrepareAsync(fixture.Request, "preparer", Tenant, Company);
        await attempt.Should().ThrowAsync<InvalidOperationException>().WithMessage("*native book-currency lines only*");
    }

    private sealed record Fixture(Mock<ILedgerJournalStore> Store, LedgerAccountingPeriod SourcePeriod,
        LedgerAccountingPeriod Target, LedgerJournalEntryRecord Source, PreparePeriodAdjustmentRequest Request,
        PeriodAdjustmentPreparationService Service, ManualJournalEntryWorkbenchService Workbench,
        AutomatedJournalDraftIntakeService Intake);

    private static async Task<Fixture> FixtureAsync(bool mark = false, bool foreign = false)
    {
        var book = new LedgerBookRecord(Guid.NewGuid(), Fund, Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Synthetic book", "USD", At, At);
        var period = new LedgerAccountingPeriod(Guid.NewGuid(), book.LedgerBookId, 2026, 5, "May", new(2026, 5, 1), new(2026, 5, 31), "HardClosed", At.AddDays(-30), At, 3);
        var target = new LedgerAccountingPeriod(Guid.NewGuid(), book.LedgerBookId, 2026, 6, "June", new(2026, 6, 1), new(2026, 6, 30), "Open", At, null, 1);
        var id = Guid.NewGuid();
        var currency = foreign ? "EUR" : "USD";
        var entry = new JournalEntry(id, At, "Synthetic source",
        [
            new LedgerEntry(Guid.NewGuid(), id, At, LedgerAccounts.Cash, 100m, 0m, "Synthetic source",
                currency: new LedgerEntryCurrency(currency, "USD", 100m, 0m, 1m)),
            new LedgerEntry(Guid.NewGuid(), id, At, LedgerAccounts.CapitalAccount, 0m, 100m, "Synthetic source",
                currency: new LedgerEntryCurrency(currency, "USD", 0m, 100m, 1m))
        ], Metadata(mark));
        var source = new LedgerJournalEntryRecord(entry, book.LedgerBookId, period.PeriodId, null, null, 1, At);
        var store = new Mock<ILedgerJournalStore>();
        store.Setup(x => x.GetLedgerBookAsync(book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        store.Setup(x => x.QueryAsync(It.Is<LedgerJournalEntryQuery>(q => q.JournalEntryId == id && q.LedgerBookId == book.LedgerBookId), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { source });
        store.Setup(x => x.GetPeriodAsync(period.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(period);
        store.Setup(x => x.GetPeriodAsync(target.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        var configStore = new InMemoryAccountingConfigurationStore();
        await configStore.SaveAsync(new AccountingConfigurationWorkspaceDto(Fund, null,
            AccountingConfigurationStatusDto.Draft, "test", At, [],
            [new("cash", "Assets:Cash", "Cash", "Asset"), new("capital", "Equity:Capital Account", "Capital Account", "Equity")],
            [], [], [], [], TenantId: Tenant, CompanyId: Company));
        var config = new AccountingConfigurationService(configStore, new InMemoryAccountingActionAuditStore());
        var drafts = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(drafts, config, new InMemoryAccountingActionAuditStore(), journalStore: store.Object);
        var intake = new AutomatedJournalDraftIntakeService(workbench, drafts, config);
        var request = new PreparePeriodAdjustmentRequest(book.LedgerBookId, id, target.PeriodId, new(2026, 6, 1), "synthetic-correction-1", "Correct retained source in current period");
        return new(store, period, target, source, request, new PeriodAdjustmentPreparationService(store.Object, intake), workbench, intake);
    }

    private static JournalEntryMetadata Metadata(bool mark)
    {
        if (!mark)
            return new(EffectiveDate: new(2026, 5, 31), ActivityType: "capital");
        var evidence = JsonSerializer.Serialize(new[] { new ValuationMarkEvidence(Fund, "SYNTHETIC", null, null,
            new(2026, 5, 31), new(2026, 5, 31), 100m, DailyPortfolioPriceConfidence.High, 1,
            DailyPortfolioPriceConfidence.Medium, "synthetic-policy-v1", "evidence://synthetic/closing-mark") });
        return new(ActivityType: "ManualJournalEntry", EffectiveDate: new(2026, 5, 31),
            IdempotencyKey: "fair-value|synthetic", Tags: new Dictionary<string, string>
            {
                [ValuationMarkEvidenceGuard.EvidenceTag] = evidence,
                [ValuationMarkEvidenceGuard.DigestTag] = Sha256Digest.ComputeUtf8(evidence)
            });
    }
}
