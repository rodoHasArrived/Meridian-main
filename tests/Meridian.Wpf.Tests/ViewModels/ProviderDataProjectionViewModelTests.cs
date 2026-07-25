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
    public void Refresh_RendersOnlyTheExplicitTenantAndCompanyProjection()
    {
        var requests = new Requests();
        var service = new ProviderDataReadModelService([requests], availabilityProviders: [new Availability()]);
        var browserProjection = ProviderDataProjectionEndpoints.CreateProjection(
            service,
            "tenant-shared",
            "company-alpha");
        var viewModel = new ProviderDataProjectionViewModel(service);

        viewModel.Refresh("tenant-shared", "company-alpha");

        viewModel.PnlStreams.Should().ContainSingle();
        viewModel.PnlStreams[0].Item.AccountId.Should().Be("DU-ALPHA");
        viewModel.PnlStreams[0].Provenance.Key.Should().Be("pnl-du123");
        viewModel.PnlStreams[0].Provenance.Source!.ProviderConnectionId.Should().Be("ib-gateway-1");
        viewModel.PnlStreams[0].Availability.Entitlement.Should().Be("real-time P&L");
        viewModel.PnlStreams[0].Availability.ConnectionState.Should().Be("Connected");
        viewModel.Projection.Should().BeEquivalentTo(browserProjection);
        requests.UnscopedReadCount.Should().Be(0);
    }

    [Fact]
    public void Refresh_WithoutTenantOrCompanyScope_FailsBeforeAnyUnscopedRead()
    {
        var requests = new Requests();
        var viewModel = new ProviderDataProjectionViewModel(new ProviderDataReadModelService([requests]));

        viewModel.Invoking(subject => subject.Refresh("", "company-alpha"))
            .Should().Throw<ArgumentException>();
        viewModel.Invoking(subject => subject.Refresh("tenant-shared", " "))
            .Should().Throw<ArgumentException>();
        requests.UnscopedReadCount.Should().Be(0);
    }

    private sealed class Requests : ITenantScopedProviderDataReadService
    {
        private readonly ProviderDataRequestReadModel _alpha = Request(7, "DU-ALPHA");
        private readonly ProviderDataRequestReadModel _beta = Request(8, "DU-BETA");
        private readonly ProviderDataRequestReadModel _ownerless = Request(9, "DU-OWNERLESS");

        public int UnscopedReadCount { get; private set; }

        public IReadOnlyList<ProviderDataRequestReadModel> GetRequests()
        {
            UnscopedReadCount++;
            return [_alpha, _beta, _ownerless];
        }

        public IReadOnlyList<ProviderDataRequestReadModel> GetRequests(string tenantId, string companyId)
            => (tenantId, companyId) switch
            {
                ("tenant-shared", "company-alpha") => [_alpha],
                ("tenant-shared", "company-beta") => [_beta],
                _ => []
            };

        public async IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }

        public async IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(
            string tenantId,
            string companyId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }

        private static ProviderDataRequestReadModel Request(int requestId, string accountId) =>
            new(
                requestId,
                "ib",
                "pnl",
                ProviderDataRequestStatus.Streaming,
                Timestamp,
                Evidence(),
                Pnl: new(accountId, null, 11m, 12m, 13m, null, null, Evidence()));
    }
    private sealed class Availability : IProviderDataAvailabilityReadService { public IReadOnlyList<ProviderDataAvailability> GetAvailability() => [new("ib", true, "Connected", DateTimeOffset.UtcNow, "real-time P&L")]; }
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);
    private static ProviderDataProvenance Evidence() => new("ib", "ib-gateway-1", Timestamp.AddSeconds(-1), Timestamp, "real-time P&L", "pnl", "real-time", "pnl", "pnl-du123", "correlation-7", "pnl-du123");
}
