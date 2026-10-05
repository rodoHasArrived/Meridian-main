using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class BrokerageConnectionIsolationTests
{
    [Fact]
    public async Task IndependentConnections_DoNotSharePendingOAuthStateOrRevokeEachOther()
    {
        var firstConfiguration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var secondConfiguration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var options = new BrokerageConnectionOptions("robinhood", "Test", "https://example.test/authorize",
            "https://example.test/token", "", "test-client", "", "https://example.test/callback", "read");
        var first = new BrokerageConnectionService(options, NullLogger<BrokerageConnectionService>.Instance,
            configuration: new CompositionConfiguration(firstConfiguration));
        var second = new BrokerageConnectionService(options, NullLogger<BrokerageConnectionService>.Instance,
            configuration: new CompositionConfiguration(secondConfiguration));

        await first.StartConnectionAsync();
        (await second.GetStatusAsync()).State.Should().Be(BrokerageConnectionStateDto.Disconnected);
        secondConfiguration["ROBINHOOD_BROKERAGE_OAUTH_STATE"].Should().BeNull();

        await second.StartConnectionAsync();
        var firstState = firstConfiguration["ROBINHOOD_BROKERAGE_OAUTH_STATE"];
        firstState.Should().NotBeNullOrEmpty();
        firstState.Should().NotBe(secondConfiguration["ROBINHOOD_BROKERAGE_OAUTH_STATE"]);

        await second.RevokeAsync();
        firstConfiguration["ROBINHOOD_BROKERAGE_OAUTH_STATE"].Should().Be(firstState);
        secondConfiguration["ROBINHOOD_BROKERAGE_OAUTH_STATE"].Should().BeNull();
    }
}
