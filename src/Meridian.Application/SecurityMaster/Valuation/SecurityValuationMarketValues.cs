using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Calculations;

namespace Meridian.Application.SecurityMaster.Valuation;

/// <summary>
/// Turns a per-unit price and a quantity in the class's lot unit into a market value with the F#
/// market-value functions, so no valuation model re-implements them: a percent-of-par price on
/// face value (par x factor x price / 100) and a currency price on units (units x price).
/// </summary>
internal static class SecurityValuationMarketValues
{
    /// <summary>
    /// Outstanding face relative to original principal on <paramref name="asOfDate"/>, including
    /// completed contractual payments not already reflected in the applicable factor evidence.
    /// Uses the same opening balance as the calculated cash-flow projection.
    /// </summary>
    public static decimal OutstandingFactorAsOf(SecurityDetailDto security, DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(security);
        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var principalBasis = terms.PrincipalFace is > 0m ? terms.PrincipalFace.Value : 100m;
        return SecurityPrincipalBalance.OpeningOutstanding(security, terms, asOfDate, roundToCashPrecision: false) / principalBasis;
    }

    /// <summary>
    /// The market value of <paramref name="quantity"/> at <paramref name="price"/>. A percent-of-par
    /// price is per 100 of current face, so the face-value quantity is scaled by
    /// <paramref name="factor"/> when the model knows the factor its price was quoted on, and
    /// otherwise by <see cref="OutstandingFactorAsOf"/> on <paramref name="asOfDate"/>; the factor is applied once.
    /// </summary>
    public static decimal? Compute(
        SecurityDetailDto security,
        decimal? quantity,
        decimal? price,
        SecurityPriceUnit unit,
        DateOnly asOfDate,
        ICollection<string> diagnostics,
        decimal? factor = null)
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
                return SecurityCalculations.bondMarketValue(units, factor ?? OutstandingFactorAsOf(security, asOfDate), perUnit);
            case SecurityPriceUnit.CurrencyPerUnit:
                return SecurityCalculations.equityMarketValue(units, perUnit);
            default:
                diagnostics.Add("MarketValueUnavailable: the price carries no quote unit, so quantity cannot be valued.");
                return null;
        }
    }
}
