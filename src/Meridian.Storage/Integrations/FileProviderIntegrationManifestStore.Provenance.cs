using System.Text.Json;
using Meridian.Contracts.Integrations;

namespace Meridian.Storage.Integrations;

public sealed partial class FileProviderIntegrationManifestStore
{
    public async Task SaveRawPayloadAsync(RawIngestionPayloadDto payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.SyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.PayloadId);
        var typeInfo = ProviderIntegrationContractsJsonContext.Default.RawIngestionPayloadDto;
        var snapshot = JsonSerializer.SerializeToElement(payload, typeInfo);
        payload = snapshot.Deserialize(typeInfo)!;

        await using var entityLock = await AcquireEntityLockAsync(
            "evidence-locks", $"payload:{HashSegment(payload.SyncRunId)}:{HashSegment(payload.PayloadId)}", ct).ConfigureAwait(false);
        var path = GetSyncRunScopedPath("raw-payloads", payload.SyncRunId, payload.PayloadId);
        var existing = await ReadAsync(path, typeInfo, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(existing, typeInfo), snapshot))
            {
                throw new InvalidOperationException("A retained raw payload and its provenance cannot be changed.");
            }

            return;
        }

        await ValidateEvidenceManifestReferencesAsync(payload.ManifestReference, payload.OriginalManifestReference,
            payload.SourceSyncRunId, payload.ReplayMode, payload.SyncRunId, allowRetainedLegacy: false, ct).ConfigureAwait(false);
        await WriteAsync(path, payload, typeInfo, ct).ConfigureAwait(false);
    }

    public Task SaveSyncRunAsync(ProviderIntegrationSyncRunDto syncRun, CancellationToken ct = default)
        => SaveSyncRunCoreAsync(syncRun, createOnly: false, ct);

    public Task<bool> TryCreateSyncRunAsync(ProviderIntegrationSyncRunDto syncRun, CancellationToken ct = default)
        => SaveSyncRunCoreAsync(syncRun, createOnly: true, ct);

    private async Task<bool> SaveSyncRunCoreAsync(
        ProviderIntegrationSyncRunDto syncRun, bool createOnly, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(syncRun);
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRun.SyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRun.ConnectionId);
        var typeInfo = ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSyncRunDto;
        syncRun = JsonSerializer.SerializeToElement(syncRun, typeInfo).Deserialize(typeInfo)!;

        await using var entityLock = await AcquireEntityLockAsync("evidence-locks", $"run:{syncRun.SyncRunId}", ct).ConfigureAwait(false);
        var path = GetSyncRunPath(syncRun.SyncRunId);
        if (createOnly && File.Exists(path))
        {
            // Claim the identity before replay writes payloads or rows. Even identical retries
            // and incomplete legacy/corrupt records already own this run ID.
            return false;
        }

        if (createOnly && (syncRun.Status != ProviderIntegrationProcessingStatusDto.Received || syncRun.CompletedAt is not null))
        {
            throw new ArgumentException("A new sync run claim must be Received and have no completion timestamp.", nameof(syncRun));
        }

        var existing = await ReadAsync(path, typeInfo, ct).ConfigureAwait(false);
        if (existing is not null &&
            (!ReferencesEqual(existing.ManifestReference, syncRun.ManifestReference) ||
             !ReferencesEqual(existing.OriginalManifestReference, syncRun.OriginalManifestReference) ||
             existing.SourceSyncRunId != syncRun.SourceSyncRunId || existing.ReplayMode != syncRun.ReplayMode ||
             existing.ManifestId != syncRun.ManifestId || existing.ConnectionId != syncRun.ConnectionId ||
             existing.ProviderId != syncRun.ProviderId || existing.Capability != syncRun.Capability ||
             existing.EndpointKey != syncRun.EndpointKey || existing.RawPayloadId != syncRun.RawPayloadId ||
             existing.StartedAt != syncRun.StartedAt))
        {
            throw new InvalidOperationException("A retained sync run's identity and manifest provenance cannot be changed.");
        }

        if (syncRun.ManifestReference is not null && syncRun.ManifestId != syncRun.ManifestReference.ManifestId)
        {
            throw new InvalidDataException("The sync run manifest ID does not match its exact manifest reference.");
        }

        await ValidateEvidenceManifestReferencesAsync(syncRun.ManifestReference, syncRun.OriginalManifestReference,
            syncRun.SourceSyncRunId, syncRun.ReplayMode, syncRun.SyncRunId, allowRetainedLegacy: existing is not null, ct).ConfigureAwait(false);
        await WriteAsync(path, syncRun, typeInfo, ct).ConfigureAwait(false);
        return true;
    }

    private async Task ValidateEvidenceManifestReferencesAsync(
        ProviderIntegrationManifestReferenceDto? applied,
        ProviderIntegrationManifestReferenceDto? original,
        string? sourceSyncRunId,
        ProviderIntegrationReplayModeDto? replayMode,
        string syncRunId,
        bool allowRetainedLegacy,
        CancellationToken ct)
    {
        if (allowRetainedLegacy && applied is null && original is null && sourceSyncRunId is null && replayMode is null)
        {
            // Only already-retained legacy evidence may remain unverified. New records must
            // carry exact references; historical mappings are never inferred from current state.
            return;
        }

        if (applied is null || original is null || applied.ManifestId != original.ManifestId)
        {
            throw new InvalidDataException("Bound evidence requires both applied and original references for the same manifest.");
        }

        if ((sourceSyncRunId is null) != (replayMode is null) || sourceSyncRunId == syncRunId ||
            (sourceSyncRunId is not null && string.IsNullOrWhiteSpace(sourceSyncRunId)) ||
            replayMode is not (null or ProviderIntegrationReplayModeDto.Original or ProviderIntegrationReplayModeDto.Remediation))
        {
            throw new InvalidDataException("Replay evidence requires a distinct source run and an explicit replay mode.");
        }

        if ((replayMode is null or ProviderIntegrationReplayModeDto.Original) && !ReferencesEqual(applied, original))
        {
            throw new InvalidDataException("Original-mapping evidence must apply its retained original manifest reference.");
        }

        if (replayMode == ProviderIntegrationReplayModeDto.Remediation && applied.ManifestVersion <= original.ManifestVersion)
        {
            throw new InvalidDataException("Remediation evidence must select a newer mapping than the original ingestion mapping.");
        }

        await ValidateRetainedManifestReferenceAsync(applied, ct).ConfigureAwait(false);
        if (!ReferencesEqual(applied, original))
        {
            await ValidateRetainedManifestReferenceAsync(original, ct).ConfigureAwait(false);
        }
    }

    private async Task ValidateRetainedManifestReferenceAsync(ProviderIntegrationManifestReferenceDto reference, CancellationToken ct)
    {
        ValidateStoredManifestReference(reference, reference.ManifestId);
        var manifest = await GetManifestVersionAsync(reference.ManifestId, reference.ManifestVersion, ct).ConfigureAwait(false);
        if (manifest is null || !ProviderIntegrationManifestIdentity.Matches(reference, ProviderIntegrationManifestIdentity.Create(manifest)))
        {
            throw new InvalidDataException("Evidence must bind to an existing manifest version with the exact retained digest.");
        }
    }

    private static bool ReferencesEqual(ProviderIntegrationManifestReferenceDto? left, ProviderIntegrationManifestReferenceDto? right)
        => left is null ? right is null : ProviderIntegrationManifestIdentity.Matches(left, right);
}
