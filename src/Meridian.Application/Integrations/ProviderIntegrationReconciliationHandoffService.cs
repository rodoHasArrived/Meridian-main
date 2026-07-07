using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationReconciliationHandoffService
{
    private const string PromotionTarget = "reconciliation-staging";
    private const int DefaultRecentRunLimit = 10;

    private readonly IProviderIntegrationManifestStore store;
    private readonly ProviderIntegrationPromotionReadinessService readiness;
    private readonly ILogger<ProviderIntegrationReconciliationHandoffService> logger;

    public ProviderIntegrationReconciliationHandoffService(
        IProviderIntegrationManifestStore store,
        ProviderIntegrationPromotionReadinessService readiness,
        ILogger<ProviderIntegrationReconciliationHandoffService>? logger = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        this.logger = logger ?? NullLogger<ProviderIntegrationReconciliationHandoffService>.Instance;
    }

    public async Task<ProviderIntegrationReconciliationHandoffResultDto> HandoffAsync(
        ProviderIntegrationReconciliationHandoffRequestDto request,
        CancellationToken ct = default)
        => await HandoffAsync(null, request, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationReconciliationHandoffResultDto> HandoffAsync(
        string? tenantId,
        ProviderIntegrationReconciliationHandoffRequestDto request,
        CancellationToken ct = default)
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "reconciliation-handoff",
            new ProviderIntegrationBoundaryContext(
                TenantId: tenantId,
                ConnectionId: request?.ConnectionId),
            () => HandoffCoreAsync(tenantId, request, ct)).ConfigureAwait(false);

    private async Task<ProviderIntegrationReconciliationHandoffResultDto> HandoffCoreAsync(
        string? tenantId,
        ProviderIntegrationReconciliationHandoffRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApprovalEvidenceId);
        ArgumentNullException.ThrowIfNull(request.StagingRecordIds);
        ct.ThrowIfCancellationRequested();

        var requestedIds = request.StagingRecordIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedIds.Length == 0)
        {
            throw new ArgumentException("At least one staging record id is required.", nameof(request));
        }

        var scopedStore = ResolveStore(tenantId);
        var existingRecords = await scopedStore
            .ListReconciliationHandoffRecordsAsync(request.ConnectionId, ct)
            .ConfigureAwait(false);
        var existingRecordIds = existingRecords
            .Select(record => record.StagingRecordId)
            .Where(stagingRecordId => requestedIds.Contains(stagingRecordId, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var preview = await readiness
            .PreviewAsync(tenantId, request.ConnectionId, request.RecentRunLimit ?? DefaultRecentRunLimit, ct)
            .ConfigureAwait(false);
        var rowsById = preview.Rows.ToDictionary(row => row.StagingRecordId, StringComparer.Ordinal);
        var issues = new List<ValidationIssueDto>();
        var acceptedRows = new List<ProviderIntegrationPromotionReadinessRowDto>();

        foreach (var stagingRecordId in requestedIds)
        {
            ct.ThrowIfCancellationRequested();
            if (existingRecordIds.Contains(stagingRecordId))
            {
                issues.Add(new ValidationIssueDto(
                    "handoff.record.already-handed-off",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Requested staging record already has retained reconciliation handoff evidence.",
                    "stagingRecordIds",
                    "Review the existing handoff history instead of creating a duplicate handoff."));
                continue;
            }

            if (!rowsById.TryGetValue(stagingRecordId, out var row))
            {
                issues.Add(new ValidationIssueDto(
                    "handoff.record.not-found",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Requested staging record was not found in the current promotion readiness window.",
                    "stagingRecordIds",
                    "Refresh the preview or increase the recent run limit before retrying."));
                continue;
            }

            if (row.Status != ProviderIntegrationPromotionReadinessStatusDto.ReadyForReconciliation)
            {
                issues.Add(new ValidationIssueDto(
                    "handoff.record.not-ready",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Requested staging record is not ready for reconciliation handoff.",
                    "stagingRecordIds",
                    "Resolve the row's account, security, or validation blockers before handoff."));
                issues.AddRange(row.Issues);
                continue;
            }

            acceptedRows.Add(row);
        }

        if (issues.Count > 0)
        {
            return new ProviderIntegrationReconciliationHandoffResultDto(
                Accepted: false,
                HandoffId: null,
                ConnectionId: request.ConnectionId,
                PromotionTarget: PromotionTarget,
                Records: [],
                AcceptedRecordCount: 0,
                RejectedRecordCount: requestedIds.Length - acceptedRows.Count,
                DuplicateRecordCount: existingRecordIds.Count,
                Issues: issues,
                Message: "No reconciliation handoff records were persisted because one or more requested rows were not ready or already had retained handoff evidence.");
        }

        var handoffId = CreateHandoffId(request.RequestedAt);
        var records = acceptedRows
            .Select(row => new ProviderIntegrationReconciliationHandoffRecordDto(
                handoffId,
                request.ConnectionId,
                row.SyncRunId,
                row.StagingRecordId,
                row.Capability,
                PromotionTarget,
                request.RequestedBy,
                request.RequestedAt,
                request.ApprovalEvidenceId,
                request.Note,
                row.ProviderAccountId,
                row.InternalAccountId,
                row.InternalSecurityId,
                row.SecurityRoute,
                row.Issues))
            .ToArray();

        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            await scopedStore.SaveReconciliationHandoffRecordAsync(record, ct).ConfigureAwait(false);
        }

        return new ProviderIntegrationReconciliationHandoffResultDto(
            Accepted: true,
            HandoffId: handoffId,
            ConnectionId: request.ConnectionId,
            PromotionTarget: PromotionTarget,
            Records: records,
            AcceptedRecordCount: records.Length,
            RejectedRecordCount: 0,
            DuplicateRecordCount: 0,
            Issues: [],
            Message: "Reconciliation handoff evidence was retained for the selected staged records.");
    }

    public async Task<ProviderIntegrationReconciliationHandoffHistoryDto> GetHistoryAsync(
        string connectionId,
        CancellationToken ct = default)
        => await GetHistoryAsync(null, connectionId, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationReconciliationHandoffHistoryDto> GetHistoryAsync(
        string? tenantId,
        string connectionId,
        CancellationToken ct = default)
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "reconciliation-handoff-history",
            new ProviderIntegrationBoundaryContext(TenantId: tenantId, ConnectionId: connectionId),
            () => GetHistoryCoreAsync(tenantId, connectionId, ct)).ConfigureAwait(false);

    private async Task<ProviderIntegrationReconciliationHandoffHistoryDto> GetHistoryCoreAsync(
        string? tenantId,
        string connectionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ct.ThrowIfCancellationRequested();

        var scopedStore = ResolveStore(tenantId);
        _ = await scopedStore.GetConnectionAsync(connectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration connection '{connectionId}' was not found.");
        var records = (await scopedStore.ListReconciliationHandoffRecordsAsync(connectionId, ct).ConfigureAwait(false))
            .OrderByDescending(record => record.RequestedAt)
            .ThenBy(record => record.HandoffId, StringComparer.Ordinal)
            .ThenBy(record => record.StagingRecordId, StringComparer.Ordinal)
            .ToArray();

        return new ProviderIntegrationReconciliationHandoffHistoryDto(
            connectionId,
            records,
            records.Length,
            records.Select(record => record.HandoffId).Distinct(StringComparer.Ordinal).Count(),
            records.FirstOrDefault()?.RequestedAt);
    }

    private static string CreateHandoffId(DateTimeOffset requestedAt)
        => $"handoff-{requestedAt.UtcDateTime:yyyyMMddHHmmss}-{Guid.NewGuid():N}";

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;
}
