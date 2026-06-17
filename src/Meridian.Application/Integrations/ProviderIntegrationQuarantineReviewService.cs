using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationQuarantineReviewService
{
    private const int DefaultRecentRunLimit = 10;
    private const int MaxRecentRunLimit = 50;

    private readonly IProviderIntegrationManifestStore store;

    public ProviderIntegrationQuarantineReviewService(IProviderIntegrationManifestStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ProviderIntegrationQuarantineReviewDto> GetReviewAsync(
        string connectionId,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
        => await GetReviewAsync(null, connectionId, recentRunLimit, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationQuarantineReviewDto> GetReviewAsync(
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

        var records = new List<QuarantinedRecordDto>();
        var decisions = new List<ProviderIntegrationQuarantineDecisionDto>();
        foreach (var syncRun in syncRuns)
        {
            ct.ThrowIfCancellationRequested();
            records.AddRange(await scopedStore.ListQuarantinedRecordsAsync(syncRun.SyncRunId, ct).ConfigureAwait(false));
            decisions.AddRange(await scopedStore.ListQuarantineDecisionsAsync(syncRun.SyncRunId, ct).ConfigureAwait(false));
        }

        var issueGroups = records
            .SelectMany(record => record.ValidationErrors)
            .GroupBy(
                issue => new
                {
                    issue.Code,
                    issue.Severity,
                    issue.TargetField,
                    issue.Message,
                    issue.SuggestedFix
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
        var latestDecisionsByRecord = BuildLatestDecisionLookup(decisions);
        var decisionPosture = BuildDecisionPosture(records, latestDecisionsByRecord);

        return new ProviderIntegrationQuarantineReviewDto(
            connectionId,
            syncRuns.Select(syncRun => syncRun.SyncRunId).ToArray(),
            records.OrderByDescending(record => record.CreatedAt).ThenBy(record => record.QuarantineRecordId, StringComparer.Ordinal).ToArray(),
            issueGroups,
            decisions.OrderByDescending(decision => decision.ReviewedAt).ThenBy(decision => decision.DecisionId, StringComparer.Ordinal).ToArray(),
            records.Count,
            records.SelectMany(record => record.ValidationErrors).Count(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical),
            records.SelectMany(record => record.ValidationErrors).Count(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Warning),
            decisionPosture.PendingReviewRecordCount,
            decisionPosture.DecisionedRecordCount,
            decisionPosture.ReplayRequestedRecordCount,
            decisionPosture.IgnoredRecordCount,
            decisionPosture.CashPositionCandidateCount);
    }

    public async Task<ProviderIntegrationQuarantineResolutionResultDto> ResolveAsync(
        ProviderIntegrationQuarantineResolutionRequestDto request,
        CancellationToken ct = default)
        => await ResolveAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationQuarantineResolutionResultDto> ResolveAsync(
        string? tenantId,
        ProviderIntegrationQuarantineResolutionRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.QuarantineRecordId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReviewedBy);
        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        _ = await scopedStore.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{request.ConnectionId}' was not found.");
        var records = await scopedStore.ListQuarantinedRecordsAsync(request.SyncRunId, ct).ConfigureAwait(false);
        var record = records.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.QuarantineRecordId, request.QuarantineRecordId))
            ?? throw new KeyNotFoundException($"Provider integration quarantine record '{request.QuarantineRecordId}' was not found.");

        if (!StringComparer.Ordinal.Equals(record.ConnectionId, request.ConnectionId))
        {
            throw new InvalidOperationException("Provider integration quarantine record is not linked to the requested connection.");
        }

        var decision = new ProviderIntegrationQuarantineDecisionDto(
            $"quarantine-decision-{Guid.NewGuid():N}",
            request.SyncRunId,
            request.QuarantineRecordId,
            request.ConnectionId,
            request.Action,
            request.ReviewedBy,
            request.ReviewedAt,
            request.Note);
        await scopedStore.SaveQuarantineDecisionAsync(decision, ct).ConfigureAwait(false);
        return new ProviderIntegrationQuarantineResolutionResultDto(
            Resolved: true,
            record,
            decision,
            "Provider integration quarantine review decision recorded.");
    }

    private static int NormalizeLimit(int recentRunLimit)
    {
        if (recentRunLimit <= 0)
        {
            return DefaultRecentRunLimit;
        }

        return Math.Min(recentRunLimit, MaxRecentRunLimit);
    }

    private static IReadOnlyDictionary<string, ProviderIntegrationQuarantineDecisionDto> BuildLatestDecisionLookup(
        IEnumerable<ProviderIntegrationQuarantineDecisionDto> decisions)
        => decisions
            .GroupBy(DecisionRecordKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(decision => decision.ReviewedAt)
                    .ThenByDescending(decision => decision.DecisionId, StringComparer.Ordinal)
                    .First(),
                StringComparer.Ordinal);

    private static QuarantineDecisionPosture BuildDecisionPosture(
        IEnumerable<QuarantinedRecordDto> records,
        IReadOnlyDictionary<string, ProviderIntegrationQuarantineDecisionDto> latestDecisionsByRecord)
    {
        var pendingReviewRecordCount = 0;
        var decisionedRecordCount = 0;
        var replayRequestedRecordCount = 0;
        var ignoredRecordCount = 0;
        var cashPositionCandidateCount = 0;

        foreach (var record in records)
        {
            if (!latestDecisionsByRecord.TryGetValue(RecordKey(record), out var decision))
            {
                pendingReviewRecordCount++;
                continue;
            }

            decisionedRecordCount++;
            switch (decision.Action)
            {
                case ProviderIntegrationQuarantineResolutionActionDto.ReplayAfterMappingChange:
                    replayRequestedRecordCount++;
                    break;
                case ProviderIntegrationQuarantineResolutionActionDto.IgnoreProviderRecord:
                    ignoredRecordCount++;
                    break;
                case ProviderIntegrationQuarantineResolutionActionDto.MarkAsCashPosition:
                    cashPositionCandidateCount++;
                    break;
                case ProviderIntegrationQuarantineResolutionActionDto.ReviewOnly:
                default:
                    pendingReviewRecordCount++;
                    break;
            }
        }

        return new QuarantineDecisionPosture(
            pendingReviewRecordCount,
            decisionedRecordCount,
            replayRequestedRecordCount,
            ignoredRecordCount,
            cashPositionCandidateCount);
    }

    private static string DecisionRecordKey(ProviderIntegrationQuarantineDecisionDto decision)
        => $"{decision.SyncRunId}\u001f{decision.QuarantineRecordId}";

    private static string RecordKey(QuarantinedRecordDto record)
        => $"{record.SyncRunId}\u001f{record.QuarantineRecordId}";

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;

    private readonly record struct QuarantineDecisionPosture(
        int PendingReviewRecordCount,
        int DecisionedRecordCount,
        int ReplayRequestedRecordCount,
        int IgnoredRecordCount,
        int CashPositionCandidateCount);
}
