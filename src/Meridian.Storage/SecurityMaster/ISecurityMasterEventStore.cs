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

    /// <summary>
    /// Reports — and when <paramref name="apply"/> is true, rewrites in one transaction —
    /// stored corporate-action EventType values that are non-canonical aliases of the
    /// catalog vocabulary (e.g. legacy "Split" rows becoming "StockSplit"). Values that do
    /// not normalize are reported and left untouched. Stores without rewrite support (the
    /// default) report an empty result.
    /// </summary>
    Task<CorporateActionEventTypeNormalizationResult> NormalizeCorporateActionEventTypesAsync(
        bool apply, CancellationToken ct = default)
        => Task.FromResult(CorporateActionEventTypeNormalizationResult.Empty);
}
