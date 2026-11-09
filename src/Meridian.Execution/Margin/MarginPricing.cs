using Meridian.Execution.Models;

namespace Meridian.Execution.Margin;

/// <summary>
/// Shared pricing guard for margin models.
/// </summary>
internal static class MarginPricing
{
    /// <summary>
    /// Position size in price units: quantity × contract multiplier. Prices are quoted per
    /// contract while the paper book moves cash by the multiplier, so a margin notional that
    /// ignored it would value one 100-multiplier option at a hundredth of the cash paid for it
    /// and report false margin calls.
    /// </summary>
    public static decimal EconomicQuantity(ExecutionPosition position)
        => position.ExactQuantity * (position.ContractMultiplier > 0m ? position.ContractMultiplier : 1m);

    /// <summary>
    /// Fails closed when an open position has no current price. Skipping the position would
    /// drop its requirement (and, for shorts, its liability) from the result, so a stale or
    /// partial price feed would understate margin risk instead of surfacing it.
    /// Flat (zero-quantity) positions need no price.
    /// </summary>
    /// <exception cref="MarginPriceUnavailableException">An open position has no price.</exception>
    public static void ThrowIfAnyUnpriced(
        IReadOnlyDictionary<string, ExecutionPosition> positions,
        IReadOnlyDictionary<string, decimal> prices)
    {
        List<string>? missing = null;
        foreach (var (symbol, position) in positions)
        {
            if (position.ExactQuantity != 0m && !prices.ContainsKey(symbol))
                (missing ??= []).Add(symbol);
        }

        if (missing is not null)
            throw new MarginPriceUnavailableException(missing);
    }
}

/// <summary>
/// Thrown when a margin requirement cannot be computed because one or more open positions
/// have no current price.
/// </summary>
public sealed class MarginPriceUnavailableException : InvalidOperationException
{
    public MarginPriceUnavailableException(IReadOnlyList<string> symbols)
        : base($"Cannot compute margin: no current price for open position(s) {string.Join(", ", symbols)}.")
    {
        Symbols = symbols;
    }

    /// <summary>Open-position symbols that had no price.</summary>
    public IReadOnlyList<string> Symbols { get; }
}
