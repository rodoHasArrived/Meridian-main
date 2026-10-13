using Meridian.Contracts.Accounting.Lots;

namespace Meridian.Backtesting.Sdk;

/// <summary>Validates backtest/ledger parity without inventing Security Master or acquisition FX facts.</summary>
public static class CanonicalOpenLotAdapter
{
    public static OpenLotDto BindCanonical(this OpenLot simulated, OpenLotDto retained)
    {
        ArgumentNullException.ThrowIfNull(simulated);
        OpenLotValidation.Validate(retained);
        if (simulated.IsShort)
            throw new InvalidOperationException("Short-lot canonical cutover requires an explicitly approved direction model.");
        if (simulated.LotId != retained.TaxLotRecordId || simulated.Quantity != retained.OpenQuantity
            || DateOnly.FromDateTime(simulated.OpenedAt.UtcDateTime) != retained.AcquiredDate
            || retained.Acquisition.QuantityBasis != LotQuantityBasis.Units
            || simulated.CostBasis() != retained.OpenTransactionCostBasis)
            throw new InvalidOperationException("Backtest lot identity, quantity, date, or basis differs from the retained canonical lot.");
        return retained;
    }
}
