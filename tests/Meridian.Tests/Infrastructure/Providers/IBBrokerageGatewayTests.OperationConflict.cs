using FluentAssertions;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    [Fact]
    public async Task ModifyOrderAsync_WhileACancelIsPending_FailsAsNotSentWithoutPlacingAnOrder()
    {
        var client = CreateAcknowledgingClient();
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        var placed = 0;
        client.OnPlaceOrder = (_, _, _) => placed++;
        // IB has not answered the cancel, so it still owns the order's operation slot.
        client.OnCancelOrder = (_, _) => { };
        using var stopCancel = new CancellationTokenSource();
        var cancel = sut.CancelOrderAsync("working-order", stopCancel.Token);

        var modify = () => sut.ModifyOrderAsync("working-order", new OrderModification { NewQuantity = 12m });

        await modify.Should().ThrowAsync<BrokerageRequestNotSentException>().WithMessage("*pending operation*");
        placed.Should().Be(0, "the conflict is detected before anything is sent to IB");
        stopCancel.Cancel();
        await FluentActions.Awaiting(() => cancel).Should().ThrowAsync<OperationCanceledException>();
    }
}
