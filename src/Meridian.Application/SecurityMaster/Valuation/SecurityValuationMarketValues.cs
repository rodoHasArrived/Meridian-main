using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Calculations;

namespace Meridian.Application.SecurityMaster.Valuation;

/// <summary>
/// Turns a per-unit price and a quantity in the class's lot unit into a market value with the F#
/// market-value functions, so no valuation model re-implements them: a percent-of-par price on
/// face value (par x factor x price / 100, the factor from the resolved terms) and a currency
/// price on units (units x price).
/// </summary>
internal static class SecurityValuationMarketValues
{
    public static decimal? Compute(
        SecurityDetailDto security,
        decimal? quantity,
        decimal? price,
        SecurityPriceUnit unit,
        ICollection<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (quantity is not { } units || price is not { } perUnit)
        {
            return null;
        }

        switch (unit)
        {
            case SecurityPriceUnit.PercentOfPar:
                var factor = StructuredCashFlowTermsResolver.Resolve(security).CurrentFactor ?? 1m;
                return SecurityCalculations.bondMarketValue(units, factor, perUnit);
            case SecurityPriceUnit.CurrencyPerUnit:
                return SecurityCalculations.equityMarketValue(units, perUnit);
            default:
                diagnostics.Add("MarketValueUnavailable: the price carries no quote unit, so quantity cannot be valued.");
                return null;
        }
    }
}
