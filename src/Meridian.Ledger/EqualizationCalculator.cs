using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>
/// Equalisation adjustment applied to a subscription so performance fees are borne fairly by
/// investors who subscribe at different points relative to the class high-water mark. Exactly one
/// of the two amounts is non-zero (or both zero at the high-water mark).
/// </summary>
public sealed record EqualizationAdjustment(
    decimal EqualisationCredit,
    decimal ContingentRedemption)
{
    /// <summary>Subscriber is above the high-water mark and prepaid performance fee to recover.</summary>
    public bool HasEqualisationCredit => EqualisationCredit > 0m;

    /// <summary>Subscriber is below the high-water mark and owes fee as the NAV recovers.</summary>
    public bool HasContingentRedemption => ContingentRedemption > 0m;
}

/// <summary>
/// Computes the single-NAV equalisation adjustment for a subscription. When the dealing NAV per
/// unit is above the class high-water mark, existing holders have already borne performance fee on
/// the appreciation, so the new subscriber receives an equalisation credit
/// (perfRate x (nav - highWater) per unit) they recover later. When the dealing NAV is below the
/// high-water mark, the subscriber would ride the recovery fee-free, so a contingent redemption
/// (perfRate x (highWater - nav) per unit) is tracked and collected as the NAV recovers. Pure and
/// deterministic.
/// </summary>
public static class EqualizationCalculator
{
    public static EqualizationAdjustment Compute(
        decimal navPerUnit,
        decimal highWaterNavPerUnit,
        decimal subscriptionUnits,
        decimal performanceFeeRate)
    {
        if (navPerUnit <= 0m)
            throw new ArgumentOutOfRangeException(nameof(navPerUnit), navPerUnit, "NAV per unit must be positive.");
        if (highWaterNavPerUnit < 0m)
            throw new ArgumentOutOfRangeException(nameof(highWaterNavPerUnit), highWaterNavPerUnit, "High-water NAV per unit cannot be negative.");
        if (subscriptionUnits < 0m)
            throw new ArgumentOutOfRangeException(nameof(subscriptionUnits), subscriptionUnits, "Subscription units cannot be negative.");
        if (performanceFeeRate < 0m || performanceFeeRate >= 1m)
            throw new ArgumentOutOfRangeException(nameof(performanceFeeRate), performanceFeeRate, "Performance fee rate must be in [0, 1).");

        if (performanceFeeRate == 0m || subscriptionUnits == 0m)
            return new EqualizationAdjustment(0m, 0m);

        if (navPerUnit > highWaterNavPerUnit)
        {
            var credit = RoundCurrency(performanceFeeRate * (navPerUnit - highWaterNavPerUnit) * subscriptionUnits);
            return new EqualizationAdjustment(credit, 0m);
        }

        if (navPerUnit < highWaterNavPerUnit)
        {
            var contingent = RoundCurrency(performanceFeeRate * (highWaterNavPerUnit - navPerUnit) * subscriptionUnits);
            return new EqualizationAdjustment(0m, contingent);
        }

        return new EqualizationAdjustment(0m, 0m);
    }
}
