using Meridian.Contracts.Accounting.Lots;

namespace Meridian.Execution.Sdk;

/// <summary>Shadow cutover guard: execution identity/economics must agree with the retained canonical lot.</summary>
public static class CanonicalTaxLotAdapter
{
    public static OpenLotDto BindCanonical(this TaxLot execution, OpenLotDto retained)
    {
        ArgumentNullException.ThrowIfNull(execution);
        OpenLotValidation.Validate(retained);
        if (execution.IsShort)
            throw new InvalidOperationException("Short-lot canonical cutover requires an explicitly approved direction model.");
        if (execution.LotId != retained.TaxLotRecordId || execution.Quantity != retained.OpenQuantity
            || DateOnly.FromDateTime(execution.OpenedAt.UtcDateTime) != retained.AcquiredDate
            || retained.Acquisition.QuantityBasis != LotQuantityBasis.Units
            || execution.TotalCost != retained.OpenTransactionCostBasis)
            throw new InvalidOperationException("Execution lot identity, quantity, date, or basis differs from the retained canonical lot.");
        // Symbols are display evidence. A ticker rename cannot re-key a retained lot.
        return retained;
    }
}
