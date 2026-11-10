using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using GatewayOrderStatus = Meridian.Execution.Models.OrderStatus;

namespace Meridian.Tests.Execution;

/// <summary>
/// Coverage for <see cref="OrderManagementHostedService"/> and <see cref="OrderLifecycleManager"/>,
/// which previously had no test references.
/// </summary>
public sealed class OrderLifecycleServicesTests
{
    [Fact]
    public async Task HostedService_ForwardsStartAndStopToALifecycleAwareOrderManager()
    {
        var manager = Substitute.For<IOrderManager, IOrderManagementLifecycle>();
        var lifecycle = (IOrderManagementLifecycle)manager;
        var hosted = new OrderManagementHostedService(manager);
        using var cts = new CancellationTokenSource();

        await hosted.StartAsync(cts.Token);
        await hosted.StopAsync(cts.Token);

        await lifecycle.Received(1).StartAsync(cts.Token);
        await lifecycle.Received(1).StopAsync(cts.Token);
    }

    [Fact]
    public async Task HostedService_WithoutLifecycle_CompletesWithoutCallingTheManager()
    {
        var manager = Substitute.For<IOrderManager>();
        var hosted = new OrderManagementHostedService(manager);

        await hosted.StartAsync(CancellationToken.None);
        await hosted.StopAsync(CancellationToken.None);

        manager.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task LifecycleManager_KeepsTheLatestUpdatePerOrderAndStopsOnDispose()
    {
        var updates = Channel.CreateUnbounded<OrderStatusUpdate>();
        var gateway = Substitute.For<IOrderGateway>();
        gateway.StreamOrderUpdatesAsync(Arg.Any<CancellationToken>())
            .Returns(call => updates.Reader.ReadAllAsync(call.Arg<CancellationToken>()));
        var manager = new OrderLifecycleManager(gateway, NullLogger<OrderLifecycleManager>.Instance);

        manager.Start();
        manager.Start(); // idempotent
        await updates.Writer.WriteAsync(Update("o-1", GatewayOrderStatus.Working, 0m));
        await updates.Writer.WriteAsync(Update("o-1", GatewayOrderStatus.PartiallyFilled, 40m));
        await updates.Writer.WriteAsync(Update("o-2", GatewayOrderStatus.Rejected, 0m));
        await WaitUntilAsync(() => manager.GetAllOrders().Count == 2
            && manager.GetStatus("o-1")?.FilledQuantity == 40m);

        manager.GetStatus("o-1")!.Status.Should().Be(GatewayOrderStatus.PartiallyFilled);
        manager.GetStatus("o-2")!.Status.Should().Be(GatewayOrderStatus.Rejected);
        manager.GetStatus("unknown").Should().BeNull();
        gateway.Received(1).StreamOrderUpdatesAsync(Arg.Any<CancellationToken>());

        var dispose = manager.DisposeAsync().AsTask();
        (await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(dispose,
            "disposing cancels the update stream rather than waiting for the gateway to close it");
    }

    [Fact]
    public async Task LifecycleManager_StreamFailure_KeepsAlreadyTrackedOrders()
    {
        var gateway = Substitute.For<IOrderGateway>();
        gateway.StreamOrderUpdatesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => FailingStream(Update("o-1", GatewayOrderStatus.Filled, 10m)));
        var manager = new OrderLifecycleManager(gateway, NullLogger<OrderLifecycleManager>.Instance);

        manager.Start();
        await WaitUntilAsync(() => manager.GetStatus("o-1") is not null);
        await manager.DisposeAsync();

        manager.GetStatus("o-1")!.Status.Should().Be(GatewayOrderStatus.Filled);
    }

    private static async IAsyncEnumerable<OrderStatusUpdate> FailingStream(
        OrderStatusUpdate first,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return first;
        await Task.Yield();
        throw new InvalidOperationException("stream broke");
    }

    private static OrderStatusUpdate Update(string orderId, GatewayOrderStatus status, decimal filled) =>
        new(orderId, orderId, "AAPL", status, filled, filled > 0m ? 100m : null, null, DateTimeOffset.UtcNow);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(10);
        }
    }
}
