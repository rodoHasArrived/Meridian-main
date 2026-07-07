using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed class DefaultReconciliationIngestionScheduler : IReconciliationIngestionScheduler
{
    public async Task<IReadOnlyList<DataSourceSnapshot>> CaptureAsync(
        IReadOnlyList<IReconciliationSourceAdapter> adapters,
        ReconciliationIngestionRequest request,
        CancellationToken ct)
    {
        var snapshots = new List<DataSourceSnapshot>();
        foreach (var adapter in adapters.OrderBy(static a => a.SourceType))
        {
            snapshots.Add(await adapter.CaptureSnapshotAsync(request, ct).ConfigureAwait(false));
        }

        return snapshots;
    }
}
