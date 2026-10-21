using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    // Session-scoped order ids: a later IB login can hand the numeric id of an order recovered in an
    // earlier session to an unrelated order, so nothing recovered earlier may describe or route it.

    [Fact]
    public async Task OrderRecovery_ReusedGatewayId_UsesTheNewSessionsIdentityAndDropsTheOldAlias()
    {
        var cancelled = new List<int>();
        var sessions = 0;
        var client = RecoveryClient(c => c.RaiseOpenOrder(sessions == 0
            ? RecoverableOpenOrder()
            : RecoverableOpenOrder(clientOrderId: "client-2") with { Symbol = "MSFT", Action = "SELL" }), cancelled);
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var recovery = (IBrokerageOrderRecoveryGateway)sut;
        (await recovery.GetOrderForRecoveryAsync("client-1"))!.OrderId.Should().Be("client-1");

        sessions++;
        client.RaiseConnectionLost();
        client.RaiseConnectionRestored();
        client.RaiseManagedAccounts("DU123456");
        var recovered = await recovery.GetOrderForRecoveryAsync("client-2");

        recovered.Should().NotBeNull();
        recovered!.OrderId.Should().Be("client-2", "the snapshot row carries the new session's orderRef");
        recovered.Symbol.Should().Be("MSFT");
        recovered.Side.Should().Be(OrderSide.Sell);
        var staleCancel = () => sut.CancelOrderAsync("client-1");
        await staleCancel.Should().ThrowAsync<InvalidOperationException>("the earlier session's alias no longer names this order");
        cancelled.Should().BeEmpty();
        (await sut.CancelOrderAsync("client-2")).OrderStatus.Should().Be(OrderStatus.Cancelled);
        cancelled.Should().Equal(7001);
    }

    [Fact]
    public async Task OrderRecovery_ReusedGatewayId_IgnoresTheEarlierSessionsFillReport()
    {
        var sessions = 0;
        var client = RecoveryClient(c =>
        {
            if (sessions == 0)
            {
                c.RaiseOpenOrder(RecoverableOpenOrder() with { FilledQuantity = 3m, Status = "Submitted" });
                c.RaiseOrderStatus(new IBOrderStatusUpdate(7001, "Submitted", 3m, 7m, 100d, 100d, 1L, 1, null, null,
                    DateTimeOffset.UtcNow));
                return;
            }
            c.RaiseOpenOrder(RecoverableOpenOrder(clientOrderId: "client-2"));
        });
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var recovery = (IBrokerageOrderRecoveryGateway)sut;
        var earlier = await recovery.GetOrderForRecoveryAsync("client-1");
        earlier!.FilledQuantity.Should().Be(3m);

        sessions++;
        client.RaiseConnectionLost();
        client.RaiseConnectionRestored();
        client.RaiseManagedAccounts("DU123456");
        var recovered = await recovery.GetOrderForRecoveryAsync("client-2");

        recovered.Should().NotBeNull();
        recovered!.ReportType.Should().Be(ExecutionReportType.New);
        recovered.FilledQuantity.Should().Be(0m, "the cached partial fill belonged to the earlier session's order");
        recovered.FillPrice.Should().BeNull();
    }

    [Fact]
    public async Task OrderRecovery_AveragePriceForFewerSharesThanRecovered_StaysUnresolved()
    {
        var client = RecoveryClient(c =>
        {
            // The cumulative average covers two shares; the open-order row that follows says five are filled.
            c.RaiseOrderStatus(new IBOrderStatusUpdate(7001, "Submitted", 2m, 8m, 100d, 100d, 1L, 1, null, null,
                DateTimeOffset.UtcNow));
            c.RaiseOpenOrder(RecoverableOpenOrder() with { FilledQuantity = 5m, Status = "Submitted" });
        });
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        (await ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1"))
            .Should().BeNull("an average that does not cover the recovered quantity would misprice the remainder");
    }

    [Fact]
    public async Task OrderRecovery_FillAdvancedWithoutANewAverage_StaysUnresolved()
    {
        var client = RecoveryClient(c =>
        {
            c.RaiseOrderStatus(new IBOrderStatusUpdate(7001, "Submitted", 2m, 8m, 100d, 100d, 1L, 1, null, null,
                DateTimeOffset.UtcNow));
            // Three more shares fill, reported without an average or a last price.
            c.RaiseOrderStatus(new IBOrderStatusUpdate(7001, "Submitted", 5m, 5m, 0d, 0d, 1L, 1, null, null,
                DateTimeOffset.UtcNow));
            c.RaiseOpenOrder(RecoverableOpenOrder() with { FilledQuantity = 5m, Status = "Submitted" });
        });
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        (await ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1"))
            .Should().BeNull("the cached average covered two shares; the five-share fill has no price of its own");
    }

    [Fact]
    public async Task Recovery_RetainedIdentityDiffersFromTheMatchedOrder_InstallsNoRoute()
    {
        var cancelled = new List<int>();
        var client = RecoveryClient(c => c.RaiseOpenOrder(RecoverableOpenOrder()), cancelled);
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var retained = RecoveryOriginal with { Symbol = "MSFT" };
        var amendment = new PendingOrderAmendment(retained, retained with { Quantity = 20m }, "7001");

        (await ((IBrokerageOrderAmendmentRecoveryGateway)sut).GetAmendmentForRecoveryAsync("client-1", retained, amendment))
            .Should().BeNull("the orderRef matched a different instrument");
        (await ((IBrokerageRetainedOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1", retained))
            .Should().BeNull();

        var cancel = () => sut.CancelOrderAsync("client-1");
        await cancel.Should().ThrowAsync<InvalidOperationException>("no route was installed for the mismatched order");
        cancelled.Should().BeEmpty();
    }
}
