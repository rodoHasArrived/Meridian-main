using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using AppSecurityMaster = Meridian.Application.SecurityMaster;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Maps Security Master review-queue rows (held amendments, exception cases) to the shared contract
/// DTOs both workstations read, so neither client deserializes an Application or Storage type.
/// </summary>
public static class SecurityMasterReviewQueueProjection
{
    public static SecurityMasterHeldAmendmentDto ToDto(AppSecurityMaster.SecurityMasterHeldAmendment row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new SecurityMasterHeldAmendmentDto(
            row.HeldAmendmentId,
            row.SecurityId,
            row.SourceSystem,
            row.RequestJson,
            row.DominatedFieldPaths,
            row.ConflictIds,
            row.HeldAtVersion,
            row.HeldAt,
            row.Status,
            row.ClosedBy,
            row.ClosedAt);
    }

    /// <summary>The queue row with the SLA verdict and age (hours, one decimal) computed at <paramref name="asOf"/>.</summary>
    public static SecurityMasterExceptionCaseQueueItemDto ToQueueItem(SecurityMasterExceptionCase row, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(row);
        var dto = new SecurityMasterExceptionCaseDto(
            row.CaseId,
            row.CaseKey,
            row.Category,
            row.RuleCode,
            row.SecurityId,
            row.Severity,
            row.Title,
            row.Detail,
            row.Status,
            row.Assignee,
            row.OpenedAt,
            row.LastSeenAt,
            row.SlaDueAt,
            row.ClosedAt,
            row.ClosedReason);
        return new SecurityMasterExceptionCaseQueueItemDto(dto, row.IsBreached(asOf), Math.Round((asOf - row.OpenedAt).TotalHours, 1));
    }
}
