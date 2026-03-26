using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public interface ISecurityMasterEventStore
{
    Task AppendAsync(Guid securityId, long expectedVersion, IReadOnlyList<SecurityMasterEventEnvelope> events, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadSinceSequenceAsync(long sequenceExclusive, int take, CancellationToken ct = default);
    Task<long> GetLatestSequenceAsync(CancellationToken ct = default);

    /// <summary>
    /// Appends one or more corporate action events for a security.
    /// </summary>
    Task AppendCorporateActionAsync(CorporateActionDto action, CancellationToken ct = default);

    /// <summary>
    /// Returns all corporate action events for a security in ascending ex-date order.
    /// </summary>
    Task<IReadOnlyList<CorporateActionDto>> LoadCorporateActionsAsync(Guid securityId, CancellationToken ct = default);
}
