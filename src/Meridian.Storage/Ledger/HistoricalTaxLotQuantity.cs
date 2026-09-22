using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

internal sealed record DatedTaxLotQuantityMutation(
    Guid TaxLotRecordId, Guid MutationBatchId, AtomicTaxLotMutationKind Kind,
    decimal Before, decimal Delta, decimal After, long ExpectedVersion, long ResultVersion,
    DateOnly EffectiveDate, Guid SecurityId, Guid BookPositionId);

/// <summary>Restates quantity using immutable journal effective dates, not mutation recording time.</summary>
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
            if (lot.OpenQuantity != lot.OriginalQuantity || lot.Version != 1 ||
                lot.OriginatingMutationBatchId.HasValue || lot.LastMutationBatchId.HasValue)
                throw Missing();
            return lot with { OpenQuantity = lot.AcquiredDate <= effectiveDate ? lot.OriginalQuantity : 0m };
        }

        var ordered = history.OrderBy(static mutation => mutation.ResultVersion).ToArray();
        var running = lot.OriginatingMutationBatchId.HasValue ? 0m : lot.OriginalQuantity;
        var asOf = lot.AcquiredDate <= effectiveDate && !lot.OriginatingMutationBatchId.HasValue
            ? lot.OriginalQuantity : 0m;
        var lastVersion = 0L;
        for (var i = 0; i < ordered.Length; i++)
        {
            var mutation = ordered[i];
            if (mutation.TaxLotRecordId != lot.TaxLotRecordId || mutation.SecurityId != lot.SecurityId ||
                mutation.BookPositionId != lot.BookPositionId || mutation.ExpectedVersion < lastVersion ||
                mutation.ResultVersion != mutation.ExpectedVersion + 1 || mutation.ResultVersion > lot.Version ||
                mutation.Before != running || mutation.After != mutation.Before + mutation.Delta ||
                mutation.After < 0m || mutation.After > lot.OriginalQuantity ||
                mutation.EffectiveDate < lot.AcquiredDate)
                throw Missing();
            if (mutation.Kind == AtomicTaxLotMutationKind.Acquisition)
            {
                if (i != 0 || mutation.MutationBatchId != lot.OriginatingMutationBatchId ||
                    mutation.Before != 0m || mutation.After != lot.OriginalQuantity ||
                    mutation.EffectiveDate != lot.AcquiredDate)
                    throw Missing();
            }
            else if (mutation.Kind != AtomicTaxLotMutationKind.Disposal || mutation.Delta >= 0m)
                throw Missing();

            running = mutation.After;
            lastVersion = mutation.ResultVersion;
            if (mutation.EffectiveDate <= effectiveDate) asOf += mutation.Delta;
        }
        if (running != lot.OpenQuantity || ordered[^1].MutationBatchId != lot.LastMutationBatchId ||
            asOf < 0m || asOf > lot.OriginalQuantity ||
            (lot.OriginatingMutationBatchId.HasValue && ordered[0].Kind != AtomicTaxLotMutationKind.Acquisition))
            throw Missing();

        // Version and immutable acquisition facts stay current; only the read projection's quantity
        // is restated. It must never be saved as the current lot or used as a disposal selection.
        return lot with { OpenQuantity = asOf };
    }
}
