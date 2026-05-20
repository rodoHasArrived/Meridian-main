using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.Storage;
using Meridian.Storage.Archival;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityValidationSnapshotStore
{
    Task<SecurityValidationSnapshotDto> RecordAsync(
        SecurityValidationReportDto report,
        SecurityValidationSnapshotRequestDto request,
        CancellationToken ct = default);
}

public sealed class FileSecurityValidationSnapshotStore : ISecurityValidationSnapshotStore
{
    private readonly string _rootDirectory;

    public FileSecurityValidationSnapshotStore(StorageOptions storageOptions)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _rootDirectory = Path.Combine(
            storageOptions.RootPath,
            "governance",
            "security-master",
            "validation-snapshots");
    }

    public async Task<SecurityValidationSnapshotDto> RecordAsync(
        SecurityValidationReportDto report,
        SecurityValidationSnapshotRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var recordedAt = DateTimeOffset.UtcNow;
        var reportJson = JsonSerializer.Serialize(
            report,
            SecurityMasterJsonContext.Default.SecurityValidationReportDto);
        var snapshot = new SecurityValidationSnapshotDto(
            SnapshotId: Guid.NewGuid(),
            SecurityId: report.SecurityId,
            Scope: report.Scope,
            Workflow: request.Workflow,
            WorkflowReference: string.IsNullOrWhiteSpace(request.WorkflowReference)
                ? null
                : request.WorkflowReference.Trim(),
            Actor: string.IsNullOrWhiteSpace(request.Actor) ? null : request.Actor.Trim(),
            Reason: request.Reason.Trim(),
            RecordedAtUtc: recordedAt,
            ReportHashSha256: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reportJson))).ToLowerInvariant(),
            Report: report,
            EvidenceLinks: request.EvidenceLinks);

        var path = Path.Combine(_rootDirectory, $"{recordedAt:yyyy-MM}.jsonl");
        var line = JsonSerializer.Serialize(
            snapshot,
            SecurityMasterJsonContext.Default.SecurityValidationSnapshotDto);
        await AtomicFileWriter.AppendLinesAsync(path, [line], ct).ConfigureAwait(false);
        return snapshot;
    }
}

public sealed class NullSecurityValidationSnapshotStore : ISecurityValidationSnapshotStore
{
    public Task<SecurityValidationSnapshotDto> RecordAsync(
        SecurityValidationReportDto report,
        SecurityValidationSnapshotRequestDto request,
        CancellationToken ct = default)
        => Task.FromResult(new SecurityValidationSnapshotDto(
            SnapshotId: Guid.Empty,
            SecurityId: report.SecurityId,
            Scope: report.Scope,
            Workflow: request.Workflow,
            WorkflowReference: request.WorkflowReference,
            Actor: request.Actor,
            Reason: request.Reason,
            RecordedAtUtc: report.EvaluatedAtUtc,
            ReportHashSha256: string.Empty,
            Report: report,
            EvidenceLinks: request.EvidenceLinks));
}
