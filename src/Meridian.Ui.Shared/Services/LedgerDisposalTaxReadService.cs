using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Reads certified, journal-backed disposal results without recalculating replacement matches or
/// substituting today's mutable account policy for the retained revision.
/// </summary>
public sealed class LedgerDisposalTaxReadService(ILedgerTaxLotDisposalHistory? history, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<LedgerJournalTaxResultsDto> ReadAsync(
        Guid ledgerBookId, Guid periodId, JournalEntry entry, string functionalCurrency,
        CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        LedgerJournalTaxResultsDto Missing(string message) => new(ledgerBookId, periodId,
            entry.JournalEntryId, functionalCurrency, now, "MissingEvidence", message, []);
        if (history is null)
            return Missing("Retained disposal history is unavailable. Tax results cannot be established.");

        IReadOnlyList<LedgerTaxLotDisposalHistoryRecord> disposals;
        try
        {
            disposals = await history.GetTaxLotDisposalHistoryAsync(ledgerBookId, [entry.JournalEntryId], ct)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is LedgerValidationException or NotSupportedException or ArgumentException or InvalidOperationException or JsonException)
        {
            return Missing("Retained disposal history could not be certified. Review the disposal's acquisition and policy evidence.");
        }
        if (disposals.Any(disposal => disposal.JournalEntryId != entry.JournalEntryId))
            return Missing("Retained disposal history does not match the requested journal.");

        var results = disposals.Select(disposal => Project(disposal, entry, ledgerBookId,
            functionalCurrency, DateOnly.FromDateTime(now.UtcDateTime))).ToArray();
        return new(ledgerBookId, periodId, entry.JournalEntryId, functionalCurrency, now, "Available",
            results.Length == 0 ? "No retained tax-lot disposal is linked to this journal."
                : "Results reflect retained journal evidence. Refresh reads newly retained evidence; it does not post or finalize tax adjustments.",
            results);
    }

    public static LedgerDisposalTaxResultDto Project(LedgerTaxLotDisposalHistoryRecord disposal,
        JournalEntry entry, Guid ledgerBookId, string functionalCurrency, DateOnly asOfDate)
    {
        var saleDate = entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(entry.Timestamp.UtcDateTime);
        LedgerDisposalTaxResultDto Result(string state, string reason, LedgerTaxLotReliefProjection? projection = null,
            IReadOnlyList<LedgerDisposalTaxParcelDto>? parcels = null, DateOnly? windowEnd = null, bool reevaluate = false)
            => new(disposal.MutationBatchId, disposal.JournalEntryId, saleDate, disposal.Account.Name,
                disposal.Account.Symbol, disposal.ReliefMethod.ToString(), disposal.PolicyRevision, disposal.RecordedAt,
                state, reason, state != "Settled", reevaluate, windowEnd,
                projection is null ? null : projection.Selections.Select(static selection => selection.TaxCharacter)
                    .Distinct().Count() > 1 ? "Mixed" : projection.Selections[0].TaxCharacter.ToString(),
                projection?.RealizedGainOrLoss, projection?.RecognizedGainOrLoss, projection?.DisallowedWashSaleLoss,
                parcels ?? []);

        LedgerTaxLotReliefProjection projection;
        try
        {
            projection = CanonicalDisposalHistoryProjector.Project(disposal, entry, ledgerBookId, functionalCurrency);
        }
        catch (Exception exception) when (exception is LedgerValidationException or ArgumentException or InvalidOperationException)
        {
            return Result("MissingEvidence", "Retained acquisition, relief, or journal evidence is missing or inconsistent; tax figures cannot be certified.");
        }

        var losses = projection.Selections.Where(static selection => selection.RealizedGainOrLoss < 0m).ToArray();
        var lossAmount = losses.Sum(static selection => -selection.RealizedGainOrLoss);
        var deferred = projection.DisallowedWashSaleLoss;
        // Retained matches use ledger-lot units; projections use face units for face lots.
        // Compare in retained units to reject an overstated aggregate without overflowing.
        var quantityScale = disposal.CanonicalLots![0].Acquisition.QuantityBasis == Meridian.Contracts.Accounting.Lots.LotQuantityBasis.Face
            ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
        var lossQuantity = losses.Sum(static loss => loss.QuantityRelieved) / quantityScale;
        var allocations = disposal.WashSaleBasisIncreases.SelectMany(static increase => increase.SourceAllocations).ToArray();
        if (disposal.WashSaleBasisIncreases.Any(static increase => increase.Amount < 0m) ||
            deferred > lossAmount || disposal.MatchedReplacementQuantity < 0m ||
            disposal.MatchedReplacementQuantity > lossQuantity)
            return Result("MissingEvidence", "Retained wash-sale amounts do not reconcile to the relieved loss parcels.");

        // Never spread a legacy aggregate deferral across mixed parcels by quantity. Attribution
        // is unambiguous for a single loss parcel or a fully deferred loss; otherwise require
        // retained source allocations.
        var allocated = allocations.GroupBy(static allocation => allocation.Source.Lot.LotId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Sum(static allocation => allocation.Amount), StringComparer.Ordinal);
        var hasAllocationEvidence = allocations.Length > 0 && allocations.All(allocation => allocation.Amount >= 0m &&
            losses.Any(loss => loss.Lot.LotId == allocation.Source.Lot.LotId)) && allocated.Values.Sum() == deferred &&
            losses.All(loss => allocated.GetValueOrDefault(loss.Lot.LotId) <= -loss.RealizedGainOrLoss);
        var fullyDeferred = deferred > 0m && deferred == lossAmount;
        var parcelEvidenceMissing = deferred > 0m && losses.Length > 1 && !fullyDeferred && !hasAllocationEvidence;
        var parcels = projection.Selections.Select(selection =>
        {
            decimal? parcelDeferred = selection.RealizedGainOrLoss >= 0m || deferred == 0m ? 0m
                : hasAllocationEvidence ? allocated.GetValueOrDefault(selection.Lot.LotId)
                : fullyDeferred ? -selection.RealizedGainOrLoss
                : losses.Length == 1 ? deferred : null;
            return new LedgerDisposalTaxParcelDto(selection.Lot.LotId, selection.Lot.AcquiredDate,
                selection.Lot.HoldingPeriodStart, selection.HoldingPeriodDays, selection.HoldingPeriodExtendedByWashSale,
                selection.TaxCharacter.ToString(), selection.QuantityRelieved, selection.Proceeds, selection.CostBasis,
                selection.RealizedGainOrLoss, selection.RealizedGainOrLoss + parcelDeferred, parcelDeferred);
        }).ToArray();

        if (string.IsNullOrWhiteSpace(disposal.PolicyRevision))
            return Result("MissingEvidence", "The exact applied policy revision was not retained.", projection, parcels);
        if (parcelEvidenceMissing)
            return Result("MissingEvidence", "Disposal totals are retained, but deferred-loss attribution to individual loss parcels is missing.", projection, parcels);
        if (losses.Length == 0)
            return Result("Settled", "Every relieved parcel has a nonnegative economic result; replacement acquisitions cannot defer a loss.", projection, parcels);

        var policies = disposal.WashSaleBasisIncreases.Select(static increase => increase.AppliedPolicy).Distinct().ToArray();
        var policy = policies.Length == 1 ? policies[0] : null;
        if (policy is null || policy.PolicyId != disposal.PolicyRevision || policy.WindowDays < 0 || !Enum.IsDefined(policy.Scope))
            return Result("MissingEvidence", "The wash-sale policy settings applied to this loss were not retained consistently with its exact revision. Finality cannot be established.", projection, parcels);
        if (!policy.AppliesOn(saleDate))
            return Result("MissingEvidence", "Retained deferrals conflict with the policy's applicability on the sale date.", projection, parcels);

        DateOnly windowEnd;
        try
        { windowEnd = saleDate.AddDays(policy.WindowDays); }
        catch (ArgumentOutOfRangeException)
        {
            return Result("MissingEvidence", "The retained replacement window is invalid.", projection, parcels);
        }
        if (deferred == lossAmount && disposal.MatchedReplacementQuantity == lossQuantity)
            return Result("Settled", "The retained replacement matches cover every loss parcel and defer the full economic loss.", projection, parcels, windowEnd);
        return asOfDate <= windowEnd
            ? Result("Provisional", "The replacement window remains open through the displayed date. Additional acquisitions can change the retained recognized loss.", projection, parcels, windowEnd)
            : Result("Provisional", "The replacement window has closed, but no completed re-evaluation or governed finalization is retained. Review and retain that evidence before treating this result as settled.", projection, parcels, windowEnd, reevaluate: true);
    }
}
