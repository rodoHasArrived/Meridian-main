using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;

namespace Meridian.Execution;

/// <summary>
/// Deterministic identity for an outbound fill event. The fill id is derived from the
/// execution report's own content rather than generated, so a replayed report produces the
/// same id and the accounting handoff stays idempotent across retries and restarts.
/// </summary>
public sealed partial class OrderManagementSystem
{
    private static TradeExecutedEvent CreateTradeExecutedEvent(
        ExecutionReport fillIncrement,
        decimal cumulativeFilledQuantity,
        decimal realizedPnl,
        decimal newCash,
        string? financialAccountId)
    {
        if (fillIncrement.FillPrice is not { } fillPrice)
        {
            throw new InvalidOperationException(
                $"Fill report '{fillIncrement.OrderId}' for '{fillIncrement.Symbol}' has no execution price.");
        }

        var canonicalIdentity = string.Join(
            "|",
            EncodeIdentityPart(fillIncrement.OrderId),
            EncodeIdentityPart(fillIncrement.ClientOrderId),
            EncodeIdentityPart(fillIncrement.GatewayOrderId),
            EncodeIdentityPart(fillIncrement.Symbol),
            ((int)fillIncrement.Side).ToString(CultureInfo.InvariantCulture),
            fillIncrement.FilledQuantity.ToString(CultureInfo.InvariantCulture),
            cumulativeFilledQuantity.ToString(CultureInfo.InvariantCulture),
            fillPrice.ToString(CultureInfo.InvariantCulture),
            (fillIncrement.Commission ?? 0m).ToString(CultureInfo.InvariantCulture),
            fillIncrement.Timestamp.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            EncodeIdentityPart(financialAccountId));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity));
        var fillId = new Guid(hash.AsSpan(0, 16));

        return new TradeExecutedEvent(
            fillId,
            fillIncrement.ClientOrderId ?? fillIncrement.OrderId,
            fillIncrement.Symbol,
            fillIncrement.Side,
            fillIncrement.FilledQuantity,
            fillPrice,
            fillIncrement.Commission ?? 0m,
            realizedPnl,
            newCash,
            fillIncrement.Timestamp,
            financialAccountId);
    }

    private static string EncodeIdentityPart(string? value)
        => value is null
            ? "-"
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
