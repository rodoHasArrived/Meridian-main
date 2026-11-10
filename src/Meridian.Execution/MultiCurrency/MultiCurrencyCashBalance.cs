namespace Meridian.Execution.MultiCurrency;

/// <summary>
/// A multi-currency cash balance that tracks cash amounts per ISO 4217 currency code.
/// </summary>
/// <remarks>
/// Used by execution contexts that need to manage holdings in multiple currencies (e.g.,
/// a global equity portfolio that settles trades in USD, EUR, GBP, and JPY simultaneously).
/// The balance is thread-safe for concurrent reads after construction; mutations require
/// the caller to ensure exclusive access or use a copy-on-write pattern.
/// </remarks>
public sealed class MultiCurrencyCashBalance
{
    private readonly Dictionary<string, decimal> _balances;

    /// <summary>
    /// Initialises an empty balance.
    /// </summary>
    public MultiCurrencyCashBalance() => _balances = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initialises the balance from an existing dictionary of currency → amount pairs.
    /// </summary>
    public MultiCurrencyCashBalance(IReadOnlyDictionary<string, decimal> initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _balances = new(initial, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns all currencies that have a non-zero balance.</summary>
    public IReadOnlyDictionary<string, decimal> Balances => _balances;

    /// <summary>
    /// Gets the balance for <paramref name="currency"/>; returns <c>0</c> when not present.
    /// </summary>
    public decimal Get(string currency) =>
        _balances.TryGetValue(currency, out var amount) ? amount : 0m;

    /// <summary>
    /// Adds <paramref name="amount"/> to the balance for <paramref name="currency"/>.
    /// </summary>
    public void Add(string currency, decimal amount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        var key = currency.ToUpperInvariant();
        _balances[key] = _balances.GetValueOrDefault(key) + amount;
    }

    /// <summary>
    /// Subtracts <paramref name="amount"/> from the balance for <paramref name="currency"/>.
    /// </summary>
    public void Subtract(string currency, decimal amount) => Add(currency, -amount);

    /// <summary>
    /// Converts all currency balances to a single <paramref name="baseCurrency"/> total using
    /// the provided <paramref name="fxRates"/> dictionary (keyed by currency, valued as rate to
    /// base). The base currency itself converts at 1:1 when no rate is supplied for it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A non-zero balance is held in a currency that has no rate. Treating a missing rate as 1:1
    /// would silently misstate the total (1,000,000 JPY is not 1,000,000 USD), so the conversion
    /// fails instead.
    /// </exception>
    public decimal ToBaseCurrency(IReadOnlyDictionary<string, decimal> fxRates, string baseCurrency)
    {
        ArgumentNullException.ThrowIfNull(fxRates);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);

        var total = 0m;
        foreach (var (currency, amount) in _balances)
        {
            if (amount == 0m)
                continue;

            if (TryGetRate(fxRates, currency, out var rate))
            {
                total += amount * rate;
            }
            else if (string.Equals(currency, baseCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                total += amount;
            }
            else
            {
                throw new InvalidOperationException(
                    $"No FX rate to {baseCurrency.Trim().ToUpperInvariant()} was supplied for the {currency} cash balance.");
            }
        }

        return total;
    }

    private static bool TryGetRate(IReadOnlyDictionary<string, decimal> fxRates, string currency, out decimal rate)
    {
        if (fxRates.TryGetValue(currency, out rate))
            return true;

        // Balances are keyed upper-case; tolerate a caller-supplied map with different casing.
        foreach (var (key, value) in fxRates)
        {
            if (string.Equals(key, currency, StringComparison.OrdinalIgnoreCase))
            {
                rate = value;
                return true;
            }
        }

        rate = 0m;
        return false;
    }

    /// <summary>Returns a snapshot copy of the current balances.</summary>
    public IReadOnlyDictionary<string, decimal> Snapshot() =>
        new Dictionary<string, decimal>(_balances, StringComparer.OrdinalIgnoreCase);
}
