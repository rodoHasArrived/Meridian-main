using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

public sealed record SecurityRateRepairFrozenItem(Guid SecurityId, long BaseVersion, string DisplayName, string AssetClass, string? RawValue);
public sealed record SecurityRateRepairManifest(
    string TenantId, string? CompanyId, string Maker, DateTimeOffset CreatedAt,
    string CommandSha256, string EvidenceSha256, CreateSecurityRateRepairBatchRequest Request,
    IReadOnlyList<SecurityRateRepairFrozenItem> Items);
public sealed record SecurityRateRepairProgress(
    Guid SecurityId, string State, Guid WorkflowId, Guid? RevisionId = null,
    string? ImpactReceiptId = null, string? ApprovalReceiptId = null, string? ErrorCode = null, string? Error = null,
    string? SubmittedBy = null, string? AssignedReviewer = null);
public sealed record SecurityRateRepairRetainedBatch(
    SecurityRateRepairManifest Manifest, long Version, IReadOnlyList<SecurityRateRepairProgress> Items);

/// <summary>Durable immutable roster and separately mutable, resumable per-record outcomes.</summary>
public interface ISecurityRateRepairStore
{
    Task<SecurityRateRepairRetainedBatch> CreateAsync(SecurityRateRepairManifest manifest, CancellationToken ct = default);
    Task<SecurityRateRepairRetainedBatch?> GetAsync(Guid batchId, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityRateRepairRetainedBatch>> ListAsync(string tenantId, CancellationToken ct = default);
    Task<IAsyncDisposable> AcquireCommandLeaseAsync(Guid batchId, string tenantId, long expectedVersion, CancellationToken ct = default);
    Task ExecuteItemAsync(Guid batchId, string tenantId, Guid securityId,
        Func<SecurityRateRepairProgress, Task<SecurityRateRepairProgress>> operation, CancellationToken ct = default);
}

public sealed record SecurityRateRepairEvidence(string EvidenceId, string ContentSha256);

/// <summary>Resolves owned retained bytes and hashes them on the server; client hashes are never authoritative.</summary>
public interface ISecurityRateRepairEvidenceResolver
{
    Task<SecurityRateRepairEvidence> ResolveAsync(string evidenceId, string tenantId, CancellationToken ct = default);
}

public sealed class SecurityRateRepairConflictException(string message) : InvalidOperationException(message);
