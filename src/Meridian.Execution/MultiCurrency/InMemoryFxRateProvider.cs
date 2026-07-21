namespace Meridian.Execution.MultiCurrency;

/// <summary>
/// A deterministic, in-memory <see cref="IFxRateProvider"/> backed by a seeded set of
/// <see cref="FxRate"/> quotes. It resolves a requested pair through four strategies, in order:
/// <list type="number">
///   <item>identity — a currency against itself is always <c>1</c>;</item>
///   <item>direct — a seeded quote for the exact pair;</item>
///   <item>inverse — a seeded quote for the reversed pair, inverted;</item>
///   <item>triangulation — legs through a configured pivot currency (e.g. USD).</item>
/// </list>
/// Quotes are selected as-of a point in time: the most recent quote whose timestamp is at or
/// before the requested instant. When every quote for a pair post-dates the requested instant, no
/// rate was known as of that time and the provider reports none rather than a future quote, so
/// callers fail closed instead of leaking look-ahead information. The provider performs no network
/// I/O and no wall-clock reads, so results are fully reproducible for a given seed — a property the
/// reconciliation and ledger layers rely on.
/// </summary>
public sealed class InMemoryFxRateProvider : IFxRateProvider
{
    private readonly IReadOnlyDictionary<(string From, string To), IReadOnlyList<FxRate>> _quotesByPair;
    private readonly string? _pivotCurrency;

    /// <summary>
    /// Creates a provider from <paramref name="rates"/>. When <paramref name="pivotCurrency"/> is
    /// supplied, pairs with no direct or inverse quote are resolved by triangulating through it.
    /// </summary>
    public InMemoryFxRateProvider(IEnumerable<FxRate> rates, string? pivotCurrency = null)
    {
        ArgumentNullException.ThrowIfNull(rates);
        _quotesByPair = rates
            .Where(static rate => rate is not null
                && !string.IsNullOrWhiteSpace(rate.BaseCurrency)
                && !string.IsNullOrWhiteSpace(rate.QuoteCurrency))
            .GroupBy(static rate => (Normalize(rate.BaseCurrency), Normalize(rate.QuoteCurrency)))
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<FxRate>)group
                    .OrderBy(static rate => rate.AsOf)
                    .ToArray());
        _pivotCurrency = string.IsNullOrWhiteSpace(pivotCurrency) ? null : Normalize(pivotCurrency);
    }

    /// <inheritdoc />
    public ValueTask<FxRate?> GetRateAsync(
        string fromCurrency,
        string toCurrency,
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromCurrency);
        ArgumentException.ThrowIfNullOrWhiteSpace(toCurrency);
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Resolve(Normalize(fromCurrency), Normalize(toCurrency), asOf));
    }

    /// <inheritdoc />
    public async ValueTask<FxRate> GetRequiredRateAsync(
        string fromCurrency,
        string toCurrency,
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        var rate = await GetRateAsync(fromCurrency, toCurrency, asOf, ct).ConfigureAwait(false);
        return rate
            ?? throw new InvalidOperationException(
                $"No FX rate is available to convert {Normalize(fromCurrency)} to {Normalize(toCurrency)} as of {asOf:O}.");
    }

    private FxRate? Resolve(string from, string to, DateTimeOffset asOf)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return new FxRate(from, to, 1m, asOf);
        }

        if (ResolveDirectOrInverse(from, to, asOf) is { } direct)
        {
            return direct;
        }

        // Triangulate through the pivot: from -> pivot -> to. Only attempted when a pivot is
        // configured and it is not already one of the endpoints (which the direct/inverse pass
        // would have handled).
        if (_pivotCurrency is { } pivot
            && !string.Equals(pivot, from, StringComparison.Ordinal)
            && !string.Equals(pivot, to, StringComparison.Ordinal)
            && ResolveDirectOrInverse(from, pivot, asOf) is { } firstLeg
            && ResolveDirectOrInverse(pivot, to, asOf) is { } secondLeg)
        {
            var effectiveAsOf = firstLeg.AsOf <= secondLeg.AsOf ? firstLeg.AsOf : secondLeg.AsOf;
            return new FxRate(from, to, firstLeg.Rate * secondLeg.Rate, effectiveAsOf);
        }

        return null;
    }

    private FxRate? ResolveDirectOrInverse(string from, string to, DateTimeOffset asOf)
    {
        if (SelectAsOf(from, to, asOf) is { } directRate)
        {
            return new FxRate(from, to, directRate.Rate, directRate.AsOf);
        }

        if (SelectAsOf(to, from, asOf) is { Rate: not 0m } inverseRate)
        {
            return new FxRate(from, to, 1m / inverseRate.Rate, inverseRate.AsOf);
        }

        return null;
    }

    private FxRate? SelectAsOf(string from, string to, DateTimeOffset asOf)
    {
        if (!_quotesByPair.TryGetValue((from, to), out var quotes) || quotes.Count == 0)
        {
            return null;
        }

        // Quotes are pre-sorted ascending by AsOf. Return the most recent quote effective at or
        // before the requested instant. When the whole series is in the future, no rate was known as
        // of the requested instant, so report none rather than a future quote: honoring an as-of
        // request with a later rate would leak information unavailable at that time into backtests,
        // ledger translations, and reconciliations.
        FxRate? best = null;
        foreach (var quote in quotes)
        {
            if (quote.AsOf <= asOf)
            {
                best = quote;
            }
            else
            {
                break;
            }
        }

        return best;
    }

    private static string Normalize(string currency) => currency.Trim().ToUpperInvariant();
}
