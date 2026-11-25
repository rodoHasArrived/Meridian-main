using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Governed FX requires a recognized monetary currency with a defined minor-unit scale.</summary>
public static class ConsolidationFxCurrencyRules
{
    public static bool IsSupported(string? currency)
        => currency is { Length: 3 } && currency.All(c => c is >= 'A' and <= 'Z') &&
            CurrencyCodeCatalog.IsRecognized(currency) && currency is not
            ("XAG" or "XAU" or "XBA" or "XBB" or "XBC" or "XBD" or "XDR" or "XPD" or "XPT" or "XSU" or "XUA");

    public static void RequireSupported(string currency)
    {
        if (!IsSupported(currency))
            throw new ArgumentException("Governed FX requires a recognized monetary currency with a defined minor-unit scale.");
    }

    public static void Validate(ConsolidationFxRateSetDto value)
    {
        ConsolidationFxValidation.Validate(value);
        foreach (var rate in value.Rates)
        {
            RequireSupported(rate.FromCurrency);
            RequireSupported(rate.ToCurrency);
        }
    }

    public static void Validate(ConsolidationFxPolicyDto value)
    {
        ConsolidationFxValidation.Validate(value);
        RequireSupported(value.ReportingCurrency);
    }

    public static void Validate(ConsolidationFxBookConfigurationDto value)
    {
        ConsolidationFxValidation.Validate(value);
        Validate(value.Policy);
    }
}
