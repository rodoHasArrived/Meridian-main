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
    public void AccrualSchedulesControl_ListsApprovesPreviewsRunsAndProposesWithoutBindingErrors()
    {
        WpfTestThread.Run(async () =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            using var errors = new BindingErrorCapture();
            var client = new ScheduleClient();
            using var workspace = new IntercompanyAccrualScheduleWorkspace(client);
            var control = new IntercompanyAccrualSchedulesControl { DataContext = workspace };
            var window = Host(control);
            try
            {
                await ArrangeAsync(window, control);
                Assert.False(Button(control, "Approve schedule").IsEnabled);
                Assert.False(Button(control, "Run now").IsEnabled);

                await ExecuteAsync(Button(control, "Load books"));
                await ArrangeAsync(window, control);
                var book = Named<ComboBox>(control, "Schedule ledger book");
                Assert.Equal(BindingStatus.Active, book.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)!.Status);
                book.SelectedValue = client.BookId.ToString("D");
                await workspace.BookLoad;
                await ArrangeAsync(window, control);

                var schedules = Named<DataGrid>(control, "Accrual schedules");
                Assert.Single(schedules.Items.Cast<object>());
                schedules.SelectedIndex = 0;
                await ArrangeAsync(window, control);
                Assert.Contains("Propose · synthetic-maker", Named<TextBox>(control, "Accrual schedule history").Text);

                await ExecuteAsync(Button(control, "Approve schedule"));
                await ArrangeAsync(window, control);
                Assert.Equal(1, client.LastDecision!.ExpectedVersion);
                Assert.Equal(IntercompanyAccrualScheduleActionDto.Approve, client.LastDecision.Action);
                Assert.Contains("nothing posts", Named<TextBlock>(control, "Accrual schedule status").Text);

                await ExecuteAsync(Button(control, "Preview due periods"));
                await ArrangeAsync(window, control);
                Assert.Single(Named<DataGrid>(control, "Accrual schedule periods").Items.Cast<object>());
                Assert.Contains("Nothing was created", Named<TextBlock>(control, "Accrual schedule period status").Text);

                await ExecuteAsync(Button(control, "Run now"));
                await ArrangeAsync(window, control);
                Assert.Equal("Drafted", Assert.Single(workspace.Windows).Outcome);
                Assert.Equal(1, client.Runs);

                var interest = Named<ComboBox>(control, "Schedule loan-interest profile");
                interest.SelectedValue = client.InterestId.ToString("D");
                await ArrangeAsync(window, control);
                Assert.Equal(client.PrincipalId.ToString("D"), Named<ComboBox>(control, "Schedule loan principal profile").SelectedValue);
                SetText(Named<TextBox>(control, "Schedule proposal reason"), "Synthetic monthly accrual");
                await ArrangeAsync(window, control);
                await ExecuteAsync(Button(control, "Propose schedule for review"));
                await ArrangeAsync(window, control);
                Assert.Equal((client.InterestId, (Guid?)client.PrincipalId, "Synthetic monthly accrual"),
                    (client.LastProposal!.ProfileArrangementId, client.LastProposal.PrincipalArrangementId, client.LastProposal.Reason));
                Assert.Equal(2, schedules.Items.Count);
                Assert.True(control.IsVisible);
                Assert.Empty(errors.Messages);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>Synthetic schedule transport; asserts UI state only, never the schedule rules or accounting correctness.</summary>
    private sealed class ScheduleClient : IWorkstationIntercompanyAgreementApiClient
    {
        private readonly Guid _lender = Guid.NewGuid(), _borrower = Guid.NewGuid(), _loan = Guid.NewGuid();
        private readonly List<IntercompanyAccrualScheduleDto> _schedules = [];
        public Guid BookId { get; } = Guid.NewGuid();
        public Guid InterestId { get; } = Guid.NewGuid();
        public Guid PrincipalId { get; } = Guid.NewGuid();
        public DecideIntercompanyAccrualScheduleRequest? LastDecision { get; private set; }
        public ProposeIntercompanyAccrualScheduleRequest? LastProposal { get; private set; }
        public int Runs { get; private set; }

        public ScheduleClient()
            => _schedules.Add(new(Guid.NewGuid(), "synthetic-tenant", null, BookId, IntercompanyAccrualScheduleKindDto.LoanInterest, Guid.NewGuid(), Guid.NewGuid(),
                1, IntercompanyAccrualScheduleStatusDto.Proposed, null,
                [new(Guid.NewGuid(), Guid.NewGuid(), IntercompanyAccrualScheduleActionDto.Propose, "synthetic-maker", RecordedAt, "Synthetic schedule")]));

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new IntercompanyOperationCapabilities(CanManageLedger: true, CanViewLedger: true)));

        public Task<ApiResponse<LedgerBookDto[]>> GetLedgerBooksAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<LedgerBookDto[]>.Ok([new(BookId, "synthetic-lender-fund", _lender, FundStructureNodeKindDto.Entity, "Synthetic lender book", "USD", RecordedAt, RecordedAt)]));

        public Task<ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>> GetCoverageProfilesAsync(Guid bookId, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Ok(
                [Profile(InterestId, IntercompanyAccountingTemplateKindDto.LoanInterest, _loan), Profile(PrincipalId, IntercompanyAccountingTemplateKindDto.Loan, _loan),
                 Profile(Guid.NewGuid(), IntercompanyAccountingTemplateKindDto.Loan, Guid.NewGuid())]));

        private ResolvedIntercompanyAccountingArrangementDto Profile(Guid id, IntercompanyAccountingTemplateKindDto kind, Guid agreement)
        {
            var terms = new IntercompanyAgreementDraftDto("IC-SYN", "Synthetic loan", _lender, "Lender", _borrower, "Borrower", IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 5, 1));
            var legal = new IntercompanyAgreementRevisionDto(agreement, 1, null, null, IntercompanyAgreementActionDto.Activate, terms, Guid.NewGuid(), "synthetic-legal-hash",
                ["synthetic-author"], "synthetic-author", "synthetic-reviewer", RecordedAt, "Approved synthetic terms");
            return new(new(id, Guid.NewGuid(), "Synthetic " + kind, kind, agreement, 1, legal.PayloadHash, RecordedAt, null, Guid.NewGuid(), 1, 3,
                IntercompanyAccountingArrangementStatusDto.Approved, [new(_lender, BookId, "syn-recv", "syn-pay")],
                "synthetic-preparer", RecordedAt, "synthetic-reviewer", RecordedAt, "synthetic-reviewer", RecordedAt), legal);
        }

        public Task<ApiResponse<IntercompanyAccrualScheduleDto[]>> GetAccrualSchedulesAsync(Guid bookId, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyAccrualScheduleDto[]>.Ok([.. _schedules.Where(item => item.LedgerBookId == bookId)]));

        public Task<ApiResponse<IntercompanyAccrualScheduleDto>> ProposeAccrualScheduleAsync(ProposeIntercompanyAccrualScheduleRequest request, CancellationToken ct = default)
        {
            LastProposal = request;
            var created = new IntercompanyAccrualScheduleDto(Guid.NewGuid(), "synthetic-tenant", null, request.LedgerBookId, request.Kind, request.ProfileArrangementId,
                request.PrincipalArrangementId, 1, IntercompanyAccrualScheduleStatusDto.Proposed, null,
                [new(Guid.NewGuid(), request.RequestId, IntercompanyAccrualScheduleActionDto.Propose, "synthetic-maker", RecordedAt, request.Reason)]);
            _schedules.Add(created);
            return Task.FromResult(ApiResponse<IntercompanyAccrualScheduleDto>.Ok(created));
        }

        public Task<ApiResponse<IntercompanyAccrualScheduleDto>> DecideAccrualScheduleAsync(Guid scheduleId, Guid bookId, DecideIntercompanyAccrualScheduleRequest request, CancellationToken ct = default)
        {
            LastDecision = request;
            var index = _schedules.FindIndex(item => item.ScheduleId == scheduleId);
            var current = _schedules[index];
            var next = current with
            {
                Version = current.Version + 1,
                Status = IntercompanyAccrualScheduleStatusDto.Active,
                ActiveFrom = ObservationDate,
                Events = [.. current.Events, new(Guid.NewGuid(), request.RequestId, request.Action, "synthetic-checker", RecordedAt, request.Reason)]
            };
            _schedules[index] = next;
            return Task.FromResult(ApiResponse<IntercompanyAccrualScheduleDto>.Ok(next));
        }

        public Task<ApiResponse<IntercompanyAccrualScheduleRunDto>> GetAccrualScheduleDueAsync(Guid scheduleId, Guid bookId, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyAccrualScheduleRunDto>.Ok(new(scheduleId, asOf, false,
                [new(new(2026, 10, 1), new(2026, 11, 1), IntercompanyAccrualScheduleOutcomeDto.Due, Guid.NewGuid(), null, null)])));

        public Task<ApiResponse<IntercompanyAccrualScheduleRunDto>> RunAccrualScheduleAsync(Guid scheduleId, Guid bookId, CancellationToken ct = default)
        {
            Runs++;
            return Task.FromResult(ApiResponse<IntercompanyAccrualScheduleRunDto>.Ok(new(scheduleId, ObservationDate, true,
                [new(new(2026, 10, 1), new(2026, 11, 1), IntercompanyAccrualScheduleOutcomeDto.Drafted, Guid.NewGuid(), "Draft", null)])));
        }

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
