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
    public void CounterpartProposalsControl_RendersExactSourceAmountsAndRetriesWithoutBindingErrors()
    {
        WpfTestThread.Run(async () =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            using var errors = new BindingErrorCapture();
            var client = new CounterpartClient();
            using var workspace = new IntercompanyCounterpartQueueWorkspace(client) { SourceBook = client.BookId.ToString("D"), AsOf = "2026-11-13" };
            var control = new IntercompanyCounterpartProposalsControl { DataContext = workspace };
            var window = Host(control);
            try
            {
                await ArrangeAsync(window, control);
                await ExecuteAsync(Button(control, "Load counterpart evidence"));
                await ArrangeAsync(window, control);
                var records = Named<DataGrid>(control, "Counterpart delivery records");
                Assert.Single(records.Items.Cast<object>());
                Assert.Single(Named<DataGrid>(control, "Counterpart booking breaks").Items.Cast<object>());
                var retry = Button(control, "Retry counterpart delivery");
                Assert.False(retry.IsEnabled);

                workspace.SelectedProposal = Assert.Single(workspace.Proposals);
                await ArrangeAsync(window, control);
                Assert.Same(workspace.SelectedProposal, records.SelectedItem);
                Assert.Single(Named<DataGrid>(control, "Counterpart source lines").Items.Cast<object>());
                Assert.Equal("12345678901234567890.12345678", workspace.SelectedProposal!.Lines.Single().TransactionDebit);
                var provenance = Named<TextBox>(control, "Counterpart proposal provenance");
                Assert.Equal(BindingStatus.Active, provenance.GetBindingExpression(TextBox.TextProperty)!.Status);
                Assert.Contains("Source digest synthetic-source-digest", provenance.Text);
                Assert.False(Button(control, "Preview counterpart FX repricing").IsEnabled);

                SetText(Named<TextBox>(control, "Delivery retry reason"), "Synthetic destination period reopened");
                await ArrangeAsync(window, control);
                await ExecuteAsync(retry);
                await ArrangeAsync(window, control);
                Assert.Equal("Synthetic destination period reopened", client.LastRetry!.Reason);
                Assert.Equal(IntercompanyProposalStatusDto.Pending, workspace.SelectedProposal!.Status);
                Assert.Contains("retry was scheduled", workspace.StatusText);
                Assert.True(control.IsVisible);
                Assert.True(double.IsFinite(control.ActualWidth) && control.ActualWidth > 0);
                Assert.Empty(errors.Messages);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>Synthetic proposal transport; asserts UI state only, never delivery or accounting correctness.</summary>
    private sealed class CounterpartClient : IWorkstationIntercompanyAgreementApiClient
    {
        private IntercompanyProposalDto _proposal;
        public Guid BookId { get; } = Guid.NewGuid();
        public RetryIntercompanyProposalRequest? LastRetry { get; private set; }

        public CounterpartClient()
        {
            var source = new IntercompanyProposalSourceDto(Guid.NewGuid(), "synthetic-tenant", null, Guid.NewGuid(), BookId, Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), "SYN-TX-1", new DateOnly(2026, 9, 30), "USD",
                [new IntercompanyProposalSourceLineDto(Guid.NewGuid(), "Due from affiliate", "Asset", "synthetic-node", ExactBalance, 0m, "USD", ExactBalance, 0m)]);
            _proposal = new IntercompanyProposalDto(source, "synthetic-source-digest", IntercompanyProposalStatusDto.NeedsFix, 2, RecordedAt, LastError: "Synthetic destination period is closed");
        }

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new IntercompanyOperationCapabilities(CanManageLedger: true, CanViewLedger: true)));
        public Task<ApiResponse<IntercompanyProposalDto[]>> GetCounterpartProposalsAsync(Guid sourceBookId, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyProposalDto[]>.Ok([_proposal]));
        public Task<ApiResponse<IntercompanyBreakDto[]>> GetCounterpartBreaksAsync(Guid sourceBookId, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyBreakDto[]>.Ok([new IntercompanyBreakDto(_proposal.Source.ProposalId, _proposal.Source.AccountingArrangementId,
                "SYN-TX-1", "DeliveryBlocked", new DateOnly(2026, 9, 30), 44, "Source effective date", "Synthetic destination period is closed", _proposal.Source.SourceJournalId, null)]));
        public Task<ApiResponse<IntercompanyProposalDto>> RetryCounterpartProposalAsync(Guid proposalId, RetryIntercompanyProposalRequest request, CancellationToken ct = default)
        {
            LastRetry = request;
            _proposal = _proposal with { Status = IntercompanyProposalStatusDto.Pending, LastError = null };
            return Task.FromResult(ApiResponse<IntercompanyProposalDto>.Ok(_proposal));
        }

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
