using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Contracts.Integrity;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportPackRunReadService
{
    internal static IReadOnlyList<ReportPackWorkflowRecordDto> FilterWorkflowRecords(
        IReadOnlyList<ReportPackWorkflowRecordDto> records,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext is null)
        {
            return records;
        }

        return records
            .Where(record => IsWorkflowRecordAccessible(record, accessContext))
            .ToArray();
    }

    private static IReadOnlyList<ReportingScheduleRecordDto> FilterSchedules(
        IReadOnlyList<ReportingScheduleRecordDto> schedules,
        ReportAccessQueryContext? accessContext,
        IReadOnlyList<WorkstationReportingTemplatePayload> visibleTemplates)
    {
        if (accessContext is null)
        {
            return schedules;
        }

        var visibleTemplateIds = visibleTemplates
            .Where(static template => template.IsAccessible)
            .Select(static template => template.TemplateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return schedules
            .Where(schedule => visibleTemplateIds.Contains(schedule.TemplateId))
            .Where(schedule => IsScheduleAccessible(schedule, accessContext))
            .ToArray();
    }

    internal static bool IsWorkflowRecordAccessible(
        ReportPackWorkflowRecordDto record,
        ReportAccessQueryContext accessContext)
    {
        if (accessContext.RequireBoundScope)
        {
            if (string.IsNullOrWhiteSpace(record.TenantId)
                || string.IsNullOrWhiteSpace(record.CompanyId)
                || string.IsNullOrWhiteSpace(record.AccessPolicySnapshotHash)
                || !string.Equals(record.TenantId, accessContext.TenantId, StringComparison.Ordinal)
                || !string.Equals(record.CompanyId, accessContext.CompanyId, StringComparison.Ordinal)
                || !HasValidWorkflowAccessSnapshot(record))
            {
                return false;
            }
        }
        else if ((!string.IsNullOrWhiteSpace(record.TenantId)
                  && !string.IsNullOrWhiteSpace(accessContext.TenantId)
                  && !string.Equals(record.TenantId, accessContext.TenantId, StringComparison.OrdinalIgnoreCase))
                 || (!string.IsNullOrWhiteSpace(record.CompanyId)
                     && !string.IsNullOrWhiteSpace(accessContext.CompanyId)
                     && !string.Equals(record.CompanyId, accessContext.CompanyId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return IsAccessible(record.AccessPolicy, accessContext);
    }

    private static bool HasValidWorkflowAccessSnapshot(ReportPackWorkflowRecordDto record)
    {
        if (record.AccessPolicy is null || string.IsNullOrWhiteSpace(record.AccessPolicySnapshotHash))
        {
            return false;
        }

        byte[] retainedHash;
        try
        {
            retainedHash = Convert.FromHexString(record.AccessPolicySnapshotHash.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var computedHash = Sha256Digest.ComputeBytesUtf8(JsonSerializer.Serialize(record.AccessPolicy));
        return retainedHash.Length == computedHash.Length
            && CryptographicOperations.FixedTimeEquals(retainedHash, computedHash);
    }

    private static bool IsScheduleAccessible(
        ReportingScheduleRecordDto schedule,
        ReportAccessQueryContext accessContext)
    {
        if (accessContext.RequireBoundScope)
        {
            if (string.IsNullOrWhiteSpace(schedule.TenantId)
                || string.IsNullOrWhiteSpace(schedule.CompanyId)
                || !string.Equals(schedule.TenantId, accessContext.TenantId, StringComparison.Ordinal)
                || !string.Equals(schedule.CompanyId, accessContext.CompanyId, StringComparison.Ordinal))
            {
                return false;
            }
        }
        else if ((!string.IsNullOrWhiteSpace(schedule.TenantId)
                  && !string.IsNullOrWhiteSpace(accessContext.TenantId)
                  && !string.Equals(schedule.TenantId, accessContext.TenantId, StringComparison.OrdinalIgnoreCase))
                 || (!string.IsNullOrWhiteSpace(schedule.CompanyId)
                     && !string.IsNullOrWhiteSpace(accessContext.CompanyId)
                     && !string.Equals(schedule.CompanyId, accessContext.CompanyId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return ReportAccessPolicyEvaluator.Evaluate(schedule.AccessPolicySnapshot, accessContext).IsAccessible;
    }

    private static bool IsAccessible(ReportAccessPolicyDto? policy, ReportAccessQueryContext? accessContext) =>
        accessContext is null || ReportAccessPolicyEvaluator.Evaluate(policy, accessContext).IsAccessible;
}
