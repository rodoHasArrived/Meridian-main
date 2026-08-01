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
            // The gateway scans metadata with OrdinalIgnoreCase, so a caller writing
            // "Notional" into an ordinal dictionary would route dollars the risk rails
            // never measured. Match how the value is actually consumed.
            var raw = TryReadValue(metadata, key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            // Same NumberStyles the gateway parses with: a value it would reject as a
            // decimal must not be read as one here, or the two paths disagree about
            // whether the order is dollar-sized at all.
            if (decimal.TryParse(
                    raw,
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var value) && value > 0m)
            {
                return value;
            }

            // The gateway also accepts a boolean flag meaning "quantity is dollars", and it
            // accepts more spellings than bool.TryParse does. Recognizing fewer of them than
            // the gateway is a silent bypass: "notional=yes" on a 100,000-quantity order in
            // a $0.01 symbol routes $100,000 while the rails measure $1,000 of shares.
            if (IsTrue(raw))
            {
                return Math.Abs(quantity);
            }

            // The first non-blank alias is the only one the gateway consults: its
            // ReadMetadataString returns on that alias whether or not the value parses.
            // Falling through to a later alias here would read a number the gateway never
            // sees — "notional=false, alpaca:notional=1" on 100,000 shares would be
            // measured as a $1 order while Alpaca routes all 100,000 via Qty. This order
            // is quantity-sized; say so.
            return null;
        }

        return null;
    }

    /// <summary>
    /// Boolean spellings the brokerage gateways accept for a "quantity is dollars" flag.
    /// Kept in step with <c>AlpacaBrokerageGateway.ReadMetadataBool</c>.
    /// </summary>
    private static bool IsTrue(string raw)
    {
        var normalized = raw.Trim().ToLowerInvariant();
        return bool.TryParse(normalized, out var parsed)
            ? parsed
            : normalized is "1" or "yes" or "y";
    }

    private static string? TryReadValue(IReadOnlyDictionary<string, string> metadata, string key)
    {
        if (metadata.TryGetValue(key, out var direct))
        {
            return direct;
        }

        foreach (var (candidateKey, value) in metadata)
        {
            if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }
}
