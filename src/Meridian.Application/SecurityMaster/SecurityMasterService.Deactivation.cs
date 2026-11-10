using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterService
{
    public async Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default)
    {
        await _termsCapabilityVerifier.VerifyAsync(ct).ConfigureAwait(false);
        var aliasProjection = await _store.GetProjectionAsync(request.SecurityId, ct).ConfigureAwait(false);
        var current = await _rebuilder.RebuildEconomicDefinitionAsync(request.SecurityId, aliasProjection, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security '{request.SecurityId}' was not found.");

        var currentProjection = SecurityEconomicDefinitionAdapter.ToProjection(current, aliasProjection?.Aliases);
        // Ordered as in an amendment: the class check, the decode, then the round-trip guards.
        EnsureAssetClassIsRecognizedHere(currentProjection);
        var currentRecord = SecurityMasterMapping.ToRecord(currentProjection);
        EnsureAssetClassRoundTripsSafely(currentProjection);
        var result = SecurityMasterCommandFacade.Deactivate(currentRecord, SecurityMasterMapping.ToDeactivateCommand(request));
        var deactivateClassOverride = GetProfileBackedAssetClassOverride(currentProjection);
        var deactivateTermsOverride = GetProfileBackedAssetSpecificTermsOverride(currentProjection, assetSpecificTermsPatch: null);
        var projection = SecurityEconomicDefinitionAdapter.CarryRetainedEconomicTerms(
            currentProjection,
            CreateProjectionFromResult(
                result,
                currentProjection.Aliases,
                deactivateClassOverride,
                deactivateTermsOverride));
        // S2-B3: same rule as create and amend — the typed record is authoritative unless a
        // profile-backed override replaced the projection's class or terms.
        var economic = deactivateClassOverride is null && deactivateTermsOverride is null
            ? SecurityEconomicDefinitionAdapter.ToEconomicRecord(result.Snapshot!.Record, projection)
            : SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var envelope = SecurityMasterMapping.ToEventEnvelope(
            economic,
            "SecurityDeactivated",
            request.UpdatedBy,
            request.SourceSystem,
            request.Reason,
            projection.Version);

        await _canonicalWriteScope.ExecuteAsync(request.SecurityId, async () =>
        {
            await _eventStore.AppendAsync(request.SecurityId, request.ExpectedVersion, [envelope], ct).ConfigureAwait(false);
            await _store.UpsertProjectionAsync(projection, ct).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
        await OnProjectionCommittedAsync(projection.SecurityId).ConfigureAwait(false);
        await SaveSnapshotIfNeededAsync(economic, _serviceCts.Token).ConfigureAwait(false);
    }
}
