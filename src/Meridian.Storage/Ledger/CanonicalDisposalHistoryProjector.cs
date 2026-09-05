using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Certifies disposal economics against retained canonical acquisition facts before reporting.</summary>
public static class CanonicalDisposalHistoryProjector
{
    public static LedgerTaxLotReliefProjection Project(LedgerTaxLotDisposalHistoryRecord disposal,
        JournalEntry entry, Guid ledgerBookId, string functionalCurrency)
    {
        var canonical = disposal.CanonicalLots;
        if (canonical is null || canonical.Count != disposal.Lots.Count || canonical.Count == 0)
            throw new LedgerValidationException("Disposal history lacks canonical acquisition evidence. Resolve the open-lot backfill exception for this durable lot with reviewed acquisition evidence.");
        var history = new List<LedgerTaxLotDisposalHistoryLot>(canonical.Count);
        var seen = new HashSet<Guid>();
        for (var index = 0; index < canonical.Count; index++)
        {
            var lot = canonical[index];
            OpenLotValidation.Validate(lot);
            var retained = disposal.Lots[index];
            var scale = lot.Acquisition.QuantityBasis == LotQuantityBasis.Face
                ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
            var quantity = retained.Quantity * scale;
            if (!seen.Add(lot.TaxLotRecordId) || lot.LedgerBookId != ledgerBookId ||
                lot.Acquisition.FunctionalCurrency != functionalCurrency || lot.LotId != retained.LotId ||
                lot.AcquiredDate != retained.AcquiredDate || retained.HoldingPeriodStart > retained.AcquiredDate ||
                quantity <= 0m || quantity > lot.OpenQuantity ||
                lot.OpenFunctionalCostBasis * quantity / lot.OpenQuantity != retained.CostBasis ||
                retained.UnitCost * retained.Quantity != retained.CostBasis ||
                entry.Lines.Any(line => line.Dimensions?.InstrumentId != lot.SecurityId ||
                    line.Dimensions?.PositionId != lot.BookPositionId))
                throw new LedgerValidationException("Retained disposal quantity, basis, or security/book-position scope differs from canonical lot evidence.");
            history.Add(retained with { Quantity = quantity, UnitCost = retained.UnitCost / scale });
        }
        var recognized = entry.Lines.Where(line => line.Account.Name == LedgerAccounts.RealizedGain.Name)
            .Sum(static line => line.Credit - line.Debit)
            - entry.Lines.Where(line => line.Account.Name == LedgerAccounts.RealizedLoss.Name)
                .Sum(static line => line.Debit - line.Credit);
        var projection = LedgerTaxLotReliefHistoryProjector.Project(new LedgerTaxLotDisposalHistory(
            disposal.MutationBatchId, disposal.JournalEntryId, disposal.Account,
            entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(entry.Timestamp.UtcDateTime),
            disposal.ReliefMethod, history, recognized, disposal.WashSaleBasisIncreases,
            disposal.MatchedReplacementQuantity))
            ?? throw new LedgerValidationException("Retained canonical disposal cannot produce a complete report projection.");
        if (projection.CostBasis != disposal.Lots.Sum(static lot => lot.CostBasis) ||
            projection.RecognizedGainOrLoss != recognized)
            throw new LedgerValidationException("Canonical disposal report does not reconcile to retained journal economics.");
        return projection with { CanonicalOpenLots = canonical };
    }
}
