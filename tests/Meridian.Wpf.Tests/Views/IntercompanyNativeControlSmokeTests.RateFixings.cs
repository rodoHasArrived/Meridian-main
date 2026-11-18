using System.Windows.Controls;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed partial class IntercompanyNativeControlSmokeTests
{
    [Fact]
    public void RateFixingsControl_LoadsExactRatesAndApprovesThePendingDigestWithoutBindingErrors()
    {
        WpfTestThread.Run(async () =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            using var errors = new BindingErrorCapture();
            var client = new FixingClient();
            using var workspace = new IntercompanyRateFixingWorkspace(client);
            var control = new IntercompanyRateFixingsControl { DataContext = workspace };
            var window = Host(control);
            try
            {
                await ArrangeAsync(window, control);
                var load = Button(control, "Load fixings");
                var approve = Button(control, "Approve pending value");
                Assert.False(load.IsEnabled);

                SetText(Named<TextBox>(control, "Fixing index reference"), "SOFR");
                SetText(Named<TextBox>(control, "Fixings from date"), "2026-05-01");
                SetText(Named<TextBox>(control, "Fixings to date"), "2026-05-31");
                await ArrangeAsync(window, control);
                await ExecuteAsync(load);
                await ArrangeAsync(window, control);

                var grid = Named<DataGrid>(control, "Retained rate fixings");
                Assert.Single(grid.Items.Cast<object>());
                Assert.Equal("0.0425000000000000000000000001 proposed by rates-maker", Assert.Single(workspace.Fixings).Pending);
                Assert.False(approve.IsEnabled);
                grid.SelectedIndex = 0;
                await ArrangeAsync(window, control);
                Assert.True(approve.IsEnabled);

                await ExecuteAsync(approve);
                await ArrangeAsync(window, control);
                Assert.Equal(FixingClient.Digest, client.LastDecision!.ExpectedValueDigest);
                Assert.Equal(IntercompanyRateFixingActionDto.Approve, client.LastDecision.Action);
                Assert.True(control.IsVisible);
                Assert.Empty(errors.Messages);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>Synthetic fixing transport; asserts UI state only.</summary>
    private sealed class FixingClient : IWorkstationIntercompanyAgreementApiClient
    {
        public static readonly string Digest = new('f', 64);
        private static readonly IntercompanyRateFixingDto Fixing = new(Guid.NewGuid(), "tenant-a", "SOFR", new(2026, 5, 28), 1, null,
            new(0.0425000000000000000000000001m, "Synthetic publisher", "Synthetic fixture", "rates-maker", DateTimeOffset.UnixEpoch, Digest),
            [new(Guid.NewGuid(), Guid.NewGuid(), IntercompanyRateFixingActionDto.Propose, "rates-maker", DateTimeOffset.UnixEpoch, Digest)]);
        public DecideIntercompanyRateFixingRequest? LastDecision { get; private set; }

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new IntercompanyOperationCapabilities(CanManageLedger: true, CanViewLedger: true)));
        public Task<ApiResponse<IntercompanyRateFixingDto[]>> GetRateFixingsAsync(string indexReference, DateOnly from, DateOnly to, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyRateFixingDto[]>.Ok([Fixing]));
        public Task<ApiResponse<IntercompanyRateFixingDto>> DecideRateFixingAsync(Guid fixingId, DecideIntercompanyRateFixingRequest request, CancellationToken ct = default)
        {
            LastDecision = request;
            return Task.FromResult(ApiResponse<IntercompanyRateFixingDto>.Ok(Fixing));
        }

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
