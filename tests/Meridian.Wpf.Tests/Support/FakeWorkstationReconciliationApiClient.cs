using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Support;

internal sealed class FakeWorkstationReconciliationApiClient : IWorkstationReconciliationApiClient
{
    private readonly Dictionary<string, ReconciliationBreakQueueItem> _breakQueueById;
    private readonly Dictionary<string, ReconciliationRunDetail> _runDetailsByRunId;
    private readonly Dictionary<string, ReconciliationRunDetail> _runDetailsByReconciliationRunId;

    public FakeWorkstationReconciliationApiClient(
        IEnumerable<ReconciliationBreakQueueItem>? breakQueueItems = null,
        IEnumerable<ReconciliationRunDetail>? runDetails = null)
    {
        _breakQueueById = (breakQueueItems ?? [])
            .ToDictionary(item => item.BreakId, StringComparer.OrdinalIgnoreCase);
        _runDetailsByRunId = new Dictionary<string, ReconciliationRunDetail>(StringComparer.OrdinalIgnoreCase);
        _runDetailsByReconciliationRunId = new Dictionary<string, ReconciliationRunDetail>(StringComparer.OrdinalIgnoreCase);

        foreach (var detail in runDetails ?? [])
        {
            _runDetailsByRunId[detail.Summary.RunId] = detail;
            _runDetailsByReconciliationRunId[Normalize(detail.Summary.ReconciliationRunId)] = detail;
        }
    }

    public Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetBreakQueueAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>(
            _breakQueueById.Values
                .OrderByDescending(item => item.DetectedAt)
                .ToArray());

    public Task<ReconciliationRunDetail?> GetLatestRunDetailAsync(string runId, CancellationToken ct = default)
    {
        _runDetailsByRunId.TryGetValue(runId, out var detail);
        return Task.FromResult(detail);
    }

    public Task<ReconciliationRunDetail?> GetRunDetailAsync(string reconciliationRunId, CancellationToken ct = default)
    {
        _runDetailsByReconciliationRunId.TryGetValue(Normalize(reconciliationRunId), out var detail);
        return Task.FromResult(detail);
    }

    public Task<WorkstationReconciliationActionResult> ReviewBreakAsync(
        string breakId,
        ReviewReconciliationBreakRequest request,
        CancellationToken ct = default)
    {
        if (!_breakQueueById.TryGetValue(breakId, out var item))
        {
            return Task.FromResult(new WorkstationReconciliationActionResult(false, "Break was not found.", null));
        }

        var updated = item with
        {
            Status = ReconciliationBreakQueueStatus.InReview,
            AssignedTo = request.AssignedTo,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ReviewedBy = request.ReviewedBy,
            ReviewedAt = DateTimeOffset.UtcNow
        };
        _breakQueueById[breakId] = updated;
        return Task.FromResult(new WorkstationReconciliationActionResult(true, null, updated));
    }

    public Task<WorkstationReconciliationActionResult> ResolveBreakAsync(
        string breakId,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct = default)
    {
        if (!_breakQueueById.TryGetValue(breakId, out var item))
        {
            return Task.FromResult(new WorkstationReconciliationActionResult(false, "Break was not found.", null));
        }

        var updated = item with
        {
            Status = request.Status,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ResolvedBy = request.ResolvedBy,
            ResolvedAt = DateTimeOffset.UtcNow,
            ResolutionNote = request.ResolutionNote
        };
        _breakQueueById[breakId] = updated;
        return Task.FromResult(new WorkstationReconciliationActionResult(true, null, updated));
    }

    private static string Normalize(string value)
        => value.Replace("-", string.Empty, StringComparison.Ordinal).Trim();
}
