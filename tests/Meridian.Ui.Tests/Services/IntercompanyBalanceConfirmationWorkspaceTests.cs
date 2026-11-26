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
    public async Task ClosePolicyIsProposedWithTheChosenFindings_ApprovedAtTheShownVersion_AndDisabledWithAReason()
    {
        var client = new Client { ServesClosePolicies = true };
        using var view = await LoadAsync(client);
        Assert.StartsWith("No active close policy", view.ClosePolicyText);
        Assert.False(view.ProposeClosePolicyCommand.CanExecute(null));

        view.BlockingCodeChoices.Single(item => item.Code == "balances-changed").IsSelected = true;
        view.ClosePolicyReason = "Disputes block month-end close.";
        Assert.True(view.ProposeClosePolicyCommand.CanExecute(null));
        await view.ProposeClosePolicyCommand.ExecuteAsync(null);
        var proposal = Assert.IsType<ProposeIntercompanyConfirmationClosePolicyRequest>(Assert.Single(client.ClosePolicyCommands));
        Assert.Equal(LenderBook, proposal.LedgerBookId);
        Assert.Equal(["disputed", "balances-changed"], proposal.BlockingCodes);
        Assert.StartsWith("Proposed close policy v1", view.ClosePolicyText);

        await view.ApproveClosePolicyCommand.ExecuteAsync(null);
        var approval = Assert.IsType<DecideIntercompanyConfirmationClosePolicyRequest>(client.ClosePolicyCommands[1]);
        Assert.Equal((IntercompanyConfirmationClosePolicyActionDto.Approve, 1L), (approval.Action, approval.ExpectedVersion));
        Assert.StartsWith("Active close policy v2", view.ClosePolicyText);

        Assert.False(view.DisableClosePolicyCommand.CanExecute(null));
        view.ClosePolicyReason = "Pair wound down.";
        await view.DisableClosePolicyCommand.ExecuteAsync(null);
        Assert.Equal("Pair wound down.", Assert.IsType<DecideIntercompanyConfirmationClosePolicyRequest>(client.ClosePolicyCommands[2]).Reason);
        Assert.StartsWith("No active close policy", view.ClosePolicyText);
        Assert.Contains("advisory again", view.StatusText);
    }

    [Fact]
    public async Task AnUnreadableClosePolicyIsReportedRatherThanShownAsNone()
    {
        using var view = await LoadAsync(new Client());
        Assert.StartsWith("The book's confirmation close policy could not be read", view.ClosePolicyText);
        Assert.False(view.ProposeClosePolicyCommand.CanExecute(null));
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

    [Fact]
    public async Task AResolvedDisputeDraftsAnAdjustmentForThisBooksSide_NothingPosted()
    {
        var client = new Client { Status = IntercompanyBalanceConfirmationStatusDto.Resolved };
        using var view = await LoadAsync(client);
        view.SelectedConfirmation = Assert.Single(view.Confirmations);
        Assert.False(view.DraftAdjustmentCommand.CanExecute(null));

        view.AdjustmentAmount = "-250.10";
        view.AdjustmentOffsetAccount = "a-suspense";
        Assert.True(view.DraftAdjustmentCommand.CanExecute(null));
        await view.DraftAdjustmentCommand.ExecuteAsync(null);

        var (id, request) = Assert.Single(client.Adjustments);
        Assert.Equal((ConfirmationId, IntercompanyBalanceConfirmationPartyDto.PartyOne, 1L, "digest-1"), (id, request.Party, request.ExpectedVersion, request.ExpectedBalanceDigest));
        Assert.Equal((IntercompanyAccountRoleDto.Receivable, -250.10m, "a-suspense", MonthEnd), (request.Role, request.AdjustmentAmount, request.OffsetAccountNodeId, request.AccountingDate));
        Assert.Contains("Nothing was submitted or posted", view.StatusText);
        Assert.Equal("", view.AdjustmentAmount);
    }

    [Fact]
    public async Task ACrossCompanyPairShowsEachCompany_AndRecordsOnlyThisBooksResolution()
    {
        var client = new Client
        {
            Status = IntercompanyBalanceConfirmationStatusDto.Disputed,
            CrossCompany = true,
            ActionResult = _ => ApiResponse<IntercompanyBalanceConfirmationDto>.Ok(Confirmation(IntercompanyBalanceConfirmationStatusDto.Disputed, crossCompany: true))
        };
        using var view = await LoadAsync(client);
        var row = Assert.Single(view.Confirmations);
        Assert.EndsWith("(this book) · company-a", row.PartyOne);
        Assert.EndsWith(" · company-b", row.PartyTwo);
        view.SelectedConfirmation = row;
        view.Reason = "Lender will book the accrual.";

        await view.ResolveCommand.ExecuteAsync(null);

        var request = Assert.Single(client.Actions).Request;
        Assert.Equal((IntercompanyBalanceConfirmationActionDto.Resolve, (IntercompanyBalanceConfirmationPartyDto?)IntercompanyBalanceConfirmationPartyDto.PartyOne),
            (request.Action, request.Party));
        Assert.Contains("once the other company records its own", view.StatusText);
    }

    private static async Task<IntercompanyBalanceConfirmationWorkspace> LoadAsync(Client client)
    {
        var view = new IntercompanyBalanceConfirmationWorkspace(client) { LedgerBook = LenderBook.ToString("D"), AsOf = "2026-06-30" };
        await view.LoadCommand.ExecuteAsync(null);
        return view;
    }

    internal static IntercompanyBalanceConfirmationDto Confirmation(IntercompanyBalanceConfirmationStatusDto status = IntercompanyBalanceConfirmationStatusDto.Proposed,
        bool crossCompany = false)
    {
        static IntercompanyBalanceConfirmationSideDto Side(Guid book, decimal amount) => new(Guid.NewGuid(), book, "USD", amount, "USD", amount, "hash", []);
        return new(ConfirmationId, "tenant-a", "company-a", Profile, Guid.NewGuid(), MonthEnd, 1, status,
            Side(LenderBook, 12_345_678_901_234_567.89m), Side(BorrowerBook, -12_345_678_901_234_567.89m), 0m, "digest-1",
            [new(Guid.NewGuid(), Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Propose, null, "accounting-operations", DateTimeOffset.UnixEpoch, "digest-1", null)])
        {
            PartyOneCompanyId = crossCompany ? "company-a" : null,
            PartyTwoCompanyId = crossCompany ? "company-b" : null
        };
    }

    private sealed class Client : IWorkstationIntercompanyAgreementApiClient
    {
        public bool CanManageLedger { get; init; } = true;
        public IntercompanyBalanceConfirmationStatusDto Status { get; init; } = IntercompanyBalanceConfirmationStatusDto.Proposed;
        public Func<IntercompanyBalanceConfirmationActionRequest, ApiResponse<IntercompanyBalanceConfirmationDto>>? ActionResult { get; init; }
        public List<(Guid Book, DateOnly AsOf)> SummaryReads { get; } = [];
        public bool CrossCompany { get; init; }
        public List<(Guid Id, CreateIntercompanyConfirmationAdjustmentDraftRequest Request)> Adjustments { get; } = [];
        public Task<ApiResponse<IntercompanyConfirmationAdjustmentDraftResult>> CreateConfirmationAdjustmentDraftAsync(Guid confirmationId,
            CreateIntercompanyConfirmationAdjustmentDraftRequest request, CancellationToken ct = default)
        {
            Adjustments.Add((confirmationId, request));
            return Task.FromResult(ApiResponse<IntercompanyConfirmationAdjustmentDraftResult>.Ok(new(Guid.NewGuid(), "Draft", LenderBook, request.AccountingDate)));
        }
        public List<(Guid Id, IntercompanyBalanceConfirmationActionRequest Request)> Actions { get; } = [];
        public List<ProposeIntercompanyBalanceConfirmationRequest> Proposals { get; } = [];
        public int ProfileReads { get; private set; }
        public bool ServesClosePolicies { get; init; }
        public IntercompanyConfirmationClosePolicyDto? ClosePolicy { get; set; }
        public List<object> ClosePolicyCommands { get; } = [];

        public Task<ApiResponse<IntercompanyConfirmationClosePolicyDto[]>> GetConfirmationClosePoliciesAsync(Guid ledgerBookId, CancellationToken ct = default)
            => Task.FromResult(ServesClosePolicies
                ? ApiResponse<IntercompanyConfirmationClosePolicyDto[]>.Ok(ClosePolicy is null ? [] : [ClosePolicy])
                : ApiResponse<IntercompanyConfirmationClosePolicyDto[]>.Fail("Close policies unavailable.", 503));
        public Task<ApiResponse<IntercompanyConfirmationClosePolicyDto>> ProposeConfirmationClosePolicyAsync(ProposeIntercompanyConfirmationClosePolicyRequest request, CancellationToken ct = default)
        {
            ClosePolicyCommands.Add(request);
            ClosePolicy = new(Guid.NewGuid(), "tenant", null, request.LedgerBookId, request.BlockingCodes, 1, IntercompanyConfirmationClosePolicyStatusDto.Proposed, null,
                [new(Guid.NewGuid(), request.RequestId, IntercompanyConfirmationClosePolicyActionDto.Propose, "maker", DateTimeOffset.UnixEpoch, request.Reason)]);
            return Task.FromResult(ApiResponse<IntercompanyConfirmationClosePolicyDto>.Ok(ClosePolicy));
        }
        public Task<ApiResponse<IntercompanyConfirmationClosePolicyDto>> DecideConfirmationClosePolicyAsync(Guid policyId, Guid ledgerBookId,
            DecideIntercompanyConfirmationClosePolicyRequest request, CancellationToken ct = default)
        {
            ClosePolicyCommands.Add(request);
            ClosePolicy = ClosePolicy! with
            {
                Version = ClosePolicy.Version + 1,
                Status = request.Action switch
                {
                    IntercompanyConfirmationClosePolicyActionDto.Approve => IntercompanyConfirmationClosePolicyStatusDto.Active,
                    IntercompanyConfirmationClosePolicyActionDto.Reject => IntercompanyConfirmationClosePolicyStatusDto.Rejected,
                    _ => IntercompanyConfirmationClosePolicyStatusDto.Disabled
                },
                ActiveFrom = request.Action == IntercompanyConfirmationClosePolicyActionDto.Approve ? MonthEnd : ClosePolicy.ActiveFrom,
                Events = [.. ClosePolicy.Events, new(Guid.NewGuid(), request.RequestId, request.Action, "checker", DateTimeOffset.UnixEpoch, request.Reason)]
            };
            return Task.FromResult(ApiResponse<IntercompanyConfirmationClosePolicyDto>.Ok(ClosePolicy));
        }

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new(CanManageLedger: CanManageLedger, CanViewLedger: true)));
        public Task<ApiResponse<IntercompanyBalanceConfirmationSummaryDto>> GetBalanceConfirmationsAsync(Guid ledgerBookId, DateOnly asOf, CancellationToken ct = default)
        {
            SummaryReads.Add((ledgerBookId, asOf));
            return Task.FromResult(ApiResponse<IntercompanyBalanceConfirmationSummaryDto>.Ok(new(ledgerBookId, asOf, [Confirmation(Status, CrossCompany)],
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
