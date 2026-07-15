namespace Meridian.Ledger;

/// <summary>Generates a period-by-period depreciation schedule for a fixed asset.</summary>
public interface IDepreciationScheduleCalculator
{
    /// <summary>
    /// Builds the depreciation schedule for <paramref name="asset"/>.
    /// <see cref="DepreciationMethod.StraightLine"/> and <see cref="DepreciationMethod.DecliningBalance"/>
    /// produce a full forward schedule from the asset record alone.
    /// <see cref="DepreciationMethod.UnitsOfProduction"/> is usage-driven and requires
    /// <paramref name="projectedUnitsPerPeriod"/> (projected or actual units for each period); it
    /// cannot be projected forward from the asset record alone.
    /// </summary>
    IReadOnlyList<DepreciationPeriod> BuildSchedule(
        FixedAssetRecord asset,
        IReadOnlyList<long>? projectedUnitsPerPeriod = null);
}
