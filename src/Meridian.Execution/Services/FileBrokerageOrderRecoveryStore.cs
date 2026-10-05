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
        if (snapshot.Version != 1 || snapshot.Orders is null || !string.Equals(snapshot.GatewayId, gatewayId, StringComparison.Ordinal))
            throw new InvalidDataException("Brokerage order recovery snapshot belongs to a different gateway or unsupported version.");
        ScopeIdentity = snapshot.ScopeIdentity;
        foreach (var order in snapshot.Orders)
        {
            if (order.State is null || string.IsNullOrWhiteSpace(order.State.OrderId)
                || string.IsNullOrWhiteSpace(order.State.Symbol) || order.State.Quantity <= 0m
                || order.State.FilledQuantity < 0m || order.State.FilledQuantity > order.State.Quantity
                || !_orders.TryAdd(order.State.OrderId, order))
                throw new InvalidDataException("Brokerage order recovery snapshot contains missing or duplicate order identities.");
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
            if (ScopeIdentity is null && _orders.Count > 0)
                throw new InvalidOperationException("Retained brokerage orders lack verified account and environment scope.");
            ScopeIdentity = scopeIdentity;
        }
    }

    public void RequireRecovery(string orderId)
    {
        lock (_sync)
        {
            if (_orders.TryGetValue(orderId, out var order))
                Save(order with { RequiresRecovery = true });
        }
    }

    public IReadOnlyList<RetainedBrokerageOrder> Load()
    {
        lock (_sync)
            return _orders.Values.ToArray();
    }

    public void Save(RetainedBrokerageOrder order)
    {
        lock (_sync)
        {
            // Concurrent acknowledgement and stream paths must never persist a fill regression.
            if (_orders.TryGetValue(order.State.OrderId, out var previous)
                && previous.State.FilledQuantity > order.State.FilledQuantity)
                order = order with { State = previous.State };
            var next = new Dictionary<string, RetainedBrokerageOrder>(_orders, StringComparer.Ordinal)
            {
                [order.State.OrderId] = order
            };
            AtomicFileWriter.Write(_path, JsonSerializer.Serialize(
                new BrokerageOrderRecoverySnapshot(1, GatewayId, next.Values.ToArray(), ScopeIdentity),
                ExecutionJsonContext.Default.BrokerageOrderRecoverySnapshot));
            _orders[order.State.OrderId] = order;
        }
    }
}

public sealed record RetainedBrokerageOrder(OrderState State, string? BrokerOrderId, bool RequiresRecovery);

internal sealed record BrokerageOrderRecoverySnapshot(int Version, string GatewayId, RetainedBrokerageOrder[] Orders, string? ScopeIdentity = null);
