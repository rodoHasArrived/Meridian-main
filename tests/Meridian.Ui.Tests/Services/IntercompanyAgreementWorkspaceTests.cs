using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed partial class IntercompanyAgreementWorkspaceTests
{
    [Fact]
    public async Task MissingAuthorityIsVisibleAndDoesNotPermitCreate()
    {
        var client = new Client { ListResponse = ApiResponse<IntercompanyAgreementListDto>.Fail("Registry unavailable", 503) };
        var view = new IntercompanyAgreementWorkspace(client);
        await view.LoadCommand.ExecuteAsync(null);
        Assert.Contains("Registry unavailable", view.StatusText);
        Assert.False(view.NewCommand.CanExecute(null));
        Assert.Empty(view.Agreements);
    }

    [Fact]
    public async Task ReadOnlyCapabilityDisablesCreation()
    {
        var view = new IntercompanyAgreementWorkspace(new Client { ListResponse = ApiResponse<IntercompanyAgreementListDto>.Ok(new([], null)) });
        await view.LoadCommand.ExecuteAsync(null);
        Assert.False(view.NewCommand.CanExecute(null));
        Assert.Contains("No agreements", view.StatusText);
    }

    [Fact]
    public async Task UnsavedDraftBlocksNavigationAndDiscardDoesNotWrite()
    {
        var client = new Client();
        var view = new IntercompanyAgreementWorkspace(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.NewCommand.Execute(null);
        view.Name = "Treasury loan";
        Assert.True(view.IsDirty);
        Assert.False(view.LoadCommand.CanExecute(null));
        Assert.False(view.OpenCommand.CanExecute(null));
        view.DiscardCommand.Execute(null);
        Assert.False(view.IsDirty);
        Assert.False(view.IsEditing);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task LostResponseRetainsInputAndReusesExactCommandIdentity()
    {
        var client = new Client();
        var view = await DraftAsync(client);
        await view.SaveCommand.ExecuteAsync(null);
        Assert.True(view.IsDirty);
        Assert.Equal("Treasury loan", view.Name);
        Assert.Contains("local input has been retained", view.StatusText);
        await view.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, client.Commands.Count);
        Assert.Equal(client.Commands[0].CommandId, client.Commands[1].CommandId);
        Assert.Equal(client.Commands[0].ProposalId, client.Commands[1].ProposalId);
        view.Notes = "Changed terms";
        await view.SaveCommand.ExecuteAsync(null);
        Assert.NotEqual(client.Commands[0].CommandId, client.Commands[2].CommandId);
    }

    [Fact]
    public async Task ConflictRetainsDraftAndRequiresExplicitReload()
    {
        var client = new Client { CommandResponse = ApiResponse<IntercompanyAgreementCommandResultDto>.Fail("conflict", 409) };
        var view = await DraftAsync(client);
        await view.SaveCommand.ExecuteAsync(null);
        Assert.Contains("retained version changed", view.StatusText);
        Assert.Equal("Treasury loan", view.Name);
        Assert.False(view.ApproveCommand.CanExecute(null));
        Assert.True(view.IsDirty);
    }

    [Theory]
    [InlineData("five percent")]
    [InlineData("0,05")]
    [InlineData("1,000")]
    public async Task InvalidRateDoesNotSendCommandOrInventZero(string rate)
    {
        var client = new Client();
        var view = await DraftAsync(client);
        view.FixedAnnualRate = rate;
        await view.SaveCommand.ExecuteAsync(null);
        Assert.Empty(client.Commands);
        Assert.Contains("decimal fraction", view.StatusText);
    }

    [Fact]
    public async Task NegativeFractionIsRetainedExactly()
    {
        var client = new Client();
        var view = await DraftAsync(client);
        view.FixedAnnualRate = "-0.005";
        await view.SaveCommand.ExecuteAsync(null);
        Assert.Equal(-0.005m, Assert.Single(client.Commands).Draft!.Terms!.FixedAnnualRate);
    }

    [Fact]
    public async Task DirtyScopeCannotChangeAndCleanScopeInvalidatesCapabilities()
    {
        var client = new Client { ListResponse = ApiResponse<IntercompanyAgreementListDto>.Ok(new([], "old-cursor") { CanCreate = true }) };
        var view = await DraftAsync(client);
        var originalDate = view.AsOf;
        view.AsOf = "2026-01-01";
        Assert.Equal(originalDate, view.AsOf);
        Assert.Contains("Save or discard", view.StatusText);
        view.DiscardCommand.Execute(null);
        view.EntityFilter = Guid.NewGuid().ToString("D");
        Assert.False(view.NewCommand.CanExecute(null));
        Assert.False(view.MoreCommand.CanExecute(null));
    }

    [Fact]
    public async Task RejectedActivationCreatesReplacementOnSameAgreement()
    {
        var detail = RejectedDetail();
        var client = new Client { DetailResponse = ApiResponse<IntercompanyAgreementDetailDto>.Ok(detail) };
        var view = new IntercompanyAgreementWorkspace(client) { SelectedRow = detail };
        await view.OpenCommand.ExecuteAsync(null);
        Assert.True(view.ReplaceProposalCommand.CanExecute(null));
        view.ReplaceProposalCommand.Execute(null);
        Assert.True(view.IsDirty);
        await view.SaveCommand.ExecuteAsync(null);
        Assert.Equal("propose", Assert.Single(client.Actions));
        var command = Assert.Single(client.Commands);
        Assert.Equal(detail.AgreementId, command.AgreementId);
        Assert.NotEqual(detail.Proposals[0].ProposalId, command.ProposalId);
        Assert.Equal(IntercompanyAgreementActionDto.Activate, command.Draft!.Action);
    }

    [Fact]
    public async Task CompareLatestPreservesLocalInputUntilExplicitDiscard()
    {
        var latest = RejectedDetail();
        var client = new Client { DetailResponse = ApiResponse<IntercompanyAgreementDetailDto>.Ok(latest) };
        var view = await DraftAsync(client);
        view.Name = "Unsaved local name";
        await view.SaveCommand.ExecuteAsync(null);
        latest = latest with { AgreementId = client.Commands[0].AgreementId };
        client.DetailResponse = ApiResponse<IntercompanyAgreementDetailDto>.Ok(latest);
        await view.CompareLatestCommand.ExecuteAsync(null);
        Assert.Equal("Unsaved local name", view.Name);
        Assert.True(view.IsDirty);
        Assert.Contains("Latest retained aggregate version", view.ConflictSummary);
        view.DiscardCommand.Execute(null);
        Assert.Equal(latest.AgreementId, view.Detail!.AgreementId);
        Assert.False(view.IsDirty);
    }

    [Fact]
    public async Task JournalReadRefusalReportsUnknownCoverage()
    {
        var detail = RejectedDetail();
        var client = new Client { DetailResponse = ApiResponse<IntercompanyAgreementDetailDto>.Ok(detail) };
        var view = new IntercompanyAgreementWorkspace(client) { SelectedRow = detail, LedgerBook = Guid.NewGuid().ToString("D") };
        await view.OpenCommand.ExecuteAsync(null);
        await view.LoadJournalLinesCommand.ExecuteAsync(null);
        Assert.Contains("Coverage unknown", view.JournalStatus);
        Assert.Empty(view.JournalLines);
    }

    [Fact]
    public async Task AccountChangeClearsLocalEvidenceAndCapabilities()
    {
        var view = await DraftAsync(new Client());
        view.EvidenceReference = "evidence://evidence-vault/previous-account";
        view.Rationale = "Sensitive prior-account rationale";
        view.ResetSession();
        Assert.Empty(view.Name);
        Assert.Empty(view.EvidenceReference);
        Assert.Empty(view.Rationale);
        Assert.False(view.IsDirty);
        Assert.False(view.NewCommand.CanExecute(null));
        Assert.False(view.SaveCommand.CanExecute(null));
        Assert.Null(view.Detail);
    }

    [Fact]
    public async Task LateReadCannotRepopulatePreviousAccountAfterSessionChange()
    {
        var pending = new TaskCompletionSource<ApiResponse<IntercompanyAgreementListDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client { ListHandler = () => pending.Task };
        var view = new IntercompanyAgreementWorkspace(client);
        var load = view.LoadCommand.ExecuteAsync(null);
        Assert.True(view.IsBusy);
        view.ResetSession();
        pending.SetResult(ApiResponse<IntercompanyAgreementListDto>.Ok(new([RejectedDetail()], null) { CanCreate = true }));
        await load;
        Assert.Empty(view.Agreements);
        Assert.False(view.NewCommand.CanExecute(null));
        Assert.Contains("Session changed", view.StatusText);
        Assert.False(view.IsBusy);
    }

    private static IntercompanyAgreementDetailDto RejectedDetail()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var draft = new IntercompanyAgreementDraftDto("IC-1", "Rejected terms", Guid.NewGuid(), "Lender", Guid.NewGuid(), "Borrower",
            IntercompanyAgreementTypeDto.Loan, "USD", new DateOnly(2026, 1, 1), Terms: new(Notes: "Retained source terms"));
        var proposal = new IntercompanyAgreementProposalDto(Guid.NewGuid(), IntercompanyAgreementActionDto.Activate,
            IntercompanyAgreementProposalStateDto.Rejected, draft, null, ["author"], "author", now, "hash", now, now, "Needs repair");
        return new(id, 3, draft.Code, draft.Name, draft.PartyOneId, draft.PartyOneRole, draft.PartyTwoId, draft.PartyTwoRole,
            draft.AgreementType, draft.Currency, new DateOnly(2026, 1, 1), IntercompanyAgreementStatusDto.Unapproved, null,
            null, [proposal], [], [], new(false, false, false, false, false, false, false) { CanCreateProposal = true });
    }

    private static async Task<IntercompanyAgreementWorkspace> DraftAsync(Client client)
    {
        var view = new IntercompanyAgreementWorkspace(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.NewCommand.Execute(null);
        view.Code = "IC-1";
        view.Name = "Treasury loan";
        view.PartyOne = Guid.NewGuid().ToString("D");
        view.PartyTwo = Guid.NewGuid().ToString("D");
        view.Currency = "USD";
        view.Notes = "Loan terms supported by retained evidence";
        return view;
    }

    private sealed class Client : IWorkstationIntercompanyAgreementApiClient
    {
        public ApiResponse<IntercompanyAgreementListDto> ListResponse { get; init; } = ApiResponse<IntercompanyAgreementListDto>.Ok(new([], null) { CanCreate = true });
        public ApiResponse<IntercompanyAgreementCommandResultDto> CommandResponse { get; init; } = ApiResponse<IntercompanyAgreementCommandResultDto>.Fail("Service unreachable", 0, true);
        public ApiResponse<IntercompanyAgreementDetailDto> DetailResponse { get; set; } = ApiResponse<IntercompanyAgreementDetailDto>.Fail("unavailable", 404);
        public List<IntercompanyAgreementCommandDto> Commands { get; } = [];
        public List<string> Actions { get; } = [];
        public List<Meridian.Contracts.Ledger.AppendLegacyIntercompanyAttributionRequest> LegacyCommands { get; } = [];
        public List<(Guid Book, DateOnly Date)> CoverageProfileReads { get; } = [];
        public Task<ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>> GetCoverageProfilesAsync(Guid bookId, DateOnly asOf, CancellationToken ct = default)
        { CoverageProfileReads.Add((bookId, asOf)); return Task.FromResult(ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Ok([])); }
        public Task<ApiResponse<Meridian.Contracts.Ledger.LegacyIntercompanyAttributionDto>> AppendLegacyCoverageAsync(Meridian.Contracts.Ledger.AppendLegacyIntercompanyAttributionRequest request, CancellationToken ct = default)
        { LegacyCommands.Add(request); return Task.FromResult(ApiResponse<Meridian.Contracts.Ledger.LegacyIntercompanyAttributionDto>.Fail("Authority unavailable", 503)); }
        public Func<Task<ApiResponse<IntercompanyAgreementListDto>>>? ListHandler { get; init; }
        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => ListHandler?.Invoke() ?? Task.FromResult(ListResponse);
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => Task.FromResult(DetailResponse);
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyAgreementJournalLinesDto>.Fail("Ledger unavailable", 503));
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default)
        { Commands.Add(command); Actions.Add(action); return Task.FromResult(CommandResponse); }
    }
}
