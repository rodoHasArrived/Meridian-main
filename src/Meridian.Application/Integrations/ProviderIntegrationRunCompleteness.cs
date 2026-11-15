using System.Globalization;
using System.Text.Json;
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

        return await GetSourceIssueAsync(store, run, records, ct).ConfigureAwait(false);
    }

    // Selected-row review needs complete source capture, not successful validation of every
    // other row. Activation above still requires the entire population to be validated.
    internal static async Task<ValidationIssueDto?> GetSourceIssueAsync(
        IProviderIntegrationManifestStore store,
        ProviderIntegrationSyncRunDto run,
        IReadOnlyList<IntegrationStagingRecordDto> records,
        CancellationToken ct)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = run;
        var currentRecords = records;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var quarantined = await store.ListQuarantinedRecordsAsync(current.SyncRunId, ct).ConfigureAwait(false);
            if (!visited.Add(current.SyncRunId) || current.CompletedAt is null || current.CompletedAt < current.StartedAt ||
                !HasConservedPopulation(current, currentRecords, quarantined) ||
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

            var payloadIds = currentRecords.Select(record => record.RawPayloadId).Append(current.RawPayloadId).ToArray();
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
                    store, current, retainedManifest, firstPayload, payloadIds, ct,
                    verifyPopulation: true).ConfigureAwait(false))
                return Incomplete();

            if (current.SourceSyncRunId is not { } sourceId)
                return current.ReplayMode is null && ProviderIntegrationManifestIdentity.Matches(applied, original) ? null : Incomplete();
            if (current.ReplayMode is not (ProviderIntegrationReplayModeDto.Original or ProviderIntegrationReplayModeDto.Remediation))
                return Incomplete();
            var source = await store.GetSyncRunAsync(sourceId, ct).ConfigureAwait(false);
            if (source is null)
                return Incomplete();
            current = source;
            currentRecords = await store.ListStagingRecordsAsync(source.SyncRunId, ct).ConfigureAwait(false);
        }
    }

    private static bool HasConservedPopulation(ProviderIntegrationSyncRunDto run,
        IReadOnlyList<IntegrationStagingRecordDto> records, IReadOnlyList<QuarantinedRecordDto> quarantined)
    {
        if (run.Status is not (ProviderIntegrationProcessingStatusDto.Validated or ProviderIntegrationProcessingStatusDto.Quarantined) ||
            run.RecordsReceived < 0 || run.RecordsAccepted < 0 || run.RecordsQuarantined < 0 ||
            (run.Status == ProviderIntegrationProcessingStatusDto.Validated && run.RecordsQuarantined != 0) ||
            (run.Status == ProviderIntegrationProcessingStatusDto.Quarantined && run.RecordsQuarantined == 0) ||
            run.RecordsReceived != (long)run.RecordsAccepted + run.RecordsQuarantined ||
            records.Count != run.RecordsAccepted || quarantined.Count != run.RecordsQuarantined ||
            records.Select(record => record.StagingRecordId).Distinct(StringComparer.Ordinal).Count() != records.Count ||
            quarantined.Select(record => record.QuarantineRecordId).Distinct(StringComparer.Ordinal).Count() != quarantined.Count ||
            records.Any(record => record.SyncRunId != run.SyncRunId || record.ConnectionId != run.ConnectionId ||
                record.Capability != run.Capability || record.Status != ProviderIntegrationProcessingStatusDto.Validated) ||
            quarantined.Any(record => record.SyncRunId != run.SyncRunId || record.ConnectionId != run.ConnectionId ||
                record.Capability != run.Capability || record.Status != ProviderIntegrationProcessingStatusDto.Quarantined ||
                !record.ValidationErrors.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical)))
            return false;

        // Critical mapping/validation issues may describe retained rejected rows. An unexplained
        // run failure, including a failed fetch with no rejected row, cannot establish capture.
        var rowIssues = quarantined.SelectMany(record => record.ValidationErrors)
            .Where(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical).ToList();
        return run.Issues.Where(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical)
            .All(issue => rowIssues.Remove(issue));
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
        IReadOnlyList<string?> stagedPayloadIds, CancellationToken ct, bool verifyPopulation = false)
    {
        // REST capture metadata identifies the page sequence, including empty pages that have
        // no staged row referring to them. CSV/replay envelopes do not claim a REST page number.
        if (!first.RequestMetadata.ContainsKey("page"))
            return !verifyPopulation || HasCompleteEnvelopePopulation(first, run);
        var endpoint = manifest.Endpoints.FirstOrDefault(candidate => candidate.EndpointKey == run.EndpointKey && candidate.Capability == run.Capability);
        if (endpoint is null)
            return false;
        var captured = new HashSet<string>(StringComparer.Ordinal);
        var payload = first;
        long received = 0;
        for (var page = 1; page <= 100; page++)
        {
            var expectedId = ProviderIntegrationRestDryRunService.StableId("raw-payload", run.SyncRunId,
                run.EndpointKey, page.ToString(CultureInfo.InvariantCulture));
            if (payload.PayloadId != expectedId || !payload.RequestMetadata.TryGetValue("page", out var retainedPage) ||
                retainedPage != page.ToString(CultureInfo.InvariantCulture))
                return false;
            captured.Add(payload.PayloadId);
            if (verifyPopulation)
                received += ProviderIntegrationRestDryRunService.ExtractRecords(payload.RawPayload, endpoint.Response.RecordsPath).Count;
            var cursor = endpoint.Pagination.Type == ProviderIntegrationPaginationTypeDto.Cursor &&
                !string.IsNullOrWhiteSpace(endpoint.Pagination.CursorPath)
                    ? ProviderIntegrationRestDryRunService.ReadJsonString(payload.RawPayload, endpoint.Pagination.CursorPath)
                    : null;
            if (string.IsNullOrWhiteSpace(cursor))
                return stagedPayloadIds.All(id => id is not null && captured.Contains(id)) &&
                    (!verifyPopulation || received == run.RecordsReceived);
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

    private static bool HasCompleteEnvelopePopulation(RawIngestionPayloadDto payload, ProviderIntegrationSyncRunDto run)
    {
        // Historical complete, validated captures may lack an intake-format marker. They retain
        // the existing provenance checks, but cannot authorize newly supported mixed populations.
        if (run.SourceSyncRunId is null && run.EndpointKey != ProviderIntegrationDryRunService.ManualCsvEndpointKey)
            return run.RecordsQuarantined == 0;
        if (payload.RawPayload.ValueKind != JsonValueKind.Object ||
            !payload.RawPayload.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array ||
            records.GetArrayLength() != run.RecordsReceived)
            return false;
        if (run.SourceSyncRunId is not null)
            return payload.RequestMetadata.TryGetValue("recordCount", out var replayCount) &&
                int.TryParse(replayCount, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count == run.RecordsReceived;
        return run.EndpointKey == ProviderIntegrationDryRunService.ManualCsvEndpointKey &&
            payload.RequestMetadata.TryGetValue("fileName", out var fileName) &&
            payload.RawPayload.TryGetProperty("fileName", out var retainedName) && retainedName.ValueKind == JsonValueKind.String &&
            retainedName.GetString() == fileName && payload.RawPayload.TryGetProperty("contentType", out var contentType) &&
            contentType.ValueKind == JsonValueKind.String && contentType.GetString() == "text/csv" &&
            payload.RawPayload.TryGetProperty("recordCount", out var csvCount) && csvCount.ValueKind == JsonValueKind.Number &&
            csvCount.TryGetInt32(out var recordCount) &&
            recordCount == run.RecordsReceived;
    }

    private static ValidationIssueDto Incomplete()
        => new("source.run.incomplete", ProviderIntegrationIssueSeverityDto.Critical,
            "The source run has no complete, verified retained ingestion evidence. Its staged rows remain available for review.",
            null, "Fetch and validate complete source evidence before activation or reconciliation handoff.");
}
