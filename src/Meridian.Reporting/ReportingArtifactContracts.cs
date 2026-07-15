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
