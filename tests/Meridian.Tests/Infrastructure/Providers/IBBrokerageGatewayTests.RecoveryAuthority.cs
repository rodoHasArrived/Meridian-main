using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    [Fact]
    public async Task OrderRecovery_LegacyOwnershipWithoutAnExplicitPin_InstallsNoRoute()
    {
        var queried = false;
        var client = RecoveryClient(c =>
        {
            queried = true;
            c.RaiseOpenOrder(RecoverableOpenOrder());
        });
        await using var sut = new IBBrokerageGateway(DefaultOptions(), NullLogger<IBBrokerageGateway>.Instance, client);
        await sut.ConnectAsync();

        (await sut.GetOrderForRecoveryAsync("client-1")).Should().BeNull();
        (await sut.GetOrderForRecoveryAsync("client-1", RecoveryOriginal)).Should().BeNull(
            "pre-change retained order states have no broker account and must not invent one");
        queried.Should().BeFalse("an orderRef does not prove the retained external account");
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CancelOrderAsync("client-1"));
    }

    [Theory]
    [InlineData("DU123456", true)]
    [InlineData("DU654321", false)]
    public async Task OrderRecovery_MultiAccountLogin_RequiresTheRetainedAcknowledgementAccount(string observedAccount, bool expected)
    {
        var client = RecoveryClient(c =>
        {
            c.RaiseOpenOrder(RecoverableOpenOrder(account: observedAccount));
            if (expected)
                c.RaiseOpenOrder(RecoverableOpenOrder(orderId: 7002, account: "DU654321"));
        });
        client.ManagedAccounts = "DU123456,DU654321";
        await using var sut = new IBBrokerageGateway(DefaultOptions(), NullLogger<IBBrokerageGateway>.Instance, client);
        await sut.ConnectAsync();
        var retained = RecoveryOriginal with { FilledQuantity = 0m, AverageFillPrice = null, BrokerAccountId = "DU123456" };

        var recovered = await sut.GetOrderForRecoveryAsync("client-1", retained);

        if (expected)
        {
            recovered.Should().NotBeNull();
            recovered!.BrokerAccountId.Should().Be("DU123456");
        }
        else
        {
            recovered.Should().BeNull("another managed account cannot borrow the retained orderRef");
            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CancelOrderAsync("client-1"));
        }
    }

    [Theory]
    [InlineData("U123456", true)]
    [InlineData("DU123456", false)]
    [InlineData("DF123456", true)]
    [InlineData("DU123456,U123456", true)]
    [InlineData("DU123456bad", true)]
    public async Task OrderRecovery_UnverifiedOrContradictoryEnvironment_InstallsNoRoute(string accounts, bool paper)
    {
        var queried = false;
        var client = RecoveryClient(c =>
        {
            queried = true;
            c.RaiseOpenOrder(RecoverableOpenOrder());
        });
        client.ManagedAccounts = accounts;
        await using var sut = CreateSut(client, paper);
        await sut.ConnectAsync();

        sut.ScopeIdentity.Should().BeNull();
        (await sut.GetOrderForRecoveryAsync("client-1")).Should().BeNull();
        queried.Should().BeFalse("the socket port and configured flag do not prove the broker environment");
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CancelOrderAsync("client-1"));
    }

    [Fact]
    public async Task BrokerAcknowledgement_RetainsItsAccountAcrossFileReload_AndRejectsAnotherAccount()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ib-account-proof-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var client = CreateAcknowledgingClient();
            client.ManagedAccounts = "DU1";
            await using var gateway = new IBBrokerageGateway(DefaultOptions(), NullLogger<IBBrokerageGateway>.Instance, client);
            await gateway.ConnectAsync();
            var path = Path.Combine(directory, "orders.json");
            var store = new FileBrokerageOrderRecoveryStore(path, "ibkr");
            await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                recoveryStore: store, portfolioState: new PaperTradingPortfolio(100_000m));
            await oms.StartAsync();

            var placed = await oms.PlaceOrderAsync(WorkingOrder());
            placed.Success.Should().BeTrue();
            placed.OrderState!.BrokerAccountId.Should().Be("DU1");
            new FileBrokerageOrderRecoveryStore(path, "ibkr").Load().Should().ContainSingle()
                .Which.State.BrokerAccountId.Should().Be("DU1");

            var wrongAccount = new ExecutionReport
            {
                OrderId = "working-order", ClientOrderId = "working-order", GatewayOrderId = "11001",
                BrokerAccountId = "DU2", ReportType = ExecutionReportType.New, Symbol = "AAPL",
                Side = OrderSide.Buy, OrderStatus = OrderStatus.Accepted, OrderQuantity = 10m
            };
            await Assert.ThrowsAsync<InvalidDataException>(() => oms.ReconcileRecoveryOrderAsync(wrongAccount));
            oms.GetOrder("working-order")!.BrokerAccountId.Should().Be("DU1");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
