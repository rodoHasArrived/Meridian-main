using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    private static FakeIbBrokerageClient RecoveryClient(Action<FakeIbBrokerageClient> snapshot, List<int>? cancelled = null)
        => new()
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                snapshot(c);
                c.RaiseOpenOrdersCompleted();
            },
            OnCancelOrder = (c, id) =>
            {
                cancelled?.Add(id);
                c.RaiseOrderStatus(new IBOrderStatusUpdate(id, "Cancelled", 0m, 0m, 0d, 0d, 1L, 1, null, null,
                    DateTimeOffset.UtcNow));
            }
        };

    [Theory]
    [InlineData("")]     // IB reported no security type
    [InlineData("OPT")]  // a derivative whose contract detail the snapshot cannot rebuild
    public async Task OrderRecovery_UnconfirmedStockContract_StaysUnresolved(string securityType)
    {
        var client = RecoveryClient(c => c.RaiseOpenOrder(RecoverableOpenOrder() with { SecurityType = securityType }));
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        (await ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1"))
            .Should().BeNull("only a confirmed stock contract can be rebuilt from the snapshot");
    }

    [Fact]
    public async Task AmendmentRecovery_TrailWithOtherTerms_StaysUnresolved()
    {
        // The snapshot shows the proposed quantity, but it cannot show whether the trail changed.
        var client = RecoveryClient(c => c.RaiseOpenOrder(RecoverableOpenOrder() with { Quantity = 20m }));
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var original = RecoveryOriginal with { FilledQuantity = 0m, AverageFillPrice = null, Status = OrderStatus.Accepted };
        var amendment = new PendingOrderAmendment(original, original with { Quantity = 20m }, "7001") { ProposedTrail = 1.5m };

        var recovered = await ((IBrokerageOrderAmendmentRecoveryGateway)sut)
            .GetAmendmentForRecoveryAsync("client-1", original, amendment);

        recovered.Should().BeNull("IB snapshots never carry the trail, so no trail amendment can be resolved from one");
    }

    [Fact]
    public async Task RecoveredRoute_AfterASessionChange_IsRefusedUntilRecoveredAgain()
    {
        var cancelled = new List<int>();
        var client = RecoveryClient(c => c.RaiseOpenOrder(RecoverableOpenOrder()), cancelled);
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var recovery = (IBrokerageOrderRecoveryGateway)sut;
        (await recovery.GetOrderForRecoveryAsync("client-1")).Should().NotBeNull();

        // A new login can reuse order id 7001 for an unrelated order.
        client.RaiseConnectionLost();
        client.RaiseConnectionRestored();
        client.RaiseManagedAccounts("DU123456");
        var stale = () => sut.CancelOrderAsync("client-1");

        await stale.Should().ThrowAsync<InvalidOperationException>().WithMessage("*earlier IB session*");
        cancelled.Should().BeEmpty("a route recovered in an earlier session must not cancel in this one");
        (await recovery.GetOrderForRecoveryAsync("client-1")).Should().NotBeNull("reconciliation recovers it again");
        (await sut.CancelOrderAsync("client-1")).OrderStatus.Should().Be(OrderStatus.Cancelled);
        cancelled.Should().Equal([7001]);
    }

    [Fact]
    public async Task RecoverySnapshot_StatusEchoesForUntrackedOrders_AreNotPublishedAsLiveReports()
    {
        IBBrokerageGateway? gateway = null;
        var client = CreateAcknowledgingClient();
        client.OnRequestOpenOrders = c =>
        {
            // An order in an account outside this login, then a live update for an order this session tracks.
            c.RaiseOpenOrder(RecoverableOpenOrder(account: "U9999999"));
            c.RaiseOrderStatus(new IBOrderStatusUpdate(7001, "Submitted", 4m, 6m, 99.5d, 99.5d, 1L, 1, null, null,
                DateTimeOffset.UtcNow));
            c.RaiseOrderStatus(Status(11001, "Submitted", 2m));
            c.RaiseOpenOrdersCompleted();
        };
        await using var sut = gateway = CreateSut(client);
        await sut.ConnectAsync();
        await sut.SubmitOrderAsync(WorkingOrder());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var seen = new List<ExecutionReport>();
        var reading = ReadUntilAsync(sut.StreamExecutionReportsAsync(timeout.Token), report =>
        {
            seen.Add(report);
            return report.GatewayOrderId == "11001" && report.FilledQuantity == 2m;
        }, timeout.Token);

        var recovered = await ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1");
        await reading;

        recovered.Should().BeNull("the order belongs to an account outside the verified login");
        seen.Should().NotContain(report => report.GatewayOrderId == "7001",
            "an unvalidated snapshot echo must not reach the OMS as a live report");
    }
}
