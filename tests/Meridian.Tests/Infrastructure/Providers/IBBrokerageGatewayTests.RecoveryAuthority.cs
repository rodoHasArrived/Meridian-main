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
    public async Task WrongAccountAmendmentRecovery_PreservesOrderPendingAliasesAndDurableEvidence()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ib-amendment-account-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var original = RecoveryOriginal with
            {
                BrokerAccountId = "DU1", FilledQuantity = 0m, AverageFillPrice = null, Status = OrderStatus.Accepted,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var amendment = new PendingOrderAmendment(original, original with { Quantity = 20m, LimitPrice = 200m }, "7001")
            { CandidateBrokerOrderId = "7002" };
            var path = Path.Combine(directory, "orders.json");
            var store = new FileBrokerageOrderRecoveryStore(path, "ibkr");
            store.BindScope("paper:DU1");
            store.Save(new(original, "7001", true)
            {
                PendingAmendment = amendment, RequiresAmendmentRecovery = true, BrokerOrderAliases = ["7001"]
            });
            var other = original with { OrderId = "other-order" };
            store.Save(new(other, "8001", true) { BrokerOrderAliases = ["8001"] });
            var client = RecoveryClient(_ => { });
            client.ManagedAccounts = "DU1";
            await using var gateway = new IBBrokerageGateway(DefaultOptions(), NullLogger<IBBrokerageGateway>.Instance, client);
            await gateway.ConnectAsync();
            await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                recoveryStore: store, portfolioState: new PaperTradingPortfolio(100_000m));
            await oms.StartAsync();
            var pendingBefore = oms.GetPendingAmendmentForRecovery(original.OrderId);
            var durableBefore = await File.ReadAllTextAsync(path);
            var wrong = new ExecutionReport
            {
                OrderId = original.OrderId, ClientOrderId = original.OrderId, GatewayOrderId = "7002", ReplacesOrderId = "7001",
                BrokerAccountId = "DU2", ReportType = ExecutionReportType.Modified, Symbol = "AAPL", Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Accepted, OrderQuantity = 20m, OrderLimitPrice = 200m, HasOrderTerms = true
            };

            await Assert.ThrowsAsync<InvalidDataException>(() => oms.ReconcileRecoveryOrderAsync(wrong));

            oms.GetOrder(original.OrderId).Should().Be(original);
            oms.GetPendingAmendmentForRecovery(original.OrderId).Should().BeSameAs(pendingBefore);
            (await File.ReadAllTextAsync(path)).Should().Be(durableBefore);
            store.Load().Single(order => order.State.OrderId == original.OrderId).BrokerOrderAliases.Should().Equal("7001");
            // The rejected candidate must not be reserved as this order's active/alias identity.
            // A different retained order's independently proven report may still use that id.
            await oms.ReconcileRecoveryOrderAsync(wrong with
            {
                OrderId = other.OrderId, ClientOrderId = other.OrderId, ReplacesOrderId = null, BrokerAccountId = "DU1",
                ReportType = ExecutionReportType.New, OrderQuantity = 10m, HasOrderTerms = false, OrderLimitPrice = null
            });
            oms.GetOrder(other.OrderId)!.BrokerAccountId.Should().Be("DU1");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(10, false)]
    [InlineData(1, true)]
    public async Task LateOpenOrder_RetainsAccountWithoutRegressingOrRebookingEarlierFills(int filled, bool executionFill)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ib-late-account-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var client = CreateAcknowledgingClient();
            client.ManagedAccounts = "DU1";
            client.OnPlaceOrder = (c, id, _) => c.RaiseOrderStatus(Status(id, "Submitted"));
            await using var gateway = new IBBrokerageGateway(DefaultOptions(), NullLogger<IBBrokerageGateway>.Instance, client);
            await gateway.ConnectAsync();
            var path = Path.Combine(directory, "orders.json");
            var store = new FileBrokerageOrderRecoveryStore(path, "ibkr");
            var portfolio = new PaperTradingPortfolio(100_000m);
            var publisher = new AmendmentRacePublisher();
            await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                recoveryStore: store, portfolioState: portfolio, tradeEventPublisher: publisher);
            await oms.StartAsync();
            (await oms.PlaceOrderAsync(WorkingOrder())).Success.Should().BeTrue();
            if (filled > 0)
            {
                if (executionFill)
                    client.RaiseExecution(new(11001, "AAPL", "BOT", filled, 100d, filled, 100d,
                        "owned-fill", "DU1", "NYSE", 1L, null, DateTimeOffset.UtcNow));
                else
                    client.RaiseOrderStatus(new(11001, filled == 10 ? "Filled" : "Submitted", filled, 10 - filled,
                        100d, 100d, 1L, 1, null, null, DateTimeOffset.UtcNow));
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (filled > 0)
            {
                while ((await oms.ExecutionReports.ReadAsync(timeout.Token)).FilledQuantity != filled) { }
            }
            client.RaiseOpenOrder(OpenOrder(11001, WorkingOrder()));
            if (!executionFill)
            {
                while ((await oms.ExecutionReports.ReadAsync(timeout.Token)).BrokerAccountId != "DU1") { }
            }

            var state = oms.GetOrder("working-order")!;
            state.BrokerAccountId.Should().Be("DU1");
            state.FilledQuantity.Should().Be(filled);
            state.Status.Should().Be(filled == 10 ? OrderStatus.Filled : filled > 0 ? OrderStatus.PartiallyFilled : OrderStatus.Accepted);
            (portfolio.Positions.GetValueOrDefault("AAPL")?.Quantity ?? 0L).Should().Be(filled);
            publisher.Events.Count.Should().Be(filled > 0 ? 1 : 0, "account enrichment must not repeat accounting handoff");
            new FileBrokerageOrderRecoveryStore(path, "ibkr").Load().Should().ContainSingle()
                .Which.State.Should().Be(state);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ForeignAccountExecution_DoesNotPoisonCurrentFillsOrDurableState()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ib-foreign-execution-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var client = CreateAcknowledgingClient();
            client.ManagedAccounts = "DU1";
            await using var gateway = new IBBrokerageGateway(DefaultOptions(), NullLogger<IBBrokerageGateway>.Instance, client);
            await gateway.ConnectAsync();
            var path = Path.Combine(directory, "orders.json");
            var store = new FileBrokerageOrderRecoveryStore(path, "ibkr");
            var portfolio = new PaperTradingPortfolio(100_000m);
            var publisher = new AmendmentRacePublisher();
            await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                recoveryStore: store, portfolioState: portfolio, tradeEventPublisher: publisher);
            await oms.StartAsync();
            (await oms.PlaceOrderAsync(WorkingOrder())).Success.Should().BeTrue();

            client.RaiseExecution(new(11001, "AAPL", "BOT", 1m, 100d, 1m, 100d,
                "foreign-fill", "DU2", "NYSE", 1L, null, DateTimeOffset.UtcNow));
            var barrier = Status(11001, "Submitted");
            client.RaiseOrderStatus(barrier);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while ((await oms.ExecutionReports.ReadAsync(timeout.Token)).Timestamp != barrier.ReceivedAt) { }

            var state = oms.GetOrder("working-order")!;
            state.FilledQuantity.Should().Be(0m);
            state.BrokerAccountId.Should().Be("DU1");
            portfolio.Positions.Should().BeEmpty();
            publisher.Events.Should().BeEmpty();
            new FileBrokerageOrderRecoveryStore(path, "ibkr").Load().Should().ContainSingle()
                .Which.State.Should().Be(state);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
