using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Defines equality for replaying one provider event/version into the durable source inbox.
/// Observation time and locally assigned proposal identity are delivery metadata; evidence and
/// economic/source-chain content are authoritative and must not be silently replaced.
/// </summary>
public static class CorporateActionSourceProposalReplayComparer
{
    public static bool HasSameSourcePayload(
        CorporateActionSourceProposalDto existing,
        CorporateActionSourceProposalDto candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);

        return existing.SecurityId == candidate.SecurityId
               && existing.PayloadSchemaVersion == candidate.PayloadSchemaVersion
               && Sha256Digest.FixedEquals(existing.EconomicFingerprint, candidate.EconomicFingerprint)
               && string.Equals(
                   NormalizeLifecycle(existing.ProposedAction.LifecycleState),
                   NormalizeLifecycle(candidate.ProposedAction.LifecycleState),
                   StringComparison.Ordinal)
               && existing.ProposedAction.SupersedesCorpActId == candidate.ProposedAction.SupersedesCorpActId
               && existing.SupersedesProposalId == candidate.SupersedesProposalId
               && existing.ProviderIdentity.ReleaseStatus == candidate.ProviderIdentity.ReleaseStatus
               && string.Equals(
                   existing.ProviderIdentity.EvidenceHash,
                   candidate.ProviderIdentity.EvidenceHash,
                   StringComparison.Ordinal)
               && string.Equals(
                   existing.ProviderIdentity.EvidenceReference,
                   candidate.ProviderIdentity.EvidenceReference,
                   StringComparison.Ordinal);
    }

    private static string NormalizeLifecycle(string? value) =>
        string.IsNullOrWhiteSpace(value) ? CorporateActionLifecycleStates.Confirmed : value.Trim();
}
