using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// The desktop accrual and counterpart tabs pick books and accounting profiles from shared reads instead of
/// typed identifiers. A lender's book maps a LoanInterest profile and the Loan profile of the same legal
/// agreement, plus an unrelated Loan profile of another agreement; the borrower owns its own book.
/// Synthetic responses only; no WPF runtime is involved.
/// </summary>
public sealed class IntercompanyBookChoiceTests
{
    private static readonly Guid LenderEntity = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid BorrowerEntity = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    private static readonly Guid LenderBook = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid BorrowerBook = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    private static readonly Guid LoanAgreement = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherAgreement = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly Guid InterestProfile = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly Guid PrincipalProfile = Guid.Parse("d2222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherLoanProfile = Guid.Parse("d3333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Accrual_ChoosingABookListsItsLoanInterestProfilesAndThePrincipalOfTheSameAgreement()
    {
        var client = new Client();
        using var view = new IntercompanyInterestAccrualWorkspace(client);

        await view.LoadChoicesCommand.ExecuteAsync(null);
        Assert.Equal([LenderBook.ToString("D"), BorrowerBook.ToString("D")], view.BookChoices.Select(choice => choice.Id).Order());
        Assert.Contains("Lender book · USD", view.BookChoices.Single(choice => choice.Id == LenderBook.ToString("D")).Label);

        view.SourceBook = LenderBook.ToString("D");
        await view.ProfileChoicesLoad;
        Assert.Equal(LenderBook, Assert.Single(client.ProfileReads));
        // Only the LoanInterest profile is an interest choice; the Loan profiles are principal candidates.
        Assert.Equal(InterestProfile.ToString("D"), Assert.Single(view.InterestProfileChoices).Id);
        Assert.Empty(view.PrincipalProfileChoices);

        view.InterestProfile = InterestProfile.ToString("D");
        // The other agreement's Loan profile maps the same book but cannot supply this loan's principal.
        Assert.Equal(PrincipalProfile.ToString("D"), Assert.Single(view.PrincipalProfileChoices).Id);
        Assert.Equal(PrincipalProfile.ToString("D"), view.PrincipalProfile);

        view.AccrualFrom = "2026-06-01";
        view.AccrualTo = "2026-07-01";
        Assert.True(view.PreviewCommand.CanExecute(null));
    }

    [Fact]
    public async Task Accrual_ChoosingAnotherBookClearsTheProfilesAndIgnoresTheEarlierBooksSlowRead()
    {
        var slow = new TaskCompletionSource<ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>>();
        var client = new Client { ProfileHandler = book => book == LenderBook ? slow.Task : Task.FromResult(ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Ok([])) };
        using var view = new IntercompanyInterestAccrualWorkspace(client);
        await view.LoadChoicesCommand.ExecuteAsync(null);

        view.SourceBook = LenderBook.ToString("D");
        var lenderRead = view.ProfileChoicesLoad;
        view.SourceBook = BorrowerBook.ToString("D");
        await view.ProfileChoicesLoad;
        slow.SetResult(ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Ok(Profiles()));
        await lenderRead;

        Assert.Empty(view.InterestProfileChoices);
        Assert.Empty(view.PrincipalProfileChoices);
        Assert.Equal("", view.InterestProfile);
        Assert.Contains("No approved loan-interest profile maps this book", view.ChoicesStatus);
    }

    [Fact]
    public async Task Accrual_UnavailableListsSayWhyAndLeaveNothingSelectable()
    {
        var client = new Client
        {
            Books = ApiResponse<LedgerBookDto[]>.Fail("Ledger books are offline.", 503),
            ProfileHandler = _ => Task.FromResult(ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Fail("Profile authority is offline.", 503))
        };
        using var view = new IntercompanyInterestAccrualWorkspace(client);

        await view.LoadChoicesCommand.ExecuteAsync(null);
        Assert.Empty(view.BookChoices);
        Assert.Contains("Ledger books are offline.", view.ChoicesStatus);

        view.SourceBook = LenderBook.ToString("D");
        await view.ProfileChoicesLoad;
        Assert.Empty(view.InterestProfileChoices);
        Assert.Contains("Profile authority is offline.", view.ChoicesStatus);
        Assert.False(view.PreviewCommand.CanExecute(null));
    }

    [Fact]
    public async Task Accrual_SignOutClearsEveryList()
    {
        var client = new Client();
        using var view = new IntercompanyInterestAccrualWorkspace(client);
        await view.LoadChoicesCommand.ExecuteAsync(null);
        view.SourceBook = LenderBook.ToString("D");
        await view.ProfileChoicesLoad;
        view.InterestProfile = InterestProfile.ToString("D");

        view.ResetSession();

        Assert.Empty(view.BookChoices);
        Assert.Empty(view.InterestProfileChoices);
        Assert.Empty(view.PrincipalProfileChoices);
        Assert.Equal(("", "", ""), (view.SourceBook, view.InterestProfile, view.PrincipalProfile));
    }

    [Fact]
    public async Task Counterpart_ExistingCounterpartBookChoicesAreTheCounterpartyEntitysBooksOnly()
    {
        var client = new Client();
        using var view = new IntercompanyCounterpartQueueWorkspace(client);
        await view.LoadChoicesCommand.ExecuteAsync(null);
        Assert.Equal(2, view.BookChoices.Count);
        Assert.Empty(view.TargetBookChoices);

        view.SelectedProposal = new(Proposal(counterparty: BorrowerEntity));
        Assert.Equal(BorrowerBook.ToString("D"), Assert.Single(view.TargetBookChoices).Id);

        view.SelectedProposal = new(Proposal(counterparty: Guid.NewGuid()));
        Assert.Empty(view.TargetBookChoices);
        Assert.Equal("", view.TargetBook);
    }

    private static IntercompanyProposalDto Proposal(Guid counterparty)
        => new(new(Guid.NewGuid(), "tenant", "company", Guid.NewGuid(), LenderBook, LenderEntity, counterparty, InterestProfile, Guid.NewGuid(),
            "loan-interest:20260601-20260701", new(2026, 6, 30), "USD", [new(Guid.NewGuid(), "Interest receivable", "Asset", "a-int-recv", 10m, 0m, "USD", 10m, 0m)]),
            "source-digest", IntercompanyProposalStatusDto.Pending, 0, DateTimeOffset.UnixEpoch);

    private static ResolvedIntercompanyAccountingArrangementDto[] Profiles() =>
    [
        Profile(InterestProfile, IntercompanyAccountingTemplateKindDto.LoanInterest, LoanAgreement, "Loan interest"),
        Profile(PrincipalProfile, IntercompanyAccountingTemplateKindDto.Loan, LoanAgreement, "Loan principal"),
        Profile(OtherLoanProfile, IntercompanyAccountingTemplateKindDto.Loan, OtherAgreement, "Unrelated loan")
    ];

    private static ResolvedIntercompanyAccountingArrangementDto Profile(Guid id, IntercompanyAccountingTemplateKindDto kind, Guid agreement, string reference)
    {
        var start = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var terms = new IntercompanyAgreementDraftDto("IC-" + reference, reference, LenderEntity, "Lender", BorrowerEntity, "Borrower",
            IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 5, 1));
        var legal = new IntercompanyAgreementRevisionDto(agreement, 1, null, null, IntercompanyAgreementActionDto.Activate, terms, Guid.NewGuid(),
            new string('a', 64), ["legal-author"], "legal-author", "legal-reviewer", start, "Approved synthetic terms");
        var arrangement = new IntercompanyAccountingArrangementDto(id, Guid.NewGuid(), reference, kind, agreement, 1, legal.PayloadHash, start, null,
            Guid.NewGuid(), 1, 3, IntercompanyAccountingArrangementStatusDto.Approved,
            [new(LenderEntity, LenderBook, "a-recv", "a-pay"), new(BorrowerEntity, BorrowerBook, "b-recv", "b-pay")],
            "mapping-preparer", start, "mapping-reviewer", start, "mapping-reviewer", start);
        return new(arrangement, legal);
    }

    private static LedgerBookDto Book(Guid id, Guid entity, string name)
        => new(id, name.ToLowerInvariant().Replace(' ', '-'), entity, FundStructureNodeKindDto.Entity, name, "USD", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private sealed class Client : IWorkstationIntercompanyAgreementApiClient
    {
        public ApiResponse<LedgerBookDto[]> Books { get; init; } = ApiResponse<LedgerBookDto[]>.Ok(
            [Book(LenderBook, LenderEntity, "Lender book"), Book(BorrowerBook, BorrowerEntity, "Borrower book")]);
        public Func<Guid, Task<ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>>> ProfileHandler { get; init; }
            = _ => Task.FromResult(ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Ok(Profiles()));
        public List<Guid> ProfileReads { get; } = [];

        public Task<ApiResponse<LedgerBookDto[]>> GetLedgerBooksAsync(CancellationToken ct = default) => Task.FromResult(Books);
        public Task<ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>> GetCoverageProfilesAsync(Guid bookId, DateOnly asOf, CancellationToken ct = default)
        {
            ProfileReads.Add(bookId);
            return ProfileHandler(bookId);
        }

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
