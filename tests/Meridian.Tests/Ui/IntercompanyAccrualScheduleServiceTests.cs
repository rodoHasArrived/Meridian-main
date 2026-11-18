using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Data = Meridian.Tests.FinancialOperations.Intercompany.IntercompanyProposalTestData;

namespace Meridian.Tests.Ui;

/// <summary>
/// Opt-in accrual schedules over a synthetic monthly loan from 1 June 2026. The schedule is approved on 2 July, so
/// June (last day 30 June) is not due; July and August are complete by 3 September; September is not yet complete.
/// The drafter is a fake over a real in-memory draft store; nothing here posts.
/// </summary>
public sealed class IntercompanyAccrualScheduleServiceTests : IDisposable
{
    private static readonly Guid Interest = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid Principal = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly DateOnly AsOf = new(2026, 9, 3);
    private readonly string _path = Path.Combine(Path.GetTempPath(), "meridian-schedule-service-" + Guid.NewGuid().ToString("N"), "schedules.json");
    private readonly InMemoryManualJournalEntryDraftStore _drafts = new();
    private readonly Drafter _drafter;
    private readonly IIntercompanyAccrualScheduleStore _store;
    private readonly FixedClock _clock = new(new(2026, 7, 2, 9, 0, 0, TimeSpan.Zero));

    public IntercompanyAccrualScheduleServiceTests()
    {
        _drafter = new Drafter(_drafts);
        _store = new FileIntercompanyAccrualScheduleStore(_path, new NoTenant());
    }

