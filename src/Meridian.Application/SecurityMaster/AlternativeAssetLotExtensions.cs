using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Adapts the canonical commitment, fund-unit and notional lots to the <see cref="LedgerTaxLot"/>
/// shape the relief engines consume, the way <see cref="FaceValueLotExtensions"/> does for
/// face-value lots.
/// </summary>
public static class AlternativeAssetLotExtensions
{
    /// <summary>
    /// A fund unit lot is already in ledger shape: quantity is the unit count and unit cost is the
    /// acquisition NAV per unit, so quantity times unit cost reproduces <see cref="FundUnitLot.CostBasis"/>.
    /// </summary>
    public static LedgerTaxLot ToLedgerTaxLot(this FundUnitLot lot)
    {
        ArgumentNullException.ThrowIfNull(lot);

        return new LedgerTaxLot(lot.LotId, lot.AcquiredDate, lot.Units, lot.NavPerUnit, lot.SecurityId)
        {
            UnitKind = SecurityLotUnitKind.Units,
            Multiplier = 1m
        };
    }

    /// <summary>
    /// The ledger carries contributed capital, not the commitment: the relief engines relieve what
    /// was paid in, while the unfunded balance is an obligation the ledger projects separately. The
    /// ledger lot is therefore <see cref="SecurityLotUnitKind.CurrencyAmount"/> of
    /// <see cref="CommitmentLot.FundedAmount"/> at a unit cost of one, and an unfunded commitment has
    /// no ledger lot yet.
    /// </summary>
    public static LedgerTaxLot ToLedgerTaxLot(this CommitmentLot lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        if (lot.FundedAmount <= 0m)
            throw new InvalidOperationException($"Commitment lot '{lot.LotId}' has no contributed capital to carry as a ledger lot.");

        return new LedgerTaxLot(lot.LotId, lot.CommittedDate, lot.FundedAmount, unitCost: 1m, lot.SecurityId)
        {
            UnitKind = SecurityLotUnitKind.CurrencyAmount,
            Multiplier = 1m
        };
    }

    /// <summary>
    /// A notional lot's quantity is the notional outstanding and its only cost is the upfront
    /// payment, so the ledger lot carries that notional at a unit cost that reproduces
    /// <see cref="NotionalLot.CostBasis"/> (zero for a par contract). A fully amortized lot has no
    /// notional left to carry.
    /// </summary>
    public static LedgerTaxLot ToLedgerTaxLot(this NotionalLot lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        if (lot.CurrentNotional <= 0m)
            throw new InvalidOperationException($"Notional lot '{lot.LotId}' is fully amortized and has no notional to carry as a ledger lot.");

        return new LedgerTaxLot(
            lot.LotId,
            lot.EffectiveDate,
            lot.CurrentNotional,
            unitCost: lot.UpfrontPayment / lot.CurrentNotional,
            lot.SecurityId)
        {
            UnitKind = SecurityLotUnitKind.Notional,
            Multiplier = 1m
        };
    }
}
