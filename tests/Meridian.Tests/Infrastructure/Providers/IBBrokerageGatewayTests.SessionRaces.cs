using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    [Theory]
    [InlineData(true)]   // managedAccounts arrives during the reconnect handshake, before ConnectionRestored
    [InlineData(false)]  // managedAccounts arrives after ConnectionRestored
    public async Task ScopeIdentity_AutomaticReconnect_BindsTheRestoredSessionsAccounts(bool accountsBeforeRestore)
    {
        var client = new FakeIbBrokerageClient { OnRequestNextValidId = c => c.RaiseNextValidId(9001) };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var generation = Connection(sut).ConnectionGeneration;

        client.RaiseConnectionLost();
        Connection(sut).ScopeIdentity.Should().BeNull("the lost session's accounts are no longer verified");
        if (accountsBeforeRestore)
            client.RaiseManagedAccounts("DU123456");
        client.RaiseConnectionRestored();
        if (!accountsBeforeRestore)
            client.RaiseManagedAccounts("DU123456");

        Connection(sut).ScopeIdentity.Should().Be("paper:DU123456", "the restored session reported its accounts");
        Connection(sut).ConnectionGeneration.Should().Be(generation + 2, "losing and restoring each end a session");
    }

    [Fact]
    public async Task ScopeIdentity_AutomaticReconnectWithoutNewAccounts_DoesNotReuseTheOldSessions()
    {
        var client = new FakeIbBrokerageClient { OnRequestNextValidId = c => c.RaiseNextValidId(9001) };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        Connection(sut).ScopeIdentity.Should().Be("paper:DU123456");

        client.RaiseConnectionLost();
        client.RaiseConnectionRestored();

        Connection(sut).ScopeIdentity.Should().BeNull("accounts from the previous session never bind to the new one");
    }

    [Fact]
    public async Task OrderRecovery_StaleRecoveryRacingACurrentOne_NeverRemovesTheCurrentCancellationPath()
    {
        var cancelled = new List<int>();
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                c.RaiseOpenOrder(RecoverableOpenOrder());
                c.RaiseOpenOrdersCompleted();
            },
            OnCancelOrder = (c, id) =>
            {
                cancelled.Add(id);
                c.RaiseOrderStatus(new IBOrderStatusUpdate(id, "Cancelled", 0m, 0m, 0d, 0d, 1L, 1, null, null,
                    DateTimeOffset.UtcNow));
            }
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var recovery = (IBrokerageOrderRecoveryGateway)sut;
        ExecutionReport? current = null;
        var raced = false;
        // While the first (soon stale) recovery installs its routes, the transport reconnects and a
        // recovery in the new session runs to completion.
        sut.RecoveryRoutesInstalling = () =>
        {
            if (raced)
                return;
            raced = true;
            client.RaiseConnectionLost();
            client.RaiseConnectionRestored();
            client.RaiseManagedAccounts("DU123456");
            var task = recovery.GetOrderForRecoveryAsync("client-1");
            task.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("the current-session recovery must not deadlock");
            current = task.Result;
        };

        var stale = () => recovery.GetOrderForRecoveryAsync("client-1");

        await stale.Should().ThrowAsync<InvalidOperationException>().WithMessage("*connection changed*");
        current.Should().NotBeNull("the current-session recovery succeeded");
        var cancel = await sut.CancelOrderAsync("client-1");
        cancelled.Should().Equal([7001], "the stale recovery must not remove the current recovery's cancellation path");
        cancel.OrderStatus.Should().Be(OrderStatus.Cancelled);
    }
}
