using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// A published-revision handler that can say what it did to the canonical record, so publish can
/// tell the operator whether the approved edit changed the golden record or stayed overlay-only.
/// </summary>
public interface ISecurityMasterCanonicalMergeOutcomeReporter
{
    /// <summary>Returns and forgets the outcome recorded for <paramref name="revisionId"/>, or null.</summary>
    SecurityMasterCanonicalMergeOutcomeDto? TakeOutcome(Guid revisionId);
}
