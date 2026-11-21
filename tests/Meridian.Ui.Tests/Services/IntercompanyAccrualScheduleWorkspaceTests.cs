using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

/// <summary>Desktop accrual schedule state over synthetic shared-API responses; no WPF runtime is involved.</summary>
public sealed class IntercompanyAccrualScheduleWorkspaceTests
{
    private static readonly Guid LenderEntity = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid BorrowerEntity = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    private static readonly Guid Book = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherBook = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    private static readonly Guid LoanAgreement = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherAgreement = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly Guid Interest = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly Guid Principal = Guid.Parse("d2222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherLoan = Guid.Parse("d3333333-3333-3333-3333-333333333333");
    private static readonly Guid ProposedId = Guid.Parse("e1111111-1111-1111-1111-111111111111");
    private static readonly Guid ActiveId = Guid.Parse("e2222222-2222-2222-2222-222222222222");
    private static readonly Guid DraftId = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ChoosingABookListsItsExactSchedulesAndTheSameAgreementProfiles()
    {
        var client = new Client();
        using var view = new IntercompanyAccrualScheduleWorkspace(client);
        await view.LoadChoicesCommand.ExecuteAsync(null);
        Assert.Equal(2, view.BookChoices.Count);

        view.SourceBook = Book.ToString("D");
        await view.BookLoad;

        Assert.Equal([Book], client.ScheduleReads);
        Assert.Equal([ProposedId, ActiveId], view.Schedules.Select(row => row.ScheduleId));
        var proposed = view.Schedules[0];
        Assert.Equal((IntercompanyAccrualScheduleStatusDto.Proposed, 1L, "Not approved", "maker"),
            (proposed.Status, proposed.Version, proposed.ActiveFrom, proposed.ProposedBy));
        Assert.Contains("Loan interest", proposed.Profile);
        Assert.Equal("2026-11-02", view.Schedules[1].ActiveFrom);
        Assert.Contains("1 active, 1 awaiting approval", view.StatusText);
        Assert.Contains("nothing posts", view.StatusText);
        Assert.Equal(Interest.ToString("D"), Assert.Single(view.InterestProfileChoices).Id);

        view.InterestProfile = Interest.ToString("D");
        // The other agreement's Loan profile maps the same book but cannot supply this loan's principal.
        Assert.Equal(Principal.ToString("D"), Assert.Single(view.PrincipalProfileChoices).Id);
        Assert.Equal(Principal.ToString("D"), view.PrincipalProfile);
        Assert.True(view.CanManageLedger);
        Assert.Contains("proposer cannot approve", view.PermissionNote);
    }

    [Fact]
    public async Task ProposeSendsTheChosenProfilesAndReasonAndDraftsNothing()
    {
        var client = new Client();
        using var view = await OpenAsync(client);
        view.InterestProfile = Interest.ToString("D");
        Assert.False(view.ProposeCommand.CanExecute(null));
        view.ProposeReason = "  Monthly loan interest  ";
        await view.ProposeCommand.ExecuteAsync(null);

        var sent = Assert.Single(client.Proposals);
        Assert.Equal((Book, IntercompanyAccrualScheduleKindDto.LoanInterest, Interest, (Guid?)Principal, "Monthly loan interest"),
            (sent.LedgerBookId, sent.Kind, sent.ProfileArrangementId, sent.PrincipalArrangementId, sent.Reason));
        Assert.NotEqual(Guid.Empty, sent.RequestId);
        Assert.Equal(3, view.Schedules.Count);
        Assert.Equal(IntercompanyAccrualScheduleStatusDto.Proposed, view.SelectedSchedule!.Status);
        Assert.Contains("drafts nothing until another operator approves it", view.StatusText);
        Assert.Contains("proposer cannot approve", view.StatusText);
        Assert.Equal("", view.ProposeReason);
        Assert.Empty(client.Runs);
    }

    [Fact]
    public async Task ApproveSendsTheExactVersionTheReviewerSaw()
    {
        var client = new Client();
        using var view = await OpenAsync(client);
        view.SelectedSchedule = view.Schedules.Single(row => row.ScheduleId == ProposedId);
        Assert.False(view.DisableCommand.CanExecute(null));
        Assert.False(view.RunCommand.CanExecute(null));
        await view.ApproveCommand.ExecuteAsync(null);

        var (schedule, book, request) = Assert.Single(client.Decisions);
        Assert.Equal((ProposedId, Book, IntercompanyAccrualScheduleActionDto.Approve, 1L, (string?)null),
            (schedule, book, request.Action, request.ExpectedVersion, request.Reason));
        Assert.Equal(IntercompanyAccrualScheduleStatusDto.Active, view.SelectedSchedule!.Status);
        Assert.Equal(2, view.SelectedSchedule.Version);
        Assert.Contains("Approve · checker", view.History);
        Assert.Contains("nothing posts", view.StatusText);
        Assert.True(view.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefusedDecisionKeepsItsIdentityOnlyForAnIdenticalRetry()
    {
        var client = new Client { Decide = (_, _) => ApiResponse<IntercompanyAccrualScheduleDto>.Fail("A schedule is approved by someone other than the person who proposed it.", 409) };
        using var view = await OpenAsync(client);
        view.SelectedSchedule = view.Schedules.Single(row => row.ScheduleId == ProposedId);
        view.DecisionReason = "Wrong profile";
        await view.RejectCommand.ExecuteAsync(null);
        Assert.Contains("someone other than the person who proposed it", view.StatusText);
        Assert.Contains("Your input has been retained", view.StatusText);
        await view.RejectCommand.ExecuteAsync(null);
        view.DecisionReason = "Wrong principal profile";
        await view.RejectCommand.ExecuteAsync(null);

        var ids = client.Decisions.Select(item => item.Request.RequestId).ToArray();
        Assert.Equal(3, ids.Length);
        Assert.Equal(ids[0], ids[1]);
        Assert.NotEqual(ids[1], ids[2]);
        Assert.All(client.Decisions, item => Assert.Equal((IntercompanyAccrualScheduleActionDto.Reject, 1L), (item.Request.Action, item.Request.ExpectedVersion)));
        Assert.Equal("Wrong principal profile", client.Decisions[2].Request.Reason);
    }

    [Fact]
    public async Task ConfirmedDecisionRetiresItsIdentity()
    {
        var client = new Client();
        using var view = await OpenAsync(client);
        view.SelectedSchedule = view.Schedules.Single(row => row.ScheduleId == ActiveId);
        view.DecisionReason = "Loan repaid";
        await view.DisableCommand.ExecuteAsync(null);
        Assert.Equal(IntercompanyAccrualScheduleStatusDto.Disabled, view.SelectedSchedule!.Status);
        Assert.Contains("Future runs are stopped", view.StatusText);
        Assert.False(view.RunCommand.CanExecute(null));
        Assert.Equal((IntercompanyAccrualScheduleActionDto.Disable, 3L, "Loan repaid"),
            (client.Decisions[0].Request.Action, client.Decisions[0].Request.ExpectedVersion, client.Decisions[0].Request.Reason));
    }

    [Fact]
    public async Task ReadOnlyAccountMayPreviewDuePeriodsButHasNoMutationCommands()
    {
        var client = new Client { CanManageLedger = false };
        using var view = await OpenAsync(client);
        Assert.Empty(client.ProfileReads);
        Assert.Empty(view.InterestProfileChoices);
        Assert.StartsWith("Read-only", view.PermissionNote);
        view.SelectedSchedule = view.Schedules.Single(row => row.ScheduleId == ProposedId);
        view.DecisionReason = "Any reason";
        view.ProposeReason = "Any reason";
        Assert.False(view.ProposeCommand.CanExecute(null));
        Assert.False(view.ApproveCommand.CanExecute(null));
        Assert.False(view.RejectCommand.CanExecute(null));
        view.SelectedSchedule = view.Schedules.Single(row => row.ScheduleId == ActiveId);
        view.DecisionReason = "Any reason";
        Assert.False(view.DisableCommand.CanExecute(null));
        Assert.False(view.RunCommand.CanExecute(null));
        Assert.True(view.PreviewDueCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreviewAndRunShowEveryPeriodOutcomeFromTheServer()
    {
        var client = new Client();
        using var view = await OpenAsync(client);
        view.SelectedSchedule = view.Schedules.Single(row => row.ScheduleId == ActiveId);
        await view.PreviewDueCommand.ExecuteAsync(null);
        var (dueSchedule, dueBook, asOf) = Assert.Single(client.DueReads);
        Assert.Equal((ActiveId, Book, DateOnly.FromDateTime(DateTime.UtcNow)), (dueSchedule, dueBook, asOf));
        Assert.Equal(["Due", "Already drafted", "Refused"], view.Windows.Select(row => row.Outcome));
        Assert.Equal("2026-10-01 up to 2026-11-01", view.Windows[0].Period);
        Assert.Equal($"{DraftId:D} · Draft", view.Windows[1].Draft);
        Assert.Equal("No open period.", view.Windows[2].Reason);
        Assert.Contains("1 period(s) due", view.WindowStatus);
        Assert.Contains("Nothing was created", view.WindowStatus);
        Assert.Empty(client.Runs);

        await view.RunCommand.ExecuteAsync(null);
        Assert.Equal([(ActiveId, Book)], client.Runs);
        Assert.Equal(["Drafted", "Already drafted", "Refused"], view.Windows.Select(row => row.Outcome));
        Assert.Contains("1 period(s) drafted", view.WindowStatus);
        Assert.Contains("nothing was approved or posted", view.StatusText);
    }

    [Fact]
    public async Task ScopeChangeAndSignOutClearSchedulesPermissionAndPendingIdentity()
    {
        var pending = new TaskCompletionSource<ApiResponse<IntercompanyAccrualScheduleDto[]>>();
        var client = new Client();
        using var view = await OpenAsync(client);
        view.SelectedSchedule = view.Schedules.Single(row => row.ScheduleId == ActiveId);
        await view.PreviewDueCommand.ExecuteAsync(null);
        client.ListHandler = book => book == OtherBook ? pending.Task : Task.FromResult(ApiResponse<IntercompanyAccrualScheduleDto[]>.Ok([]));

        view.SourceBook = OtherBook.ToString("D");
        Assert.Empty(view.Schedules);
        Assert.Empty(view.Windows);
        Assert.Null(view.SelectedSchedule);
        Assert.Equal("", view.InterestProfile);

        view.ResetSession();
        pending.SetResult(ApiResponse<IntercompanyAccrualScheduleDto[]>.Ok([Schedule(ActiveId, OtherBook, IntercompanyAccrualScheduleStatusDto.Active, 3)]));
        await view.BookLoad;
        Assert.Empty(view.Schedules);
        Assert.Empty(view.BookChoices);
        Assert.False(view.CanManageLedger);
        Assert.Equal("", view.SourceBook);
        Assert.False(view.LoadCommand.CanExecute(null));
        Assert.False(view.IsBusy);
    }

    [Fact]
    public async Task UnavailableListingIsRefusedRatherThanShownAsEmpty()
    {
        var client = new Client { ListHandler = _ => Task.FromResult(ApiResponse<IntercompanyAccrualScheduleDto[]>.Fail("down", 503)) };
        using var view = await OpenAsync(client);
        Assert.Empty(view.Schedules);
        Assert.StartsWith("Accrual schedule request refused: down", view.StatusText);
    }

    private static async Task<IntercompanyAccrualScheduleWorkspace> OpenAsync(Client client)
    {
        var view = new IntercompanyAccrualScheduleWorkspace(client) { SourceBook = Book.ToString("D") };
        await view.BookLoad;
        return view;
    }

    internal static IntercompanyAccrualScheduleDto Schedule(Guid id, Guid book, IntercompanyAccrualScheduleStatusDto status, long version)
    {
        var events = new List<IntercompanyAccrualScheduleEventDto> { new(Guid.NewGuid(), Guid.NewGuid(), IntercompanyAccrualScheduleActionDto.Propose, "maker", At, "Monthly accrual") };
        if (version >= 2)
            events.Add(new(Guid.NewGuid(), Guid.NewGuid(), IntercompanyAccrualScheduleActionDto.Approve, "checker", At.AddDays(1)));
        return new(id, "tenant", "company", book, IntercompanyAccrualScheduleKindDto.LoanInterest, Interest, Principal, version, status,
            status == IntercompanyAccrualScheduleStatusDto.Proposed ? null : new DateOnly(2026, 11, 2), events);
    }

    private static IntercompanyAccrualScheduleRunDto Run(Guid schedule, DateOnly asOf, bool executed)
        => new(schedule, asOf, executed,
        [
            new(new(2026, 10, 1), new(2026, 11, 1), executed ? IntercompanyAccrualScheduleOutcomeDto.Drafted : IntercompanyAccrualScheduleOutcomeDto.Due,
                Guid.NewGuid(), executed ? "Draft" : null, null),
            new(new(2026, 9, 1), new(2026, 10, 1), IntercompanyAccrualScheduleOutcomeDto.AlreadyDrafted, DraftId, "Draft", null),
            new(new(2026, 8, 1), new(2026, 9, 1), IntercompanyAccrualScheduleOutcomeDto.Refused, null, null, "No open period.")
        ]);

    private static ResolvedIntercompanyAccountingArrangementDto Profile(Guid id, IntercompanyAccountingTemplateKindDto kind, Guid agreement, string reference)
    {
        var start = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var terms = new IntercompanyAgreementDraftDto("IC-" + reference, reference, LenderEntity, "Lender", BorrowerEntity, "Borrower",
            IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 5, 1));
        var legal = new IntercompanyAgreementRevisionDto(agreement, 1, null, null, IntercompanyAgreementActionDto.Activate, terms, Guid.NewGuid(),
            new string('a', 64), ["legal-author"], "legal-author", "legal-reviewer", start, "Approved synthetic terms");
        var arrangement = new IntercompanyAccountingArrangementDto(id, Guid.NewGuid(), reference, kind, agreement, 1, legal.PayloadHash, start, null,
            Guid.NewGuid(), 1, 3, IntercompanyAccountingArrangementStatusDto.Approved,
            [new(LenderEntity, Book, "a-recv", "a-pay")],
            "mapping-preparer", start, "mapping-reviewer", start, "mapping-reviewer", start);
        return new(arrangement, legal);
    }

    private static LedgerBookDto LedgerBook(Guid id, Guid entity, string name)
        => new(id, name.ToLowerInvariant().Replace(' ', '-'), entity, FundStructureNodeKindDto.Entity, name, "USD", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private sealed class Client : IWorkstationIntercompanyAgreementApiClient
    {
        private readonly Dictionary<Guid, IntercompanyAccrualScheduleDto> _schedules = new()
        {
            [ProposedId] = Schedule(ProposedId, Book, IntercompanyAccrualScheduleStatusDto.Proposed, 1),
            [ActiveId] = Schedule(ActiveId, Book, IntercompanyAccrualScheduleStatusDto.Active, 3)
        };
        public bool CanManageLedger { get; init; } = true;
        public List<Guid> ScheduleReads { get; } = [];
        public List<Guid> ProfileReads { get; } = [];
        public List<ProposeIntercompanyAccrualScheduleRequest> Proposals { get; } = [];
        public List<(Guid Schedule, Guid Book, DecideIntercompanyAccrualScheduleRequest Request)> Decisions { get; } = [];
        public List<(Guid Schedule, Guid Book, DateOnly AsOf)> DueReads { get; } = [];
        public List<(Guid Schedule, Guid Book)> Runs { get; } = [];
        public Func<Guid, Task<ApiResponse<IntercompanyAccrualScheduleDto[]>>>? ListHandler { get; set; }
        public Func<Guid, DecideIntercompanyAccrualScheduleRequest, ApiResponse<IntercompanyAccrualScheduleDto>>? Decide { get; init; }

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new(CanManageLedger: CanManageLedger, CanViewLedger: true)));
        public Task<ApiResponse<LedgerBookDto[]>> GetLedgerBooksAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<LedgerBookDto[]>.Ok([LedgerBook(Book, LenderEntity, "Lender book"), LedgerBook(OtherBook, BorrowerEntity, "Borrower book")]));
        public Task<ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>> GetCoverageProfilesAsync(Guid bookId, DateOnly asOf, CancellationToken ct = default)
        {
            ProfileReads.Add(bookId);
            return Task.FromResult(ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Ok(
            [
                Profile(Interest, IntercompanyAccountingTemplateKindDto.LoanInterest, LoanAgreement, "Loan interest"),
                Profile(Principal, IntercompanyAccountingTemplateKindDto.Loan, LoanAgreement, "Loan principal"),
                Profile(OtherLoan, IntercompanyAccountingTemplateKindDto.Loan, OtherAgreement, "Unrelated loan")
            ]));
        }

        public Task<ApiResponse<IntercompanyAccrualScheduleDto[]>> GetAccrualSchedulesAsync(Guid bookId, CancellationToken ct = default)
        {
            ScheduleReads.Add(bookId);
            return ListHandler?.Invoke(bookId) ?? Task.FromResult(ApiResponse<IntercompanyAccrualScheduleDto[]>.Ok([.. _schedules.Values.Where(item => item.LedgerBookId == bookId)]));
        }

        public Task<ApiResponse<IntercompanyAccrualScheduleDto>> ProposeAccrualScheduleAsync(ProposeIntercompanyAccrualScheduleRequest request, CancellationToken ct = default)
        {
            Proposals.Add(request);
            var created = new IntercompanyAccrualScheduleDto(Guid.NewGuid(), "tenant", "company", request.LedgerBookId, request.Kind, request.ProfileArrangementId,
                request.PrincipalArrangementId, 1, IntercompanyAccrualScheduleStatusDto.Proposed, null,
                [new(Guid.NewGuid(), request.RequestId, IntercompanyAccrualScheduleActionDto.Propose, "maker", At, request.Reason)]);
            _schedules[created.ScheduleId] = created;
            return Task.FromResult(ApiResponse<IntercompanyAccrualScheduleDto>.Ok(created));
        }

        public Task<ApiResponse<IntercompanyAccrualScheduleDto>> DecideAccrualScheduleAsync(Guid scheduleId, Guid bookId, DecideIntercompanyAccrualScheduleRequest request, CancellationToken ct = default)
        {
            Decisions.Add((scheduleId, bookId, request));
            if (Decide is not null)
                return Task.FromResult(Decide(scheduleId, request));
            var current = _schedules[scheduleId];
            var status = request.Action switch
            {
                IntercompanyAccrualScheduleActionDto.Approve => IntercompanyAccrualScheduleStatusDto.Active,
                IntercompanyAccrualScheduleActionDto.Reject => IntercompanyAccrualScheduleStatusDto.Rejected,
                _ => IntercompanyAccrualScheduleStatusDto.Disabled
            };
            var next = current with
            {
                Version = current.Version + 1,
                Status = status,
                ActiveFrom = current.ActiveFrom ?? new DateOnly(2026, 11, 17),
                Events = [.. current.Events, new(Guid.NewGuid(), request.RequestId, request.Action, "checker", At.AddDays(2), request.Reason)]
            };
            _schedules[scheduleId] = next;
            return Task.FromResult(ApiResponse<IntercompanyAccrualScheduleDto>.Ok(next));
        }

        public Task<ApiResponse<IntercompanyAccrualScheduleRunDto>> GetAccrualScheduleDueAsync(Guid scheduleId, Guid bookId, DateOnly asOf, CancellationToken ct = default)
        {
            DueReads.Add((scheduleId, bookId, asOf));
            return Task.FromResult(ApiResponse<IntercompanyAccrualScheduleRunDto>.Ok(Run(scheduleId, asOf, executed: false)));
        }

        public Task<ApiResponse<IntercompanyAccrualScheduleRunDto>> RunAccrualScheduleAsync(Guid scheduleId, Guid bookId, CancellationToken ct = default)
        {
            Runs.Add((scheduleId, bookId));
            return Task.FromResult(ApiResponse<IntercompanyAccrualScheduleRunDto>.Ok(Run(scheduleId, new(2026, 11, 17), executed: true)));
        }

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
