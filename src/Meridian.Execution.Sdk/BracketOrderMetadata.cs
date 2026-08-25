using System.Globalization;

namespace Meridian.Execution.Sdk;

/// <summary>
/// Reads the bracket/OCO child-limb prices — the take-profit limit, the stop-loss trigger, and
/// the optional stop-loss limit — from order metadata, using the same key aliases, precedence,
/// and parse rules <c>AlpacaBrokerageGateway.BuildOrderPayload</c> routes them with (kept in
/// step the way <see cref="BrokerNotionalMetadata"/> is with the notional keys). A pre-trade
/// control that gates these limbs must read them from the same place the gateway does, or the
/// gate measures one bracket while the broker works another.
/// </summary>
public static class BracketOrderMetadata
{
    /// <summary>Aliases for the take-profit leg's limit price, in gateway precedence order.</summary>
    public static IReadOnlyList<string> TakeProfitLimitKeys { get; } =
        ["take_profit.limit_price", "take_profit_limit_price", "alpaca:take_profit_limit_price"];

    /// <summary>Aliases for the stop-loss leg's trigger price, in gateway precedence order.</summary>
    public static IReadOnlyList<string> StopLossStopKeys { get; } =
        ["stop_loss.stop_price", "stop_loss_stop_price", "alpaca:stop_loss_stop_price"];

    /// <summary>Aliases for the stop-loss leg's limit price, in gateway precedence order.</summary>
    public static IReadOnlyList<string> StopLossLimitKeys { get; } =
        ["stop_loss.limit_price", "stop_loss_limit_price", "alpaca:stop_loss_limit_price"];

    /// <summary>The take-profit child leg's limit price, or <see langword="null"/> when the
    /// order carries none.</summary>
    public static decimal? TryReadTakeProfitLimit(IReadOnlyDictionary<string, string>? metadata) =>
        TryReadDecimal(metadata, TakeProfitLimitKeys);

    /// <summary>The stop-loss child leg's trigger price, or <see langword="null"/> when the
    /// order carries none.</summary>
    public static decimal? TryReadStopLossStop(IReadOnlyDictionary<string, string>? metadata) =>
        TryReadDecimal(metadata, StopLossStopKeys);

    /// <summary>The stop-loss child leg's limit price (a stop-limit exit), or
    /// <see langword="null"/> when the order carries none.</summary>
    public static decimal? TryReadStopLossLimit(IReadOnlyDictionary<string, string>? metadata) =>
        TryReadDecimal(metadata, StopLossLimitKeys);

    private static decimal? TryReadDecimal(
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyList<string> keys)
    {
        if (metadata is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            // The gateway scans each alias exactly then OrdinalIgnoreCase, skips blank values,
            // and moves to the next alias only when this one is absent or blank. Match how the
            // value is actually consumed.
            var raw = TryReadValue(metadata, key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            // First non-blank alias is the only one the gateway parses, with these exact
            // NumberStyles; a value it cannot parse routes no child leg there, so it reads as
            // no limb here — the two seams cannot disagree about whether a leg exists.
            return decimal.TryParse(
                raw,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : null;
        }

        return null;
    }

    private static string? TryReadValue(IReadOnlyDictionary<string, string> metadata, string key)
    {
        if (metadata.TryGetValue(key, out var direct) && !string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        foreach (var (candidateKey, value) in metadata)
        {
            if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
