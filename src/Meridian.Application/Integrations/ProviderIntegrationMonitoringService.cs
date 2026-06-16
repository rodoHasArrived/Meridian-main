using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationMonitoringService
{
    private const int DefaultRecentRunLimit = 10;
    private const int MaxRecentRunLimit = 50;

    private readonly IProviderIntegrationManifestStore store;

    public ProviderIntegrationMonitoringService(IProviderIntegrationManifestStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ProviderIntegrationConnectionMonitorDto> GetConnectionMonitorAsync(
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ct.ThrowIfCancellationRequested();

        var connection = await store.GetConnectionAsync(connectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{connectionId}' was not found.");
        var manifest = await store.GetManifestAsync(connection.ManifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{connection.ManifestId}' was not found.");
        var limit = NormalizeLimit(recentRunLimit);
        var syncRuns = (await store.ListSyncRunsAsync(connection.ConnectionId, ct).ConfigureAwait(false))
            .Take(limit)
            .ToArray();

        var runEvidence = new List<ProviderIntegrationSyncRunEvidenceDto>(syncRuns.Length);
        foreach (var syncRun in syncRuns)
        {
            ct.ThrowIfCancellationRequested();
            var stagingRecords = await store.ListStagingRecordsAsync(syncRun.SyncRunId, ct).ConfigureAwait(false);
            var quarantinedRecords = await store.ListQuarantinedRecordsAsync(syncRun.SyncRunId, ct).ConfigureAwait(false);
            runEvidence.Add(CreateRunEvidence(syncRun, stagingRecords.Count, quarantinedRecords.Count));
        }

        return new ProviderIntegrationConnectionMonitorDto(
            connection.ConnectionId,
            connection.ManifestId,
            connection.ProviderId,
            manifest.DisplayName,
            connection.ConnectionName,
            connection.Environment,
            connection.State,
            connection.EnabledCapabilities,
            runEvidence.FirstOrDefault(),
            runEvidence,
            runEvidence.Sum(run => run.RecordsReceived),
            runEvidence.Sum(run => run.RecordsAccepted),
            runEvidence.Sum(run => run.RecordsQuarantined),
            runEvidence.Sum(run => run.DurableStagingRecordCount),
            runEvidence.Sum(run => run.DurableQuarantinedRecordCount),
            runEvidence.Any(run => run.CriticalIssueCount > 0));
    }

    private static ProviderIntegrationSyncRunEvidenceDto CreateRunEvidence(
        ProviderIntegrationSyncRunDto syncRun,
        int durableStagingRecordCount,
        int durableQuarantinedRecordCount)
    {
        var criticalIssueCount = syncRun.Issues.Count(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        var warningIssueCount = syncRun.Issues.Count(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Warning);
        return new ProviderIntegrationSyncRunEvidenceDto(
            syncRun.SyncRunId,
            syncRun.Capability,
            syncRun.EndpointKey,
            syncRun.StartedAt,
            syncRun.CompletedAt,
            syncRun.Status,
            syncRun.RecordsReceived,
            syncRun.RecordsAccepted,
            syncRun.RecordsQuarantined,
            durableStagingRecordCount,
            durableQuarantinedRecordCount,
            criticalIssueCount,
            warningIssueCount,
            syncRun.RawPayloadId,
            syncRun.Issues);
    }

    private static int NormalizeLimit(int recentRunLimit)
    {
        if (recentRunLimit <= 0)
        {
            return DefaultRecentRunLimit;
        }

        return Math.Min(recentRunLimit, MaxRecentRunLimit);
    }
}
