using System.Collections.Immutable;

namespace Meridian.Reporting;

/// <summary>
/// Tenant-scoped content address for an immutable reporting artifact.
/// </summary>
public sealed record ReportingArtifactIdentity(
    string TenantId,
    string ContentHashSha256);

/// <summary>
/// Bytes to retain in the reporting artifact store. The store computes the content address;
/// callers cannot supply or override it.
/// </summary>
public sealed record ReportingArtifactWriteRequest(
    string TenantId,
    ReadOnlyMemory<byte> Content);

/// <summary>
/// Result of retaining artifact bytes. Repeated writes of the same bytes for the same tenant are
/// idempotent and return the existing content address.
/// </summary>
public sealed record ReportingArtifactWriteResult(
    ReportingArtifactIdentity Identity,
    long ByteSize,
    DateTimeOffset StoredAtUtc,
    bool AlreadyExisted);

/// <summary>
/// Verified immutable reporting artifact bytes.
/// </summary>
public sealed record ReportingArtifactReadResult(
    ReportingArtifactIdentity Identity,
    long ByteSize,
    DateTimeOffset StoredAtUtc,
    byte[] Content);

/// <summary>
/// Content-addressed persistence port for immutable reporting artifact bytes.
/// </summary>
public interface IReportingArtifactStore
{
    Task<ReportingArtifactWriteResult> StoreAsync(
        ReportingArtifactWriteRequest request,
        CancellationToken ct = default);

    Task<ReportingArtifactReadResult> ReadAsync(
        ReportingArtifactIdentity identity,
        CancellationToken ct = default);
}

/// <summary>
/// Raised when a requested tenant-scoped reporting artifact does not exist. Callers must not
/// regenerate released content as a fallback.
/// </summary>
public sealed class ReportingArtifactNotFoundException : KeyNotFoundException
{
    public ReportingArtifactNotFoundException(ReportingArtifactIdentity identity)
        : base($"Reporting artifact '{identity.ContentHashSha256}' was not found for tenant '{identity.TenantId}'.")
    {
        Identity = identity;
    }

    public ReportingArtifactIdentity Identity { get; }
}

/// <summary>
/// Raised when retained artifact bytes no longer match their immutable content address or size.
/// </summary>
public sealed class ReportingArtifactIntegrityException : IOException
{
    public ReportingArtifactIntegrityException(ReportingArtifactIdentity identity, string reason)
        : base($"Reporting artifact '{identity.ContentHashSha256}' for tenant '{identity.TenantId}' failed integrity verification: {reason}")
    {
        Identity = identity;
    }

    public ReportingArtifactIdentity Identity { get; }
}

/// <summary>An immutable catalog record that binds one artifact identity to its governed scope.</summary>
public sealed record ReportingRetainedArtifactRecord(
    string PackageId,
    string RunId,
    string SeriesId,
    int Revision,
    ReportingOperationalScope Scope,
    ReportingAccessScope Access,
    ReportingCertifiedSnapshotScope Snapshot,
    string ManifestId,
    string ManifestHash,
    string ArtifactId,
    string FileName,
    string ContentType,
    ReportingArtifactIdentity Identity,
    long ByteLength,
    DateTimeOffset StoredAtUtc);

/// <summary>All immutable catalog records retained for one tenant-bound report package.</summary>
public sealed record ReportingRetainedArtifactPackage(
    string PackageId,
    ImmutableArray<ReportingRetainedArtifactRecord> Artifacts);

public sealed record ReportingArtifactCatalogWriteResult(bool AlreadyExisted);

/// <summary>
/// Immutable catalog boundary. Implementations must add an entire package atomically, treat an
/// exact retry as idempotent, and reject any attempt to replace existing package metadata.
/// </summary>
public interface IReportingArtifactCatalog
{
    ValueTask<ReportingArtifactCatalogWriteResult> AddPackageAsync(
        ReportingRetainedArtifactPackage package,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one artifact through its complete tenant/package/artifact key. Production callers
    /// must use this overload so package identifiers cannot cross tenant boundaries.
    /// </summary>
    ValueTask<ReportingRetainedArtifactRecord?> GetArtifactAsync(
        string tenantId,
        string packageId,
        string artifactId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compatibility-only overload retained for source stability. It fails closed because an
    /// unscoped lookup cannot prove tenant authority.
    /// </summary>
    ValueTask<ReportingRetainedArtifactRecord?> GetArtifactAsync(
        string packageId,
        string artifactId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<ReportingRetainedArtifactRecord?>(
            new NotSupportedException(
                "Tenant-scoped reporting artifact lookup is required. Supply tenantId, packageId, and artifactId."));
}

public enum ReportingArtifactAuditAction
{
    ArtifactRetained,
    RetentionVerified,
    ContentAccessed,
    AccessDenied,
    IntegrityFailure
}

public sealed record ReportingArtifactAuditEvent(
    string EventId,
    DateTimeOffset OccurredAtUtc,
    ReportingArtifactAuditAction Action,
    string ActorId,
    string ActorTenantId,
    string TargetTenantId,
    string PackageId,
    string ArtifactId,
    string? ContentHashSha256,
    string CorrelationId,
    string? Reason);

/// <summary>Receipt from an append-only, hash-chained audit implementation.</summary>
public sealed record ReportingArtifactAuditReceipt(
    string EventId,
    long Sequence,
    string? PreviousHash,
    string Hash);

/// <summary>
/// Mandatory immutable audit boundary. Implementations append and hash-chain events; they must
/// never update or delete an existing receipt.
/// </summary>
public interface IReportingArtifactAuditStore
{
    ValueTask<ReportingArtifactAuditReceipt> AppendAsync(
        ReportingArtifactAuditEvent auditEvent,
        CancellationToken cancellationToken = default);
}

public sealed class ReportingArtifactCatalogIntegrityException : IOException
{
    public ReportingArtifactCatalogIntegrityException(string message) : base(message)
    {
    }
}

public sealed class ReportingArtifactAuditIntegrityException : IOException
{
    public ReportingArtifactAuditIntegrityException(string message) : base(message)
    {
    }
}
