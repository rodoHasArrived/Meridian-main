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
    // Through object: the gateway is sealed, so a direct cast would not compile against a
    // gateway that does not expose the connection state at all.
    private static IBrokerageConnectionState Connection(IBBrokerageGateway gateway) => (IBrokerageConnectionState)(object)gateway;

    private static IBOpenOrderUpdate RecoverableOpenOrder(int orderId = 7001, string account = "DU123456", string clientOrderId = "client-1")
        => new(orderId, "AAPL", "STK", "BUY", "LMT", 10m, 0m, 100d, null, "Submitted", clientOrderId, account, null, null, null,
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task OrderRecovery_ConnectionChangesDuringLookup_LeavesNoCancellationRoute()
    {
        IBBrokerageGateway? gateway = null;
        var cancelled = new List<int>();
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                c.RaiseOpenOrder(RecoverableOpenOrder());
                gateway!.DisconnectAsync().GetAwaiter().GetResult();
                gateway.ConnectAsync().GetAwaiter().GetResult();
                c.RaiseOpenOrdersCompleted();
            },
            OnCancelOrder = (_, id) => cancelled.Add(id)
        };
        await using var sut = gateway = CreateSut(client);
        await sut.ConnectAsync();

        var recovery = () => ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1");
        await recovery.Should().ThrowAsync<InvalidOperationException>().WithMessage("*connection changed*");

        var cancel = () => sut.CancelOrderAsync("client-1");
        await cancel.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unknown*");
        cancelled.Should().BeEmpty("a stale snapshot must never route a cancel into the next session");
    }

    [Theory]
    [InlineData(true)]   // transport dropped and the client reconnected on its own
    [InlineData(false)]  // transport dropped and has not come back
    public async Task OrderRecovery_AutomaticReconnectDuringLookup_RefusesEvidence(bool restored)
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                c.RaiseOpenOrder(RecoverableOpenOrder());
                c.RaiseConnectionLost();
                if (restored)
                    c.RaiseConnectionRestored();
                c.RaiseOpenOrdersCompleted();
            }
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var generation = Connection(sut).ConnectionGeneration;

        var recovery = () => ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1");

        await recovery.Should().ThrowAsync<InvalidOperationException>().WithMessage("*connection changed*");
        Connection(sut).ConnectionGeneration.Should().NotBe(generation);
    }

    [Fact]
    public async Task OrderRecovery_LastFillPriceOnly_StaysUnresolved()
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                c.RaiseOpenOrder(RecoverableOpenOrder());
                // IB reported the fill with no cumulative average, only the last fill price.
                c.RaiseOrderStatus(new IBOrderStatusUpdate(7001, "Submitted", 3m, 7m, 0d, 99.4d, 1L, 1, null, null,
                    DateTimeOffset.UtcNow));
                c.RaiseOpenOrdersCompleted();
            }
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        (await ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1"))
            .Should().BeNull("a last fill price must not be relabelled as the cumulative average");
    }

    [Fact]
    public async Task ScopeIdentity_IsBrokerVerifiedPerSession()
    {
        var client = new FakeIbBrokerageClient { OnRequestNextValidId = c => c.RaiseNextValidId(9001), ManagedAccounts = null };
        await using var sut = CreateSut(client);
        var connection = Connection(sut);
        connection.ScopeIdentity.Should().BeNull();

        await sut.ConnectAsync();
        connection.ScopeIdentity.Should().BeNull("no account has been verified by the broker yet");
        client.RaiseManagedAccounts("DU123456");
        connection.ScopeIdentity.Should().Be("paper:DU123456");
        connection.IsExecutionStreamHealthy.Should().BeTrue();

        client.RaiseManagedAccounts("DU654321,DU123456");
        connection.ScopeIdentity.Should().Be("paper:DU123456,DU654321", "every account of the login is bound, in a stable order");
        var generation = connection.ConnectionGeneration;

        client.RaiseConnectionLost();
        connection.ScopeIdentity.Should().BeNull("a dropped session's accounts are no longer verified");
        connection.ConnectionGeneration.Should().NotBe(generation);

        await sut.DisconnectAsync();
        connection.ScopeIdentity.Should().BeNull();
        connection.IsExecutionStreamHealthy.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]        // the broker never verified an account for this login
    [InlineData("DU654321")]  // the order belongs to an account outside the verified login
    public async Task OrderRecovery_OutsideTheVerifiedAccount_StaysUnresolved(string? verifiedAccount)
    {
        var client = new FakeIbBrokerageClient
        {
            ManagedAccounts = verifiedAccount,
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                c.RaiseOpenOrder(RecoverableOpenOrder(account: "DU123456"));
                c.RaiseOpenOrdersCompleted();
            }
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        (await ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1")).Should().BeNull();
    }

    [Theory]
    [InlineData("DU654321", true)]   // another account of the same multi-account login
    [InlineData("U9999999", false)]  // an account this login does not hold
    public async Task OrderRecovery_MultiAccountLogin_MatchesOnlyOrdersInTheLoginsAccounts(string orderAccount, bool recovered)
    {
        var client = new FakeIbBrokerageClient
        {
            ManagedAccounts = "DU123456,DU654321",
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                c.RaiseOpenOrder(RecoverableOpenOrder(account: orderAccount));
                c.RaiseOpenOrdersCompleted();
            }
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        var report = await ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1");

        Connection(sut).ScopeIdentity.Should().Be("paper:DU123456,DU654321", "the scope names every account of the login");
        if (recovered)
            report.Should().NotBeNull("a linked account of the verified login is in scope");
        else
            report.Should().BeNull("an order outside the login's accounts never matches by orderRef alone");
    }

    [Fact]
    public async Task RetainedOrders_RefuseAnUnverifiedOrDifferentScope()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ib-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var client = CreateAcknowledgingClient();
            client.ManagedAccounts = null;
            await using var gateway = CreateSut(client);
            await gateway.ConnectAsync();
            var store = new FileBrokerageOrderRecoveryStore(Path.Combine(directory, "orders.json"), "ibkr");
            store.BindScope("live:U1000000");
            await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                recoveryStore: store, portfolioState: new PaperTradingPortfolio(100_000m));

            var placed = 0;
            client.OnPlaceOrder = (_, _, _) => placed++;

            var unverified = await oms.PlaceOrderAsync(WorkingOrder());
            unverified.Success.Should().BeFalse();
            unverified.ErrorMessage.Should().Contain("not been verified");

            client.RaiseManagedAccounts("DU123456");
            var mismatched = await oms.PlaceOrderAsync(WorkingOrder() with { ClientOrderId = "second-order" });
            mismatched.Success.Should().BeFalse();
            mismatched.ErrorMessage.Should().Contain("different");
            placed.Should().Be(0, "neither order may reach IB outside the retained store's scope");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AmendmentRecovery_TrailOnlyAmendment_StaysUnresolved()
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                c.RaiseOpenOrder(RecoverableOpenOrder());
                c.RaiseOpenOrdersCompleted();
            }
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var original = RecoveryOriginal with { FilledQuantity = 0m, AverageFillPrice = null, Status = OrderStatus.Accepted };
        var amendment = new PendingOrderAmendment(original, original, "7001") { ProposedTrail = 1.5m };

        var recovered = await ((IBrokerageOrderAmendmentRecoveryGateway)sut)
            .GetAmendmentForRecoveryAsync("client-1", original, amendment);

        recovered.Should().BeNull("the snapshot carries no trailing term to tell the outcomes apart");
    }
}
