using Meridian.Execution.Sdk;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Read-only access to current order ownership and sizing. The OMS retains mutations and their
/// existing gates; the fill processor can read context without receiving the OMS itself.
/// </summary>
internal sealed class OrderFillContext(
    IReadOnlyDictionary<string, OrderState> orders,
    IReadOnlyDictionary<string, string> financialAccountIds,
    IReadOnlyDictionary<string, decimal> contractMultipliers,
    IReadOnlyDictionary<string, bool> faceValueSizing)
{
    internal bool TryGetOrder(string orderId, out OrderState state) => orders.TryGetValue(orderId, out state!);
    internal bool ContainsOrder(string orderId) => orders.ContainsKey(orderId);
    internal string? GetFinancialAccountId(string? orderId) =>
        orderId is not null && financialAccountIds.TryGetValue(orderId, out var accountId) ? accountId : null;
    internal decimal GetContractMultiplier(string? orderId) =>
        orderId is not null && contractMultipliers.TryGetValue(orderId, out var multiplier) ? multiplier : 1m;
    internal bool UsesFaceValueSizing(string orderId) => faceValueSizing.ContainsKey(orderId);
}
