using System.Windows.Controls;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed partial class IntercompanyNativeControlSmokeTests
{
    [Fact]
    public void ManagementFeeControl_PreviewsTheServersFeeAndDraftsItsDigestWithoutBindingErrors()
    {
        WpfTestThread.Run(async () =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            using var errors = new BindingErrorCapture();
            var client = new FeeClient();
            using var workspace = new IntercompanyManagementFeeWorkspace(client);
            var control = new IntercompanyManagementFeeControl { DataContext = workspace };
            var window = Host(control);
            try
            {
                await ArrangeAsync(window, control);
                var preview = Button(control, "Preview management fee");
                var draft = Button(control, "Create fee draft for review");
                Assert.False(preview.IsEnabled);

                workspace.SourceBook = FeeClient.Book.ToString("D");
                await workspace.ProfileChoicesLoad;
                SetText(Named<TextBox>(control, "First charged day"), "2026-06-01");
                SetText(Named<TextBox>(control, "Charge up to date"), "2026-07-01");
                await ArrangeAsync(window, control);
                Assert.Equal(FeeClient.Profile.ToString("D"), workspace.FeeProfile);
                await ExecuteAsync(preview);
                await ArrangeAsync(window, control);

                Assert.Single(Named<DataGrid>(control, "Fee periods").Items.Cast<object>());
                Assert.StartsWith("30000.00 USD management fee", Named<TextBlock>(control, "Fee summary").Text);
                Assert.True(draft.IsEnabled);
                await ExecuteAsync(draft);
                await ArrangeAsync(window, control);
                Assert.Equal(FeeClient.Digest, client.LastDraft!.ExpectedEvidenceDigest);
                Assert.True(control.IsVisible);
                Assert.Empty(errors.Messages);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>Synthetic fee transport; asserts UI state only, never the calculation.</summary>
    private sealed class FeeClient : IWorkstationIntercompanyAgreementApiClient
    {
        public static readonly Guid Book = Guid.NewGuid();
        public static readonly Guid Profile = Guid.NewGuid();
        public static readonly string Digest = new('e', 64);
        public CreateIntercompanyFeeAccrualDraftRequest? LastDraft { get; private set; }

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new IntercompanyOperationCapabilities(CanManageLedger: true, CanViewLedger: true)));

        public Task<ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>> GetCoverageProfilesAsync(Guid bookId, DateOnly asOf, CancellationToken ct = default)
        {
            var start = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
            var terms = new IntercompanyAgreementDraftDto("IC", "Fee", Guid.NewGuid(), "Service provider", Guid.NewGuid(), "Recipient",
                IntercompanyAgreementTypeDto.ManagementFee, "USD", new(2026, 5, 1));
            var legal = new IntercompanyAgreementRevisionDto(Guid.NewGuid(), 1, null, null, IntercompanyAgreementActionDto.Activate, terms, Guid.NewGuid(), "hash",
                ["author"], "author", "reviewer", start, "Approved");
            var arrangement = new IntercompanyAccountingArrangementDto(Profile, Guid.NewGuid(), "Management fee profile", IntercompanyAccountingTemplateKindDto.ManagementFee,
                legal.AgreementId, 1, legal.PayloadHash, start, null, Guid.NewGuid(), 1, 3, IntercompanyAccountingArrangementStatusDto.Approved, [],
                "preparer", start, "reviewer", start, "reviewer", start);
            return Task.FromResult(ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Ok([new(arrangement, legal)]));
        }

        public Task<ApiResponse<IntercompanyFeeAccrualProposalDto>> PreviewFeeAccrualAsync(IntercompanyFeeAccrualRequestDto request, CancellationToken ct = default)
        {
            var computation = new IntercompanyFeeAccrualPreviewDto(IntercompanyAccrualRules.StatusReady, IntercompanyFeeAccrualRules.RuleVersion,
                Guid.NewGuid(), "USD", request.AccrualFrom, request.AccrualTo, 30_000.00m, 30_000m, "kernel", [],
                [new(1, request.AccrualFrom, request.AccrualTo, Guid.NewGuid(), 1, "hash", 1, request.AccrualFrom, request.AccrualTo,
                    IntercompanyFixedFeeBasisDto.PerPeriod, 30_000m, null, 1m, 30_000m)], []);
            var draftId = Guid.NewGuid();
            var evidence = new IntercompanyFeeAccrualEvidenceDto(IntercompanyFeeAccrualRules.RuleVersion, draftId, "tenant-a", Book, Guid.NewGuid(),
                Guid.NewGuid(), Profile, Guid.NewGuid(), computation.AgreementId!.Value, 1, "hash", "management-fee:20260601-20260701",
                new(2026, 6, 30), IntercompanyAccountRoleDto.Receivable, "fee-recv", "fee-revenue", "USD", 30_000.00m, computation);
            return Task.FromResult(ApiResponse<IntercompanyFeeAccrualProposalDto>.Ok(new(computation, [], evidence, Digest, Draft(draftId))));
        }

        public Task<ApiResponse<ManualJournalEntryDraftDto>> CreateFeeAccrualDraftAsync(CreateIntercompanyFeeAccrualDraftRequest request, CancellationToken ct = default)
        {
            LastDraft = request;
            return Task.FromResult(ApiResponse<ManualJournalEntryDraftDto>.Ok(Draft(Guid.NewGuid())));
        }

        private static ManualJournalEntryDraftDto Draft(Guid id) => new(id, ManualJournalEntryStatusDto.Draft, "fund", Book,
            AccountingBasisKindDto.Primary, new(2026, 6, 30), null, null, null, "USD", "Management fee", "preparer",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, [], [], []);

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
