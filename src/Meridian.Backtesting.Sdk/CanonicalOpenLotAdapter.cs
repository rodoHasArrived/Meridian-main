using Meridian.Contracts.Accounting.Lots;

namespace Meridian.Backtesting.Sdk;

/// <summary>Validates backtest/ledger parity without inventing Security Master or acquisition FX facts.</summary>
public static class CanonicalOpenLotAdapter
{
    public static OpenLotDto BindCanonical(this OpenLot simulated, OpenLotDto retained)
    {
        ArgumentNullException.ThrowIfNull(simulated);
        OpenLotValidation.Validate(retained);
        if (simulated.IsShort || retained.Acquisition.Direction != OpenLotDirection.Long)
            throw new InvalidOperationException("Short-lot canonical cutover requires direction-aware posting and certified consumer parity.");
        ValidateComponents(simulated, retained);
        if (simulated.LotId != retained.TaxLotRecordId || simulated.Quantity != retained.OpenQuantity
            || DateOnly.FromDateTime(simulated.OpenedAt.UtcDateTime) != retained.AcquiredDate
            || retained.Acquisition.QuantityBasis != LotQuantityBasis.Units
            || simulated.CostBasis() != retained.OpenTransactionCostBasis)
            throw new InvalidOperationException("Backtest lot identity, quantity, date, or basis differs from the retained canonical lot.");
        return retained;
    }

    private static void ValidateComponents(OpenLot simulated, OpenLotDto retained)
    {
        var components = simulated.BasisComponents;
        if (components.Count == 0)
            return;

        const string error = "Backtest lot component quantity, provenance, or allocated basis differs from the retained canonical successor.";
        var origin = retained.Acquisition.CorporateActionLineage;
        if (origin is null || retained.Acquisition.QuantityBasis != LotQuantityBasis.Units
            || retained.OpenQuantity <= 0 || simulated.Quantity != retained.OpenQuantity)
            throw new InvalidOperationException(error);

        decimal quantity = 0m, basis = 0m;
        foreach (var component in components)
        {
            // The canonical contract certifies one immediate predecessor, not flattened ancestors
            // or additional sources. Source fill identity can only be checked for consistency.
            if (component is null || component.SourceLotId != origin.PredecessorTaxLotRecordId
                || component.SourceOpenFillId == Guid.Empty
                || component.SourceOpenFillId != components[0].SourceOpenFillId
                || component.OpenedAt != components[0].OpenedAt
                || DateOnly.FromDateTime(component.OpenedAt.UtcDateTime) != retained.AcquiredDate
                || component.SuccessorQuantity <= 0 || component.SuccessorQuantity > retained.OpenQuantity - quantity
                || component.AllocatedBasis < 0 || component.AllocatedBasis > retained.OpenTransactionCostBasis - basis)
                throw new InvalidOperationException(error);
            quantity += component.SuccessorQuantity;
            basis += component.AllocatedBasis;
        }
        // These homogeneous components represent one canonical source pool. The retained lot
        // certifies its totals, not a per-component allocation recipe; preserve simulation's
        // exact decimal residuals through fractional allocation and later partial relief.
        if (quantity != simulated.Quantity || basis != retained.OpenTransactionCostBasis)
            throw new InvalidOperationException(error);
    }
}
