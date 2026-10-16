using Meridian.Contracts.SecurityMaster;
namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// The transaction boundary for one canonical Security Master write: the event append, the
/// projection upsert and the per-field attribution that describes it. An atomic scope commits all
/// of them or none; the passthrough scope used by in-memory hosts runs them in sequence.
/// </summary>
public interface ISecurityMasterCanonicalWriteScope
{
    /// <summary>
    /// Whether writes inside <see cref="ExecuteAsync{T}"/> share one transaction. Callers record
    /// field attribution inside an atomic scope (a failure rolls the whole write back) and fall back
    /// to best-effort post-commit attribution otherwise.
    /// </summary>
    bool IsAtomic { get; }

    Task<T> ExecuteAsync<T>(Guid securityId, Func<Task<T>> operation, CancellationToken ct = default);
}

/// <summary>Runs the operation directly; used where the stores have no shared transaction.</summary>
public sealed class PassthroughSecurityMasterCanonicalWriteScope : ISecurityMasterCanonicalWriteScope
{
    public static PassthroughSecurityMasterCanonicalWriteScope Instance { get; } = new();

    public bool IsAtomic => false;

    public Task<T> ExecuteAsync<T>(Guid securityId, Func<Task<T>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation();
    }
}

/// <summary>PostgreSQL canonical write scope over <see cref="PostgresSecurityMasterMutation.ExecuteCanonicalAsync{T}"/>.</summary>
public sealed class PostgresSecurityMasterCanonicalWriteScope : ISecurityMasterCanonicalWriteScope
{
    private readonly SecurityMasterOptions _options;

    public PostgresSecurityMasterCanonicalWriteScope(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsAtomic => true;

    public Task<T> ExecuteAsync<T>(Guid securityId, Func<Task<T>> operation, CancellationToken ct = default)
        => PostgresSecurityMasterMutation.ExecuteCanonicalAsync(_options, securityId, operation, ct);
}
