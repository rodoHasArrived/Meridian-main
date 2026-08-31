using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Normalizes captured positions and cash entries onto the run's canonical axes: instrument
/// identifiers resolve through the mapping service, currency amounts convert through the same
/// fail-closed <see cref="IReconciliationFxRateProvider"/> seam the statement lane uses, and
/// accounting periods resolve through the business calendar from the entry's own posting
/// timestamp. When no FX rate is available the line keeps its source currency and amount — the
/// matcher's currency-identity guard then surfaces it as a break instead of silently comparing
/// incompatible currencies at a fabricated rate.
/// </summary>
public sealed class ReconciliationNormalizationService(
    IInstrumentMappingService instrumentMapping,
    IReconciliationFxRateProvider fxRateProvider,
    IAccountingCalendar accountingCalendar)
{
    public NormalizedPosition NormalizePosition(NormalizedPosition input, string baseCurrency, DateTimeOffset runTimestampUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        var canonical = instrumentMapping.ResolveCanonicalId(input.Cusip, input.Isin, input.Ticker, input.InternalSecurityId);
        // Convert at the position's own as-of date, not the run wall-clock: a backdated snapshot
        // must not revalue at today's rate.
        var converted = fxRateProvider.TryConvert(
            input.MarketValue,
            input.Currency,
            baseCurrency,
            DateOnly.FromDateTime(input.AsOfUtc.UtcDateTime),
            out var marketValueBase);
        return input with
        {
            InstrumentCanonicalId = canonical,
            MarketValue = converted ? marketValueBase : input.MarketValue,
            Currency = converted ? baseCurrency : input.Currency,
            AsOfUtc = runTimestampUtc
        };
    }

    public NormalizedCashEntry NormalizeCashEntry(NormalizedCashEntry input, string baseCurrency, DateTimeOffset runTimestampUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        var converted = fxRateProvider.TryConvert(
            input.Amount,
            input.Currency,
            baseCurrency,
            DateOnly.FromDateTime(input.PostedAtUtc.UtcDateTime),
            out var amountBase);
        // The posting timestamp is evidence — keep it. The accounting period derives from that
        // timestamp through the business calendar (weekend/holiday postings recognize in the next
        // business period), not from when the reconciliation run happened to execute.
        return input with
        {
            AmountBase = converted ? amountBase : input.Amount,
            BaseCurrency = converted ? baseCurrency : input.Currency,
            AccountingPeriod = accountingCalendar.ResolvePeriod(input.PostedAtUtc)
        };
    }
}
