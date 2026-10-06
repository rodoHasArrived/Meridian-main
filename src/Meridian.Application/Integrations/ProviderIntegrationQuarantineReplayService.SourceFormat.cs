using System.Text.Json;
using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed partial class ProviderIntegrationQuarantineReplayService
{
    private const string SourceRecordFormatMetadataKey = "sourceRecordFormat";
    private const string ManualCsvRecordFormat = "manual-csv-v1";
    private const string JsonRecordFormat = "json-v1";

    private static async Task<string> ResolveSourceRecordFormatAsync(
        IProviderIntegrationManifestStore scopedStore,
        ProviderIntegrationQuarantineReplayRequestDto request,
        ProviderIntegrationSyncRunDto sourceRun,
        RawIngestionPayloadDto sourcePayload,
        ProviderIntegrationManifestReferenceDto originalReference,
        CancellationToken ct)
    {
        var run = sourceRun;
        var payload = sourcePayload;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(run.SyncRunId))
            {
                throw new InvalidOperationException("The retained replay source history contains a cycle; its record format cannot be verified.");
            }

            if (payload.RequestMetadata.TryGetValue(SourceRecordFormatMetadataKey, out var format))
            {
                return format is ManualCsvRecordFormat or JsonRecordFormat
                    ? format
                    : throw new InvalidOperationException("The retained replay source record format is unsupported.");
            }

            if (run.SourceSyncRunId is not { } parentId)
            {
                return IdentifyIngestionRecordFormat(payload);
            }

            // Older replays have no format marker. Follow their verified retained ancestry;
            // neither the selected mapping nor the shape of an individual row establishes format.
            run = await scopedStore.GetSyncRunAsync(parentId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The retained replay source history is unavailable; its record format cannot be inferred.");
            if (!ProviderIntegrationManifestIdentity.Matches(run.OriginalManifestReference, originalReference) ||
                run.ManifestId != sourceRun.ManifestId || run.ProviderId != sourceRun.ProviderId ||
                run.ConnectionId != sourceRun.ConnectionId || run.Capability != sourceRun.Capability ||
                run.EndpointKey != sourceRun.EndpointKey)
            {
                throw new InvalidOperationException("The retained replay source history has inconsistent original provenance or scope.");
            }

            (_, payload) = await ValidateSourceProvenanceAsync(
                scopedStore, request with { SourceSyncRunId = parentId }, run, ct).ConfigureAwait(false);
        }
    }

    private static string IdentifyIngestionRecordFormat(RawIngestionPayloadDto payload)
    {
        // These metadata fields are written by CSV ingestion. REST records can use the same
        // endpoint name and JSON envelope, but REST request metadata does not assert CSV intake.
        if (payload.EndpointKey != ProviderIntegrationDryRunService.ManualCsvEndpointKey ||
            !payload.RequestMetadata.TryGetValue("fileName", out var fileName) ||
            !payload.RequestMetadata.TryGetValue("integrationType", out var integrationType))
        {
            return JsonRecordFormat;
        }

        var raw = payload.RawPayload;
        if (integrationType is not (nameof(IntegrationTypeDto.ManualUpload) or nameof(IntegrationTypeDto.Hybrid) or nameof(IntegrationTypeDto.SftpFile)) ||
            raw.ValueKind != JsonValueKind.Object ||
            !raw.TryGetProperty("contentType", out var contentType) || contentType.ValueKind != JsonValueKind.String || contentType.GetString() != "text/csv" ||
            !raw.TryGetProperty("fileName", out var retainedFileName) || retainedFileName.ValueKind != JsonValueKind.String || retainedFileName.GetString() != fileName ||
            !raw.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The retained manual CSV payload has no valid source envelope.");
        }

        return ManualCsvRecordFormat;
    }
}
