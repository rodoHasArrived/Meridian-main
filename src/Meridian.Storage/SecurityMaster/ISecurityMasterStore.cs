using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public interface ISecurityMasterStore
{
    Task UpsertProjectionAsync(SecurityProjectionRecord record, CancellationToken ct = default);
    Task PersistProjectionBatchAsync(
        string projectionName,
        long lastGlobalSequence,
        IReadOnlyList<SecurityProjectionRecord> records,
        CancellationToken ct = default);
    /// <summary>
    /// Inserts or updates an alias, returning the row as persisted. <c>created_at</c>/<c>created_by</c>
    /// are immutable recording facts: on conflict the stored values are retained, so the returned DTO
    /// can differ from <paramref name="alias"/> in those two members. Callers must surface the returned
    /// value rather than the one they passed — as-of rebuilds filter on <c>CreatedAt</c>, so a caller
    /// that echoes a freshly stamped creation time would report an identifier as newer than it is.
    /// Returns <c>null</c> only when the store cannot read the row back.
    /// </summary>
    Task<SecurityAliasDto?> UpsertAliasAsync(SecurityAliasDto alias, CancellationToken ct = default);
    Task DeactivateProjectionAsync(Guid securityId, DateTimeOffset effectiveTo, long version, CancellationToken ct = default);
    Task<SecurityDetailDto?> GetDetailAsync(Guid securityId, CancellationToken ct = default);
    Task<SecurityProjectionRecord?> GetProjectionAsync(Guid securityId, CancellationToken ct = default);
    Task<SecurityProjectionRecord?> GetByIdentifierAsync(SecurityIdentifierKind kind, string value, string? provider, DateTimeOffset asOfUtc, bool includeInactive, CancellationToken ct = default);
    Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityProjectionRecord>> LoadAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SecurityProjectionRecord>> LoadActiveAsync(CancellationToken ct = default);
    Task<long?> GetCheckpointAsync(string projectionName, CancellationToken ct = default);
    Task SaveCheckpointAsync(string projectionName, long lastGlobalSequence, CancellationToken ct = default);
}
