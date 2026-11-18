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
    public void BalanceConfirmationControl_ShowsExactSidesAndConfirmsThisBooksSideWithoutBindingErrors()
    {
        WpfTestThread.Run(async () =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            using var errors = new BindingErrorCapture();
            var client = new ConfirmationClient();
            using var workspace = new IntercompanyBalanceConfirmationWorkspace(client);
            var control = new IntercompanyBalanceConfirmationControl { DataContext = workspace };
            var window = Host(control);
            try
            {
                await ArrangeAsync(window, control);
                SetText(Named<TextBox>(control, "Confirmation ledger book"), client.BookId.ToString("D"));
                SetText(Named<TextBox>(control, "Confirmation as-of date"), "2026-06-30");
                await ArrangeAsync(window, control);
                await ExecuteAsync(Button(control, "Load confirmations"));
                await ArrangeAsync(window, control);

                var grid = Named<DataGrid>(control, "Reciprocal balance confirmations");
                Assert.Single(grid.Items.Cast<object>());
                Assert.Contains("Advisory only; close eligibility is unchanged.", Named<TextBlock>(control, "Balance confirmation summary").Text);
                var confirm = Button(control, "Confirm this book's balance");
                Assert.False(confirm.IsEnabled);

                grid.SelectedItem = workspace.Confirmations[0];
                await ArrangeAsync(window, control);
                var history = Named<TextBox>(control, "Balance confirmation history");
                Assert.Equal(BindingStatus.Active, history.GetBindingExpression(TextBox.TextProperty)!.Status);
                Assert.Equal("12345678901234567.89 USD (this book)", workspace.Confirmations[0].PartyOne);
                Assert.True(confirm.IsEnabled);
                await ExecuteAsync(confirm);
                await ArrangeAsync(window, control);

                Assert.Equal((IntercompanyBalanceConfirmationPartyDto.PartyOne, 1L), (client.LastAction!.Party, client.LastAction.ExpectedVersion));
                Assert.True(control.IsVisible);
                Assert.Empty(errors.Messages);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>Synthetic confirmation transport; asserts UI state only, never the accounting.</summary>
    private sealed class ConfirmationClient : IWorkstationIntercompanyAgreementApiClient
    {
        private readonly Guid _confirmation = Guid.NewGuid();
        public Guid BookId { get; } = Guid.NewGuid();
        public IntercompanyBalanceConfirmationActionRequest? LastAction { get; private set; }

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new IntercompanyOperationCapabilities(CanManageLedger: true, CanViewLedger: true)));
        public Task<ApiResponse<IntercompanyBalanceConfirmationSummaryDto>> GetBalanceConfirmationsAsync(Guid ledgerBookId, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyBalanceConfirmationSummaryDto>.Ok(new(ledgerBookId, asOf, [Confirmation(IntercompanyBalanceConfirmationStatusDto.Proposed)],
                [new("AwaitingConfirmation", "Neither side has confirmed the pinned balances.", Guid.NewGuid(), _confirmation)])));
        public Task<ApiResponse<IntercompanyBalanceConfirmationDto>> ActOnBalanceConfirmationAsync(Guid confirmationId, IntercompanyBalanceConfirmationActionRequest request, CancellationToken ct = default)
        {
            LastAction = request;
            return Task.FromResult(ApiResponse<IntercompanyBalanceConfirmationDto>.Ok(Confirmation(IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne)));
        }

        private IntercompanyBalanceConfirmationDto Confirmation(IntercompanyBalanceConfirmationStatusDto status)
            => new(_confirmation, "synthetic-tenant", null, Guid.NewGuid(), Guid.NewGuid(), new(2026, 6, 30), 1, status,
                new(Guid.NewGuid(), BookId, "USD", 12_345_678_901_234_567.89m, "USD", 12_345_678_901_234_567.89m, "synthetic-hash", []),
                new(Guid.NewGuid(), Guid.NewGuid(), "USD", -12_345_678_901_234_567.89m, "USD", -12_345_678_901_234_567.89m, "synthetic-hash", []),
                0m, "synthetic-digest", [new(Guid.NewGuid(), Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Propose, null, "synthetic-operations", RecordedAt, "synthetic-digest", null)]);

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
