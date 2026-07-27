namespace Meridian.Reporting;

/// <summary>
/// Exact durable authority scope for one statement-reconciliation report workflow.
/// Company identity is part of the key so a tenant-scoped artifact address cannot be
/// mistaken for workflow authorization.
/// </summary>
public sealed record StatementReconciliationReportAuthorityScope(
    string TenantId,
    string CompanyId,
    string WorkflowId);

/// <summary>
/// One logical workflow document mapped to immutable, content-addressed reporting bytes.
/// Mutable documents replace only this mapping; retained blob bytes remain immutable.
/// </summary>
public sealed record StatementReconciliationReportAuthorityDocument(
    StatementReconciliationReportAuthorityScope Scope,
    string DocumentKey,
    ReportingArtifactIdentity Identity,
    long ByteSize,
    bool IsImmutable,
    long Version,
    DateTimeOffset StoredAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Persistence boundary for statement-reconciliation intake, workflow snapshots, evidence,
/// and generated artifacts. Production implementations must survive process restart and enforce
/// the complete tenant/company/workflow key on every operation.
/// </summary>
public interface IStatementReconciliationReportAuthorityStore
{
    /// <summary>
    /// True only when this implementation is a shared, restart-safe production authority.
    /// File-backed compatibility implementations must return false.
    /// </summary>
    bool IsDurableAuthority { get; }

    string StorageKind { get; }

    ValueTask<IAsyncDisposable> AcquireWorkflowLeaseAsync(
        StatementReconciliationReportAuthorityScope scope,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DocumentExistsAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken = default);

    ValueTask<StatementReconciliationReportAuthorityDocument?> GetDocumentAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken = default);

    ValueTask<byte[]?> TryReadDocumentAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retains bytes and writes their logical mapping. Immutable documents allow exact retries
    /// and reject replacement; mutable documents advance their mapping version.
    /// </summary>
    ValueTask<StatementReconciliationReportAuthorityDocument> WriteDocumentAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        ReadOnlyMemory<byte> content,
        bool isImmutable,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<string>> ListDocumentKeysAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKeyPrefix,
        CancellationToken cancellationToken = default);

    /// <summary>Proves that the configured persistence authority is reachable and initialized.</summary>
    ValueTask ProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Raised when the configured statement authority cannot prove durable availability. API callers
/// should fail closed and may retry after operators restore the authority.
/// </summary>
public sealed class StatementReconciliationReportAuthorityUnavailableException
    : InvalidOperationException
{
    public StatementReconciliationReportAuthorityUnavailableException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
