using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Computes the deterministic default winner for a Security Master source conflict. Operators may
/// override the policy winner, but only by acknowledging the deviation — the deviation between the
/// policy winner and the operator's choice is the audited artifact.
/// </summary>
public interface ISecurityMasterConflictAuthorityPolicy
{
    /// <summary>
    /// Evaluates the conflict using the precedence ladder: golden-copy source → freshness (AsOf) →
    /// confidence score. The decision is bulk-eligible only when it matches the assessment's
    /// recommended winner and the assessment was already flagged bulk-eligible.
    /// </summary>
    SecurityMasterConflictAuthorityDecision Evaluate(
        SecurityMasterConflictAssessmentDto assessment,
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> providerConfidence);
}
