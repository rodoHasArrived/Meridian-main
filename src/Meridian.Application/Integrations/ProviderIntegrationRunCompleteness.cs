using System.Globalization;
using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

/// <summary>Checks retained completion evidence before staged rows can leave source review.</summary>
internal static class ProviderIntegrationRunCompleteness
{
    internal static async Task<ValidationIssueDto?> GetIssueAsync(
        IProviderIntegrationManifestStore store,
        ProviderIntegrationSyncRunDto run,
        IReadOnlyList<IntegrationStagingRecordDto> records,
        CancellationToken ct)
    {
        if (run.Status != ProviderIntegrationProcessingStatusDto.Validated || run.CompletedAt is null ||
            run.CompletedAt < run.StartedAt || run.RecordsReceived < 0 ||
            run.RecordsAccepted != run.RecordsReceived || run.RecordsQuarantined != 0 ||
            run.Issues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical) ||
            records.Count != run.RecordsAccepted || records.Any(record =>
                record.SyncRunId != run.SyncRunId || record.ConnectionId != run.ConnectionId ||
                record.Capability != run.Capability || record.Status != ProviderIntegrationProcessingStatusDto.Validated ||
                record.ValidationWarnings.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical)))
        {
            return Incomplete();
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = run;
        var payloadIds = records.Select(record => record.RawPayloadId).Append(run.RawPayloadId).ToArray();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(current.SyncRunId) || current.CompletedAt is null || current.CompletedAt < current.StartedAt ||
                current.Status is ProviderIntegrationProcessingStatusDto.Received or ProviderIntegrationProcessingStatusDto.Parsed or
                    ProviderIntegrationProcessingStatusDto.Mapped or ProviderIntegrationProcessingStatusDto.Blocked ||
                current.Issues.Any(issue => issue.Code is "http.response.incomplete" or "http.status.failed") ||
                current.ManifestReference is not { } applied || current.OriginalManifestReference is not { } original ||
                current.ManifestId != applied.ManifestId || current.ManifestId != original.ManifestId ||
                current.ProviderId != run.ProviderId || current.ConnectionId != run.ConnectionId ||
                current.Capability != run.Capability || current.EndpointKey != run.EndpointKey ||
                !ProviderIntegrationManifestIdentity.Matches(original, run.OriginalManifestReference))
            {
                return Incomplete();
            }

            var retainedManifest = await store.GetManifestVersionAsync(applied.ManifestId, applied.ManifestVersion, ct).ConfigureAwait(false);
            if (retainedManifest is null || retainedManifest.ProviderId != current.ProviderId ||
                !ProviderIntegrationManifestIdentity.Matches(applied, ProviderIntegrationManifestIdentity.Create(retainedManifest)))
            {
                return Incomplete();
            }

            RawIngestionPayloadDto? firstPayload = null;
            foreach (var payloadId in payloadIds.Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(payloadId))
                    return Incomplete();
                var payload = await store.GetRawPayloadAsync(current.SyncRunId, payloadId, ct).ConfigureAwait(false);
                if (!IsCompletePayload(payload, current, payloadId))
                {
                    return Incomplete();
                }
                if (payloadId == current.RawPayloadId)
                    firstPayload = payload;
            }

            if (firstPayload is null || !await HasCompletePageChainAsync(
                    store, current, retainedManifest, firstPayload, payloadIds, ct).ConfigureAwait(false))
                return Incomplete();

            if (current.SourceSyncRunId is not { } sourceId)
                return current.ReplayMode is null && ProviderIntegrationManifestIdentity.Matches(applied, original) ? null : Incomplete();
            if (current.ReplayMode is not (ProviderIntegrationReplayModeDto.Original or ProviderIntegrationReplayModeDto.Remediation))
                return Incomplete();
            var source = await store.GetSyncRunAsync(sourceId, ct).ConfigureAwait(false);
            if (source is null)
                return Incomplete();
            current = source;
            payloadIds = [source.RawPayloadId];
        }
    }

    internal static IntegrationStagingRecordDto WithIssue(IntegrationStagingRecordDto record, ValidationIssueDto? issue)
        => issue is null ? record : record with { ValidationWarnings = [.. record.ValidationWarnings, issue] };

    private static bool IsCompletePayload(RawIngestionPayloadDto? payload, ProviderIntegrationSyncRunDto run, string payloadId)
        => payload is not null && payload.SyncRunId == run.SyncRunId && payload.PayloadId == payloadId &&
            payload.ProviderId == run.ProviderId && payload.ConnectionId == run.ConnectionId &&
            payload.Capability == run.Capability && payload.EndpointKey == run.EndpointKey &&
            payload.SourceSyncRunId == run.SourceSyncRunId && payload.ReplayMode == run.ReplayMode &&
            ProviderIntegrationManifestIdentity.Matches(run.ManifestReference, payload.ManifestReference) &&
            ProviderIntegrationManifestIdentity.Matches(run.OriginalManifestReference, payload.OriginalManifestReference) &&
            !ProviderIntegrationHttpCompleteness.IsRetainedIncomplete(payload.RequestMetadata) &&
            !payload.RequestMetadata.Any(entry => string.Equals(entry.Key, "statusCode", StringComparison.OrdinalIgnoreCase) &&
                (!int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var status) || status < 200 || status >= 300));

    internal static async Task<bool> HasCompletePageChainAsync(
        IProviderIntegrationManifestStore store, ProviderIntegrationSyncRunDto run,
        ProviderIntegrationManifestDto manifest, RawIngestionPayloadDto first,
        IReadOnlyList<string?> stagedPayloadIds, CancellationToken ct)
    {
        // REST capture metadata identifies the page sequence, including empty pages that have
        // no staged row referring to them. CSV/replay envelopes do not claim a REST page number.
        if (!first.RequestMetadata.ContainsKey("page"))
            return true;
        var endpoint = manifest.Endpoints.FirstOrDefault(candidate => candidate.EndpointKey == run.EndpointKey && candidate.Capability == run.Capability);
        if (endpoint is null)
            return false;
        var captured = new HashSet<string>(StringComparer.Ordinal);
        var payload = first;
        for (var page = 1; page <= 100; page++)
        {
            var expectedId = ProviderIntegrationRestDryRunService.StableId("raw-payload", run.SyncRunId,
                run.EndpointKey, page.ToString(CultureInfo.InvariantCulture));
            if (payload.PayloadId != expectedId || !payload.RequestMetadata.TryGetValue("page", out var retainedPage) ||
                retainedPage != page.ToString(CultureInfo.InvariantCulture))
                return false;
            captured.Add(payload.PayloadId);
            var cursor = endpoint.Pagination.Type == ProviderIntegrationPaginationTypeDto.Cursor &&
                !string.IsNullOrWhiteSpace(endpoint.Pagination.CursorPath)
                    ? ProviderIntegrationRestDryRunService.ReadJsonString(payload.RawPayload, endpoint.Pagination.CursorPath)
                    : null;
            if (string.IsNullOrWhiteSpace(cursor))
                return stagedPayloadIds.All(id => id is not null && captured.Contains(id));
            if (page == 100)
                return false;
            var nextId = ProviderIntegrationRestDryRunService.StableId("raw-payload", run.SyncRunId,
                run.EndpointKey, (page + 1).ToString(CultureInfo.InvariantCulture));
            var next = await store.GetRawPayloadAsync(run.SyncRunId, nextId, ct).ConfigureAwait(false);
            if (!IsCompletePayload(next, run, nextId))
                return false;
            payload = next!;
        }
        return false;
    }

    private static ValidationIssueDto Incomplete()
        => new("source.run.incomplete", ProviderIntegrationIssueSeverityDto.Critical,
            "The source run has no complete, verified retained ingestion evidence. Its staged rows remain available for review.",
            null, "Fetch and validate complete source evidence before activation or reconciliation handoff.");
}
