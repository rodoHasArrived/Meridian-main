using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

public enum ReportingRunTrigger
{
    AdHoc,
    Scheduled
}

public enum ReportingRunStatus
{
    Draft,
    InReview,
    Approved,
    Released,
    Failed
}

public enum ReportingTemplateFamily
{
    InvestorStatement,
    SecFilingPacket,
    ShadowNavPack,
    PerformanceReport,
    HoldingsReport,
    CapitalAccountStatement,
    BoardPacket,
    AuditPackage,
    CertifiedDataset,
    CustomReport
}

public enum ReportingApprovalAction
{
    SubmitForReview,
    Approve,
    Release
}

public sealed record ReportingTemplateMetadata(
    string TemplateId,
    ReportingTemplateFamily Family,
    string Name,
    string Version,
    ImmutableArray<string> Sections,
    ImmutableDictionary<string, string> Tags,
    IReadOnlyList<ReportWriterGridDefinitionDto>? ReportWriterGrids = null,
    ReportAccessPolicyDto? AccessPolicy = null,
    IReadOnlyList<ReportTemplateParameterDefinitionDto>? Parameters = null);

public sealed record ReportingLineageReference(
    string SectionId,
    string DatasetSnapshotId,
    string DatasetSnapshotHash,
    string ReconciliationCheckpointId,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Immutable receipt for the durable server-owned source queried before rendering. The source
/// checkpoint is deliberately distinct from the reconciliation/readiness checkpoint: one proves
/// the exact ledger bytes and sequence boundary, while the other proves that those bytes were
/// eligible for final reporting.
/// </summary>
public sealed record ReportingAuthoritativeSourceCheckpoint(
    string SourceKind,
    string SourceId,
    string TenantId,
    string OrganizationId,
    string? CompanyId,
    string FundId,
    string LedgerBookId,
    string AccountingPeriodId,
    string AccountingBasis,
    DateOnly AsOfDate,
    DateTimeOffset CutoffUtc,
    long HighestGlobalSequence,
    int JournalEntryCount,
    int LedgerLineCount,
    string CheckpointId,
    string CheckpointHash,
    DateTimeOffset CapturedAtUtc,
    ImmutableArray<string> EvidenceIds);

public sealed record ReportingOutputManifest(
    string RunId,
    string TemplateId,
    DateOnly AsOfDate,
    ReportingRunStatus Status,
    ImmutableArray<ReportingSectionManifest> Sections,
    ImmutableArray<string> Artifacts,
    int AttemptCount,
    ReportingRunTrigger Trigger,
    string? ScheduleId = null,
    string? FailureReason = null,
    ImmutableArray<ReportingRunReportWriterGridArtifact> ReportWriterGrids = default,
    ImmutableArray<ReportWriterGridRenderDto> RenderedReportWriterGrids = default,
    string? ReportWriterDatasetSourceId = null,
    string? ReportWriterDatasetSourceLabel = null,
    int? ReportWriterDatasetRowCount = null,
    string? BrandingThemeId = null,
    ReportBrandingThemeDto? BrandingTheme = null,
    ReportAccessPolicyDto? AccessPolicy = null,
    string? RunSeriesId = null,
    int? RunAttemptOrdinal = null,
    string? PriorRunId = null,
    string? RetryReason = null,
    ImmutableArray<ReportWriterGridDiffDto> ReportWriterGridDiffs = default,
    VersionedReportTemplateIdDto? ResolvedTemplate = null,
    ReportingRunParametersDto? ResolvedParameters = null,
    ReportingRunReadinessDto? Readiness = null,
    ReportingOperationalScope? OperationalScope = null,
    ReportingAccessScope? ImmutableAccessScope = null,
    ReportingCertifiedSnapshotScope? CertifiedSnapshot = null,
    ReportingAuthoritativeSourceCheckpoint? AuthoritativeSource = null,
    ImmutableArray<IReadOnlyDictionary<string, string>> CertifiedDatasetRows = default,
    CertifiedPartnersCapitalProjection? CertifiedPartnersCapital = null);

public sealed record ReportingRunReportWriterGridArtifact(
    string GridId,
    string Title,
    string Kind,
    string Artifact,
    int DimensionCount,
    int MetricCount,
    int FormulaCount);

public sealed record ReportingSectionManifest(
    string SectionId,
    string DatasetSnapshotId,
    string ReconciliationCheckpointId,
    string Hash,
    ReportingLineageReference Lineage);

public sealed record ReportingJobContract(
    string JobId,
    string TemplateId,
    DateOnly AsOfDate,
    ReportingRunTrigger Trigger,
    int MaxRetries,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? CronExpression = null,
    string? ScheduleId = null,
    IReadOnlyList<IReadOnlyDictionary<string, string>>? DatasetRows = null,
    string? ReportWriterDatasetSourceId = null,
    string? ReportWriterDatasetSourceLabel = null,
    string? BrandingThemeId = null,
    ReportBrandingThemeDto? BrandingTheme = null,
    ReportAccessPolicyDto? AccessPolicy = null,
    string? RetryReason = null,
    bool AllowRestatement = false,
    VersionedReportTemplateIdDto? ResolvedTemplate = null,
    ReportingRunParametersDto? ResolvedParameters = null,
    ReportingRunReadinessDto? Readiness = null,
    ReportingOperationalScope? OperationalScope = null,
    ReportingAccessScope? ImmutableAccessScope = null,
    ReportingCertifiedSnapshotScope? CertifiedSnapshot = null,
    ReportingAuthoritativeSourceCheckpoint? AuthoritativeSource = null,
    string? GovernedRunSeriesId = null);

public sealed record ReportingScheduleContract(
    string ScheduleId,
    string TemplateId,
    string CronExpression,
    DateOnly NextAsOfDate,
    DateTimeOffset DueAtUtc,
    int MaxRetries,
    string RequestedBy);

public sealed record ReportingRunAuditEntry(
    string RunId,
    DateTimeOffset TimestampUtc,
    string Action,
    string Actor,
    string Notes);

public sealed record ReportingRunSnapshot(
    ReportingOutputManifest Manifest,
    IReadOnlyList<ReportingRunAuditEntry> AuditTrail,
    DateTimeOffset UpdatedAtUtc,
    string? CertifiedDatasetHashSha256 = null,
    string? ManifestHashSha256 = null);

public enum ReportingRunCreateClaimStatus
{
    Acquired,
    AlreadyExists,
    LeasedByAnotherOwner,
    Unsupported
}

public sealed record ReportingRunCreateClaimResult(
    ReportingRunCreateClaimStatus Status,
    DateTimeOffset? LeaseExpiresAtUtc = null,
    long LeaseVersion = 0);

public sealed class ReportingRunCreateClaimException : InvalidOperationException
{
    public ReportingRunCreateClaimException(
        string tenantId,
        string runId,
        string message)
        : base(message)
    {
        TenantId = tenantId;
        RunId = runId;
    }

    public string TenantId { get; }

    public string RunId { get; }
}

public interface IReportingRunStore
{
    IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25);
    IReadOnlyList<ReportingRunSnapshot> ListRuns(string tenantId, int limit = 25)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var normalizedTenantId = tenantId.Trim();
        return ListRuns(int.MaxValue)
            .Where(snapshot => string.Equals(
                snapshot.Manifest.OperationalScope?.TenantId,
                normalizedTenantId,
                StringComparison.Ordinal))
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();
    }

    IReadOnlyList<ReportingRunSnapshot> ListRuns(
        string tenantId,
        string? companyId,
        int limit = 25)
        => ListRuns(tenantId, companyId, offset: 0, limit: limit);

    IReadOnlyList<ReportingRunSnapshot> ListRuns(
        string tenantId,
        string? companyId,
        int offset,
        int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (string.IsNullOrWhiteSpace(companyId))
        {
            if (offset >= 200)
            {
                return [];
            }

            return ListRuns(tenantId, Math.Min(200, offset + Math.Clamp(limit, 1, 200)))
                .Skip(offset)
                .Take(Math.Clamp(limit, 1, 200))
                .ToArray();
        }

        var normalizedCompanyId = companyId.Trim();
        return ListRuns(tenantId, int.MaxValue)
            .Where(snapshot => string.Equals(
                snapshot.Manifest.OperationalScope?.CompanyId,
                normalizedCompanyId,
                StringComparison.Ordinal))
            .Skip(offset)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();
    }

    ReportingOutputManifest? GetManifest(string runId);
    ReportingOutputManifest? GetManifest(string tenantId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var scopedMatches = ListRuns(tenantId, 200)
            .Where(snapshot => string.Equals(
                snapshot.Manifest.RunId,
                runId.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (scopedMatches.Length > 0)
        {
            return scopedMatches.Length == 1 ? scopedMatches[0].Manifest : null;
        }

        return GetManifest(runId) is { OperationalScope: { } scope } manifest
        && string.Equals(scope.TenantId, tenantId.Trim(), StringComparison.Ordinal)
            ? manifest
            : null;
    }

    IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId);
    IReadOnlyList<ReportingRunAuditEntry> GetAudit(string tenantId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var scopedMatches = ListRuns(tenantId, 200)
            .Where(snapshot => string.Equals(
                snapshot.Manifest.RunId,
                runId.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (scopedMatches.Length > 0)
        {
            return scopedMatches.Length == 1 ? scopedMatches[0].AuditTrail : [];
        }

        var scoped = GetManifest(tenantId, runId);
        var unscoped = GetManifest(runId);
        return scoped is not null
               && unscoped is not null
               && string.Equals(
                   unscoped.OperationalScope?.TenantId,
                   scoped.OperationalScope?.TenantId,
                   StringComparison.Ordinal)
               && string.Equals(
                   unscoped.OperationalScope?.CompanyId,
                   scoped.OperationalScope?.CompanyId,
                   StringComparison.Ordinal)
            ? GetAudit(runId)
            : [];
    }

    string? GetRevision(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var manifest = GetManifest(runId);
        return manifest is null
            ? null
            : ReportingRunStoreRevision.Compute(manifest, GetAudit(runId));
    }

    string? GetRevision(string tenantId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var manifest = GetManifest(tenantId, runId);
        return manifest is null
            ? null
            : ReportingRunStoreRevision.Compute(
                manifest,
                GetAudit(tenantId, runId));
    }

    Task<ReportingRunCreateClaimResult> TryClaimCreateAsync(
        string tenantId,
        string runId,
        string leaseOwner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "A reporting run create lease must have a positive duration.");
        }

        ct.ThrowIfCancellationRequested();
        return Task.FromResult(
            GetManifest(tenantId.Trim(), runId.Trim()) is null
                ? new ReportingRunCreateClaimResult(
                    ReportingRunCreateClaimStatus.Unsupported)
                : new ReportingRunCreateClaimResult(
                    ReportingRunCreateClaimStatus.AlreadyExists));
    }

    Task<bool> RenewCreateClaimAsync(
        string tenantId,
        string runId,
        string leaseOwner,
        long leaseVersion,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseVersion);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "A reporting run create lease must have a positive duration.");
        }

        ct.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    Task ReleaseCreateClaimAsync(
        string tenantId,
        string runId,
        string leaseOwner,
        long leaseVersion,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseVersion);
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    Task SaveClaimedCreateAsync(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        string leaseOwner,
        long leaseVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseVersion);
        return SaveAsync(manifest, auditTrail, expectedRevision: null, ct);
    }

    Task SaveAsync(ReportingOutputManifest manifest, IReadOnlyList<ReportingRunAuditEntry> auditTrail, CancellationToken ct = default);

    Task SaveAsync(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        string? expectedRevision,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(auditTrail);
        var tenantId = manifest.OperationalScope?.TenantId;
        var currentRevision = string.IsNullOrWhiteSpace(tenantId)
            ? GetRevision(manifest.RunId)
            : GetRevision(tenantId, manifest.RunId);
        var candidateRevision = ReportingRunStoreRevision.Compute(manifest, auditTrail);
        if (currentRevision is null)
        {
            if (expectedRevision is not null)
            {
                throw ReportingRunConcurrencyException.ForMissing(
                    tenantId,
                    manifest.RunId,
                    expectedRevision);
            }
        }
        else if (expectedRevision is null)
        {
            if (ReportingRunStoreRevision.Matches(currentRevision, candidateRevision))
            {
                return Task.CompletedTask;
            }

            throw ReportingRunConcurrencyException.ForConflict(
                tenantId,
                manifest.RunId,
                expectedRevision,
                currentRevision);
        }
        else if (!ReportingRunStoreRevision.Matches(currentRevision, expectedRevision))
        {
            throw ReportingRunConcurrencyException.ForConflict(
                tenantId,
                manifest.RunId,
                expectedRevision,
                currentRevision);
        }
        else if (ReportingRunStoreRevision.Matches(currentRevision, candidateRevision))
        {
            return Task.CompletedTask;
        }

        return SaveAsync(manifest, auditTrail, ct);
    }
}

