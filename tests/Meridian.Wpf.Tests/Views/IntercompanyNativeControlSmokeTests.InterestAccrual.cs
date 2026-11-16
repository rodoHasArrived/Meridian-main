using System.Windows.Controls;
using System.Windows.Data;
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
    public void InterestAccrualControl_PreviewsExactServerFiguresAndDraftsTheirDigestWithoutBindingErrors()
    {
        WpfTestThread.Run(async () =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            using var errors = new BindingErrorCapture();
            var client = new AccrualClient();
            using var workspace = new IntercompanyInterestAccrualWorkspace(client);
            var control = new IntercompanyInterestAccrualControl { DataContext = workspace };
            var window = Host(control);
            try
            {
                await ArrangeAsync(window, control);
                var preview = Button(control, "Preview interest accrual");
                var draft = Button(control, "Create accrual draft for review");
                Assert.False(preview.IsEnabled);
                Assert.False(draft.IsEnabled);

                SetText(Named<TextBox>(control, "Accrual source ledger book"), client.BookId.ToString("D"));
                SetText(Named<TextBox>(control, "Loan-interest accounting profile"), Guid.NewGuid().ToString("D"));
                SetText(Named<TextBox>(control, "Loan principal accounting profile"), Guid.NewGuid().ToString("D"));
                SetText(Named<TextBox>(control, "First accrued day"), "2026-06-01");
                SetText(Named<TextBox>(control, "Accrue up to date"), "2026-07-01");
                await ArrangeAsync(window, control);
                await ExecuteAsync(preview);
                await ArrangeAsync(window, control);

                Assert.Single(Named<DataGrid>(control, "Interest accrual slices").Items.Cast<object>());
                Assert.Equal(2, Named<DataGrid>(control, "Proposed accrual journal lines").Items.Count);
                Assert.Equal("12345678901234567890.12345678", Assert.Single(workspace.Slices).UnroundedAmount);
                Assert.StartsWith("4166.67 USD interest for 2026-06-01 up to 2026-07-01", Named<TextBlock>(control, "Interest accrual summary").Text);
                var evidence = Named<TextBox>(control, "Interest accrual evidence");
                Assert.Equal(BindingStatus.Active, evidence.GetBindingExpression(TextBox.TextProperty)!.Status);
                Assert.Contains("Evidence digest " + AccrualClient.Digest, evidence.Text);
                Assert.True(draft.IsEnabled);

                await ExecuteAsync(draft);
                await ArrangeAsync(window, control);
                Assert.Equal(AccrualClient.Digest, client.LastDraft!.ExpectedEvidenceDigest);
                Assert.Equal(client.LastPreview, client.LastDraft.Accrual);
                Assert.Contains("Draft · version 1", Named<TextBlock>(control, "Interest accrual draft").Text);
                Assert.False(draft.IsEnabled);
                Assert.True(control.IsVisible);
                Assert.True(double.IsFinite(control.ActualWidth) && control.ActualWidth > 0);
                Assert.Empty(errors.Messages);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>Synthetic accrual transport; asserts UI state only, never the calculation or accounting correctness.</summary>
    private sealed class AccrualClient : IWorkstationIntercompanyAgreementApiClient
    {
        public static readonly string Digest = new('d', 64);
        private readonly Guid _draftId = Guid.NewGuid();
        public Guid BookId { get; } = Guid.NewGuid();
        public IntercompanyAccrualRequestDto? LastPreview { get; private set; }
        public CreateIntercompanyAccrualDraftRequest? LastDraft { get; private set; }

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new IntercompanyOperationCapabilities(CanManageLedger: true, CanViewLedger: true)));

        public Task<ApiResponse<IntercompanyAccrualProposalDto>> PreviewInterestAccrualAsync(IntercompanyAccrualRequestDto request, CancellationToken ct = default)
        {
            LastPreview = request;
            var agreement = Guid.NewGuid();
            var revision = Guid.NewGuid();
            var computation = new IntercompanyAccrualPreviewDto(IntercompanyAccrualRules.StatusReady, IntercompanyAccrualRules.RuleVersion, agreement, "USD",
                request.AccrualFrom, request.AccrualTo, 4166.67m, ExactBalance, "synthetic-kernel", [new("SYN-CAL", "synthetic-calendar-hash")],
                [new(1, request.AccrualFrom, request.AccrualTo, revision, 2, "synthetic-legal-hash", 1, 1_000_000m, 0.05m, "Actual360", 0.0833333333m, ExactBalance, "synthetic-opening")], []);
            var evidence = new IntercompanyAccrualEvidenceDto(IntercompanyAccrualRules.RuleVersion, _draftId, "synthetic-tenant", request.SourceBookId, Guid.NewGuid(), Guid.NewGuid(),
                request.InterestArrangementId, revision, agreement, 2, "synthetic-legal-hash", IntercompanyAccrualEvidenceGuard.TransactionId(request.AccrualFrom, request.AccrualTo),
                request.AccrualTo.AddDays(-1), IntercompanyAccountRoleDto.Receivable, "syn-int-recv", "syn-int-income", "USD", 4166.67m, computation,
                new(request.PrincipalArrangementId, IntercompanyAccountRoleDto.Receivable, ["syn-receivable"], ["syn-payable"], 1_000_000m, 1, "synthetic-opening-digest", []));
            return Task.FromResult(ApiResponse<IntercompanyAccrualProposalDto>.Ok(new(computation, [], evidence, Digest, Draft(request.SourceBookId, 0))));
        }

        public Task<ApiResponse<ManualJournalEntryDraftDto>> CreateInterestAccrualDraftAsync(CreateIntercompanyAccrualDraftRequest request, CancellationToken ct = default)
        {
            LastDraft = request;
            return Task.FromResult(ApiResponse<ManualJournalEntryDraftDto>.Ok(Draft(request.Accrual.SourceBookId, 1)));
        }

        private ManualJournalEntryDraftDto Draft(Guid book, int version)
            => new(_draftId, ManualJournalEntryStatusDto.Draft, "synthetic-profile", book, default, new(2026, 6, 30), null, null, null, "USD", "Synthetic loan interest accrual", "system",
                RecordedAt, RecordedAt, version,
                [new("line-1", AccountingTemplateLineSideDto.Debit, 4166.67m, "USD", "Assets:Interest receivable"),
                 new("line-2", AccountingTemplateLineSideDto.Credit, 4166.67m, "USD", "Income:Interest")], [], [])
            { IntercompanyAccrualEvidenceDigest = Digest };

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
