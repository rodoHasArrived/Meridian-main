using System.Collections.Concurrent;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Proven broker identities, kept separate from the client identities that key the order book.
/// An active routing identity may change; earlier replacement aliases remain reserved.
/// </summary>
internal sealed class OrderBrokerIdentityIndex
{
    private readonly ConcurrentDictionary<string, string> _activeIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _aliases = new(StringComparer.Ordinal);

    internal bool TryGetActiveId(string orderId, out string brokerOrderId) =>
        _activeIds.TryGetValue(orderId, out brokerOrderId!);

    internal bool ContainsAlias(string brokerOrderId) => _aliases.ContainsKey(brokerOrderId);

    internal bool TryResolveAlias(string brokerOrderId, out string orderId) =>
        _aliases.TryGetValue(brokerOrderId, out orderId!);

    internal KeyValuePair<string, string>[] GetAliasOwners() => _aliases.ToArray();

    internal string[] GetAliases(string orderId) => _aliases
        .Where(pair => string.Equals(pair.Value, orderId, StringComparison.Ordinal))
        .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();

    internal void ForgetActiveId(string orderId) => _activeIds.TryRemove(orderId, out _);

    /// <summary>Installs a broker identity already confirmed by dispatch or recovery.</summary>
    internal void Confirm(string orderId, string brokerOrderId)
    {
        _activeIds[orderId] = brokerOrderId;
        _aliases.TryAdd(brokerOrderId, orderId);
    }

    internal void Restore(BrokerageOrderTombstone tombstone)
    {
        foreach (var alias in tombstone.BrokerOrderAliases ?? [])
        {
            if (string.IsNullOrWhiteSpace(alias) || !_aliases.TryAdd(alias, tombstone.OrderId))
                throw new InvalidDataException("Retired broker aliases conflict with another order identity.");
        }
    }

    internal void Restore(RetainedBrokerageOrder retained)
    {
        var orderId = retained.State.OrderId;
        if (!string.IsNullOrWhiteSpace(retained.BrokerOrderId))
        {
            _activeIds[orderId] = retained.BrokerOrderId;
            _aliases[retained.BrokerOrderId] = orderId;
        }
        foreach (var alias in retained.BrokerOrderAliases ?? [])
            RestoreAlias(alias, orderId);
    }

    private void RestoreAlias(string alias, string orderId)
    {
        if (string.IsNullOrWhiteSpace(alias)
            || _aliases.TryGetValue(alias, out var owner) && owner != orderId)
            throw new InvalidDataException("Retained broker aliases conflict with another order identity.");
        _aliases[alias] = orderId;
    }

    internal void Remember(string localOrderId, ExecutionReport report)
    {
        if (report.ReportType is ExecutionReportType.ModificationPending)
            return;
        var brokerOrderId = report.GatewayOrderId;
        if (string.IsNullOrWhiteSpace(brokerOrderId)
            && !string.IsNullOrWhiteSpace(report.ClientOrderId)
            && !string.Equals(report.OrderId, report.ClientOrderId, StringComparison.Ordinal))
            brokerOrderId = report.OrderId;

        if (string.IsNullOrWhiteSpace(localOrderId) || string.IsNullOrWhiteSpace(brokerOrderId))
            return;

        // A refusal is not a new routing identity; an earlier chain member cannot displace
        // the replacement just because its delayed callback arrived last.
        var rejectedReplacement = report.ReportType is ExecutionReportType.ModificationRejected or ExecutionReportType.Rejected
            && report.ReplacesOrderId is { Length: > 0 };
        if (!rejectedReplacement && (!_activeIds.TryGetValue(localOrderId, out var currentBrokerId)
            || !_aliases.ContainsKey(brokerOrderId)
                && (string.IsNullOrWhiteSpace(report.ReplacesOrderId)
                    || string.Equals(report.ReplacesOrderId, currentBrokerId, StringComparison.Ordinal))))
            _activeIds[localOrderId] = brokerOrderId;
        _aliases.TryAdd(brokerOrderId, localOrderId);
        if (report.ReplacesOrderId is { Length: > 0 } replaced)
            _aliases.TryAdd(replaced, localOrderId);
    }

    /// <summary>
    /// Resolves proven aliases and preserves replacement-chain lifecycle semantics. The lookup
    /// is read-only; the caller owns report serialization and order-state mutation.
    /// </summary>
    internal ExecutionReport Normalize(ExecutionReport report, Func<string, OrderState?> findOrder)
    {
        var brokerId = report.GatewayOrderId ?? report.OrderId;
        string? localId = null;
        if (_aliases.TryGetValue(brokerId, out var known))
            localId = known;
        else if (report.ReplacesOrderId is { Length: > 0 } replaced
            && _aliases.TryGetValue(replaced, out var original))
            localId = original;
        if (localId is null)
            return report;

        var normalized = report with { ClientOrderId = localId };
        if (report.ReportType is ExecutionReportType.Rejected && report.OrderStatus is OrderStatus.Rejected
            && report.ReplacesOrderId is { Length: > 0 } rejectedPredecessor
            && _activeIds.TryGetValue(localId, out var workingBrokerId)
            && string.Equals(rejectedPredecessor, workingBrokerId, StringComparison.Ordinal)
            && !string.Equals(brokerId, workingBrokerId, StringComparison.Ordinal)
            && findOrder(localId) is { } working && !IsTerminal(working.Status))
        {
            normalized = normalized with
            {
                ReportType = ExecutionReportType.ModificationRejected,
                OrderStatus = working.Status,
                OrderQuantity = working.Quantity
            };
        }
        if (_aliases.ContainsKey(brokerId)
            && _activeIds.TryGetValue(localId, out var activeBrokerId)
            && !string.Equals(brokerId, activeBrokerId, StringComparison.Ordinal)
            && findOrder(localId) is { } current)
        {
            var status = report.FilledQuantity >= current.Quantity ? OrderStatus.Filled
                : report.FilledQuantity > current.FilledQuantity
                    && current.Status is OrderStatus.Accepted or OrderStatus.PendingNew
                        ? OrderStatus.PartiallyFilled
                        : current.Status;
            normalized = normalized with { OrderStatus = status, OrderQuantity = current.Quantity };
        }
        return normalized;
    }

    private static bool IsTerminal(OrderStatus status) =>
        status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Expired;
}
