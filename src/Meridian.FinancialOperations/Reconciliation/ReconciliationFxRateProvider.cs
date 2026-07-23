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

/// <summary>
/// A single directional FX quote effective as of a business date, used to seed
/// <see cref="TableReconciliationFxRateProvider"/>.
/// </summary>
public sealed record ReconciliationFxQuote(string FromCurrency, string ToCurrency, decimal Rate, DateOnly AsOf);

/// <summary>
/// A deterministic table-backed provider. Resolves a pair by identity, direct quote, inverse quote,
/// or triangulation through an optional pivot currency. Quotes are date-effective: for a requested
/// <c>asOf</c> the most recent quote at or before that date is used, and a pair whose quotes are all
/// later than <c>asOf</c> resolves to no rate so a backdated run fails closed rather than converting a
/// historical statement at a later (or today's) rate. Non-positive rates are rejected on construction.
/// </summary>
public sealed class TableReconciliationFxRateProvider : IReconciliationFxRateProvider
{
    private readonly IReadOnlyDictionary<(string From, string To), IReadOnlyList<(DateOnly AsOf, decimal Rate)>> _rates;
    private readonly string? _pivotCurrency;

    public TableReconciliationFxRateProvider(IEnumerable<ReconciliationFxQuote> quotes, string? pivotCurrency = null)
    {
        ArgumentNullException.ThrowIfNull(quotes);
        _rates = quotes
            .Where(static quote => quote is not null
                && quote.Rate > 0m
                && !string.IsNullOrWhiteSpace(quote.FromCurrency)
                && !string.IsNullOrWhiteSpace(quote.ToCurrency))
            .GroupBy(static quote => (Normalize(quote.FromCurrency), Normalize(quote.ToCurrency)))
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<(DateOnly AsOf, decimal Rate)>)group
                    .Select(static quote => (quote.AsOf, quote.Rate))
                    .OrderBy(static entry => entry.AsOf)
                    .ToArray());
        _pivotCurrency = string.IsNullOrWhiteSpace(pivotCurrency) ? null : Normalize(pivotCurrency);
    }

    public bool TryGetRate(string fromCurrency, string toCurrency, DateOnly asOf, out decimal rate)
    {
        if (string.IsNullOrWhiteSpace(fromCurrency) || string.IsNullOrWhiteSpace(toCurrency))
        {
            rate = 0m;
            return false;
        }

        return TryResolve(Normalize(fromCurrency), Normalize(toCurrency), asOf, out rate);
    }

    private bool TryResolve(string from, string to, DateOnly asOf, out decimal rate)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            rate = 1m;
            return true;
        }

        if (TryDirectOrInverse(from, to, asOf, out rate))
        {
            return true;
        }

        if (_pivotCurrency is { } pivot
            && !string.Equals(pivot, from, StringComparison.Ordinal)
            && !string.Equals(pivot, to, StringComparison.Ordinal)
            && TryDirectOrInverse(from, pivot, asOf, out var firstLeg)
            && TryDirectOrInverse(pivot, to, asOf, out var secondLeg))
        {
            rate = firstLeg * secondLeg;
            return true;
        }

        rate = 0m;
        return false;
    }

    private bool TryDirectOrInverse(string from, string to, DateOnly asOf, out decimal rate)
    {
        if (SelectAsOf(from, to, asOf, out rate))
        {
            return true;
        }

        if (SelectAsOf(to, from, asOf, out var inverse) && inverse != 0m)
        {
            rate = 1m / inverse;
            return true;
        }

        rate = 0m;
        return false;
    }

    // Prefer the most recent quote effective at or before asOf. When every quote for the pair is later
    // than asOf, report no rate: converting a backdated line at a future/today's rate would fabricate a
    // match or break, so the caller keeps the line in its source currency and surfaces it for review.
    private bool SelectAsOf(string from, string to, DateOnly asOf, out decimal rate)
    {
        rate = 0m;
        if (!_rates.TryGetValue((from, to), out var quotes) || quotes.Count == 0)
        {
            return false;
        }

        var found = false;
        foreach (var quote in quotes)
        {
            if (quote.AsOf <= asOf)
            {
                rate = quote.Rate;
                found = true;
            }
            else
            {
                break;
            }
        }

        return found;
    }

    private static string Normalize(string currency) => currency.Trim().ToUpperInvariant();
}
