using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Remediation slice 2: a field-edit draft records the canonical base it was drafted against, and
/// publish surfaces a stale proposal as itself along with the canonical-merge outcome.
/// </summary>
public sealed partial class SecurityMasterWorkbenchCommandService
{
    /// <summary>
    /// Creates the field-edit draft and records its canonical base (the version verified at staging
    /// and a snapshot of the stored keys the approved merge would overwrite), so publish can refuse it
    /// if that field changes in between. With no base available, the draft records none and publishes
    /// as before.
    /// </summary>
    private async Task<SecurityMasterRevisionRecord> CreateFieldEditDraftAsync(
        UpdateSecurityFieldRequest request,
        string validatedPath,
        SecurityMasterRevisionFieldValue stagedValue,
        long currentVersion,
        CancellationToken ct)
    {
        var fieldBase = await TryCaptureFieldBaseAsync(request.SecurityId, validatedPath, currentVersion, ct).ConfigureAwait(false);
        return fieldBase is not null
            ? await _revisions.CreateDraftAsync(
                request.SecurityId, request.Actor, validatedPath, request.EffectiveFrom, request.Justification,
                request.FundProfileId, stagedValue, fieldBase, ct).ConfigureAwait(false)
            : await _revisions.CreateDraftAsync(
                request.SecurityId, request.Actor, validatedPath, request.EffectiveFrom, request.Justification,
                request.FundProfileId, stagedValue, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The canonical base of an <c>assetSpecificTerms.*</c> edit. Null when there is no projection
    /// store, the path is not one the merge writes, or the projection is not at <paramref name="expectedVersion"/>.
    /// </summary>
    private async Task<SecurityMasterRevisionFieldBase?> TryCaptureFieldBaseAsync(
        Guid securityId, string? fieldPath, long expectedVersion, CancellationToken ct)
    {
        if (_projectionStore is null
            || string.IsNullOrWhiteSpace(fieldPath)
            || !SecurityAssetTermsFieldEditValidator.TargetsAssetSpecificTerms(fieldPath))
        {
            return null;
        }

        var projection = await _projectionStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (projection is null || projection.Version != expectedVersion)
        {
            return null;
        }

        var snapshot = SecurityAssetTermsFieldSnapshot.Capture(projection.AssetClass, projection.AssetSpecificTerms, fieldPath);
        return snapshot is null ? null : new SecurityMasterRevisionFieldBase(expectedVersion, snapshot);
    }

    /// <summary>
    /// A stale proposal is refused as itself (409): a retry cannot succeed, so the operator must
    /// discard the revision and stage the edit again against the current value.
    /// </summary>
    private static void ThrowIfStaleProposal(StaleFieldProposalException? staleRefusal)
    {
        if (staleRefusal is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(staleRefusal);
        }
    }

    private SecurityMasterCanonicalMergeOutcomeDto? TakeCanonicalMergeOutcome(Guid revisionId)
        => _handlers
            .OfType<ISecurityMasterCanonicalMergeOutcomeReporter>()
            .Select(reporter => reporter.TakeOutcome(revisionId))
            .FirstOrDefault(static outcome => outcome is not null);
}
