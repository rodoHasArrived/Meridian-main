using FluentAssertions;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    [Fact]
    public async Task ModifyOrderAsync_UnknownProcessLocalOrder_FailsAsNotSentWithoutPlacingAnOrder()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var placed = 0;
        client.OnPlaceOrder = (_, _, _) => placed++;

        await Assert.ThrowsAsync<BrokerageRequestNotSentException>(() => sut.ModifyOrderAsync("unknown-order",
            new OrderModification { NewQuantity = 12m }));

        placed.Should().Be(0);
    }
}
