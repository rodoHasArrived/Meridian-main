using Meridian.Contracts.FixedIncome;

namespace Meridian.Contracts.AssetOperations;

/// <summary>
/// Independently reviewed missing terms for a retained face-acquisition draft. The original
/// candidate remains immutable; this supplement is retained with the atomic posting evidence.
/// </summary>
public sealed record AssetAcquisitionTermsCompletionDto(
    Guid TaxLotRecordId,
    string DraftedCandidateFingerprint,
    // Preserve absence for validation instead of deserializing it as ConstantYield (enum zero).
    BondAmortizationMethod? AmortizationMethod,
    decimal? EffectiveYield,
    RetainedEvidenceIdentityDto Evidence)
{
    public const string EvidenceSubjectType = "AssetAcquisitionTermsCompletion";

    public string ApprovalSubjectId() => AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(new
    {
        TaxLotRecordId,
        DraftedCandidateFingerprint,
        AmortizationMethod,
        EffectiveYield
    });
}
