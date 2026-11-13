using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Derives interest entitlements without rewriting the immutable lot/mutation facts.</summary>
internal static class HistoricalTaxLotAccrual
{
    public static IReadOnlyList<FaceValueAccrualHolding> Project(
        LedgerTaxLotRecord lot,
        IReadOnlyList<DatedTaxLotQuantityMutation> history,
        DateOnly from,
        DateOnly through,
        CancellationToken ct = default)
    {
        if (through < from)
            throw new ArgumentException("An accrual range cannot end before it starts.", nameof(through));

        // Validate the complete chain before returning any historical entitlement. This also
        // validates mutations after the requested date and refuses unexplained current factors.
        _ = HistoricalTaxLotQuantity.Project(lot, history, through);
        if (lot.Account.AccountType != LedgerAccountType.Asset || lot.Acquisition is { Direction: not OpenLotDirection.Long })
            throw new LedgerValidationException("Retained accrual supports long asset lots only.");
        var ordered = history.OrderBy(static mutation => mutation.ResultVersion).ToArray();
        var version = lot.OriginatingMutationBatchId.HasValue ? 0L : 1L;
        foreach (var mutation in ordered)
        {
            if (mutation.ExpectedVersion != version)
                throw new LedgerValidationException("Retained accrual requires complete consecutive lot mutation history.");
            version = mutation.ResultVersion;
            if (mutation.EffectiveDate <= through && mutation.Kind is AtomicTaxLotMutationKind.CapitalReturn
                or AtomicTaxLotMutationKind.BasisRedistribution or AtomicTaxLotMutationKind.Amortization)
                throw new LedgerValidationException("Retained accrual does not model the lot's historical basis restatement.");
        }
        if (version != lot.Version || lot.BasisAdjustment is { } adjustment
            && !history.Any(mutation => mutation.MutationBatchId == adjustment.MutationBatchId
                && adjustment.Reason == (mutation.Kind switch
                {
                    AtomicTaxLotMutationKind.BasisRedistribution => OpenLotBasisAdjustmentReasons.AverageCostRedistribution,
                    AtomicTaxLotMutationKind.Amortization => OpenLotBasisAdjustmentReasons.Amortization,
                    AtomicTaxLotMutationKind.CorporateAction => OpenLotBasisAdjustmentReasons.CorporateActionSuccessor,
                    AtomicTaxLotMutationKind.CapitalReturn => OpenLotBasisAdjustmentReasons.CapitalReturn,
                    AtomicTaxLotMutationKind.FactorPaydown => OpenLotBasisAdjustmentReasons.FactorPaydown,
                    AtomicTaxLotMutationKind.Disposal => OpenLotBasisAdjustmentReasons.DisposalRelief,
                    _ => null
                })))
            throw new LedgerValidationException("Retained accrual cannot establish the lot's complete basis history.");
        if (lot.AcquiredDate > through)
            return [];

        var start = lot.AcquiredDate > from ? lot.AcquiredDate : from;
        var before = start == DateOnly.MinValue
            ? lot with { OpenQuantity = 0m, CurrentFactor = null }
            : HistoricalTaxLotQuantity.Project(lot, history, start.AddDays(-1));
        var quantity = before.OpenQuantity;
        var factor = before.CurrentFactor;
        var events = history.Where(mutation => mutation.EffectiveDate >= start && mutation.EffectiveDate <= through)
            .OrderBy(static mutation => mutation.EffectiveDate)
            .ThenBy(static mutation => mutation.ResultVersion)
            .ToLookup(static mutation => mutation.EffectiveDate);
        var snapshots = new List<FaceValueAccrualHolding>();

        for (var date = start; ; date = date.AddDays(1))
        {
            ct.ThrowIfCancellationRequested();
            var accrualFactor = factor ?? lot.BookedFactor;
            // A pristine legacy acquisition has no opening mutation; it opens on its actual date.
            if (date == lot.AcquiredDate && lot.OriginatingMutationBatchId is null)
                quantity = lot.OriginalQuantity;
            var factorClosingQuantity = 0m;
            foreach (var mutation in events[date])
            {
                quantity += mutation.Delta;
                if (mutation.Kind == AtomicTaxLotMutationKind.FactorPaydown)
                {
                    // Only a full paydown changes quantity. Restore that close for this day's
                    // interest, after preserving any same-day disposal's quantity reduction.
                    factorClosingQuantity -= mutation.Delta;
                    factor = mutation.CurrentFactorAfter;
                }
                else if (mutation.Kind == AtomicTaxLotMutationKind.Acquisition
                    || mutation.Kind == AtomicTaxLotMutationKind.CorporateAction && mutation.Delta > 0m)
                {
                    factor = mutation.CurrentFactorAfter;
                    if (date == lot.AcquiredDate)
                        accrualFactor = factor ?? lot.BookedFactor;
                }
            }

            var accrualQuantity = quantity + factorClosingQuantity;
            if (quantity < 0m || quantity > lot.OriginalQuantity || accrualQuantity < 0m || accrualQuantity > lot.OriginalQuantity)
                throw new LedgerValidationException($"Historical accrual for lot '{lot.LotId}' has inconsistent retained quantities.");
            if (accrualQuantity > 0m)
            {
                var face = (lot with { OpenQuantity = quantity, CurrentFactor = factor }).ToFaceValueLot()
                    ?? throw new LedgerValidationException($"Lot '{lot.LotId}' did not retain the face terms required for accrual.");
                snapshots.Add(new(date, face, accrualQuantity / lot.OriginalQuantity,
                    accrualFactor ?? throw new LedgerValidationException($"Lot '{lot.LotId}' has no retained accrual factor."),
                    lot.TaxLotRecordId, lot.Version, lot.Acquisition?.FaceValueTerms?.AmortizationMethod));
            }

            if (date == through)
                break;
        }
        return snapshots;
    }
}
