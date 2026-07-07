using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationMonitoringService
{
    private const int DefaultRecentRunLimit = 10;
    private const int MaxRecentRunLimit = 50;

    private readonly ILogger<ProviderIntegrationMonitoringService> logger;
    private readonly IProviderIntegrationManifestStore store;
    private readonly ILogger<ProviderIntegrationMonitoringService> logger;

    public ProviderIntegrationMonitoringService(
        IProviderIntegrationManifestStore store,
        ILogger<ProviderIntegrationMonitoringService>? logger = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.logger = logger ?? NullLogger<ProviderIntegrationMonitoringService>.Instance;
    }

    public async Task<ProviderIntegrationConnectionMonitorDto> GetConnectionMonitorAsync(
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
        => await GetConnectionMonitorAsync(null, connectionId, recentRunLimit, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationConnectionMonitorDto> GetConnectionMonitorAsync(
        string? tenantId,
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "monitor-connection",
            new ProviderIntegrationBoundaryContext(TenantId: tenantId, ConnectionId: connectionId),
            async () =>
    {
        logger.LogDebug(
            "Provider integration operation {Operation} starting for connection {ConnectionId}.",
            nameof(GetConnectionMonitorAsync),
            connectionId);
        try
        {
            return await GetConnectionMonitorCoreAsync(tenantId, connectionId, recentRunLimit, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Provider integration operation {Operation} failed for connection {ConnectionId}.",
                nameof(GetConnectionMonitorAsync),
                connectionId);
            throw;
        }
    }

    private async Task<ProviderIntegrationConnectionMonitorDto> GetConnectionMonitorCoreAsync(
        string? tenantId,
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
    {
        var evidence = await BuildConnectionSyncRunEvidenceAsync(
            tenantId,
            connectionId,
            recentRunLimit,
            ct).ConfigureAwait(false);

        return new ProviderIntegrationConnectionMonitorDto(
            evidence.Connection.ConnectionId,
            evidence.Connection.ManifestId,
            evidence.Connection.ProviderId,
            evidence.Manifest.DisplayName,
            evidence.Connection.ConnectionName,
            evidence.Connection.Environment,
            evidence.Connection.State,
            evidence.Connection.EnabledCapabilities,
            evidence.RunEvidence.FirstOrDefault(),
            evidence.RunEvidence,
            evidence.RunEvidence.Sum(run => run.RecordsReceived),
            evidence.RunEvidence.Sum(run => run.RecordsAccepted),
            evidence.RunEvidence.Sum(run => run.RecordsQuarantined),
            evidence.RunEvidence.Sum(run => run.DurableStagingRecordCount),
            evidence.RunEvidence.Sum(run => run.DurableQuarantinedRecordCount),
            evidence.RunEvidence.Any(run => run.CriticalIssueCount > 0));
    }).ConfigureAwait(false);

    public async Task<ProviderIntegrationSyncRunHistoryDto> GetConnectionSyncRunsAsync(
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
        => await GetConnectionSyncRunsAsync(null, connectionId, recentRunLimit, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationSyncRunHistoryDto> GetConnectionSyncRunsAsync(
        string? tenantId,
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "monitor-sync-runs",
            new ProviderIntegrationBoundaryContext(TenantId: tenantId, ConnectionId: connectionId),
            async () =>
    {
        logger.LogDebug(
            "Provider integration operation {Operation} starting for connection {ConnectionId}.",
            nameof(GetConnectionSyncRunsAsync),
            connectionId);
        try
        {
            return await GetConnectionSyncRunsCoreAsync(tenantId, connectionId, recentRunLimit, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Provider integration operation {Operation} failed for connection {ConnectionId}.",
                nameof(GetConnectionSyncRunsAsync),
                connectionId);
            throw;
        }
    }

    private async Task<ProviderIntegrationSyncRunHistoryDto> GetConnectionSyncRunsCoreAsync(
        string? tenantId,
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
    {
        var evidence = await BuildConnectionSyncRunEvidenceAsync(
            tenantId,
            connectionId,
            recentRunLimit,
            ct).ConfigureAwait(false);

        return new ProviderIntegrationSyncRunHistoryDto(
            evidence.Connection.ConnectionId,
            evidence.RunEvidence,
            evidence.TotalSyncRuns,
            evidence.RunEvidence.Count,
            evidence.RunEvidence.FirstOrDefault()?.StartedAt);
    }).ConfigureAwait(false);

    private async Task<ConnectionSyncRunEvidence> BuildConnectionSyncRunEvidenceAsync(
        string? tenantId,
        string connectionId,
        int recentRunLimit,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        var connection = await scopedStore.GetConnectionAsync(connectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{connectionId}' was not found.");
        var manifest = await scopedStore.GetManifestAsync(connection.ManifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{connection.ManifestId}' was not found.");
        var allSyncRuns = await scopedStore.ListSyncRunsAsync(connection.ConnectionId, ct).ConfigureAwait(false);
        var syncRuns = allSyncRuns
            .Take(NormalizeLimit(recentRunLimit))
            .ToArray();

        var runEvidence = new List<ProviderIntegrationSyncRunEvidenceDto>(syncRuns.Length);
        foreach (var syncRun in syncRuns)
        {
            ct.ThrowIfCancellationRequested();
            var stagingRecords = await scopedStore.ListStagingRecordsAsync(syncRun.SyncRunId, ct).ConfigureAwait(false);
            var quarantinedRecords = await scopedStore.ListQuarantinedRecordsAsync(syncRun.SyncRunId, ct).ConfigureAwait(false);
            runEvidence.Add(CreateRunEvidence(syncRun, stagingRecords.Count, quarantinedRecords.Count));
        }

        return new ConnectionSyncRunEvidence(
            connection,
            manifest,
            runEvidence,
            allSyncRuns.Count);
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

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;

    private sealed record ConnectionSyncRunEvidence(
        ProviderConnectionDto Connection,
        ProviderIntegrationManifestDto Manifest,
        IReadOnlyList<ProviderIntegrationSyncRunEvidenceDto> RunEvidence,
        int TotalSyncRuns);
}
