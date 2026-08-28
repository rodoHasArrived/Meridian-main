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
    Task UpsertAliasAsync(SecurityAliasDto alias, CancellationToken ct = default);
    Task DeactivateProjectionAsync(Guid securityId, DateTimeOffset effectiveTo, long version, CancellationToken ct = default);
    Task<SecurityDetailDto?> GetDetailAsync(Guid securityId, CancellationToken ct = default);
    Task<SecurityProjectionRecord?> GetProjectionAsync(Guid securityId, CancellationToken ct = default);
    Task<SecurityProjectionRecord?> GetByIdentifierAsync(SecurityIdentifierKind kind, string value, string? provider, DateTimeOffset asOfUtc, bool includeInactive, CancellationToken ct = default);
    /// <summary>
    /// Returns projections outside <paramref name="excludedSecurityIds"/> that claim at least one
    /// normalized kind/value pair in <paramref name="identifiers"/>. The caller applies validity-
    /// window overlap because each returned projection carries the complete identifier history.
    /// </summary>
    Task<IReadOnlyList<SecurityProjectionRecord>> FindIdentifierCandidatesAsync(
        IReadOnlyList<SecurityIdentifierDto> identifiers,
        IReadOnlyCollection<Guid> excludedSecurityIds,
        CancellationToken ct = default);
    Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityProjectionRecord>> LoadAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SecurityProjectionRecord>> LoadActiveAsync(CancellationToken ct = default);
    Task<long?> GetCheckpointAsync(string projectionName, CancellationToken ct = default);
    Task SaveCheckpointAsync(string projectionName, long lastGlobalSequence, CancellationToken ct = default);
}
