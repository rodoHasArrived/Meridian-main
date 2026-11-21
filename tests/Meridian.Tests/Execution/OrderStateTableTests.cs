using FluentAssertions;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// The OMS order table publishes every state write. A paused subscriber keeps only the latest
/// unread state per order incarnation, and a reused client order id is a separate incarnation
/// even when its CreatedAt matches the previous order's.
/// </summary>
public sealed class OrderStateTableTests
{
    private static readonly DateTimeOffset SameInstant = new(2026, 11, 10, 6, 0, 0, TimeSpan.Zero);

    private static OrderState State(OrderStatus status, decimal filled = 0m) => new()
    {
        OrderId = "reused",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        FilledQuantity = filled,
        Status = status,
        CreatedAt = SameInstant
    };

    private static async Task<List<OrderState>> DrainAsync(OrderStateSubscription subscription, int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = new List<OrderState>();
        await foreach (var state in subscription.ReadAllAsync(cts.Token))
        {
            read.Add(state);
            if (read.Count == count)
                break;
        }

        return read;
    }

    [Fact]
    public async Task ReusedIdWithSameCreatedAt_DoesNotOverwriteThePreviousOrdersUnreadFinalState()
    {
        var table = new OrderStateTable();
        await using var subscription = table.Broadcast.Subscribe();

        table.TryAdd("reused", State(OrderStatus.Accepted)).Should().BeTrue();
        var filled = State(OrderStatus.Filled, 10m);
        table["reused"] = filled;
        // The terminal id is reclaimed by a replacement order with an identical CreatedAt.
        table.TryUpdate("reused", State(OrderStatus.Accepted), filled).Should().BeTrue();

        var read = await DrainAsync(subscription, 2);

        read[0].Status.Should().Be(OrderStatus.Filled, "the first order's final state must not be coalesced away");
        read[0].FilledQuantity.Should().Be(10m);
        read[1].Status.Should().Be(OrderStatus.Accepted);
        read[1].FilledQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task PausedSubscriber_ReceivesOnlyTheLatestStateOfALiveOrder()
    {
        var table = new OrderStateTable();
        await using var subscription = table.Broadcast.Subscribe();

        table.TryAdd("reused", State(OrderStatus.Accepted)).Should().BeTrue();
        table["reused"] = State(OrderStatus.PartiallyFilled, 4m);
        table["reused"] = State(OrderStatus.Filled, 10m);

        var read = await DrainAsync(subscription, 1);

        read.Should().ContainSingle().Which.FilledQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task EvictionPublishesNothing_AndDisposedSubscriptionsStopReceiving()
    {
        var table = new OrderStateTable();
        var subscription = table.Broadcast.Subscribe();
        var filled = State(OrderStatus.Filled, 10m);
        table.TryAdd("reused", filled);
        (await DrainAsync(subscription, 1)).Should().ContainSingle();

        table.TryRemove(new KeyValuePair<string, OrderState>("reused", filled)).Should().BeTrue();
        await subscription.DisposeAsync();
        table.TryAdd("reused", State(OrderStatus.Accepted));

        var read = new List<OrderState>();
        await foreach (var state in subscription.ReadAllAsync())
            read.Add(state);
        read.Should().BeEmpty();
    }
}
