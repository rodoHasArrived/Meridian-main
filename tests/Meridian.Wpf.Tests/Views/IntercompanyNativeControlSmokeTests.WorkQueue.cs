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
    public void WorkQueueControl_TracksAssignsAndPreservesUnknownGuaranteeExposureWithoutBindingErrors()
    {
        WpfTestThread.Run(async () =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            using var errors = new BindingErrorCapture();
            var client = new LifecycleQueueClient();
            using var workspace = new IntercompanyAgreementWorkQueueWorkspace(client);
            var control = new IntercompanyAgreementWorkQueueControl { DataContext = workspace };
            var window = Host(control);
            try
            {
                await ArrangeAsync(window, control);
                Assert.False(Button(control, "Retain case action").IsEnabled);
                await ExecuteAsync(Button(control, "Load books"));
                await ArrangeAsync(window, control);
                Named<ComboBox>(control, "Lifecycle ledger book").SelectedValue = client.BookId.ToString("D");
                SetText(Named<TextBox>(control, "Lifecycle as-of date"), "2026-11-13");
                await ArrangeAsync(window, control);
                await ExecuteAsync(Button(control, "Load lifecycle queue"));
                await ArrangeAsync(window, control);
                var cases = Named<DataGrid>(control, "Agreement lifecycle cases");
                Assert.Single(cases.Items.Cast<object>());
                cases.SelectedIndex = 0;
                SetText(Named<TextBox>(control, "Lifecycle action reason"), "Track the upcoming expiry");
                await ArrangeAsync(window, control);
                await ExecuteAsync(Button(control, "Track this finding"));
                await ArrangeAsync(window, control);
                Assert.Equal(0, client.LastObservation!.ExpectedVersion);
                Assert.Equal("source-current", client.LastObservation.ExpectedSourceFingerprint);
                Assert.Contains("synthetic-operator", Named<TextBox>(control, "Lifecycle case history").Text);

                SetText(Named<TextBox>(control, "Lifecycle owner"), "synthetic-owner");
                SetText(Named<TextBox>(control, "Lifecycle action reason"), "Assign renewal follow-up");
                await ArrangeAsync(window, control);
                await ExecuteAsync(Button(control, "Retain case action"));
                await ArrangeAsync(window, control);
                Assert.Equal(1, client.LastUpdate!.ExpectedVersion);
                Assert.Equal("synthetic-owner", client.LastUpdate.Owner);
                Assert.Equal(IntercompanyAgreementWorkItemActionDto.AssignOwner, client.LastUpdate.Action);
                Assert.Equal("Unavailable", Assert.Single(workspace.Guarantees).Exposure);
                Assert.Equal("12345678901234567890.12345678 USD", workspace.Guarantees[0].Cap);

                Named<ComboBox>(control, "Lifecycle case action").SelectedValue = IntercompanyAgreementWorkItemActionDto.Resolve;
                SetText(Named<TextBox>(control, "Lifecycle action reason"), "Amendment retained");
                await ArrangeAsync(window, control);
                Assert.False(Button(control, "Retain case action").IsEnabled);
                SetText(Named<TextBox>(control, "Lifecycle evidence references"), "retained:amendment");
                await ArrangeAsync(window, control);
                Assert.True(Button(control, "Retain case action").IsEnabled);
                Assert.Empty(errors.Messages);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>Synthetic transport for native-control binding and interaction checks only.</summary>
    private sealed class LifecycleQueueClient : IWorkstationIntercompanyAgreementApiClient
    {
        public Guid BookId { get; } = Guid.NewGuid();
        private readonly Guid _agreement = Guid.NewGuid();
        private IntercompanyAgreementWorkItemDto _item;
        public IntercompanyAgreementWorkQueueObserveRequestDto? LastObservation { get; private set; }
        public IntercompanyAgreementWorkQueueUpdateRequestDto? LastUpdate { get; private set; }
        public LifecycleQueueClient() => _item = new("synthetic-case", BookId, _agreement, 2, "expiring", "Warning", "Loan expires on 2026-12-01.",
            "Review amendment or wind-down.", new(2026, 12, 1), ObservationDate, true, false, IntercompanyAgreementWorkItemStateDto.Open,
            null, null, null, null, 0, "source-current", 0, null, []);

        public Task<ApiResponse<LedgerBookDto[]>> GetLedgerBooksAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<LedgerBookDto[]>.Ok([new(BookId, "synthetic-fund", Guid.NewGuid(), FundStructureNodeKindDto.Entity,
                "Synthetic book", "USD", RecordedAt, RecordedAt)]));
        public Task<ApiResponse<IntercompanyAgreementWorkQueueDto>> GetAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyAgreementWorkQueueDto>.Ok(new(scope.AsOf, scope.ExpiryHorizonDays, RecordedAt, "Current", true,
                "source-current", [_item], [new(Guid.NewGuid(), "G-1", "Synthetic guarantee", 1, SyntheticHash, Guid.NewGuid(), "Guarantor", Guid.NewGuid(), "Obligor",
                    "USD", "synthetic-bank", ExactBalance, new(2026, 1, 1), new(2027, 1, 1), null, IntercompanyAgreementStatusDto.Active,
                    null, "Unavailable", "No reviewed underlying exposure mapping.")], []) { LedgerBookId = BookId, FundProfileId = "synthetic-fund", CanManage = true }));
        public Task<ApiResponse<IntercompanyAgreementWorkItemDto>> ObserveAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope,
            IntercompanyAgreementWorkQueueObserveRequestDto request, CancellationToken ct = default)
        {
            LastObservation = request;
            return Retain(request.CommandId, "Observe", request.Reason, null);
        }
        public Task<ApiResponse<IntercompanyAgreementWorkItemDto>> UpdateAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope,
            IntercompanyAgreementWorkQueueUpdateRequestDto request, CancellationToken ct = default)
        {
            LastUpdate = request;
            return Retain(request.CommandId, request.Action.ToString(), request.Reason, request.Owner);
        }
        private Task<ApiResponse<IntercompanyAgreementWorkItemDto>> Retain(Guid command, string action, string reason, string? owner)
        {
            var version = _item.Version + 1;
            _item = _item with { Version = version, RecordHash = $"case-hash-{version}", IsTracked = true, Owner = owner, FirstSeenAtUtc = RecordedAt,
                LastObservedAtUtc = RecordedAt, AgeDays = 0, History = [.. _item.History, new(command, version, $"case-hash-{version}", action,
                    _item.State, "synthetic-operator", RecordedAt, reason, owner, null, [], ObservationDate, "source-current")] };
            return Task.FromResult(ApiResponse<IntercompanyAgreementWorkItemDto>.Ok(_item));
        }
        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