    [Fact]
    public async Task AnActiveScheduleDraftsEachCompletedPeriodAfterApproval_ThenNeverRedraftsThem()
    {
        var service = Service();
        var schedule = await ActiveAsync(service);

        var first = await service.RunAsync("tenant-a", "company-a", schedule.ScheduleId, AsOf);
        var second = await service.RunAsync("tenant-a", "company-a", schedule.ScheduleId, AsOf);

        Assert.Equal([(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1)), (new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1))],
            first.Windows.Select(item => (item.AccrualFrom, item.AccrualTo)));
        Assert.All(first.Windows, item => Assert.Equal(IntercompanyAccrualScheduleOutcomeDto.Drafted, item.Outcome));
        Assert.All(second.Windows, item => Assert.Equal(IntercompanyAccrualScheduleOutcomeDto.AlreadyDrafted, item.Outcome));
        Assert.Equal(2, _drafter.Created.Count);
        Assert.All(_drafter.Created, item => Assert.Equal(IntercompanyAccrualScheduleService.PreparerFor(schedule), item.Actor));
        Assert.All(_drafter.Created, item => Assert.Equal("digest-" + item.Request.Accrual.AccrualFrom.ToString("yyyyMMdd"), item.Request.ExpectedEvidenceDigest));
    }

    [Fact]
    public async Task ARefusedPeriodIsReportedWithItsReason_AndTheOthersStillDraft()
    {
        _drafter.Refuse = new(2026, 8, 1);
        var service = Service();
        var schedule = await ActiveAsync(service);

        var run = await service.RunAsync("tenant-a", "company-a", schedule.ScheduleId, AsOf);

        Assert.Equal([IntercompanyAccrualScheduleOutcomeDto.Drafted, IntercompanyAccrualScheduleOutcomeDto.Refused], run.Windows.Select(item => item.Outcome));
        Assert.Contains("do not cover", run.Windows[1].Reason);
        Assert.Single(_drafter.Created);
    }

    [Fact]
    public async Task AProposedScheduleShowsWhatWouldBeDue_ButNeverDrafts()
    {
        var service = Service();
        var proposed = await service.ProposeAsync("tenant-a", "company-a", "maker", Propose());

        var due = await service.DueAsync("tenant-a", "company-a", proposed.ScheduleId, AsOf);

        Assert.False(due.Executed);
        Assert.Empty(due.Windows); // Proposed today: nothing has completed since.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync("tenant-a", "company-a", proposed.ScheduleId, AsOf));
        Assert.Empty(_drafter.Created);
    }

    [Fact]
    public async Task OneOpenScheduleExistsPerBookAndProfile_AndTheProposerCannotApprove()
    {
        var service = Service();
        var proposed = await service.ProposeAsync("tenant-a", "company-a", "maker", Propose());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProposeAsync("tenant-a", "company-a", "maker", Propose()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync("tenant-a", "company-a", "maker", proposed.ScheduleId,
            new(Guid.NewGuid(), IntercompanyAccrualScheduleActionDto.Approve, 1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DueAsync("tenant-a", "company-b", proposed.ScheduleId, AsOf));
    }

    [Fact]
    public async Task TheRunnerDraftsEveryActiveSchedule_AndIgnoresDisabledOnes()
    {
        var service = Service();
        var active = await ActiveAsync(service);
        var runnerClock = new FixedClock(new(2026, 9, 3, 1, 0, 0, TimeSpan.Zero));
        var provider = new ServiceCollection().AddSingleton(_store).AddSingleton(service).BuildServiceProvider();

        var runs = await new IntercompanyAccrualScheduleRunner(provider, NullLogger<IntercompanyAccrualScheduleRunner>.Instance, runnerClock).RunOnceAsync(CancellationToken.None);
        await service.DecideAsync("tenant-a", "company-a", "checker", active.ScheduleId,
            new(Guid.NewGuid(), IntercompanyAccrualScheduleActionDto.Disable, 2, "Loan repaid."));
        var afterDisable = await new IntercompanyAccrualScheduleRunner(provider, NullLogger<IntercompanyAccrualScheduleRunner>.Instance, runnerClock).RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, Assert.Single(runs).Windows.Count(item => item.Outcome == IntercompanyAccrualScheduleOutcomeDto.Drafted));
        Assert.Empty(afterDisable);
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private async Task<IntercompanyAccrualScheduleDto> ActiveAsync(IntercompanyAccrualScheduleService service)
    {
        var proposed = await service.ProposeAsync("tenant-a", "company-a", "maker", Propose());
        return await service.DecideAsync("tenant-a", "company-a", "checker", proposed.ScheduleId,
            new(Guid.NewGuid(), IntercompanyAccrualScheduleActionDto.Approve, 1));
    }

    private static ProposeIntercompanyAccrualScheduleRequest Propose()
        => new(Guid.NewGuid(), Data.BookA, IntercompanyAccrualScheduleKindDto.LoanInterest, Interest, Principal, "Monthly loan interest for the lender book.");

    private IntercompanyAccrualScheduleService Service()
    {
        var profile = IntercompanyArrangementTestData.Create(Interest, Guid.NewGuid(), IntercompanyAccountingTemplateKindDto.LoanInterest,
            Data.EntityA, Data.EntityB, "USD", [new(Data.EntityA, Data.BookA, "a-int-recv", "a-int-pay", null, "a-int-income", "a-int-expense"),
                new(Data.EntityB, Data.BookB, "b-int-recv", "b-int-pay", null, "b-int-income", "b-int-expense")],
            Guid.NewGuid(), new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var terms = new IntercompanyAgreementTermsDto
        {
            StructuredTerms = new(new(new(2026, 6, 1), new(2027, 6, 1), IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following),
                IntercompanyRateBasisDto.Fixed, 0.05m, DayCount: IntercompanyDayCountDto.Actual360, Compounding: IntercompanyCompoundingDto.Simple)
        };
        profile = profile with { LegalRevision = profile.LegalRevision with { Terms = profile.LegalRevision.Terms with { PartyOneRole = "Lender", PartyTwoRole = "Borrower", Terms = terms } } };
        var history = new IntercompanyAccountingArrangementHistoryDto([new(Guid.NewGuid(), "digest", IntercompanyAccountingArrangementActionDto.Approve,
            "mapping-reviewer", profile.EffectiveFrom, null, profile.Arrangement)]);
        var arrangements = new Mock<IIntercompanyAccountingArrangementStore>();
        arrangements.Setup(x => x.GetAsync(It.IsAny<string>(), Interest, It.IsAny<CancellationToken>())).ReturnsAsync(history);
        var journals = new Mock<ILedgerJournalStore>();
        journals.Setup(x => x.GetLedgerBookAsync(Data.BookA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedgerBookRecord(Data.BookA, "source-fund", Data.EntityA, FundStructureNodeKindDto.Entity, "Source", "USD", Data.Now, Data.Now));
        return new(_store, _drafter, IntercompanyArrangementTestData.Service(arrangements.Object, profile),
            new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]), journals.Object, _drafts, _clock);
    }

    /// <summary>Ready unless the window starts on <see cref="Refuse"/>; a created draft is saved so later runs see it.</summary>
    private sealed class Drafter(InMemoryManualJournalEntryDraftStore drafts) : IIntercompanyInterestAccrualDrafter
    {
        public DateOnly? Refuse { get; set; }
        public List<(string Actor, CreateIntercompanyAccrualDraftRequest Request)> Created { get; } = [];

        public Task<IntercompanyAccrualProposalDto> PreviewAsync(string tenantId, string? companyId, string actor, IntercompanyAccrualRequestDto request, CancellationToken ct = default)
        {
            var refused = request.AccrualFrom == Refuse;
            var computation = new IntercompanyAccrualPreviewDto(refused ? IntercompanyAccrualRules.StatusUnavailable : IntercompanyAccrualRules.StatusReady,
                IntercompanyAccrualRules.RuleVersion, Guid.NewGuid(), "USD", request.AccrualFrom, request.AccrualTo, refused ? null : 100m, refused ? null : 100m,
                "kernel", [], [], refused ? ["Posted accruals do not cover 2026-07-15."] : []);
            if (refused)
                return Task.FromResult(new IntercompanyAccrualProposalDto(computation, computation.Diagnostics));
            var draft = Draft(IntercompanyAccrualSourceBuilder.DraftId(tenantId, request), request.AccrualTo.AddDays(-1), tenantId, companyId);
            var evidence = new IntercompanyAccrualEvidenceDto(IntercompanyAccrualRules.RuleVersion, draft.JournalEntryId, tenantId, request.SourceBookId,
                Data.EntityA, Data.EntityB, request.InterestArrangementId, Guid.NewGuid(), Guid.NewGuid(), 1, "hash", "tx", draft.AccountingDate,
                IntercompanyAccountRoleDto.Receivable, "a", "b", "USD", 100m, computation,
                new(request.PrincipalArrangementId, IntercompanyAccountRoleDto.Receivable, ["a"], ["b"], 1m, 1, "digest", []));
            return Task.FromResult(new IntercompanyAccrualProposalDto(computation, [], evidence, "digest-" + request.AccrualFrom.ToString("yyyyMMdd"), draft));
        }

        public async Task<ManualJournalEntryDraftDto> CreateDraftAsync(string tenantId, string? companyId, string actor, CreateIntercompanyAccrualDraftRequest request, CancellationToken ct = default)
        {
            Created.Add((actor, request));
            var draft = Draft(IntercompanyAccrualSourceBuilder.DraftId(tenantId, request.Accrual), request.Accrual.AccrualTo.AddDays(-1), tenantId, companyId);
            await drafts.SaveAsync(draft, ct);
            return draft;
        }

        private static ManualJournalEntryDraftDto Draft(Guid id, DateOnly date, string tenantId, string? companyId)
            => new(id, ManualJournalEntryStatusDto.Draft, "source-fund", Data.BookA, AccountingBasisKindDto.Primary, date, null, null, null, "USD",
                "Loan interest accrual", "accrual-schedule", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, [], [], [], TenantId: tenantId, CompanyId: companyId);
    }

    private sealed class NoTenant : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => null;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
