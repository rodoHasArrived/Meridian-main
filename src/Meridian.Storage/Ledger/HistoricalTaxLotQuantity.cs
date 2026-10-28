using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// One retained quantity mutation dated by its journal's effective date. <c>CurrentFactorAfter</c> is
/// the paydown factor the lot held after the mutation, read from the retained after-snapshot: null on
/// every row of a lot still at its booked factor, which is every row written before the factor
/// existed; stated on every row after a paydown, and on a successor's opening row when it inherits
/// its predecessor's factor.
/// </summary>
internal sealed record DatedTaxLotQuantityMutation(
    Guid TaxLotRecordId, Guid MutationBatchId, AtomicTaxLotMutationKind Kind,
    decimal Before, decimal Delta, decimal After, long ExpectedVersion, long ResultVersion,
    DateOnly EffectiveDate, Guid SecurityId, Guid BookPositionId, decimal? CurrentFactorAfter = null);

/// <summary>
/// Restates quantity and paydown factor using immutable journal effective dates, not mutation
/// recording time. Basis stays current: every other kind already reads the current open basis
/// scaled to the as-of quantity, and the posting-time allocation reads it under row lock after
/// proving no later-dated mutation exists.
/// </summary>
internal static class HistoricalTaxLotQuantity
{
    public static LedgerTaxLotRecord Project(LedgerTaxLotRecord lot,
        IReadOnlyList<DatedTaxLotQuantityMutation> history, DateOnly effectiveDate)
    {
        LedgerValidationException Missing() => new(
            $"Historical quantity for lot '{lot.TaxLotRecordId}' cannot be established from complete retained mutation evidence.");

        if (history.Count == 0)
        {
            // A pristine legacy acquisition has no relief to reverse. A reduced or rewritten legacy
            // row supplies no effective dates: treating its current quantity as history invents facts.
            // A factor without the paydown that wrote it is likewise unexplained.
            if (lot.OpenQuantity != lot.OriginalQuantity || lot.Version != 1 ||
                lot.OriginatingMutationBatchId.HasValue || lot.LastMutationBatchId.HasValue || lot.CurrentFactor is not null)
                throw Missing();
            return lot with { OpenQuantity = lot.AcquiredDate <= effectiveDate ? lot.OriginalQuantity : 0m };
        }

        var ordered = history.OrderBy(static mutation => mutation.ResultVersion).ToArray();
        var running = lot.OriginatingMutationBatchId.HasValue ? 0m : lot.OriginalQuantity;
        var asOf = lot.AcquiredDate <= effectiveDate && !lot.OriginatingMutationBatchId.HasValue
            ? lot.OriginalQuantity : 0m;
        // The factor the lot holds after each replayed row, and the factor it held as of the date.
        decimal? runningFactor = null;
        decimal? factorAsOf = null;
        DateOnly? lastPaydownDate = null;
        var lastVersion = 0L;
        for (var i = 0; i < ordered.Length; i++)
        {
            var mutation = ordered[i];
            if (mutation.TaxLotRecordId != lot.TaxLotRecordId || mutation.SecurityId != lot.SecurityId ||
                mutation.BookPositionId != lot.BookPositionId || mutation.ExpectedVersion < lastVersion ||
                mutation.ResultVersion != mutation.ExpectedVersion + 1 || mutation.ResultVersion > lot.Version ||
                mutation.Before != running || mutation.After != mutation.Before + mutation.Delta ||
                mutation.After < 0m || mutation.After > lot.OriginalQuantity ||
                mutation.EffectiveDate < lot.AcquiredDate
                || (lot.Acquisition?.CorporateActionLineage is { } acquisitionOrigin && mutation.EffectiveDate < acquisitionOrigin.EffectiveDate))
                throw Missing();
            if (mutation.Kind == AtomicTaxLotMutationKind.Acquisition
                || (mutation.Kind == AtomicTaxLotMutationKind.CorporateAction && mutation.Delta > 0m))
            {
                if (i != 0 || mutation.MutationBatchId != lot.OriginatingMutationBatchId ||
                    mutation.Before != 0m || mutation.After != lot.OriginalQuantity ||
                    (mutation.Kind == AtomicTaxLotMutationKind.Acquisition && mutation.EffectiveDate != lot.AcquiredDate)
                    || (mutation.Kind == AtomicTaxLotMutationKind.CorporateAction
                        && lot.Acquisition?.CorporateActionLineage is { } successorOrigin && mutation.EffectiveDate != successorOrigin.EffectiveDate)
                    // An acquisition opens at the booked factor; only a successor opens with one.
                    || (mutation.Kind == AtomicTaxLotMutationKind.Acquisition && mutation.CurrentFactorAfter is not null)
                    || (mutation.CurrentFactorAfter is { } inherited && (inherited < 0m || inherited >= (lot.BookedFactor ?? 1m))))
                    throw Missing();
                runningFactor = mutation.CurrentFactorAfter;
                if (mutation.EffectiveDate <= effectiveDate)
                    factorAsOf = runningFactor;
            }
            else if (mutation.Kind == AtomicTaxLotMutationKind.CorporateAction)
            {
                if (mutation.Before <= 0m || mutation.After != 0m || mutation.Delta != -mutation.Before
                    || mutation.CurrentFactorAfter != runningFactor)
                    throw Missing();
            }
            else if (mutation.Kind == AtomicTaxLotMutationKind.FactorPaydown)
            {
                // A paydown keeps quantity, or closes the lot when the factor reaches zero. Factors
                // decline strictly in version order and paydowns are dated in version order, so the
                // factor as of any date is the latest paydown on or before it.
                if (mutation.Before <= 0m || !(mutation.Delta == 0m || mutation.After == 0m)
                    || mutation.CurrentFactorAfter is not { } factor || factor < 0m
                    || factor >= (runningFactor ?? lot.BookedFactor ?? throw Missing())
                    || (mutation.After == 0m) != (factor == 0m)
                    || (lastPaydownDate is { } previous && mutation.EffectiveDate < previous))
                    throw Missing();
                runningFactor = factor;
                lastPaydownDate = mutation.EffectiveDate;
                if (mutation.EffectiveDate <= effectiveDate)
                    factorAsOf = factor;
            }
            // An average-cost survivor restatement changes basis only; it never moves quantity.
            else if (mutation.Kind is AtomicTaxLotMutationKind.BasisRedistribution or AtomicTaxLotMutationKind.Amortization or AtomicTaxLotMutationKind.CapitalReturn
                    ? mutation.Delta != 0m || mutation.Before <= 0m
                    : mutation.Kind != AtomicTaxLotMutationKind.Disposal || mutation.Delta >= 0m)
                throw Missing();
            // Every other kind carries the factor forward unchanged in its retained snapshot.
            else if (mutation.CurrentFactorAfter != runningFactor)
                throw Missing();

            running = mutation.After;
            lastVersion = mutation.ResultVersion;
            if (mutation.EffectiveDate <= effectiveDate)
                asOf += mutation.Delta;
        }
        if (running != lot.OpenQuantity || ordered[^1].MutationBatchId != lot.LastMutationBatchId ||
            asOf < 0m || asOf > lot.OriginalQuantity ||
            (lot.OriginatingMutationBatchId.HasValue && ordered[0].Kind is not
                (AtomicTaxLotMutationKind.Acquisition or AtomicTaxLotMutationKind.CorporateAction)) ||
            runningFactor != lot.CurrentFactor)
            throw Missing();

        // Version, basis and immutable acquisition facts stay current; only the read projection's
        // quantity and factor are restated. It must never be saved as the current lot or used as a
        // disposal selection.
        return lot with { OpenQuantity = asOf, CurrentFactor = factorAsOf };
    }
}
