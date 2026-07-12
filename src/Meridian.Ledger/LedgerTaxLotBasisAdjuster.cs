namespace Meridian.Ledger;

/// <summary>
/// Applies reference-data-derived <see cref="LedgerTaxLotBasisAdjustment"/>s to open tax lots,
/// producing the effective lots the relief engine relieves against. This is the seam that lets
/// day-count (amortized cost), factor (pool paydown), and corporate-action (split, return of
/// capital) economics from the Security Master flow into cost-basis accounting.
/// </summary>
public static class LedgerTaxLotBasisAdjuster
{
    /// <summary>
    /// Returns <paramref name="openLots"/> restated by <paramref name="adjustments"/>. Adjustments
    /// are applied per lot in <see cref="LedgerTaxLotBasisAdjustment.EffectiveDate"/> order; a lot
    /// whose quantity is reduced to zero or below (a full factor paydown or reverse split) is
    /// dropped from the effective set. When <paramref name="adjustments"/> is empty the original
    /// lots are returned unchanged.
    /// </summary>
    public static IReadOnlyList<LedgerTaxLot> Apply(
        IReadOnlyList<LedgerTaxLot> openLots,
        IReadOnlyList<LedgerTaxLotBasisAdjustment> adjustments)
    {
        ArgumentNullException.ThrowIfNull(openLots);
        ArgumentNullException.ThrowIfNull(adjustments);

        if (adjustments.Count == 0)
            return openLots;

        var ordered = adjustments
            .Select(static adjustment => adjustment.EnsureValid())
            .OrderBy(static adjustment => adjustment.EffectiveDate)
            .ThenBy(static adjustment => adjustment.Kind)
            .ThenBy(static adjustment => adjustment.Reference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var effective = new List<LedgerTaxLot>(openLots.Count);
        foreach (var lot in openLots)
        {
            var quantity = lot.Quantity;
            var unitCost = lot.UnitCost;

            foreach (var adjustment in ordered)
            {
                if (!adjustment.AppliesTo(lot))
                    continue;

                (quantity, unitCost) = ApplyOne(adjustment, quantity, unitCost);
            }

            quantity = RoundQuantity(quantity);
            if (quantity <= 0m)
                continue; // Fully paid down / reverse-split to nothing; no basis remains to relieve.

            effective.Add(new LedgerTaxLot(
                lot.LotId,
                lot.AcquiredDate,
                quantity,
                RoundUnitCost(Math.Max(0m, unitCost)),
                lot.SecurityId));
        }

        return effective;
    }

    private static (decimal quantity, decimal unitCost) ApplyOne(
        LedgerTaxLotBasisAdjustment adjustment,
        decimal quantity,
        decimal unitCost)
        => adjustment.Kind switch
        {
            // Split: total basis is preserved (quantity × unit cost is invariant).
            LedgerTaxLotBasisAdjustmentKind.Split => (quantity * adjustment.Value, unitCost / adjustment.Value),

            // Return of capital reduces per-unit basis, floored at zero.
            LedgerTaxLotBasisAdjustmentKind.ReturnOfCapital => (quantity, Math.Max(0m, unitCost - adjustment.Value)),

            // Factor paydown scales remaining face; per-unit basis is unchanged, so the paid-down
            // principal is relieved from basis in proportion to the factor reduction.
            LedgerTaxLotBasisAdjustmentKind.Factor => (quantity * adjustment.Value, unitCost),

            // Premium amortization (negative) / discount accretion (positive) moves basis toward par.
            LedgerTaxLotBasisAdjustmentKind.Amortization => (quantity, Math.Max(0m, unitCost + adjustment.Value)),

            _ => throw new ArgumentOutOfRangeException(nameof(adjustment), adjustment.Kind, "Unsupported basis-adjustment kind."),
        };

    private static decimal RoundQuantity(decimal quantity)
        => decimal.Round(quantity, 10, MidpointRounding.AwayFromZero);

    private static decimal RoundUnitCost(decimal unitCost)
        => decimal.Round(unitCost, 10, MidpointRounding.AwayFromZero);
}
