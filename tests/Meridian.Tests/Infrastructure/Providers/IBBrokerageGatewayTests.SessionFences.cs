using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    private static readonly TimeSpan SafetyNet = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CancelOrderAsync_SessionChangeCannotLandBetweenTheSessionCheckAndTheSend()
    {
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseSend = new ManualResetEventSlim();
        var client = RecoveryClient(c => c.RaiseOpenOrder(RecoverableOpenOrder()));
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        (await ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1")).Should().NotBeNull();
        client.OnCancelOrder = (c, id) =>
        {
            // The recovered id is being written to IB; hold the write until the test releases it.
            sendStarted.TrySetResult();
            releaseSend.Wait(SafetyNet);
            c.RaiseOrderStatus(new IBOrderStatusUpdate(id, "Cancelled", 0m, 0m, 0d, 0d, 1L, 1, null, null,
                DateTimeOffset.UtcNow));
        };
        // Dedicated threads: the blocked send must not starve the reconnect of a pool thread.
        var cancel = Task.Factory.StartNew(() => sut.CancelOrderAsync("client-1"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        await sendStarted.Task.WaitAsync(SafetyNet);

        // An automatic reconnect fires on the transport thread while the send is in progress.
        var reconnect = Task.Factory.StartNew(client.RaiseConnectionLost,
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        // Negative check: a short bounded wait. On a slow runner the reconnect merely has less time to
        // run, so this can only pass vacuously, never fail spuriously.
        var finishedDuringSend = await Task.WhenAny(reconnect, Task.Delay(TimeSpan.FromMilliseconds(200))) == reconnect;
        var completedDuringSend = finishedDuringSend || reconnect.IsCompleted;
        releaseSend.Set();
        await cancel.WaitAsync(SafetyNet);
        await reconnect.WaitAsync(SafetyNet);

        completedDuringSend.Should().BeFalse("the session cannot change while a session-scoped id is being sent");
        reconnect.IsCompletedSuccessfully.Should().BeTrue("the reconnect proceeds once the send completes");
    }

    [Fact]
    public async Task StaleRouteCleanup_RacingACurrentRecovery_KeepsTheReinstalledRoute()
    {
        var cancelled = new List<int>();
        var client = RecoveryClient(c => c.RaiseOpenOrder(RecoverableOpenOrder()), cancelled);
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var recovery = (IBrokerageOrderRecoveryGateway)sut;
        (await recovery.GetOrderForRecoveryAsync("client-1")).Should().NotBeNull();
        client.RaiseConnectionLost();
        client.RaiseConnectionRestored();
        client.RaiseManagedAccounts("DU123456");
        var staleRouteSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.StaleRouteCleanupStarting = () => staleRouteSeen.TrySetResult();
        Task<ExecutionReport>? staleCancel = null;
        // While the new session's recovery holds the gate to install, a cancel resolves the old
        // session's route and parks at the gate; it can only clean up after the reinstall.
        sut.RecoveryRoutesInstalling = () =>
        {
            if (staleCancel is not null)
                return;
            staleCancel = Task.Run(() => sut.CancelOrderAsync("client-1"));
            staleRouteSeen.Task.Wait(SafetyNet).Should().BeTrue("the cancel captured the stale route");
        };

        (await recovery.GetOrderForRecoveryAsync("client-1")).Should().NotBeNull();
        try
        {
            await staleCancel!.WaitAsync(SafetyNet);
        }
        catch (InvalidOperationException)
        {
            // The pre-fix cleanup refused the cancel after removing the reinstalled route.
        }
        if (cancelled.Count == 0)
        {
            var retry = () => sut.CancelOrderAsync("client-1");
            await retry.Should().NotThrowAsync("the current session's recovered route must survive the stale cleanup");
        }

        cancelled.Should().Equal([7001]);
    }

    [Fact]
    public async Task OpenOrdersSnapshot_LateEndOfAnAbandonedRequest_DoesNotCompleteTheNextOne()
    {
        var requests = 0;
        using var abandon = new CancellationTokenSource();
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                if (++requests == 1)
                {
                    c.RaiseOpenOrder(RecoverableOpenOrder(7001));
                    abandon.Cancel(); // the caller gives up; this request's openOrderEnd arrives late
                    return;
                }
                c.RaiseOpenOrder(RecoverableOpenOrder(7001));
                c.RaiseOpenOrdersCompleted(); // the late end owed to the abandoned first request
                c.RaiseOpenOrder(RecoverableOpenOrder(7002)); // a second order claiming the same orderRef
                c.RaiseOpenOrdersCompleted();
            }
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var recovery = (IBrokerageOrderRecoveryGateway)sut;
        var first = () => recovery.GetOrderForRecoveryAsync("client-1", abandon.Token);
        await first.Should().ThrowAsync<OperationCanceledException>();

        var retried = await recovery.GetOrderForRecoveryAsync("client-1");

        retried.Should().BeNull("two orders share the orderRef; a partial snapshot must not make it look unique");
    }
}
