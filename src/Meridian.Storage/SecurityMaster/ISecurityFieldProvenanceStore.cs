using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Durable per-field provenance for Security Master records: which source asserted the value at a
/// field path, as of when, under which origin (canonical conflict resolution vs. operator overlay).
/// Conflict resolution writes its rows transactionally with the conflict close (see
/// <c>PostgresSecurityMasterConflictService</c>); this store covers standalone reads and the
/// non-transactional writers.
/// </summary>
public interface ISecurityFieldProvenanceStore
{
    /// <summary>Upserts one attribution row keyed by (security, field path, origin).</summary>
    Task UpsertAsync(SecurityFieldProvenanceRecord record, CancellationToken ct = default);

    /// <summary>All attribution rows for a security, ordered by field path then origin.</summary>
    Task<IReadOnlyList<SecurityFieldProvenanceRecord>> GetAsync(Guid securityId, CancellationToken ct = default);
}
