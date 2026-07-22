using FluentAssertions;
using Meridian.ProviderSdk;
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
        var viewModel = new ProviderDataProjectionViewModel(service);

        viewModel.Refresh();

        viewModel.PnlStreams.Should().ContainSingle();
        viewModel.PnlStreams[0].Provenance.Key.Should().Be("ib/7/pnl/pnl:DU123:");
        viewModel.PnlStreams[0].Availability.Entitlement.Should().Be("real-time P&L");
        viewModel.PnlStreams[0].Availability.ConnectionState.Should().Be("Connected");
        viewModel.Projection!.PnlStreams.Should().BeEquivalentTo(service.GetProjection().PnlStreams);
    }

    private sealed class Requests : IProviderDataReadService
    {
        public IReadOnlyList<ProviderDataRequestReadModel> GetRequests() => [new(7, "ib", "pnl", ProviderDataRequestStatus.Streaming, DateTimeOffset.UtcNow, Pnl: new("DU123", null, 11m, 12m, 13m))];
        public async IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; }
    }
    private sealed class Availability : IProviderDataAvailabilityReadService { public IReadOnlyList<ProviderDataAvailability> GetAvailability() => [new("ib", true, "Connected", DateTimeOffset.UtcNow, "real-time P&L")]; }
}
