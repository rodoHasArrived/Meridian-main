using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Deterministic backfill checks shared by retention, review and atomic application.</summary>
public static class OpenLotBackfillRules
{
    private static readonly JsonSerializerOptions SourceJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static OpenLotBackfillFactsDto ReadFacts(byte[] content, string hash)
    {
        if (content is null || content.Length is 0 or > 262144)
            throw new LedgerValidationException("Retained lot acquisition evidence must contain 1 to 262144 bytes.");
        if (!Sha256Digest.IsCanonical(hash) || !Sha256Digest.FixedEquals(hash, Sha256Digest.Compute(content)))
            throw new LedgerValidationException("Retained lot acquisition source bytes do not match their SHA-256 digest.");
        OpenLotBackfillFactsDto facts;
        try
        {
            facts = JsonSerializer.Deserialize<OpenLotBackfillFactsDto>(content, SourceJsonOptions)
                ?? throw new LedgerValidationException("Retained lot acquisition facts are required.");
        }
        catch (JsonException ex)
        {
            throw new LedgerValidationException("Retained lot acquisition source must contain valid typed JSON: " + ex.Message);
        }
        if (facts.LedgerBookId == Guid.Empty || facts.TaxLotRecordId == Guid.Empty
            || facts.SecurityId == Guid.Empty || facts.BookPositionId == Guid.Empty
            || facts.SecurityMasterVersion <= 0 || facts.BookPositionVersion <= 0
            || facts.AcquiredDate == default || facts.HoldingPeriodStartDate == default)
            throw new LedgerValidationException("Acquisition evidence requires exact book, lot, security, position, versions and dates.");
        return facts;
    }

    public static LedgerTaxLotRecord Enrich(LedgerTaxLotRecord lot, OpenLotBackfillEvidenceDto evidence)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(evidence);
        var facts = evidence.Facts;
        if (lot.Acquisition is not null)
            throw new LedgerValidationException("An evidenced lot cannot be backfilled again; retain correction lineage instead.");
        if (facts.LedgerBookId != lot.LedgerBookId || facts.TaxLotRecordId != lot.TaxLotRecordId
            || evidence.LedgerBookId != lot.LedgerBookId || evidence.TaxLotRecordId != lot.TaxLotRecordId)
            throw new LedgerValidationException("Retained acquisition evidence belongs to a different lot or ledger book.");
        if (evidence.ReviewStatus != "Accepted" || string.IsNullOrWhiteSpace(evidence.ReviewedBy)
            || evidence.ReviewedAtUtc is null || evidence.Version != 2
            || string.Equals(evidence.ReviewedBy, evidence.RetainedBy, StringComparison.OrdinalIgnoreCase))
            throw new LedgerValidationException("Backfill requires independent accepted review of retained acquisition bytes.");
        if (facts.AcquiredDate != lot.AcquiredDate || facts.LegacyOriginalQuantity != lot.OriginalQuantity
            || facts.LegacyUnitCost != lot.UnitCost || facts.LegacyCurrency != lot.Currency)
            throw new LedgerValidationException("Backfill source does not reconcile to the durable acquisition date, quantity, currency and basis.");
        if ((lot.SecurityId != Guid.Empty && lot.SecurityId != facts.SecurityId)
            || (lot.BookPositionId != Guid.Empty && lot.BookPositionId != facts.BookPositionId))
            throw new LedgerValidationException("Backfill cannot replace an existing Security Master or book-position identity.");
        if (lot.HasFaceValueTerms && (lot.OriginalFace != facts.OriginalFace
            || lot.BookedFactor != facts.FaceValueTerms?.BookedFactor || lot.ParBasis != facts.FaceValueTerms?.ParBasis))
            throw new LedgerValidationException("Backfill cannot replace retained face acquisition terms.");
        // Version 2 is the accepted reviewed snapshot, retained by the reviewer at review commit.
        // The immutable version-1 source's original retaining actor/time remain in the evidence
        // table and OpenLotBackfillEvidenceDto; they are not overwritten by this accepted identity.
        // No request-side evidence URI is promoted by Apply.
        var identity = new RetainedEvidenceIdentityDto(evidence.EvidenceRecordId.ToString("D"),
            "evidence://open-lot-backfill/" + evidence.EvidenceRecordId.ToString("D"), evidence.ContentHashSha256,
            evidence.SourceSystem, evidence.SourceReference, evidence.ReviewStatus, evidence.ReviewedBy,
            evidence.ReviewedAtUtc.Value, facts.AcquiredDate, evidence.Version,
            evidence.ReviewedAtUtc.Value, evidence.ReviewedBy, "OpenLotAcquisition", lot.TaxLotRecordId.ToString("D"));
        var result = lot with
        {
            SecurityId = facts.SecurityId,
            BookPositionId = facts.BookPositionId,
            OriginalFace = facts.OriginalFace,
            BookedFactor = facts.FaceValueTerms?.BookedFactor,
            ParBasis = facts.FaceValueTerms?.ParBasis,
            Acquisition = new(facts.QuantityBasis, facts.AcquisitionCurrency, facts.FunctionalCurrency,
                facts.AcquisitionFxRateToFunctional, facts.TransactionCostBasis, facts.FunctionalCostBasis,
                facts.HoldingPeriodStartDate, facts.FaceValueTerms, [identity])
        };
        _ = result.ToOpenLot();
        return result;
    }

    public static IReadOnlyList<string> Issues(LedgerTaxLotRecord lot)
    {
        var issues = new List<string>();
        if (lot.SecurityId == Guid.Empty) issues.Add("MissingSecurityIdentity");
        if (lot.BookPositionId == Guid.Empty) issues.Add("MissingBookPositionIdentity");
        if (lot.Acquisition is null)
        {
            issues.Add("MissingAcquisitionQuantityBasis");
            issues.Add("MissingAcquisitionCurrencyFxEvidence");
        }
        else
        {
            try { _ = lot.ToOpenLot(); }
            catch (Exception ex) when (ex is ArgumentException or LedgerValidationException)
            { issues.Add("UnreconciledAcquisitionEvidence"); }
        }
        return issues;
    }
}
