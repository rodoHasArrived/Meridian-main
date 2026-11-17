using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

/// <summary>Desktop reciprocal-confirmation state over synthetic shared-API responses; no WPF runtime is involved.</summary>
public sealed class IntercompanyBalanceConfirmationWorkspaceTests
{
    private static readonly Guid LenderBook = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid BorrowerBook = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    private static readonly Guid Profile = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid ConfirmationId = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly DateOnly MonthEnd = new(2026, 6, 30);

    [Fact]
    public async Task LoadShowsBothSidesExactlyAndStatesThatCloseIsUnchanged()
    {
        var client = new Client();
        using var view = await LoadAsync(client);

        var row = Assert.Single(view.Confirmations);
        Assert.Equal("12345678901234567.89 USD (this book)", row.PartyOne);
        Assert.Equal("-12345678901234567.89 USD", row.PartyTwo);
        Assert.Equal(IntercompanyBalanceConfirmationPartyDto.PartyOne, row.ThisBookParty);
        Assert.Contains("Advisory only; close eligibility is unchanged.", view.SummaryText);
        Assert.Contains("Neither side has confirmed", Assert.Single(view.Findings));
        Assert.Equal(Profile.ToString("D"), view.SelectedProfile);
        Assert.Equal((LenderBook, MonthEnd), Assert.Single(client.SummaryReads));
    }

    [Fact]
    public async Task ConfirmSendsThisBooksSideAgainstTheExactVersionAndDigestShown()
    {
        var client = new Client();
        using var view = await LoadAsync(client);
        view.SelectedConfirmation = view.Confirmations[0];

        await view.ConfirmCommand.ExecuteAsync(null);

        var (id, sent) = Assert.Single(client.Actions);
        Assert.Equal(ConfirmationId, id);
        Assert.Equal((IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyOne, 1L, "digest-1", (string?)null),
            (sent.Action, sent.Party, sent.ExpectedVersion, sent.ExpectedBalanceDigest, sent.Reason));
        Assert.Equal("This book's side confirmed the pinned balances.", view.StatusText);
    }

    [Fact]
    public async Task ARefusedActionKeepsItsIdentityForAnIdenticalRetryOnly()
    {
        var client = new Client { ActionResult = _ => ApiResponse<IntercompanyBalanceConfirmationDto>.Fail("Network dropped", 503) };
        using var view = await LoadAsync(client);
        view.SelectedConfirmation = view.Confirmations[0];
        view.Reason = "Borrower is short 1,000.";

        await view.DisputeCommand.ExecuteAsync(null);
        await view.DisputeCommand.ExecuteAsync(null);
        view.Reason = "Borrower is short 1,000 of interest.";
        await view.DisputeCommand.ExecuteAsync(null);

        Assert.Equal(3, client.Actions.Count);
        Assert.Equal(client.Actions[0].Request.RequestId, client.Actions[1].Request.RequestId);
        Assert.NotEqual(client.Actions[1].Request.RequestId, client.Actions[2].Request.RequestId);
        Assert.Contains("Your input has been retained", view.StatusText);
    }

    [Fact]
    public async Task ProposeSendsTheChosenProfileAndDateOnly()
    {
        var client = new Client();
        using var view = await LoadAsync(client);

        await view.ProposeCommand.ExecuteAsync(null);

        Assert.Equal((Profile, MonthEnd), (Assert.Single(client.Proposals).AccountingArrangementId, client.Proposals[0].AsOf));
    }