public static class ReportingRunStoreRevision
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static string Compute(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(auditTrail);
        var normalized = manifest with
        {
            Sections = OrEmpty(manifest.Sections),
            Artifacts = OrEmpty(manifest.Artifacts),
            ReportWriterGrids = OrEmpty(manifest.ReportWriterGrids),
            RenderedReportWriterGrids = OrEmpty(manifest.RenderedReportWriterGrids),
            ReportWriterGridDiffs = OrEmpty(manifest.ReportWriterGridDiffs),
            CertifiedDatasetRows = OrEmpty(manifest.CertifiedDatasetRows)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ReportingRunRevisionPayload(normalized, auditTrail),
            JsonOptions);
        using var document = JsonDocument.Parse(payload);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, document.RootElement);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    public static bool Matches(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> value) =>
        value.IsDefault ? ImmutableArray<T>.Empty : value;

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element
                             .EnumerateObject()
                             .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException(
                    $"Unsupported reporting run revision JSON kind '{element.ValueKind}'.");
        }
    }

    private sealed record ReportingRunRevisionPayload(
        ReportingOutputManifest Manifest,
        IReadOnlyList<ReportingRunAuditEntry> AuditTrail);
}

public sealed class ReportingRunConcurrencyException : InvalidOperationException
{
    private ReportingRunConcurrencyException(
        string? tenantId,
        string runId,
        string? expectedRevision,
        string? actualRevision,
        string message)
        : base(message)
    {
        TenantId = tenantId;
        RunId = runId;
        ExpectedRevision = expectedRevision;
        ActualRevision = actualRevision;
    }

    public string? TenantId { get; }

    public string RunId { get; }

    public string? ExpectedRevision { get; }

    public string? ActualRevision { get; }

    public static ReportingRunConcurrencyException ForConflict(
        string? tenantId,
        string runId,
        string? expectedRevision,
        string actualRevision) =>
        new(
            tenantId,
            runId,
            expectedRevision,
            actualRevision,
            $"Reporting run '{tenantId}/{runId}' changed after it was loaded; expected revision '{expectedRevision ?? "<create>"}', retained revision is '{actualRevision}'. Reload and retry.");

    public static ReportingRunConcurrencyException ForMissing(
        string? tenantId,
        string runId,
        string expectedRevision) =>
        new(
            tenantId,
            runId,
            expectedRevision,
            actualRevision: null,
            $"Reporting run '{tenantId}/{runId}' no longer exists at expected revision '{expectedRevision}'. Reload and retry.");
}

public sealed record ReportingApprovalDecision(
    ReportingApprovalAction Action,
    string Actor,
    string Role,
    string Notes,
    DateTimeOffset DecidedAtUtc);
