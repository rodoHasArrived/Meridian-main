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
        if (disposal.JournalEntryId != entry.JournalEntryId)
            throw new LedgerValidationException("Retained disposal belongs to a different journal.");
        if (canonical is null || canonical.Count != disposal.Lots.Count || canonical.Count == 0)
            throw new LedgerValidationException("Disposal history lacks canonical acquisition evidence. Resolve the open-lot backfill exception for this durable lot with reviewed acquisition evidence.");
        var averageCost = disposal.ReliefMethod == LedgerTaxLotReliefMethod.AverageCost;
        if (averageCost)
            CertifyAverageCostRelief(disposal, canonical);
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
                (!averageCost && (lot.OpenFunctionalCostBasis * quantity / lot.OpenQuantity != retained.CostBasis ||
                    retained.UnitCost * retained.Quantity != retained.CostBasis)) ||
                entry.Lines.Any(line => line.Dimensions?.InstrumentId != lot.SecurityId ||
                    line.Dimensions?.PositionId != lot.BookPositionId))
                throw new LedgerValidationException("Retained disposal quantity, basis, or security/book-position scope differs from canonical lot evidence.");
            history.Add(retained with
            {
                Quantity = quantity,
                // Average cost relieves the pooled basis, so each slice reports the unit cost its
                // certified pooled basis implies rather than the lot's own acquisition cost.
                UnitCost = averageCost ? retained.CostBasis / quantity : retained.UnitCost / scale,
                HoldingPeriodStart = retained.HoldingPeriodStart < lot.Acquisition.HoldingPeriodStartDate
                    ? retained.HoldingPeriodStart : lot.Acquisition.HoldingPeriodStartDate
            });
        }
        var assetLines = entry.Lines.Where(line => line.Account == disposal.Account).ToArray();
        if (assetLines.Length != 1 || assetLines[0].Debit != 0m ||
            assetLines[0].Credit != disposal.Lots.Sum(static lot => lot.CostBasis))
            throw new LedgerValidationException("Retained disposal basis does not match the exact asset-account journal movement.");
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

    /// <summary>
    /// Re-runs pooled relief over the retained pre-relief pool (relieved lots plus every survivor
    /// the batch restated) and requires each retained slice to be exactly what the kernel relieves.
    /// </summary>
    private static void CertifyAverageCostRelief(LedgerTaxLotDisposalHistoryRecord disposal, IReadOnlyList<OpenLotDto> canonical)
    {
        var pool = disposal.PoolLots;
        if (pool is null || pool.Count < canonical.Count ||
            canonical.Any(lot => !pool.Any(member => member.TaxLotRecordId == lot.TaxLotRecordId)))
            throw new LedgerValidationException("Average-cost disposal history lacks its retained pre-relief pool; canonical reporting is blocked.");
        var scale = canonical[0].Acquisition.QuantityBasis == LotQuantityBasis.Face
            ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
        OpenLotReliefResultDto relief;
        try
        {
            relief = new OpenLotReliefService().Select(pool,
                disposal.Lots.Sum(static lot => lot.Quantity) * scale, OpenLotReliefMethod.AverageCost);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new LedgerValidationException($"Average-cost disposal history cannot be re-relieved: {exception.Message}");
        }

        if (relief.Selections.Count != canonical.Count || relief.Selections.Where((slice, index) =>
                slice.TaxLotRecordId != canonical[index].TaxLotRecordId ||
                slice.Quantity != disposal.Lots[index].Quantity * scale ||
                slice.FunctionalCostBasis != disposal.Lots[index].CostBasis).Any())
            throw new LedgerValidationException("Retained average-cost slices differ from the pooled relief over the retained pool.");
    }
}
