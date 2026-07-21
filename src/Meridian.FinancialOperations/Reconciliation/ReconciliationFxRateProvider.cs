namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Reconciliation-scoped FX seam. Returns the conversion rate from one currency to another as of a
/// business date, or <c>false</c> when no rate is known. The matcher relies on the false result to
/// <em>fail closed</em>: a cross-currency line with no available rate stays in its original currency
/// and surfaces as a break rather than being silently (mis)matched against a base-currency balance.
/// </summary>
/// <remarks>
/// This lives inside the financial-operations layer so the reconciliation pipeline does not take a
/// dependency on the execution layer's async <c>IFxRateProvider</c>. The two seams are intentionally
/// distinct: execution/ledger translation is async and quote-timestamped, while reconciliation needs
/// a synchronous, fail-closed lookup during row mapping.
/// </remarks>
public interface IReconciliationFxRateProvider
{
    bool TryGetRate(string fromCurrency, string toCurrency, DateOnly asOf, out decimal rate);
}

/// <summary>Conversion helpers layered over <see cref="IReconciliationFxRateProvider"/>.</summary>
public static class ReconciliationFxRateProviderExtensions
{
    /// <summary>
    /// Converts <paramref name="amount"/> from <paramref name="fromCurrency"/> to
    /// <paramref name="toCurrency"/>. Returns <c>true</c> with the converted amount when a rate is
    /// available; otherwise returns <c>false</c> and echoes the original amount unchanged so callers
    /// can keep the line in its source currency.
    /// </summary>
    public static bool TryConvert(
        this IReconciliationFxRateProvider provider,
        decimal amount,
        string fromCurrency,
        string toCurrency,
        DateOnly asOf,
        out decimal converted)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider.TryGetRate(fromCurrency, toCurrency, asOf, out var rate))
        {
            converted = decimal.Round(amount * rate, 6, MidpointRounding.ToEven);
            return true;
        }

        converted = amount;
        return false;
    }
}

/// <summary>
/// The safe default: only a currency against itself resolves (rate <c>1</c>). Every genuine
/// cross-currency conversion returns <c>false</c>, so a deployment that has not configured FX rates
/// reconciles same-currency lines exactly and fails closed on everything else.
/// </summary>
public sealed class IdentityReconciliationFxRateProvider : IReconciliationFxRateProvider
{
    public static IdentityReconciliationFxRateProvider Instance { get; } = new();

    public bool TryGetRate(string fromCurrency, string toCurrency, DateOnly asOf, out decimal rate)
    {
        if (!string.IsNullOrWhiteSpace(fromCurrency)
            && string.Equals(fromCurrency.Trim(), toCurrency?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            rate = 1m;
            return true;
        }

        rate = 0m;
        return false;
    }
}

/// <summary>A single directional FX quote used to seed <see cref="TableReconciliationFxRateProvider"/>.</summary>
public sealed record ReconciliationFxQuote(string FromCurrency, string ToCurrency, decimal Rate);

/// <summary>
/// A deterministic table-backed provider. Resolves a pair by identity, direct quote, inverse quote,
/// or triangulation through an optional pivot currency. Rates are treated as constant across dates
/// (the <c>asOf</c> argument is accepted for interface parity but not used to select between quotes),
/// which keeps deployment-seeded reconciliation rates simple and reproducible.
/// </summary>
public sealed class TableReconciliationFxRateProvider : IReconciliationFxRateProvider
{
    private readonly IReadOnlyDictionary<(string From, string To), decimal> _rates;
    private readonly string? _pivotCurrency;

    public TableReconciliationFxRateProvider(IEnumerable<ReconciliationFxQuote> quotes, string? pivotCurrency = null)
    {
        ArgumentNullException.ThrowIfNull(quotes);
        var map = new Dictionary<(string, string), decimal>();
        foreach (var quote in quotes)
        {
            if (quote is null || quote.Rate == 0m
                || string.IsNullOrWhiteSpace(quote.FromCurrency)
                || string.IsNullOrWhiteSpace(quote.ToCurrency))
            {
                continue;
            }

            map[(Normalize(quote.FromCurrency), Normalize(quote.ToCurrency))] = quote.Rate;
        }

        _rates = map;
        _pivotCurrency = string.IsNullOrWhiteSpace(pivotCurrency) ? null : Normalize(pivotCurrency);
    }

    public bool TryGetRate(string fromCurrency, string toCurrency, DateOnly asOf, out decimal rate)
    {
        if (string.IsNullOrWhiteSpace(fromCurrency) || string.IsNullOrWhiteSpace(toCurrency))
        {
            rate = 0m;
            return false;
        }

        return TryResolve(Normalize(fromCurrency), Normalize(toCurrency), out rate);
    }

    private bool TryResolve(string from, string to, out decimal rate)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            rate = 1m;
            return true;
        }

        if (TryDirectOrInverse(from, to, out rate))
        {
            return true;
        }

        if (_pivotCurrency is { } pivot
            && !string.Equals(pivot, from, StringComparison.Ordinal)
            && !string.Equals(pivot, to, StringComparison.Ordinal)
            && TryDirectOrInverse(from, pivot, out var firstLeg)
            && TryDirectOrInverse(pivot, to, out var secondLeg))
        {
            rate = firstLeg * secondLeg;
            return true;
        }

        rate = 0m;
        return false;
    }

    private bool TryDirectOrInverse(string from, string to, out decimal rate)
    {
        if (_rates.TryGetValue((from, to), out rate))
        {
            return true;
        }

        if (_rates.TryGetValue((to, from), out var inverse) && inverse != 0m)
        {
            rate = 1m / inverse;
            return true;
        }

        rate = 0m;
        return false;
    }

    private static string Normalize(string currency) => currency.Trim().ToUpperInvariant();
}
