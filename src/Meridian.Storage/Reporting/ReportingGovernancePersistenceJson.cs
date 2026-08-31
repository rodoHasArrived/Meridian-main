using System.Collections.Immutable;
using System.Text.Json;
using Meridian.Reporting;

namespace Meridian.Storage.Reporting;

/// <summary>
/// Canonicalizes governed-reporting payloads at the durable JSON boundary. Default immutable
/// arrays are semantically empty but are not directly serializable by the source-generated
/// converters, so persistence always writes and hydrates their canonical empty representation.
/// </summary>
internal static class ReportingGovernancePersistenceJson
{
    internal static string SerializeRun(GovernedReportingRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return JsonSerializer.Serialize(
            Normalize(run),
            ReportingGovernanceJsonContext.Default.GovernedReportingRun);
    }

    internal static GovernedReportingRun DeserializeRun(string payload) =>
        Normalize(
            JsonSerializer.Deserialize(
                payload,
                ReportingGovernanceJsonContext.Default.GovernedReportingRun)
            ?? throw new JsonException("The retained reporting run payload was null."));

    internal static string SerializeRestatement(ReportingRestatementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(
            Normalize(request),
            ReportingGovernanceJsonContext.Default.ReportingRestatementRequest);
    }

    internal static ReportingRestatementRequest DeserializeRestatement(string payload) =>
        Normalize(
            JsonSerializer.Deserialize(
                payload,
                ReportingGovernanceJsonContext.Default.ReportingRestatementRequest)
            ?? throw new JsonException("The retained reporting restatement payload was null."));

    internal static string SerializeAudit(ReportingGovernanceAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return JsonSerializer.Serialize(
            Normalize(entry),
            ReportingGovernanceJsonContext.Default.ReportingGovernanceAuditEntry);
    }

    internal static ReportingGovernanceAuditEntry DeserializeAudit(string payload) =>
        Normalize(
            JsonSerializer.Deserialize(
                payload,
                ReportingGovernanceJsonContext.Default.ReportingGovernanceAuditEntry)
            ?? throw new JsonException("The retained reporting governance audit payload was null."));

    private static GovernedReportingRun Normalize(GovernedReportingRun run) =>
        run with
        {
            Access = Normalize(run.Access),
            CreationAuthority = Normalize(run.CreationAuthority),
            Readiness = Normalize(run.Readiness),
            Approval = Normalize(run.Approval),
            Release = Normalize(run.Release),
            AuditTrail = NormalizeAudit(run.AuditTrail)
        };

    private static ReportingRestatementRequest Normalize(ReportingRestatementRequest request) =>
        request with
        {
            ChangedLines = NormalizeChangedLines(request.ChangedLines),
            RequestedBy = Normalize(request.RequestedBy),
            ApprovedBy = request.ApprovedBy is null ? null : Normalize(request.ApprovedBy),
            AuditTrail = NormalizeAudit(request.AuditTrail),
            RequestedChangedLines = NormalizeChangedLines(request.RequestedChangedLines)
        };

    private static ReportingAccessScope Normalize(ReportingAccessScope access) =>
        access is null
            ? null!
            : access with { Principals = OrEmpty(access.Principals) };

    private static ReportingAuthorityScope Normalize(ReportingAuthorityScope authority) =>
        authority is null
            ? null!
            : authority with
            {
                Permissions = OrEmpty(authority.Permissions),
                PrincipalIds = OrEmpty(authority.PrincipalIds)
            };

    private static ReportingReadinessReceipt? Normalize(ReportingReadinessReceipt? readiness) =>
        readiness is null
            ? null
            : readiness with { Checks = NormalizeReadinessChecks(readiness.Checks) };

    private static ReportingApprovalReceipt? Normalize(ReportingApprovalReceipt? approval) =>
        approval is null
            ? null
            : approval with { Authority = Normalize(approval.Authority) };

    private static ReportingReleaseReceipt? Normalize(ReportingReleaseReceipt? release) =>
        release is null
            ? null
            : release with
            {
                Authority = Normalize(release.Authority),
                Artifacts = OrEmpty(release.Artifacts),
                EvidenceIds = OrEmpty(release.EvidenceIds)
            };

    private static ReportingGovernanceAuditEntry Normalize(ReportingGovernanceAuditEntry entry) =>
        entry is null
            ? null!
            : entry with { Authority = Normalize(entry.Authority) };

    private static ImmutableArray<ReportingReadinessCheck> NormalizeReadinessChecks(
        ImmutableArray<ReportingReadinessCheck> checks) =>
        checks.IsDefault
            ? []
            : checks
                .Select(static check => check is null
                    ? null!
                    : check with { EvidenceIds = OrEmpty(check.EvidenceIds) })
                .ToImmutableArray();

    private static ImmutableArray<ReportingRestatementChangedLine> NormalizeChangedLines(
        ImmutableArray<ReportingRestatementChangedLine> changedLines) =>
        changedLines.IsDefault
            ? []
            : changedLines
                .Select(static line => line is null
                    ? null!
                    : line with { EvidenceIds = OrEmpty(line.EvidenceIds) })
                .ToImmutableArray();

    private static ImmutableArray<ReportingGovernanceAuditEntry> NormalizeAudit(
        ImmutableArray<ReportingGovernanceAuditEntry> audit) =>
        audit.IsDefault
            ? []
            : audit.Select(static entry => Normalize(entry)).ToImmutableArray();

    private static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> values) =>
        values.IsDefault ? [] : values;
}
