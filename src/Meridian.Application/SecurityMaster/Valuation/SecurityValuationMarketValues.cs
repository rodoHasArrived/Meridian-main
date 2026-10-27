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
    /// <summary>
    /// The paydown factor the market value applies to a face-value quantity: the resolved terms'
    /// scalar current factor, or 1 when the terms carry none. A percent-of-par price must be quoted
    /// per 100 of the face this factor produces, so the factor is applied exactly once.
    /// </summary>
    public static decimal ResolvedFactor(SecurityDetailDto security)
    {
        ArgumentNullException.ThrowIfNull(security);
        return StructuredCashFlowTermsResolver.Resolve(security).CurrentFactor ?? 1m;
    }

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
                return SecurityCalculations.bondMarketValue(units, ResolvedFactor(security), perUnit);
            case SecurityPriceUnit.CurrencyPerUnit:
                return SecurityCalculations.equityMarketValue(units, perUnit);
            default:
                diagnostics.Add("MarketValueUnavailable: the price carries no quote unit, so quantity cannot be valued.");
                return null;
        }
    }
}
