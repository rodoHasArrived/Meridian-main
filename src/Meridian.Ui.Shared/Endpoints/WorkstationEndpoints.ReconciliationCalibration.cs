using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Reconciliation calibration summary sub-helpers for the workstation API surface: auto-match /
/// T0-closure rate math, calibration profile + status derivation, signoff predicates, and value
/// normalization. Split out of the WorkstationEndpoints core partial as a behavior-preserving
/// relocation; the shared BuildReconciliationCalibrationSummary entry (called from core and the
/// Reconciliation partial) remains in core and reaches these helpers across the partial.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static decimal CalculateAutoMatchRate(int totalBreakCount, int activeBreakCount)
        => totalBreakCount <= 0 ? 1m : decimal.Round((decimal)Math.Max(0, totalBreakCount - activeBreakCount) / totalBreakCount, 4);

    private static decimal CalculateT0ClosureRate(int totalBreakCount, int resolvedBreakCount, int dismissedBreakCount)
        => totalBreakCount <= 0 ? 1m : decimal.Round((decimal)(resolvedBreakCount + dismissedBreakCount) / totalBreakCount, 4);

    private static ReconciliationCalibrationProfileSummaryDto BuildCalibrationProfileSummary(
        IGrouping<(string Profile, string Route), ReconciliationBreakQueueItem> group)
    {
        var items = group.ToArray();
        var toleranceBands = items
            .Where(static item => item.ToleranceBand.HasValue)
            .Select(static item => item.ToleranceBand!.Value)
            .ToArray();

        return new ReconciliationCalibrationProfileSummaryDto(
            ToleranceProfileId: group.Key.Profile,
            ExceptionRoute: group.Key.Route,
            HighestSeverity: items
                .OrderByDescending(static item => item.Severity)
                .First()
                .Severity,
            MaxToleranceBand: toleranceBands.Length == 0 ? null : toleranceBands.Max(),
            TotalBreakCount: items.Length,
            OpenBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Open),
            InReviewBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.InReview),
            ResolvedBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Resolved),
            DismissedBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Dismissed),
            PendingSignoffCount: items.Count(static item => RequiresCalibrationSignoff(item)),
            SignedOffCount: items.Count(static item => IsSignedOff(item)),
            LastUpdatedAt: items
                .OrderByDescending(static item => item.LastUpdatedAt)
                .First()
                .LastUpdatedAt);
    }

    private static ReconciliationCalibrationStatusDto DetermineReconciliationCalibrationStatus(
        int totalBreakCount,
        int activeBreakCount,
        int criticalOpenBreakCount,
        int pendingSignoffCount,
        int missingCalibrationMetadataCount)
    {
        if (totalBreakCount == 0)
        {
            return ReconciliationCalibrationStatusDto.Ready;
        }

        if (criticalOpenBreakCount > 0 || missingCalibrationMetadataCount > 0)
        {
            return ReconciliationCalibrationStatusDto.Blocked;
        }

        return activeBreakCount > 0 || pendingSignoffCount > 0
            ? ReconciliationCalibrationStatusDto.ReviewRequired
            : ReconciliationCalibrationStatusDto.Ready;
    }

    private static string BuildReconciliationCalibrationSummaryText(
        ReconciliationCalibrationStatusDto status,
        int totalBreakCount,
        int activeBreakCount,
        int criticalOpenBreakCount,
        int pendingSignoffCount,
        int missingCalibrationMetadataCount,
        int profileCount)
    {
        if (totalBreakCount == 0)
        {
            return "No reconciliation breaks require calibration.";
        }

        if (missingCalibrationMetadataCount > 0)
        {
            return $"{missingCalibrationMetadataCount} reconciliation break(s) are missing tolerance or sign-off metadata.";
        }

        if (criticalOpenBreakCount > 0)
        {
            return $"{criticalOpenBreakCount} critical reconciliation break(s) block calibration sign-off.";
        }

        if (status == ReconciliationCalibrationStatusDto.ReviewRequired)
        {
            return $"{activeBreakCount} reconciliation break(s) need review across {profileCount} tolerance profile(s); {pendingSignoffCount} sign-off item(s) remain open.";
        }

        return "All reconciliation breaks are resolved or dismissed; calibration is ready for accounting sign-off.";
    }

    private static bool HasMissingCalibrationMetadata(ReconciliationBreakQueueItem item)
        => (item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview) &&
           (string.IsNullOrWhiteSpace(item.ExceptionRoute) ||
            string.IsNullOrWhiteSpace(item.ToleranceProfileId) ||
            !item.ToleranceBand.HasValue ||
            string.IsNullOrWhiteSpace(item.RequiredSignoffRole) ||
            string.IsNullOrWhiteSpace(item.SignoffStatus));

    private static bool RequiresCalibrationSignoff(ReconciliationBreakQueueItem item)
        => (item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview) &&
           !IsTerminalCalibrationSignoff(item.SignoffStatus);

    private static bool IsTerminalCalibrationSignoff(string? signoffStatus)
        => string.Equals(signoffStatus, "signed-off", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(signoffStatus, "dismissed", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(signoffStatus, "monitor", StringComparison.OrdinalIgnoreCase);

    private static bool IsSignedOff(ReconciliationBreakQueueItem item)
        => string.Equals(item.SignoffStatus, "signed-off", StringComparison.OrdinalIgnoreCase) ||
           item.SignoffHistory?.Count > 0;

    private static string NormalizeCalibrationValue(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
