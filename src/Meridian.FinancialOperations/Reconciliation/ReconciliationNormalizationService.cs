using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed class ReconciliationNormalizationService(
    IInstrumentMappingService instrumentMapping,
    IFxRateProvider fxRateProvider,
    IAccountingCalendar accountingCalendar)
{
    public NormalizedPosition NormalizePosition(NormalizedPosition input, string baseCurrency, DateTimeOffset runTimestampUtc)
    {
        var canonical = instrumentMapping.ResolveCanonicalId(input.Cusip, input.Isin, input.Ticker, input.InternalSecurityId);
        var fx = fxRateProvider.Convert(1m, input.Currency, baseCurrency, runTimestampUtc);
        return input with
        {
            InstrumentCanonicalId = canonical,
            MarketValue = decimal.Round(input.MarketValue * fx, 6),
            Currency = baseCurrency,
            AsOfUtc = runTimestampUtc
        };
    }

    public NormalizedCashEntry NormalizeCashEntry(NormalizedCashEntry input, string baseCurrency, DateTimeOffset runTimestampUtc)
    {
        var amountBase = fxRateProvider.Convert(input.Amount, input.Currency, baseCurrency, runTimestampUtc);
        return input with
        {
            AmountBase = decimal.Round(amountBase, 6),
            BaseCurrency = baseCurrency,
            PostedAtUtc = runTimestampUtc,
            AccountingPeriod = accountingCalendar.ResolvePeriod(runTimestampUtc)
        };
    }
}
