namespace Meridian.Execution.MultiCurrency;

/// <summary>
/// Provides FX exchange rates on demand, enabling the execution and ledger layers
/// to translate foreign-currency cash flows into a base (reporting) currency.
/// </summary>
public interface IFxRateProvider
{
    /// <summary>
    /// Returns the exchange rate from <paramref name="fromCurrency"/> to
    /// <paramref name="toCurrency"/> as of <paramref name="asOf"/>.
    /// </summary>
    /// <param name="fromCurrency">ISO 4217 source currency code (e.g., "EUR").</param>
    /// <param name="toCurrency">ISO 4217 target currency code (e.g., "USD").</param>
    /// <param name="asOf">Point in time for the rate lookup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     A <see cref="FxRate"/> when a rate is available; <c>null</c> otherwise.
    /// </returns>
    ValueTask<FxRate?> GetRateAsync(
        string fromCurrency,
        string toCurrency,
        DateTimeOffset asOf,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the exchange rate from <paramref name="fromCurrency"/> to
    /// <paramref name="toCurrency"/> as of <paramref name="asOf"/>, or throws
    /// <see cref="InvalidOperationException"/> when no rate is available.
    /// </summary>
    ValueTask<FxRate> GetRequiredRateAsync(
        string fromCurrency,
        string toCurrency,
        DateTimeOffset asOf,
        CancellationToken ct = default);
}
