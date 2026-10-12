using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed partial class BrokerageOrderRecoveryTests
{
    [Fact]
    public async Task FullFillRetentionPressure_CompactsHistoryWithoutReusingIdsOrRebookingAfterRestart()
    {
        using var directory = new RecoveryDirectory();
        var gateway = new RecoveryGateway
        {
            SubmitResponse = (request, _) => Task.FromResult(Report(request.ClientOrderId!, OrderStatus.Filled, 10m))
        };
        var portfolio = new PaperTradingPortfolio(100_000m);
        var options = new OrderManagementSystemOptions { MaxRetainedOrders = 2 };
        await using (var first = CreateOms(gateway, portfolio, directory.Path, options: options))
        {
            for (var index = 0; index < 8; index++)
            {
                (await first.PlaceOrderAsync(Request($"compact-{index}"))).Success.Should().BeTrue();
                (await ReadFillAsync(first)).FilledQuantity.Should().Be(10m);
                first.GetAllRetainedOrderStates().Should().HaveCountLessThanOrEqualTo(2);
            }

            first.GetOrder("compact-0").Should().BeNull();
            (await first.PlaceOrderAsync(Request("compact-0"))).Success.Should().BeFalse();
            gateway.SubmitCount.Should().Be(8);
            await gateway.PublishAsync(Report("compact-0", OrderStatus.Filled, 10m));
            (await DrainThroughMarkerAsync(first, gateway)).Should().BeEmpty();
            portfolio.Positions["AAPL"].Quantity.Should().Be(80L);
        }

        var store = new FileBrokerageOrderRecoveryStore(directory.Path, gateway.GatewayId);
        store.Load().Should().HaveCount(2);
        store.LoadTombstones().Should().HaveCount(6);
        store.LoadTombstones().Should().Contain(item => item.OrderId == "compact-0" && item.FilledQuantity == 10m);

        var restartedGateway = new RecoveryGateway();
        await using var restarted = CreateOms(restartedGateway, portfolio, directory.Path, options: options);
        restarted.GetAllRetainedOrderStates().Should().HaveCount(2);
        (await restarted.PlaceOrderAsync(Request("compact-0"))).Success.Should().BeFalse();
        restartedGateway.SubmitCount.Should().Be(0);
        await restartedGateway.PublishAsync(Report("compact-0", OrderStatus.Filled, 10m));
        (await DrainThroughMarkerAsync(restarted, restartedGateway)).Should().BeEmpty();
        portfolio.Positions["AAPL"].Quantity.Should().Be(80L);
        portfolio.Cash.Should().Be(88_000m);
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Expired)]
    public async Task RetentionPressure_KeepsLateTerminalFillsRecoverableInProcessAndAfterRestart(OrderStatus terminal)
    {
        using var directory = new RecoveryDirectory();
        var request = Request($"late-retained-{terminal}");
        var gateway = new RecoveryGateway
        {
            SubmitResponse = (order, _) => Task.FromResult(Report(order.ClientOrderId!,
                order.ClientOrderId == request.ClientOrderId ? terminal : OrderStatus.Filled,
                order.ClientOrderId == request.ClientOrderId ? 0m : 10m))
        };
        var portfolio = new PaperTradingPortfolio(100_000m);
        var options = new OrderManagementSystemOptions { MaxRetainedOrders = 2 };
        await using (var first = CreateOms(gateway, portfolio, directory.Path, options: options))
        {
            (await first.PlaceOrderAsync(request)).OrderState!.Status.Should().Be(terminal);
            for (var index = 0; index < 6; index++)
            {
                await first.PlaceOrderAsync(Request($"pressure-{index}"));
                await ReadFillAsync(first);
            }
            first.GetOrder(request.ClientOrderId!)!.Status.Should().Be(terminal);
            first.GetAllRetainedOrderStates().Should().HaveCount(2);

            // Queue the late execution immediately before another submission triggers trimming.
            // Both possible scheduling orders must retain the terminal account/fill watermark.
            await gateway.PublishAsync(Report(request.ClientOrderId!, terminal, 4m));
            await first.PlaceOrderAsync(Request("pressure-final"));
            var fills = await DrainThroughMarkerAsync(first, gateway);
            fills.Should().ContainSingle(fill => fill.ClientOrderId == request.ClientOrderId && fill.FilledQuantity == 4m);
            await gateway.PublishAsync(Report(request.ClientOrderId!, terminal, 4m));
            (await DrainThroughMarkerAsync(first, gateway)).Should().BeEmpty();
            first.GetOrder(request.ClientOrderId!)!.FilledQuantity.Should().Be(4m);
            portfolio.Positions["AAPL"].Quantity.Should().Be(74L);
        }

        var store = new FileBrokerageOrderRecoveryStore(directory.Path, gateway.GatewayId);
        store.Load().Should().Contain(item => item.State.OrderId == request.ClientOrderId && item.State.FilledQuantity == 4m);
        store.LoadTombstones().Should().NotContain(item => item.OrderId == request.ClientOrderId);
        await using var restarted = CreateOms(new RecoveryGateway(), portfolio, directory.Path, options: options);
        restarted.GetRecoveryOrders(FundAccountId).Should().Contain(order => order.OrderId == request.ClientOrderId);
        await restarted.ReconcileRecoveryOrderAsync(Report(request.ClientOrderId!, OrderStatus.Filled, 10m));
        (await ReadFillAsync(restarted)).FilledQuantity.Should().Be(6m);
        restarted.GetRecoveryOrders(FundAccountId).Should().BeEmpty();
        portfolio.Positions["AAPL"].Quantity.Should().Be(80L);
        portfolio.Cash.Should().Be(88_000m);
    }

    [Fact]
    public async Task RetentionPressure_PreservesFillStateWhileDurableHandoffIsPaused()
    {
        using var directory = new RecoveryDirectory();
        var publisher = new PausedTradeEventPublisher();
        var gateway = new RecoveryGateway
        {
            SubmitResponse = (request, _) => Task.FromResult(Report(request.ClientOrderId!, OrderStatus.Filled, 10m))
        };
        await using var oms = CreateOms(gateway, new PaperTradingPortfolio(100_000m), directory.Path,
            publisher, new OrderManagementSystemOptions { MaxRetainedOrders = 1 });
        var first = oms.PlaceOrderAsync(Request("handoff-pending"));
        await publisher.Entered.Task.WaitAsync(Timeout);
        var second = oms.PlaceOrderAsync(Request("handoff-pressure"));
        try
        {
            // Both submissions have synchronous acknowledgements but their accounting awaits
            // the paused publisher. Compaction must not mistake that for completed processing.
            oms.GetOrder("handoff-pending").Should().NotBeNull();
            new FileBrokerageOrderRecoveryStore(directory.Path, gateway.GatewayId).LoadTombstones().Should().BeEmpty();
        }
        finally
        {
            publisher.Release.TrySetResult();
        }
        (await first.WaitAsync(Timeout)).Success.Should().BeTrue();
        (await second.WaitAsync(Timeout)).Success.Should().BeTrue();
        oms.GetAllRetainedOrderStates().Should().ContainSingle();
        var store = new FileBrokerageOrderRecoveryStore(directory.Path, gateway.GatewayId);
        store.LoadTombstones().Should().ContainSingle();
        store.Load().Should().ContainSingle().Which.RequiresRecovery.Should().BeFalse();
    }
}
