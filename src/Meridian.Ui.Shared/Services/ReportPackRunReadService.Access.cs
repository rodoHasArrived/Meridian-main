using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;

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

    private static IReadOnlyList<ReportPackDeliveryAttemptDto> FilterDeliveryAttempts(
        IReadOnlyList<ReportPackDeliveryAttemptDto> attempts,
        ReportAccessQueryContext? accessContext,
        IReadOnlyList<WorkstationReportingTemplatePayload> visibleTemplates,
        IReadOnlyList<ReportPackWorkflowRecordDto> visibleWorkflowRecords,
        IReadOnlySet<string> visibleRunIds)
    {
        if (accessContext is null)
        {
            return attempts.Select(SanitizeDeliveryAttemptForRead).ToArray();
        }

        var visibleTemplateIds = visibleTemplates
            .Where(static template => template.IsAccessible)
            .Select(static template => template.TemplateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleReportIds = visibleWorkflowRecords
            .Select(static record => record.ReportId)
            .ToHashSet();
        var visibleAttempts = attempts
            .Where(attempt =>
                visibleReportIds.Contains(attempt.ReportId)
                || (!string.IsNullOrWhiteSpace(attempt.Package?.ReportingRunId)
                    && visibleRunIds.Contains(attempt.Package.ReportingRunId))
                || accessContext.RequireBoundScope != true
                    && !string.IsNullOrWhiteSpace(attempt.Package?.ReportingTemplateId)
                    && visibleTemplateIds.Contains(attempt.Package.ReportingTemplateId))
            .Select(SanitizeDeliveryAttemptForRead)
            .ToArray();
        return visibleAttempts;
    }

    private static bool IsWorkflowRecordAccessible(
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

        var computedHash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record.AccessPolicy)));
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

    private static ReportPackDeliveryAttemptDto SanitizeDeliveryAttemptForRead(
        ReportPackDeliveryAttemptDto attempt) =>
        attempt with
        {
            DeliveryReference = SanitizeDeliveryHref(attempt.DeliveryReference),
            EvidenceLinks = SanitizeEvidenceLinks(attempt.EvidenceLinks),
            Package = attempt.Package is null ? null : SanitizeDeliveryPackageForRead(attempt.Package)
        };

    private static ReportPackDeliveryPackageDto SanitizeDeliveryPackageForRead(
        ReportPackDeliveryPackageDto package) =>
        package with
        {
            SecureLink = SanitizeDeliveryHref(package.SecureLink),
            PortalRoute = SanitizeDeliveryHref(package.PortalRoute),
            Artifacts = package.Artifacts
                .Select(static artifact => artifact with
                {
                    DownloadRoute = SanitizeOptionalDeliveryHref(artifact.DownloadRoute)
                })
                .ToArray(),
            SourceArtifacts = package.SourceArtifacts?
                .Select(SanitizeDeliveryHref)
                .ToArray(),
            PublicationEvidenceLinks = SanitizeEvidenceLinks(package.PublicationEvidenceLinks),
            RestatementEvidenceLinks = SanitizeEvidenceLinks(package.RestatementEvidenceLinks),
            AccessLinks = package.AccessLinks?
                .Select(static link => link with { Href = SanitizeDeliveryHref(link.Href) })
                .ToArray(),
            Notifications = package.Notifications?
                .Select(static notification => notification with
                {
                    Href = SanitizeDeliveryHref(notification.Href)
                })
                .ToArray(),
            DeliveryEvidencePacket = package.DeliveryEvidencePacket is null
                ? null
                : package.DeliveryEvidencePacket with
                {
                    DeliveryEvidence = SanitizeEvidenceLinks(package.DeliveryEvidencePacket.DeliveryEvidence) ?? []
                }
        };

    private static IReadOnlyList<ReportPackEvidenceLinkDto>? SanitizeEvidenceLinks(
        IReadOnlyList<ReportPackEvidenceLinkDto>? links) =>
        links?
            .Select(static link => link with
            {
                Route = SanitizeOptionalDeliveryHref(link.Route)
            })
            .ToArray();

    private static string? SanitizeOptionalDeliveryHref(string? href) =>
        string.IsNullOrWhiteSpace(href) ? href : SanitizeDeliveryHref(href);

    private static string SanitizeDeliveryHref(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return href;
        }

        var sanitized = href.Trim();
        var fragmentIndex = sanitized.IndexOf('#');
        if (fragmentIndex >= 0 && ContainsDeliverySecret(sanitized[(fragmentIndex + 1)..]))
        {
            sanitized = sanitized[..fragmentIndex];
        }

        var queryIndex = sanitized.IndexOf('?');
        if (queryIndex >= 0 && ContainsDeliverySecret(sanitized[(queryIndex + 1)..]))
        {
            sanitized = sanitized[..queryIndex];
        }

        return sanitized;
    }

    private static bool ContainsDeliverySecret(string parameters)
    {
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(parameters);
        }
        catch (UriFormatException)
        {
            decoded = parameters;
        }

        return decoded
            .Split(['&', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static parameter => parameter.Split('=', 2)[0].Trim())
            .Any(static key => key.Equals("token", StringComparison.OrdinalIgnoreCase)
                || key.Equals("access_token", StringComparison.OrdinalIgnoreCase)
                || key.Equals("grant", StringComparison.OrdinalIgnoreCase)
                || key.Equals("secret", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAccessible(ReportAccessPolicyDto? policy, ReportAccessQueryContext? accessContext) =>
        accessContext is null || ReportAccessPolicyEvaluator.Evaluate(policy, accessContext).IsAccessible;
}
