namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Canonical open lot for unit-denominated fund holdings (investment funds, money market funds,
/// unitized private vehicles), whose <see cref="SecurityAssetClassDescriptor.LotUnit"/> is
/// <see cref="SecurityLotUnitKind.Units"/>. The lot is a count of units and the NAV per unit they
/// were acquired at; cost basis is owned here so every consumer derives it identically.
/// </summary>
public sealed record FundUnitLot : IUnitizedLot
{
    public FundUnitLot(
        string lotId,
        Guid securityId,
        DateOnly acquiredDate,
        decimal units,
        decimal navPerUnit)
    {
        if (string.IsNullOrWhiteSpace(lotId))
            throw new ArgumentException("Fund unit lot identifier must not be null or whitespace.", nameof(lotId));
        if (securityId == Guid.Empty)
            throw new ArgumentException("Fund unit lots must be linked to a Security Master identity.", nameof(securityId));
        if (units <= 0m)
            throw new ArgumentOutOfRangeException(nameof(units), units, "Units must be positive.");
        if (navPerUnit < 0m)
            throw new ArgumentOutOfRangeException(nameof(navPerUnit), navPerUnit, "Acquisition NAV per unit cannot be negative.");

        LotId = lotId.Trim();
        SecurityId = securityId;
        AcquiredDate = acquiredDate;
        Units = units;
        NavPerUnit = navPerUnit;
    }

    public string LotId { get; }

    /// <summary>A fund unit lot is the unit-denominated case of the shared lot shape.</summary>
    decimal IUnitizedLot.Quantity => Units;

    SecurityLotUnitKind IUnitizedLot.UnitKind => SecurityLotUnitKind.Units;

    decimal IUnitizedLot.Multiplier => 1m;

    /// <summary>Security Master identity the lot's reference data (fund family, pricing cadence) resolves from.</summary>
    public Guid SecurityId { get; }

    public DateOnly AcquiredDate { get; }

    /// <summary>Units held; fractional units are ordinary for funds.</summary>
    public decimal Units { get; }

    /// <summary>NAV per unit the lot was acquired at, in the fund's currency.</summary>
    public decimal NavPerUnit { get; }

    public decimal CostBasis => Units * NavPerUnit;

    /// <summary>Value of the lot at <paramref name="navPerUnit"/>.</summary>
    public decimal ValueAt(decimal navPerUnit)
    {
        if (navPerUnit < 0m)
            throw new ArgumentOutOfRangeException(nameof(navPerUnit), navPerUnit, "NAV per unit cannot be negative.");

        return Units * navPerUnit;
    }

    /// <summary>The lot remaining after redeeming <paramref name="redeemedUnits"/>; cost basis scales with the units.</summary>
    public FundUnitLot WithReducedUnits(decimal redeemedUnits)
    {
        if (redeemedUnits <= 0m)
            throw new ArgumentOutOfRangeException(nameof(redeemedUnits), redeemedUnits, "Redeemed units must be positive.");
        if (redeemedUnits >= Units)
            throw new ArgumentOutOfRangeException(nameof(redeemedUnits), redeemedUnits, "Redeeming all units closes the lot; a reduced lot must keep some units.");

        return new FundUnitLot(LotId, SecurityId, AcquiredDate, Units - redeemedUnits, NavPerUnit);
    }
}
