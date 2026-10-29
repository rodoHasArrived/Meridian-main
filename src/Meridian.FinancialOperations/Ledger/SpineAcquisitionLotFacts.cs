using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Canonical acquisition facts for lots the Asset Accounting Event Spine posts, composed out of
/// <see cref="AccountingPostingCandidatePostService"/> so the writer's fact rules live in one unit.
/// </summary>
internal static class SpineAcquisitionLotFacts
{
    internal static void RequireCompleteNewAcquisition(AssetAcquisitionLotDto acquisition)
    {
        var suppliedFaceTerms = (acquisition.OriginalFace.HasValue ? 1 : 0)
            + (acquisition.BookedFactor.HasValue ? 1 : 0) + (acquisition.ParBasis.HasValue ? 1 : 0);
        if (suppliedFaceTerms is > 0 and < 3)
            throw new ArgumentException("New acquisition face amount, booked factor and par basis must be supplied together.");
        if (acquisition.HasFaceValueTerms
            && (acquisition.OriginalFace <= 0m || acquisition.BookedFactor is <= 0m or > 1m || acquisition.ParBasis <= 0m))
            throw new ArgumentException("New acquisition face amount and par basis must be positive, and booked factor must be in (0, 1].");
        if (acquisition.HasFaceValueTerms
            && acquisition.Quantity * LedgerTaxLotFaceValueTerms.LedgerLotParBasis != acquisition.OriginalFace)
            throw new ArgumentException("New acquisition face amount must equal its ledger quantity expressed per 100 of par.");
        if (acquisition.HasFaceValueTerms && acquisition.AmortizationMethod is null)
        {
            throw new ArgumentException(
                "New face acquisitions require an explicit amortization method (including NoAmortization when selected); " +
                "missing acquisition facts cannot be deferred to legacy backfill.");
        }
        if (acquisition.AmortizationMethod == BondAmortizationMethod.ConstantYield
            && acquisition.EffectiveYield is null or <= -1m)
            throw new ArgumentException("New constant-yield acquisitions require an effective yield greater than -1.");
        var settlementIssues = BondSettlementConsideration.ValidateAcquisition(acquisition);
        if (settlementIssues.Count > 0)
            throw new ArgumentException(string.Join(" ", settlementIssues));
    }

    // A lot acquired through the spine states its canonical acquisition facts on the lot of record
    // so production disposal can relieve it without a backfill exception. Every fact is known
    // rather than defaulted: the spine refuses foreign-currency events and the store requires the lot
    // currency to equal the journal functional currency, so FX is exactly one and both bases are the
    // asserted quantity-times-cost event amount. The lot-bound evidence restates each retained
    // source record (same URI, bytes hash, and source reference) as reviewed and retained with the
    // independent approval, whose maker-checker decision covered the drafted candidate this lot
    // instruction belongs to; binding to the lot happens at that approval, not at source intake.
    internal static OpenLotAcquisitionDto Build(
        LedgerTaxLotRecord lot,
        FaceValueAcquisitionTermsDto? faceTerms,
        IReadOnlyList<RetainedEvidenceIdentityDto> sourceEvidence,
        IReadOnlyList<RetainedEvidenceIdentityDto> approvalEvidence)
    {
        var approval = approvalEvidence
            .OrderByDescending(static evidence => evidence.ReviewedAtUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Acquisition lot posting requires retained approval evidence to bind acquisition facts to the lot.");
        var source = sourceEvidence
            .Where(static evidence => evidence.SubjectType != AssetAccountingEvidenceSubjects.PostingApproval)
            .ToArray();
        if (source.Length == 0)
        {
            throw new InvalidOperationException(
                "Acquisition lot posting requires retained source evidence to bind acquisition facts to the lot.");
        }

        var lotSubject = lot.TaxLotRecordId.ToString("D");
        var lotEvidence = source
            .Select(evidence => new RetainedEvidenceIdentityDto(
                $"{evidence.EvidenceId}#open-lot-acquisition:{lotSubject}",
                evidence.EvidenceUri,
                evidence.ContentHashSha256,
                evidence.SourceSystem,
                evidence.SourceReference,
                RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
                approval.ReviewedBy,
                approval.ReviewedAtUtc,
                lot.AcquiredDate,
                evidence.EvidenceVersion,
                approval.RetainedAtUtc,
                approval.RetainedBy,
                OpenLotAcquisitionEvidenceSubjectType,
                lotSubject))
            .ToArray();
        var basis = lot.OriginalQuantity * lot.UnitCost;
        var currency = lot.Currency.Trim().ToUpperInvariant();
        return new OpenLotAcquisitionDto(
            faceTerms is null ? LotQuantityBasis.Units : LotQuantityBasis.Face,
            currency,
            currency,
            AcquisitionFxRateToFunctional: 1m,
            TransactionCostBasis: basis,
            FunctionalCostBasis: basis,
            HoldingPeriodStartDate: lot.AcquiredDate,
            FaceValueTerms: faceTerms,
            lotEvidence);
    }

    private const string OpenLotAcquisitionEvidenceSubjectType = "OpenLotAcquisition";

    // A batch committed before acquisition facts were written retains no facts, and populated facts
    // join the canonical batch fingerprint. Retrying such a batch must replay exactly, so the writer
    // keeps its retained shape; the backfill workflow, not a retry, is how that lot gains facts.
    internal static async Task<bool> IsRetainedLegacyBatchAsync(
        ILedgerJournalStore? journalStore,
        Guid mutationBatchId,
        CancellationToken ct)
    {
        if (journalStore is null)
        {
            return false;
        }

        AtomicTaxLotJournalResult? retained;
        try
        {
            retained = await journalStore.GetAtomicTaxLotPostingAsync(mutationBatchId, ct).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            return false;
        }

        return retained is { MutationKind: AtomicTaxLotMutationKind.Acquisition } &&
               retained.MutatedLots.Any(static lot => lot.Acquisition is null);
    }
}
