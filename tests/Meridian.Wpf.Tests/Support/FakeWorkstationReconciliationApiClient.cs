using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Support;

internal sealed class FakeWorkstationReconciliationApiClient : IWorkstationReconciliationApiClient
{
    private readonly Dictionary<string, ReconciliationBreakQueueItem> _breakQueueById;
    private readonly Dictionary<string, ReconciliationRunDetail> _runDetailsByRunId;
    private readonly Dictionary<string, ReconciliationRunDetail> _runDetailsByReconciliationRunId;
    private readonly ReconciliationCalibrationSummaryDto? _calibrationSummaryOverride;

    public FakeWorkstationReconciliationApiClient(
        IEnumerable<ReconciliationBreakQueueItem>? breakQueueItems = null,
        IEnumerable<ReconciliationRunDetail>? runDetails = null,
        ReconciliationCalibrationSummaryDto? calibrationSummary = null)
    {
        _breakQueueById = (breakQueueItems ?? [])
            .ToDictionary(item => item.BreakId, StringComparer.OrdinalIgnoreCase);
        _runDetailsByRunId = new Dictionary<string, ReconciliationRunDetail>(StringComparer.OrdinalIgnoreCase);
        _runDetailsByReconciliationRunId = new Dictionary<string, ReconciliationRunDetail>(StringComparer.OrdinalIgnoreCase);
        _calibrationSummaryOverride = calibrationSummary;

        foreach (var detail in runDetails ?? [])
        {
            _runDetailsByRunId[detail.Summary.RunId] = detail;
            _runDetailsByReconciliationRunId[Normalize(detail.Summary.ReconciliationRunId)] = detail;
        }
    }

    public Task<ReconciliationCalibrationSummaryDto?> GetCalibrationSummaryAsync(CancellationToken ct = default)
        => Task.FromResult<ReconciliationCalibrationSummaryDto?>(
            _calibrationSummaryOverride ?? BuildCalibrationSummary(_breakQueueById.Values));

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

    private static ReconciliationCalibrationSummaryDto BuildCalibrationSummary(
        IEnumerable<ReconciliationBreakQueueItem> breakQueueItems)
    {
        var items = breakQueueItems.ToArray();
        var activeItems = items.Where(static item => item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview).ToArray();
        var missingMetadataCount = activeItems.Count(static item =>
            string.IsNullOrWhiteSpace(item.ExceptionRoute) ||
            string.IsNullOrWhiteSpace(item.ToleranceProfileId) ||
            string.IsNullOrWhiteSpace(item.RequiredSignoffRole));
        var criticalOpenCount = activeItems.Count(static item => item.Severity == ReconciliationBreakSeverity.Critical);
        var pendingSignoffCount = activeItems.Count(static item =>
            !string.IsNullOrWhiteSpace(item.RequiredSignoffRole) &&
            !IsSignedOff(item.SignoffStatus));
        var signedOffCount = items.Count(static item => IsSignedOff(item.SignoffStatus));

        var profiles = items
            .Where(static item =>
                !string.IsNullOrWhiteSpace(item.ToleranceProfileId) &&
                !string.IsNullOrWhiteSpace(item.ExceptionRoute))
            .GroupBy(static item => (item.ToleranceProfileId!, item.ExceptionRoute!))
            .Select(static group => new ReconciliationCalibrationProfileSummaryDto(
                ToleranceProfileId: group.Key.Item1,
                ExceptionRoute: group.Key.Item2,
                HighestSeverity: group.Max(static item => item.Severity),
                MaxToleranceBand: group.Max(static item => item.ToleranceBand),
                TotalBreakCount: group.Count(),
                OpenBreakCount: group.Count(static item => item.Status == ReconciliationBreakQueueStatus.Open),
                InReviewBreakCount: group.Count(static item => item.Status == ReconciliationBreakQueueStatus.InReview),
                ResolvedBreakCount: group.Count(static item => item.Status == ReconciliationBreakQueueStatus.Resolved),
                DismissedBreakCount: group.Count(static item => item.Status == ReconciliationBreakQueueStatus.Dismissed),
                PendingSignoffCount: group.Count(static item =>
                    item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview &&
                    !string.IsNullOrWhiteSpace(item.RequiredSignoffRole) &&
                    !IsSignedOff(item.SignoffStatus)),
                SignedOffCount: group.Count(static item => IsSignedOff(item.SignoffStatus)),
                LastUpdatedAt: group.Max(static item => item.LastUpdatedAt)))
            .OrderByDescending(static profile => profile.PendingSignoffCount)
            .ThenByDescending(static profile => profile.OpenBreakCount + profile.InReviewBreakCount)
            .ToArray();

        var status = missingMetadataCount > 0 || criticalOpenCount > 0
            ? ReconciliationCalibrationStatusDto.Blocked
            : activeItems.Length > 0 || pendingSignoffCount > 0
                ? ReconciliationCalibrationStatusDto.ReviewRequired
                : ReconciliationCalibrationStatusDto.Ready;

        return new ReconciliationCalibrationSummaryDto(
            AsOf: DateTimeOffset.UtcNow,
            Status: status,
            Summary: BuildCalibrationSummaryText(items.Length, missingMetadataCount, pendingSignoffCount, activeItems.Length),
            TotalBreakCount: items.Length,
            ActiveBreakCount: activeItems.Length,
            OpenBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Open),
            InReviewBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.InReview),
            ResolvedBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Resolved),
            DismissedBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Dismissed),
            CriticalOpenBreakCount: criticalOpenCount,
            PendingSignoffCount: pendingSignoffCount,
            SignedOffCount: signedOffCount,
            MissingCalibrationMetadataCount: missingMetadataCount,
            Profiles: profiles);
    }

    private static string BuildCalibrationSummaryText(
        int totalBreakCount,
        int missingMetadataCount,
        int pendingSignoffCount,
        int activeBreakCount)
    {
        if (totalBreakCount == 0)
        {
            return "No reconciliation breaks require calibration.";
        }

        if (missingMetadataCount > 0)
        {
            return $"{missingMetadataCount} active break(s) need tolerance profile, route, or sign-off metadata.";
        }

        if (pendingSignoffCount > 0)
        {
            return $"{pendingSignoffCount} active break(s) need operator sign-off before calibration can clear.";
        }

        return activeBreakCount > 0
            ? $"{activeBreakCount} active break(s) need calibration review."
            : "All reconciliation calibration items are signed off or closed.";
    }

    private static bool IsSignedOff(string? signoffStatus)
        => string.Equals(signoffStatus, "signed-off", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(signoffStatus, "signed", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(signoffStatus, "approved", StringComparison.OrdinalIgnoreCase);
}
