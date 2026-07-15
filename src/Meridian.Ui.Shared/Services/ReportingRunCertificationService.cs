using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed record CertifiedReportingRunContext(
    ReportingOperationalScope OperationalScope,
    ReportingAccessScope AccessScope,
    ReportingCertifiedSnapshotScope Snapshot);

/// <summary>
/// Freezes the exact server-resolved reporting inputs and authority scope before execution. The
/// resulting identifiers are content addressed, so retrying the same input set produces the same
/// snapshot identity even though the capture receipt has a different timestamp.
/// </summary>
public sealed class ReportingRunCertificationService
{
    public CertifiedReportingRunContext Certify(
        ReportingTemplateMetadata template,
        ReportingRunReadinessDto readiness,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? datasetRows,
        string? datasetSourceId,
        ReportAccessQueryContext accessContext)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(accessContext);
        RequireBoundScope(accessContext);

        var parameters = readiness.ResolvedParameters;
        var tenantId = accessContext.TenantId!.Trim();
        var companyId = accessContext.CompanyId!.Trim();
        var organizationId = tenantId;
        var bookId = parameters.LedgerBook.LedgerBookId?.ToString("D")
            ?? NormalizeRequired(parameters.LedgerBook.LedgerBookCode, "ledger book");
        var scope = new ReportingOperationalScope(
            tenantId,
            organizationId,
            companyId,
            parameters.Scope.FundProfileId.Trim(),
            bookId,
            parameters.PeriodId.Trim());

        var access = BuildAccessScope(template, readiness.ResolvedTemplate, accessContext);
        var sourceId = NormalizeSourceId(template, datasetSourceId);
        if (string.Equals(sourceId, "custom-request-dataset", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Caller-supplied report rows cannot be certified. Select a server-owned reporting dataset source.");
        }

        var canonicalRows = (datasetRows ?? [])
            .Select(CanonicalizeRow)
            .OrderBy(static row => row, StringComparer.Ordinal)
            .ToArray();
        var canonical = JsonSerializer.Serialize(new
        {
            template = new
            {
                readiness.ResolvedTemplate.Name,
                readiness.ResolvedTemplate.Version
            },
            scope,
            parameters,
            dataset = new
            {
                sourceId,
                rows = canonicalRows
            },
            readiness.EvidenceHash
        });
        var snapshotHash = Sha256(canonical);
        var snapshot = new ReportingCertifiedSnapshotScope(
            tenantId,
            organizationId,
            companyId,
            scope.FundId,
            bookId,
            scope.PeriodId,
            $"report-snapshot-{snapshotHash[..24]}",
            snapshotHash,
            readiness.EvaluationId,
            readiness.EvaluatedAtUtc);
        return new CertifiedReportingRunContext(scope, access, snapshot);
    }

    public ReportingReadinessReceipt BuildGovernanceReadiness(
        string runId,
        CertifiedReportingRunContext certified,
        ReportingRunReadinessDto readiness)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(certified);
        ArgumentNullException.ThrowIfNull(readiness);

        var checks = readiness.Checks
            .Select(static check => new ReportingReadinessCheck(
                check.CheckId,
                check.Status == ReportingRunReadinessStatusDto.Ready,
                check.EvidenceReferences
                    .Where(static evidence => !string.IsNullOrWhiteSpace(evidence))
                    .Select(static evidence => evidence.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static evidence => evidence, StringComparer.Ordinal)
                    .ToImmutableArray(),
                check.Status == ReportingRunReadinessStatusDto.Ready ? null : check.Summary))
            .ToImmutableArray();
        return new ReportingReadinessReceipt(
            readiness.EvaluationId,
            readiness.EvidenceHash,
            runId.Trim(),
            certified.OperationalScope.TenantId,
            certified.Snapshot.SnapshotId,
            certified.Snapshot.SnapshotHash,
            readiness.EvaluatedAtUtc,
            checks);
    }

    private static ReportingAccessScope BuildAccessScope(
        ReportingTemplateMetadata template,
        VersionedReportTemplateIdDto templateId,
        ReportAccessQueryContext accessContext)
    {
        var policy = template.AccessPolicy ?? new ReportAccessPolicyDto(
            ReportAccessModeDto.CompanyWide,
            CompanyId: accessContext.CompanyId);
        var principals = (policy.Principals ?? [])
            .Select(static principal => principal.PrincipalId?.Trim())
            .Where(static principal => !string.IsNullOrWhiteSpace(principal))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static principal => principal, StringComparer.Ordinal)
            .ToImmutableArray();
        var mode = policy.Mode switch
        {
            ReportAccessModeDto.Private => ReportingGovernanceAccessMode.Private,
            ReportAccessModeDto.Restricted => ReportingGovernanceAccessMode.Restricted,
            _ => ReportingGovernanceAccessMode.CompanyWide
        };
        var owner = NormalizeOptional(policy.OwnerPrincipalId);
        if (mode == ReportingGovernanceAccessMode.Private && owner is null)
        {
            owner = accessContext.ActorPrincipalId!.Trim();
        }

        var canonicalPolicy = JsonSerializer.Serialize(new
        {
            mode,
            owner,
            principals,
            companyId = NormalizeOptional(policy.CompanyId) ?? accessContext.CompanyId!.Trim(),
            policy.AllowOwnerAccess
        });
        return new ReportingAccessScope(
            $"report-template:{templateId.Name}:access",
            templateId.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            mode,
            owner,
            principals,
            Sha256(canonicalPolicy));
    }

    private static string NormalizeSourceId(ReportingTemplateMetadata template, string? sourceId)
    {
        var normalized = NormalizeOptional(sourceId);
        if (normalized is not null)
        {
            return normalized;
        }

        return template.ReportWriterGrids is { Count: > 0 }
            ? throw new InvalidOperationException("A server-owned dataset source is required to certify this report.")
            : "server-accounting-read-model";
    }

    private static string CanonicalizeRow(IReadOnlyDictionary<string, string> row) =>
        JsonSerializer.Serialize(row
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal));

    private static void RequireBoundScope(ReportAccessQueryContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ActorPrincipalId)
            || string.IsNullOrWhiteSpace(context.TenantId)
            || string.IsNullOrWhiteSpace(context.CompanyId))
        {
            throw new UnauthorizedAccessException(
                "A server-resolved actor, tenant, and company scope is required to certify a reporting run.");
        }
    }

    private static string NormalizeRequired(string? value, string label) =>
        NormalizeOptional(value) ?? throw new InvalidOperationException($"A {label} is required to certify the reporting snapshot.");

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
