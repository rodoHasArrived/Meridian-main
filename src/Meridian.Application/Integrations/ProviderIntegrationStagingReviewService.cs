using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationStagingReviewService
{
    private const int DefaultRecentRunLimit = 10;
    private const int MaxRecentRunLimit = 50;

    private readonly IProviderIntegrationManifestStore store;

    public ProviderIntegrationStagingReviewService(IProviderIntegrationManifestStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ProviderIntegrationStagingReviewDto> GetReviewAsync(
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
        => await GetReviewAsync(null, connectionId, recentRunLimit, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationStagingReviewDto> GetReviewAsync(
        string? tenantId,
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        _ = await scopedStore.GetConnectionAsync(connectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{connectionId}' was not found.");
        var syncRuns = (await scopedStore.ListSyncRunsAsync(connectionId, ct).ConfigureAwait(false))
            .Take(NormalizeLimit(recentRunLimit))
            .ToArray();

        var records = new List<IntegrationStagingRecordDto>();
        foreach (var syncRun in syncRuns)
        {
            ct.ThrowIfCancellationRequested();
            var runRecords = await scopedStore.ListStagingRecordsAsync(syncRun.SyncRunId, ct).ConfigureAwait(false);
            records.AddRange(runRecords.Where(record => StringComparer.Ordinal.Equals(record.ConnectionId, connectionId)));
        }

        var capabilitySummaries = records
            .GroupBy(record => record.Capability)
            .Select(group => new ProviderIntegrationStagingCapabilitySummaryDto(
                group.Key,
                group.Count(),
                group.SelectMany(record => record.ValidationWarnings).Count()))
            .OrderBy(summary => summary.Capability)
            .ToArray();

        var warningGroups = records
            .SelectMany(record => record.ValidationWarnings)
            .GroupBy(
                warning => new
                {
                    warning.Code,
                    warning.Severity,
                    warning.TargetField,
                    warning.Message,
                    warning.SuggestedFix
                })
            .Select(group => new ProviderIntegrationQuarantineIssueGroupDto(
                group.Key.Code,
                group.Key.Severity,
                group.Key.TargetField,
                group.Key.Message,
                group.Key.SuggestedFix,
                group.Count()))
            .OrderByDescending(group => group.Severity)
            .ThenByDescending(group => group.RecordCount)
            .ThenBy(group => group.IssueCode, StringComparer.Ordinal)
            .ToArray();

        return new ProviderIntegrationStagingReviewDto(
            connectionId,
            syncRuns.Select(syncRun => syncRun.SyncRunId).ToArray(),
            records.OrderByDescending(record => record.CreatedAt).ThenBy(record => record.StagingRecordId, StringComparer.Ordinal).ToArray(),
            capabilitySummaries,
            warningGroups,
            records.Count,
            records.Count(record => record.ValidationWarnings.Count == 0),
            records.Count(record => record.ValidationWarnings.Count > 0));
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
}
