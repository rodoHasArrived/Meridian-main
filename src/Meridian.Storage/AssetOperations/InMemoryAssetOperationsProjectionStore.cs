using Meridian.Contracts.AssetOperations;

namespace Meridian.Storage.AssetOperations;

public sealed class InMemoryAssetOperationsProjectionStore : IAssetOperationsProjectionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, AssetOperationsDetailDto> _details = new();

    public Task<AssetOperationsDetailDto?> GetAsync(Guid securityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_details.GetValueOrDefault(securityId));
        }
    }

    public Task UpsertAsync(
        AssetOperationsProjectionDto projection,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(approval);
        ct.ThrowIfCancellationRequested();

        var detail = new AssetOperationsDetailDto(
            projection.Subject,
            projection.TermsHistory,
            projection.LifecycleEvents,
            projection.CashFlowProjectionRuns,
            projection.ProjectedCashFlows,
            projection.ActualActivity,
            projection.ReconciliationRuns,
            projection.ReconciliationResults,
            projection.LedgerProjections,
            projection.Readiness,
            projection.WorkflowAudit);

        lock (_gate)
        {
            _details[projection.Subject.SecurityId] = detail;
        }

        return Task.CompletedTask;
    }
}
