namespace Meridian.Ledger;

/// <summary>
/// Pure fixed-asset depreciation schedule generator. Every method depreciates gross cost down to
/// salvage value, rounds each period charge to two decimal places, and absorbs any rounding
/// remainder in the final period so the closing net book value equals salvage value exactly.
/// </summary>
public sealed class DepreciationScheduleCalculator : IDepreciationScheduleCalculator
{
    private const int CurrencyScale = 2;

    /// <inheritdoc/>
    public IReadOnlyList<DepreciationPeriod> BuildSchedule(
        FixedAssetRecord asset,
        IReadOnlyList<long>? projectedUnitsPerPeriod = null)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (asset.UsefulLifeMonths <= 0)
            throw new ArgumentException("Useful life must be a positive number of months.", nameof(asset));
        if (asset.Cost < 0m)
            throw new ArgumentException("Asset cost cannot be negative.", nameof(asset));
        if (asset.SalvageValue < 0m)
            throw new ArgumentException("Salvage value cannot be negative.", nameof(asset));
        if (asset.SalvageValue > asset.Cost)
            throw new ArgumentException("Salvage value cannot exceed asset cost.", nameof(asset));

        return asset.Method switch
        {
            DepreciationMethod.StraightLine => BuildStraightLine(asset),
            DepreciationMethod.DecliningBalance => BuildDecliningBalance(asset),
            DepreciationMethod.UnitsOfProduction => BuildUnitsOfProduction(asset, projectedUnitsPerPeriod),
            _ => throw new ArgumentOutOfRangeException(nameof(asset), asset.Method, "Unsupported depreciation method."),
        };
    }

    private static IReadOnlyList<DepreciationPeriod> BuildStraightLine(FixedAssetRecord asset)
    {
        var months = asset.UsefulLifeMonths;
        var depreciable = asset.Cost - asset.SalvageValue;
        var perPeriod = Round(depreciable / months);

        var periods = new List<DepreciationPeriod>(months);
        var opening = asset.Cost;
        var accumulated = 0m;

        for (var i = 1; i <= months; i++)
        {
            var isLast = i == months;
            var charge = isLast ? depreciable - accumulated : perPeriod;
            if (charge < 0m)
                charge = 0m;
            if (!isLast && accumulated + charge > depreciable)
                charge = depreciable - accumulated;

            accumulated += charge;
            var closing = asset.Cost - accumulated;
            periods.Add(new DepreciationPeriod(i, PeriodEnd(asset.InServiceDate, i), opening, charge, closing));
            opening = closing;
        }

        return periods;
    }

    private static IReadOnlyList<DepreciationPeriod> BuildDecliningBalance(FixedAssetRecord asset)
    {
        if (asset.DecliningBalanceFactor <= 0m)
            throw new ArgumentException("Declining-balance factor must be positive.", nameof(asset));

        var months = asset.UsefulLifeMonths;
        var rate = asset.DecliningBalanceFactor / months;

        var periods = new List<DepreciationPeriod>(months);
        var opening = asset.Cost;

        for (var i = 1; i <= months; i++)
        {
            var remainingMonths = months - i + 1;
            var decliningCharge = Round(opening * rate);
            // Switch to straight-line over the remaining life once it yields the larger charge.
            var straightLineCharge = Round((opening - asset.SalvageValue) / remainingMonths);
            var charge = Math.Max(decliningCharge, straightLineCharge);

            // Never depreciate below salvage; the final period lands exactly on salvage value.
            if (i == months || opening - charge < asset.SalvageValue)
                charge = opening - asset.SalvageValue;
            if (charge < 0m)
                charge = 0m;

            var closing = opening - charge;
            periods.Add(new DepreciationPeriod(i, PeriodEnd(asset.InServiceDate, i), opening, charge, closing));
            opening = closing;
        }

        return periods;
    }

    private static IReadOnlyList<DepreciationPeriod> BuildUnitsOfProduction(
        FixedAssetRecord asset,
        IReadOnlyList<long>? projectedUnitsPerPeriod)
    {
        if (asset.TotalUnits is not { } totalUnits || totalUnits <= 0)
            throw new ArgumentException("Units-of-production depreciation requires a positive TotalUnits.", nameof(asset));
        if (projectedUnitsPerPeriod is null || projectedUnitsPerPeriod.Count == 0)
        {
            throw new ArgumentException(
                "Units-of-production depreciation requires projected units for each period.",
                nameof(projectedUnitsPerPeriod));
        }

        var depreciable = asset.Cost - asset.SalvageValue;
        var periods = new List<DepreciationPeriod>(projectedUnitsPerPeriod.Count);
        var opening = asset.Cost;
        var accumulated = 0m;
        long cumulativeUnits = 0;

        for (var i = 0; i < projectedUnitsPerPeriod.Count; i++)
        {
            var periodUnits = projectedUnitsPerPeriod[i];
            if (periodUnits < 0)
                throw new ArgumentException("Projected units cannot be negative.", nameof(projectedUnitsPerPeriod));

            cumulativeUnits = Math.Min(cumulativeUnits + periodUnits, totalUnits);
            // Track cumulative depreciation against cumulative usage so per-period rounding never
            // drifts and total depreciation is capped at the depreciable base.
            var targetAccumulated = Round(depreciable * cumulativeUnits / totalUnits);
            if (targetAccumulated > depreciable)
                targetAccumulated = depreciable;

            var charge = targetAccumulated - accumulated;
            if (charge < 0m)
                charge = 0m;

            accumulated = targetAccumulated;
            var closing = asset.Cost - accumulated;
            periods.Add(new DepreciationPeriod(i + 1, PeriodEnd(asset.InServiceDate, i + 1), opening, charge, closing));
            opening = closing;
        }

        return periods;
    }

    private static decimal Round(decimal value)
        => decimal.Round(value, CurrencyScale, MidpointRounding.AwayFromZero);

    /// <summary>Last calendar day of the month <paramref name="periodOrdinal"/> (1-based) into the life.</summary>
    private static DateOnly PeriodEnd(DateOnly inServiceDate, int periodOrdinal)
    {
        var monthStart = inServiceDate.AddMonths(periodOrdinal - 1);
        var daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        return new DateOnly(monthStart.Year, monthStart.Month, daysInMonth);
    }
}
