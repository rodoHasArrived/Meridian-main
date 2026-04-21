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
    /// Converts all currency balances to a single base-currency total using the provided
    /// <paramref name="fxRates"/> dictionary (keyed by currency, valued as rate to base).
    /// Currencies with no rate are treated as 1:1 with the base currency (a safe fallback
    /// for the base currency itself).
    /// </summary>
    public decimal ToBaseCurrency(IReadOnlyDictionary<string, decimal> fxRates)
    {
        ArgumentNullException.ThrowIfNull(fxRates);
        return _balances.Sum(kvp =>
            kvp.Value * (fxRates.TryGetValue(kvp.Key, out var rate) ? rate : 1m));
    }

    /// <summary>Returns a snapshot copy of the current balances.</summary>
    public IReadOnlyDictionary<string, decimal> Snapshot() =>
        new Dictionary<string, decimal>(_balances, StringComparer.OrdinalIgnoreCase);
}
