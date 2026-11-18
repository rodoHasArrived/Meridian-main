using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

/// <summary>Desktop fixed management-fee state over synthetic shared-API responses; no WPF runtime is involved.</summary>
public sealed class IntercompanyManagementFeeWorkspaceTests
{
    private static readonly Guid Book = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid FeeProfile = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    private static readonly Guid LoanProfile = Guid.Parse("f3333333-3333-3333-3333-333333333333");
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 7, 1);

    [Fact]
    public async Task ChoosingABookOffersOnlyManagementFeeProfiles_AndPreselectsTheFirst()
    {
        var client = new Client();
        using var view = new IntercompanyManagementFeeWorkspace(client) { SourceBook = Book.ToString("D") };
        await view.ProfileChoicesLoad;

        Assert.Equal(FeeProfile.ToString("D"), Assert.Single(view.FeeProfileChoices).Id);
        Assert.Equal(FeeProfile.ToString("D"), view.FeeProfile);
    }

    [Fact]
    public async Task APreviewShowsTheServersExactFeeAndDirection_AndTheDraftCarriesItsDigest()
    {
        var client = new Client();
        using var view = await PreviewAsync(client);

        Assert.StartsWith("30000.0000000000000000000001 USD management fee for 2026-06-01 up to 2026-07-01", view.Summary);
        Assert.Contains("service provider", view.Summary);
        var slice = Assert.Single(view.Slices);
        Assert.Equal(("30000 per period", "1"), (slice.DeclaredFee, slice.Fraction));
        Assert.Contains("intercompany-fee-v1", view.Evidence);
        Assert.Equal((Book, FeeProfile, From, To), (client.Previews[0].SourceBookId, client.Previews[0].FeeArrangementId, client.Previews[0].AccrualFrom, client.Previews[0].AccrualTo));

        await view.CreateDraftCommand.ExecuteAsync(null);

        Assert.Equal("digest-1", Assert.Single(client.Drafts).ExpectedEvidenceDigest);
        Assert.False(view.CreateDraftCommand.CanExecute(null));
        Assert.Contains("Fee draft", view.DraftStatus);
    }

    [Fact]
    public async Task AnUnavailableFeeShowsItsReasonWithoutAnAmount_AndChangingTheWindowWithdrawsAPreview()
    {
        var refused = new Client { Ready = false };
        using (var view = await PreviewAsync(refused))
        {
            Assert.Equal("Fee unavailable. No amount is estimated.", view.Summary);
            Assert.Contains("no fixed-fee basis", Assert.Single(view.Refusals));
            Assert.False(view.CreateDraftCommand.CanExecute(null));
        }

        using var ready = await PreviewAsync(new Client());
        Assert.True(ready.CreateDraftCommand.CanExecute(null));
        ready.AccrualTo = "2026-06-16";
        Assert.Empty(ready.Slices);
        Assert.False(ready.CreateDraftCommand.CanExecute(null));
        ready.ResetSession();
        Assert.Equal(("", ""), (ready.SourceBook, ready.FeeProfile));
    }

    private static async Task<IntercompanyManagementFeeWorkspace> PreviewAsync(Client client)
    {
        var view = new IntercompanyManagementFeeWorkspace(client) { SourceBook = Book.ToString("D") };
        await view.ProfileChoicesLoad;
        view.AccrualFrom = "2026-06-01";
        view.AccrualTo = "2026-07-01";
        await view.PreviewCommand.ExecuteAsync(null);
        return view;
    }

    private sealed class Client : IWorkstationIntercompanyAgreementApiClient
    {
        public bool Ready { get; init; } = true;
        public List<IntercompanyFeeAccrualRequestDto> Previews { get; } = [];
        public List<CreateIntercompanyFeeAccrualDraftRequest> Drafts { get; } = [];

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new(CanManageLedger: true, CanViewLedger: true)));

        public Task<ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>> GetCoverageProfilesAsync(Guid bookId, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Ok(
                [Profile(FeeProfile, IntercompanyAccountingTemplateKindDto.ManagementFee), Profile(LoanProfile, IntercompanyAccountingTemplateKindDto.Loan)]));

        public Task<ApiResponse<IntercompanyFeeAccrualProposalDto>> PreviewFeeAccrualAsync(IntercompanyFeeAccrualRequestDto request, CancellationToken ct = default)
        {
            Previews.Add(request);
            if (!Ready)
            {
                var refused = new IntercompanyFeeAccrualPreviewDto(IntercompanyAccrualRules.StatusUnavailable, IntercompanyFeeAccrualRules.RuleVersion,
                    null, null, From, To, null, null, "kernel", [], [], ["Agreement version 1 declares no fixed-fee basis."]);
                return Task.FromResult(ApiResponse<IntercompanyFeeAccrualProposalDto>.Ok(new(refused, refused.Diagnostics)));
            }
            var amount = 30_000.0000000000000000000001m;
            var computation = new IntercompanyFeeAccrualPreviewDto(IntercompanyAccrualRules.StatusReady, IntercompanyFeeAccrualRules.RuleVersion,
                Guid.NewGuid(), "USD", From, To, amount, amount, "kernel", [new("WEEKENDS", "hash")],
                [new(1, From, To, Guid.NewGuid(), 1, "hash", 1, From, To, IntercompanyFixedFeeBasisDto.PerPeriod, 30_000m, null, 1m, amount)], []);
            var draftId = Guid.NewGuid();
            var evidence = new IntercompanyFeeAccrualEvidenceDto(IntercompanyFeeAccrualRules.RuleVersion, draftId, "tenant-a", Book, Guid.NewGuid(),
                Guid.NewGuid(), FeeProfile, Guid.NewGuid(), computation.AgreementId!.Value, 1, "hash", "management-fee:20260601-20260701",
                new(2026, 6, 30), IntercompanyAccountRoleDto.Receivable, "fee-recv", "fee-revenue", "USD", amount, computation);
            var draft = Draft(draftId);
            return Task.FromResult(ApiResponse<IntercompanyFeeAccrualProposalDto>.Ok(new(computation, [], evidence, "digest-1", draft)));
        }

        public Task<ApiResponse<ManualJournalEntryDraftDto>> CreateFeeAccrualDraftAsync(CreateIntercompanyFeeAccrualDraftRequest request, CancellationToken ct = default)
        {
            Drafts.Add(request);
            return Task.FromResult(ApiResponse<ManualJournalEntryDraftDto>.Ok(Draft(Guid.NewGuid())));
        }

        private static ManualJournalEntryDraftDto Draft(Guid id) => new(id, ManualJournalEntryStatusDto.Draft, "fund", Book,
            AccountingBasisKindDto.Primary, new(2026, 6, 30), null, null, null, "USD", "Management fee", "preparer",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, [], [], []);

        private static ResolvedIntercompanyAccountingArrangementDto Profile(Guid id, IntercompanyAccountingTemplateKindDto kind)
        {
            var start = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
            var terms = new IntercompanyAgreementDraftDto("IC", "Fee", Guid.NewGuid(), "Service provider", Guid.NewGuid(), "Recipient",
                IntercompanyAgreementTypeDto.ManagementFee, "USD", new(2026, 5, 1));
            var legal = new IntercompanyAgreementRevisionDto(Guid.NewGuid(), 1, null, null, IntercompanyAgreementActionDto.Activate, terms, Guid.NewGuid(), "hash",
                ["author"], "author", "reviewer", start, "Approved");
            var arrangement = new IntercompanyAccountingArrangementDto(id, Guid.NewGuid(), $"{kind} profile", kind,
                legal.AgreementId, 1, legal.PayloadHash, start, null, Guid.NewGuid(), 1, 3, IntercompanyAccountingArrangementStatusDto.Approved, [],
                "preparer", start, "reviewer", start, "reviewer", start);
            return new(arrangement, legal);
        }

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
