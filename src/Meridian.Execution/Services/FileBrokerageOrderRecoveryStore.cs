using System.Text.Json;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;

namespace Meridian.Execution.Services;

/// <summary>
/// Durable broker-dispatch identities and their last completely processed order state.
/// A write must succeed before dispatch; unreadable or foreign evidence fails startup closed.
/// </summary>
public sealed class FileBrokerageOrderRecoveryStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private readonly Dictionary<string, RetainedBrokerageOrder> _orders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BrokerageOrderTombstone> _tombstones = new(StringComparer.Ordinal);

    public FileBrokerageOrderRecoveryStore(string path, string gatewayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        _path = Path.GetFullPath(path);
        GatewayId = gatewayId;
        if (!File.Exists(_path))
            return;

        var snapshot = JsonSerializer.Deserialize(File.ReadAllText(_path), ExecutionJsonContext.Default.BrokerageOrderRecoverySnapshot)
            ?? throw new InvalidDataException("Brokerage order recovery snapshot is empty.");
        if (snapshot.Version is not (1 or 2) || snapshot.Orders is null
            || (snapshot.Version == 2 && snapshot.Tombstones is null)
            || !string.Equals(snapshot.GatewayId, gatewayId, StringComparison.Ordinal))
            throw new InvalidDataException("Brokerage order recovery snapshot belongs to a different gateway or unsupported version.");
        ScopeIdentity = snapshot.ScopeIdentity;
        foreach (var order in snapshot.Orders)
        {
            if (order is null || order.State is null || string.IsNullOrWhiteSpace(order.State.OrderId)
                || string.IsNullOrWhiteSpace(order.State.Symbol) || order.State.Quantity <= 0m
                || order.State.FilledQuantity < 0m || order.State.FilledQuantity > order.State.Quantity
                || !_orders.TryAdd(order.State.OrderId, order))
                throw new InvalidDataException("Brokerage order recovery snapshot contains missing or duplicate order identities.");
        }
        foreach (var tombstone in snapshot.Tombstones ?? [])
        {
            if (tombstone is null || string.IsNullOrWhiteSpace(tombstone.OrderId)
                || tombstone.Quantity <= 0m || tombstone.FilledQuantity < 0m
                || tombstone.FilledQuantity > tombstone.Quantity
                || !IsCompactableTerminalState(tombstone.Status, tombstone.Quantity, tombstone.FilledQuantity)
                || _orders.ContainsKey(tombstone.OrderId)
                || !_tombstones.TryAdd(tombstone.OrderId, tombstone))
            {
                throw new InvalidDataException("Brokerage recovery tombstones contain invalid or duplicate terminal identities.");
            }
        }
    }

    public string GatewayId { get; }

    public string? ScopeIdentity { get; private set; }

    /// <summary>Bind retained identities to the broker-verified account and trading environment.</summary>
    public void BindScope(string scopeIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeIdentity);
        lock (_sync)
        {
            if (ScopeIdentity is not null && !string.Equals(ScopeIdentity, scopeIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException("Retained brokerage orders belong to a different broker account or trading environment.");
            if (ScopeIdentity is null && (_orders.Count > 0 || _tombstones.Count > 0))
                throw new InvalidOperationException("Retained brokerage orders lack verified account and environment scope.");
            ScopeIdentity = scopeIdentity;
        }
    }

    public void RequireRecovery(string orderId)
    {
        lock (_sync)
        {
            if (_tombstones.ContainsKey(orderId))
                throw new InvalidOperationException("A compacted brokerage order cannot be reopened for recovery.");
            if (_orders.TryGetValue(orderId, out var order))
                Save(order with { RequiresRecovery = true });
        }
    }

    public IReadOnlyList<RetainedBrokerageOrder> Load()
    {
        lock (_sync)
            return _orders.Values.ToArray();
    }

    /// <summary>Terminal client identities retained to prevent submission after full-state eviction.</summary>
    public IReadOnlyList<BrokerageOrderTombstone> LoadTombstones()
    {
        lock (_sync)
            return _tombstones.Values.ToArray();
    }

    /// <summary>
    /// Atomically replaces safe terminal state with its permanent client-identity tombstone.
    /// Cancelled, expired, uncertain, and partially filled orders retain full recovery evidence.
    /// </summary>
    public bool TryCompact(string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        lock (_sync)
        {
            if (!_orders.TryGetValue(orderId, out var order) || order.RequiresRecovery
                || !IsCompactableTerminalState(order.State.Status, order.State.Quantity, order.State.FilledQuantity)
                || (order.State.Status == OrderStatus.Rejected && !string.IsNullOrWhiteSpace(order.BrokerOrderId)))
                return false;

            var state = order.State;
            var tombstone = new BrokerageOrderTombstone(state.OrderId, state.Status, state.Quantity, state.FilledQuantity);
            var nextOrders = new Dictionary<string, RetainedBrokerageOrder>(_orders, StringComparer.Ordinal);
            nextOrders.Remove(orderId);
            var nextTombstones = new Dictionary<string, BrokerageOrderTombstone>(_tombstones, StringComparer.Ordinal)
            {
                [orderId] = tombstone
            };
            WriteSnapshot(nextOrders.Values.ToArray(), nextTombstones.Values.ToArray());
            _orders.Remove(orderId);
            _tombstones[orderId] = tombstone;
            return true;
        }
    }

    public void Save(RetainedBrokerageOrder order)
    {
        lock (_sync)
        {
            if (_tombstones.ContainsKey(order.State.OrderId))
                throw new InvalidOperationException("A compacted brokerage order identity cannot be overwritten.");
            // Concurrent acknowledgement and stream paths must never persist a fill regression.
            if (_orders.TryGetValue(order.State.OrderId, out var previous)
                && previous.State.FilledQuantity > order.State.FilledQuantity)
                order = order with { State = previous.State };
            var next = new Dictionary<string, RetainedBrokerageOrder>(_orders, StringComparer.Ordinal)
            {
                [order.State.OrderId] = order
            };
            WriteSnapshot(next.Values.ToArray(), _tombstones.Values.ToArray());
            _orders[order.State.OrderId] = order;
        }
    }

    private void WriteSnapshot(RetainedBrokerageOrder[] orders, BrokerageOrderTombstone[] tombstones) =>
        AtomicFileWriter.Write(_path, JsonSerializer.Serialize(
            new BrokerageOrderRecoverySnapshot(2, GatewayId, orders, ScopeIdentity, tombstones),
            ExecutionJsonContext.Default.BrokerageOrderRecoverySnapshot));

    private static bool IsCompactableTerminalState(OrderStatus status, decimal quantity, decimal filledQuantity) =>
        quantity > 0m && ((status == OrderStatus.Filled && filledQuantity == quantity)
            || (status == OrderStatus.Rejected && filledQuantity == 0m));
}

public sealed record RetainedBrokerageOrder(OrderState State, string? BrokerOrderId, bool RequiresRecovery);

/// <summary>Minimal terminal identity that permanently reserves a dispatched client order id.</summary>
public sealed record BrokerageOrderTombstone(string OrderId, OrderStatus Status, decimal Quantity, decimal FilledQuantity);

internal sealed record BrokerageOrderRecoverySnapshot(
    int Version,
    string GatewayId,
    RetainedBrokerageOrder[] Orders,
    string? ScopeIdentity = null,
    BrokerageOrderTombstone[]? Tombstones = null);
