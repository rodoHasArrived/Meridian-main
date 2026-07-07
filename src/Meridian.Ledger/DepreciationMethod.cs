namespace Meridian.Ledger;

/// <summary>Depreciation method applied to a capitalized fixed asset.</summary>
public enum DepreciationMethod
{
    /// <summary>Equal depreciation charge each period across the useful life.</summary>
    StraightLine,

    /// <summary>
    /// Accelerated charge on the declining net book value, auto-switching to straight-line over the
    /// remaining life once straight-line yields the larger charge. Floored at salvage value.
    /// </summary>
    DecliningBalance,

    /// <summary>Charge proportional to units produced or consumed in the period.</summary>
    UnitsOfProduction,
}
