using FluentAssertions;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Resilience;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Applies the shared <see cref="MarketDataClientContractTests{TClient}"/> suite to
/// <see cref="IBMarketDataClient"/>, the top-level IB streaming facade.
/// <para>
/// In non-IBAPI builds the facade delegates to <see cref="IBSimulationClient"/>.
/// This test validates the <em>outer</em> facade — its metadata, subscription wiring,
/// and disposal contract — regardless of whether the IBAPI package is present.
/// The separate <see cref="IBSimulationClientContractTests"/> covers the inner
/// simulation client in isolation.
/// </para>
/// </summary>
public sealed class IBMarketDataClientContractTests : MarketDataClientContractTests<IBMarketDataClient>
{
    protected override IBMarketDataClient CreateClient()
    {
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher, null);
        var depthCollector = new MarketDepthCollector(publisher);
        return new IBMarketDataClient(publisher, tradeCollector, depthCollector);
    }
}

/// <summary>
/// Verifies the IB facade reports honest connection diagnostics: lifecycle transitions
/// track real connect/disconnect calls, and simulation builds are labelled as such.
/// </summary>
public sealed class IBMarketDataClientDiagnosticsTests
{
    [Fact]
    public async Task Diagnostics_TrackConnectAndDisconnectHonestly()
    {
        var publisher = new TestMarketEventPublisher();
        await using var client = new IBMarketDataClient(
            publisher,
            new TradeDataCollector(publisher, null),
            new MarketDepthCollector(publisher));

        var initial = client.GetConnectionDiagnosticsSnapshot();
        initial.IsConnected.Should().BeFalse();
        initial.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Configured);

        await client.ConnectAsync();
        var connected = client.GetConnectionDiagnosticsSnapshot();
        connected.IsConnected.Should().BeTrue();
        connected.LastConnectedAt.Should().NotBeNull();
        if (client.IsSimulation)
        {
            connected.ProviderName.Should().Contain("simulation",
                "operators must be able to tell synthetic data from a live TWS connection");
        }

        await client.DisconnectAsync();
        var disconnected = client.GetConnectionDiagnosticsSnapshot();
        disconnected.IsConnected.Should().BeFalse();
        disconnected.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Disconnected);
        disconnected.LastDisconnectedAt.Should().NotBeNull();
    }
}
