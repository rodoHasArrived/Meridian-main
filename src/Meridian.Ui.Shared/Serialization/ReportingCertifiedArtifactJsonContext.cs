using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Infrastructure.Contracts;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;

namespace Meridian.Ui.Shared.Serialization;

internal sealed record ReportingRetainedPreviewPrimaryArtifact(
    string ArtifactId,
    string FileName,
    string ContentType);

internal sealed record ReportingRetainedPreviewDocument(
    string SchemaVersion,
    string RunId,
    string SeriesId,
    string TemplateId,
    VersionedReportTemplateIdDto ResolvedTemplate,
    DateOnly AsOfDate,
    string SourceCheckpointId,
    string CertifiedSnapshotId,
    int CertifiedDatasetRowCount,
    string CertifiedDatasetHashSha256,
    int PreviewRowCount,
    string[] Columns,
    Dictionary<string, string>[] Rows,
    ReportingRetainedPreviewPrimaryArtifact PrimaryArtifact);

internal sealed record ReportingEvidenceVaultDocument(
    string SchemaVersion,
    string RunId,
    DateOnly AsOfDate,
    ReportingAuthoritativeSourceCheckpoint Source,
    ReportingCertifiedSnapshotScope Snapshot,
    ReportingRunReadinessDto Readiness,
    ReportingSectionManifest[] Sections,
    string CertifiedDatasetHashSha256,
    Dictionary<string, string>[] CertifiedDatasetRows);

internal sealed record ReportingEvidenceAppendixDocument(
    string SchemaVersion,
    string RunId,
    string[] Evidence,
    ReportingLineageReference[] Lineage);

internal sealed record ReportingRetainedGridColumn(
    string Key,
    string Label,
    string Role);

internal sealed record ReportingRetainedGridRow(
    string RowKey,
    Dictionary<string, string> Values);

internal sealed record ReportingRetainedGridDocument(
    string SchemaVersion,
    string GridId,
    string Title,
    string Kind,
    ReportingRetainedGridColumn[] Columns,
    ReportingRetainedGridRow[] Rows,
    string[] Warnings,
    ReportWriterGridLineageDto? Lineage,
    IReadOnlyList<ReportWriterGridDataDictionaryFieldDto>? DataDictionary,
    IReadOnlyList<ReportWriterGridValidationCheckDto>? ValidationChecks,
    ReportWriterChartRenderDto? Chart);

/// <summary>
/// Source-generated JSON metadata for deterministic, retained reporting artifacts. The configured
/// web defaults intentionally match the artifact producer's historical canonical JSON options.
/// </summary>
[ImplementsAdr(
    "ADR-014",
    "Source-generated JSON metadata for retained reporting manifests and canonical artifact payloads")]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = false)]
[JsonSerializable(typeof(ReportingRetainedManifestDocument))]
[JsonSerializable(typeof(ReportingRetainedPreviewDocument))]
[JsonSerializable(typeof(ReportingEvidenceVaultDocument))]
[JsonSerializable(typeof(ReportingEvidenceAppendixDocument))]
[JsonSerializable(typeof(ReportingRetainedGridDocument))]
internal sealed partial class ReportingCertifiedArtifactJsonContext : JsonSerializerContext;
