using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterService
{
    private async Task<SecurityDetailDto> ExecuteCreateAsync(CreateSecurityRequest request, CancellationToken ct)
    {
        await _termsCapabilityVerifier.VerifyAsync(ct).ConfigureAwait(false);
        var result = SecurityMasterCommandFacade.Create(MapRequestOrRefuse(() => SecurityMasterMapping.ToCreateCommand(request)));
        var projection = CreateProjectionFromResult(
            result,
            aliases: null,
            GetProfileBackedAssetClassOverride(request.AssetClass, request.AssetSpecificTerms),
            GetProfileBackedAssetSpecificTermsOverride(request.AssetSpecificTerms));
        EnsureProfileBackedTermsAreCatalogValid(projection, request.EffectiveFrom);
        SecurityAssetPackAdmission.EnsureAdmitted(projection, request.EffectiveFrom);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var envelope = SecurityMasterMapping.ToEventEnvelope(
            economic,
            "SecurityCreated",
            request.UpdatedBy,
            request.SourceSystem,
            request.Reason,
            projection.Version);

        // Creation is the first canonical write of every governed field the record supplies, so it
        // seeds the per-field attribution the same way an amend does — diffed against an empty
        // baseline instead of a previous revision.
        var seededGovernedFields = SecurityMasterConflictDetection.ChangedGovernedFieldPaths(
            CreateEmptyGovernedBaseline(projection), projection, _assetProfileCatalog);
        SecurityProjectionRecord? persistedProjection;
        if (_canonicalWriteScope.IsAtomic)
        {
            await _canonicalWriteScope.ExecuteAsync(request.SecurityId, async () =>
            {
                await _eventStore.AppendAsync(request.SecurityId, expectedVersion: 0, [envelope], ct).ConfigureAwait(false);
                await _store.UpsertProjectionAsync(projection, ct).ConfigureAwait(false);
                await RecordFieldLineageInScopeAsync([], seededGovernedFields, projection, ct).ConfigureAwait(false);
                return true;
            }, ct).ConfigureAwait(false);
            persistedProjection = await OnProjectionCommittedAsync(projection.SecurityId).ConfigureAwait(false);
        }
        else
        {
            await _eventStore.AppendAsync(request.SecurityId, expectedVersion: 0, [envelope], ct).ConfigureAwait(false);
            await _store.UpsertProjectionAsync(projection, ct).ConfigureAwait(false);
            persistedProjection = await OnProjectionCommittedAsync(projection.SecurityId).ConfigureAwait(false);
            await TryRecordCanonicalFieldAttributionAsync(seededGovernedFields, projection, _serviceCts.Token).ConfigureAwait(false);
        }
        await SaveSnapshotIfNeededAsync(economic, _serviceCts.Token).ConfigureAwait(false);

        if (persistedProjection is not null)
            await TryRecordConflictsAsync(persistedProjection, request.SecurityId, _serviceCts.Token).ConfigureAwait(false);

        // Enqueue a best-effort corporate action backfill for the newly-created security so
        // that historical corp action data is available immediately for backtesting.
        if (_corporateActionFetcher is not null)
        {
            var ticker = projection.PrimaryIdentifierValue;
            var securityId = projection.SecurityId;
            var ct2 = _serviceCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _corporateActionFetcher.FetchAndPersistAsync(ticker, securityId, ct2)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Background corporate action sync failed for new security {Ticker} ({SecurityId})",
                        ticker, securityId);
                }
            }, ct2);
        }

        return SecurityMasterMapping.ToDetail(projection);
    }

}