    [Fact]
    public async Task DisputeAndResolveNeedAReason_AndADisputedRecordCannotBeConfirmed()
    {
        var client = new Client { Status = IntercompanyBalanceConfirmationStatusDto.Disputed };
        using var view = await LoadAsync(client);
        view.SelectedConfirmation = view.Confirmations[0];

        Assert.False(view.ConfirmCommand.CanExecute(null));
        Assert.False(view.DisputeCommand.CanExecute(null));
        Assert.False(view.ResolveCommand.CanExecute(null));
        view.Reason = "Borrower posted the missing accrual.";
        Assert.True(view.ResolveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReadOnlyOperatorsSeeConfirmationsWithoutProfilesOrActions()
    {
        var client = new Client { CanManageLedger = false };
        using var view = await LoadAsync(client);
        view.SelectedConfirmation = view.Confirmations[0];

        Assert.Empty(view.Profiles);
        Assert.Equal(0, client.ProfileReads);
        Assert.False(view.ProposeCommand.CanExecute(null));
        Assert.False(view.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChangingTheDateWithdrawsTheLoadedResult_AndSignOutClearsEverything()
    {
        var client = new Client();
        using var view = await LoadAsync(client);
        view.AsOf = "2026-07-31";
        Assert.Empty(view.Confirmations);
        Assert.False(view.ProposeCommand.CanExecute(null));

        await view.LoadCommand.ExecuteAsync(null);
        view.ResetSession();
        Assert.Equal(("", ""), (view.LedgerBook, view.SummaryText));
        Assert.Empty(view.Confirmations);
        Assert.False(view.CanManageLedger);
    }

    private static async Task<IntercompanyBalanceConfirmationWorkspace> LoadAsync(Client client)
    {
        var view = new IntercompanyBalanceConfirmationWorkspace(client) { LedgerBook = LenderBook.ToString("D"), AsOf = "2026-06-30" };
        await view.LoadCommand.ExecuteAsync(null);
        return view;
    }

    internal static IntercompanyBalanceConfirmationDto Confirmation(IntercompanyBalanceConfirmationStatusDto status = IntercompanyBalanceConfirmationStatusDto.Proposed)
    {
        static IntercompanyBalanceConfirmationSideDto Side(Guid book, decimal amount) => new(Guid.NewGuid(), book, "USD", amount, "USD", amount, "hash", []);
        return new(ConfirmationId, "tenant-a", "company-a", Profile, Guid.NewGuid(), MonthEnd, 1, status,
            Side(LenderBook, 12_345_678_901_234_567.89m), Side(BorrowerBook, -12_345_678_901_234_567.89m), 0m, "digest-1",
            [new(Guid.NewGuid(), Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Propose, null, "accounting-operations", DateTimeOffset.UnixEpoch, "digest-1", null)]);
    }

    private sealed class Client : IWorkstationIntercompanyAgreementApiClient
    {
        public bool CanManageLedger { get; init; } = true;
        public IntercompanyBalanceConfirmationStatusDto Status { get; init; } = IntercompanyBalanceConfirmationStatusDto.Proposed;
        public Func<IntercompanyBalanceConfirmationActionRequest, ApiResponse<IntercompanyBalanceConfirmationDto>>? ActionResult { get; init; }
        public List<(Guid Book, DateOnly AsOf)> SummaryReads { get; } = [];
        public List<(Guid Id, IntercompanyBalanceConfirmationActionRequest Request)> Actions { get; } = [];
        public List<ProposeIntercompanyBalanceConfirmationRequest> Proposals { get; } = [];
        public int ProfileReads { get; private set; }

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new(CanManageLedger: CanManageLedger, CanViewLedger: true)));
        public Task<ApiResponse<IntercompanyBalanceConfirmationSummaryDto>> GetBalanceConfirmationsAsync(Guid ledgerBookId, DateOnly asOf, CancellationToken ct = default)
        {
            SummaryReads.Add((ledgerBookId, asOf));
            return Task.FromResult(ApiResponse<IntercompanyBalanceConfirmationSummaryDto>.Ok(new(ledgerBookId, asOf, [Confirmation(Status)],
                [new("AwaitingConfirmation", "Neither side has confirmed the pinned balances.", Profile, ConfirmationId)])));
        }
        public Task<ApiResponse<IntercompanyBalanceConfirmationDto>> ProposeBalanceConfirmationAsync(ProposeIntercompanyBalanceConfirmationRequest request, CancellationToken ct = default)
        {
            Proposals.Add(request);
            return Task.FromResult(ApiResponse<IntercompanyBalanceConfirmationDto>.Ok(Confirmation()));
        }
        public Task<ApiResponse<IntercompanyBalanceConfirmationDto>> ActOnBalanceConfirmationAsync(Guid confirmationId, IntercompanyBalanceConfirmationActionRequest request, CancellationToken ct = default)
        {
            Actions.Add((confirmationId, request));
            return Task.FromResult(ActionResult?.Invoke(request) ?? ApiResponse<IntercompanyBalanceConfirmationDto>.Ok(Confirmation(IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne)));
        }
        public Task<ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>> GetCoverageProfilesAsync(Guid bookId, DateOnly asOf, CancellationToken ct = default)
        {
            ProfileReads++;
            var start = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
            var terms = new IntercompanyAgreementDraftDto("IC", "Loan", Guid.NewGuid(), "Lender", Guid.NewGuid(), "Borrower", IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 5, 1));
            var legal = new IntercompanyAgreementRevisionDto(Guid.NewGuid(), 1, null, null, IntercompanyAgreementActionDto.Activate, terms, Guid.NewGuid(), "hash",
                ["author"], "author", "reviewer", start, "Approved");
            var arrangement = new IntercompanyAccountingArrangementDto(Profile, Guid.NewGuid(), "Lender to borrower loan", IntercompanyAccountingTemplateKindDto.Loan,
                legal.AgreementId, 1, legal.PayloadHash, start, null, Guid.NewGuid(), 1, 3, IntercompanyAccountingArrangementStatusDto.Approved, [],
                "preparer", start, "reviewer", start, "reviewer", start);
            return Task.FromResult(ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Ok([new(arrangement, legal)]));
        }

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
