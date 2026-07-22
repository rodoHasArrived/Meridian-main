using FluentAssertions;
using Meridian.ProviderSdk;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.ViewModels;
using Xunit;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ProviderDataProjectionViewModelTests
{
    [Fact]
    public void Refresh_RendersTheSharedProjectionWithoutAdapterSpecificState()
    {
        var service = new ProviderDataReadModelService([new Requests()], availabilityProviders: [new Availability()]);
        var browserProjection = ProviderDataProjectionEndpoints.CreateProjection(service);
        var viewModel = new ProviderDataProjectionViewModel(service);

        viewModel.Refresh();

        viewModel.PnlStreams.Should().ContainSingle();
        viewModel.PnlStreams[0].Provenance.Key.Should().Be("pnl-du123");
        viewModel.PnlStreams[0].Provenance.Source!.ProviderConnectionId.Should().Be("ib-gateway-1");
        viewModel.PnlStreams[0].Availability.Entitlement.Should().Be("real-time P&L");
        viewModel.PnlStreams[0].Availability.ConnectionState.Should().Be("Connected");
        viewModel.Projection.Should().BeEquivalentTo(browserProjection);
    }

    private sealed class Requests : IProviderDataReadService
    {
        public IReadOnlyList<ProviderDataRequestReadModel> GetRequests() => [new(7, "ib", "pnl", ProviderDataRequestStatus.Streaming, Timestamp, Evidence(), Pnl: new("DU123", null, 11m, 12m, 13m, null, null, Evidence()))];
        public async IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; }
    }
    private sealed class Availability : IProviderDataAvailabilityReadService { public IReadOnlyList<ProviderDataAvailability> GetAvailability() => [new("ib", true, "Connected", DateTimeOffset.UtcNow, "real-time P&L")]; }
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);
    private static ProviderDataProvenance Evidence() => new("ib", "ib-gateway-1", Timestamp.AddSeconds(-1), Timestamp, "real-time P&L", "pnl", "real-time", "pnl", "pnl-du123", "correlation-7", "pnl-du123");
}
