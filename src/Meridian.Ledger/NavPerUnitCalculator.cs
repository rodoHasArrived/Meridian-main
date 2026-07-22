using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>
/// Converts between total class NAV, units outstanding, NAV per unit, subscription cash, and units.
/// Units round to 6 decimal places; NAV per unit rounds to 6 decimal places; cash rounds to 2.
/// </summary>
public static class NavPerUnitCalculator
{
    /// <summary>Decimal places retained for unit quantities.</summary>
    public const int UnitDecimals = 6;

    /// <summary>Decimal places retained for a NAV-per-unit price.</summary>
    public const int PriceDecimals = 6;

    /// <summary>
    /// Computes NAV per unit as total class NAV divided by units outstanding, falling back to
    /// <paramref name="inceptionNavPerUnit"/> when no units are outstanding yet.
    /// </summary>
    public static decimal ComputeNavPerUnit(decimal classNav, decimal unitsOutstanding, decimal inceptionNavPerUnit)
    {
        if (inceptionNavPerUnit <= 0m)
            throw new ArgumentOutOfRangeException(nameof(inceptionNavPerUnit), inceptionNavPerUnit, "Inception NAV per unit must be positive.");

        return unitsOutstanding <= 0m
            ? RoundPrice(inceptionNavPerUnit)
            : RoundPrice(classNav / unitsOutstanding);
    }

    /// <summary>Units issued for a subscription of <paramref name="amount"/> at <paramref name="navPerUnit"/>.</summary>
    public static decimal UnitsForSubscription(decimal amount, decimal navPerUnit)
    {
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Subscription amount cannot be negative.");
        if (navPerUnit <= 0m)
            throw new ArgumentOutOfRangeException(nameof(navPerUnit), navPerUnit, "NAV per unit must be positive.");

        return RoundUnits(amount / navPerUnit);
    }

    /// <summary>Cash paid for a redemption of <paramref name="units"/> at <paramref name="navPerUnit"/>.</summary>
    public static decimal CashForRedemption(decimal units, decimal navPerUnit)
    {
        if (units < 0m)
            throw new ArgumentOutOfRangeException(nameof(units), units, "Redemption units cannot be negative.");
        if (navPerUnit <= 0m)
            throw new ArgumentOutOfRangeException(nameof(navPerUnit), navPerUnit, "NAV per unit must be positive.");

        return RoundCurrency(units * navPerUnit);
    }

    public static decimal RoundUnits(decimal units) => Math.Round(units, UnitDecimals, MidpointRounding.ToEven);

    public static decimal RoundPrice(decimal price) => Math.Round(price, PriceDecimals, MidpointRounding.ToEven);
}
