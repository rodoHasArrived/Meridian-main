using FluentAssertions;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Resilience;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Applies the shared <see cref="MarketDataClientContractTests{TClient}"/> suite to
/// <see cref="IBSimulationClient"/>, which runs fully in-process without a live IB connection.
/// </summary>
public sealed class IBSimulationClientContractTests : MarketDataClientContractTests<IBSimulationClient>
{
    protected override IBSimulationClient CreateClient()
        => new(new TestMarketEventPublisher(), enableAutoTicks: false);
}

public sealed class IBSimulationClientDiagnosticsTests
{
    [Fact]
    public async Task Diagnostics_TrackDirectConnectAndDisconnectHonestly()
    {
        await using var client = new IBSimulationClient(
            new TestMarketEventPublisher(),
            enableAutoTicks: false);

        await client.ConnectAsync();
        client.SubscribeTrades(new Meridian.Contracts.Configuration.SymbolConfig("AAPL"));

        var connected = client.GetConnectionDiagnosticsSnapshot();
        connected.ProviderName.Should().Be("Interactive Brokers (Simulation)");
        connected.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Connected);
        connected.IsConnected.Should().BeTrue();
        connected.LastConnectedAt.Should().NotBeNull();
        connected.ActiveSubscriptions.Should().Be(1);

        await client.DisconnectAsync();

        var disconnected = client.GetConnectionDiagnosticsSnapshot();
        disconnected.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Disconnected);
        disconnected.IsConnected.Should().BeFalse();
        disconnected.LastDisconnectedAt.Should().NotBeNull();
    }
}
