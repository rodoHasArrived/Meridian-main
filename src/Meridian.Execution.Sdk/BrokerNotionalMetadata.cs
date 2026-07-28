using System.Globalization;

namespace Meridian.Execution.Sdk;

/// <summary>
/// Reads broker-native notional sizing from order metadata. Alpaca-style gateways route
/// the metadata dollar amount and discard <c>Quantity</c>, so anything that measures an
/// order's economic size — pre-trade risk rules and the OMS working-order reserve alike —
/// must read the routed notional from the same place the gateway does.
/// </summary>
public static class BrokerNotionalMetadata
{
    /// <summary>Metadata keys carrying broker-native notional sizing, in precedence order.</summary>
    public static IReadOnlyList<string> Keys { get; } = ["notional", "alpaca:notional"];

    /// <summary>
    /// Returns the routed dollar notional when the order is sized in dollars rather than
    /// quantity, otherwise <see langword="null"/>. A decimal value is the notional itself;
    /// a boolean <c>true</c> means the quantity field carries dollars.
    /// </summary>
    public static decimal? TryRead(IReadOnlyDictionary<string, string>? metadata, decimal quantity)
    {
        if (metadata is null)
        {
            return null;
        }

        foreach (var key in Keys)
        {
            if (!metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value > 0m)
            {
                return value;
            }

            // The gateway also accepts a boolean flag meaning "quantity is dollars".
            if (bool.TryParse(raw, out var isNotional) && isNotional)
            {
                return Math.Abs(quantity);
            }
        }

        return null;
    }
}
