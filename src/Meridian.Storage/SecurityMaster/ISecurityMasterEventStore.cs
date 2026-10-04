using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Thrown when an append was made against a stream version other than the one observed under the
/// stream lock — most often because the security already exists and a caller tried to create it
/// again at version 0.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/>, which is what this condition was raised as
/// before, so existing handlers keep working. It exists so callers can recognise the condition by
/// TYPE: ingest paths used to sniff exception messages for "already exists"/"duplicate", which this
/// message contains neither of, so a re-ingested security was counted as a hard failure instead of a
/// skip.
/// </remarks>
public sealed class SecurityMasterStreamVersionConflictException : InvalidOperationException
{
    public SecurityMasterStreamVersionConflictException(Guid securityId, long expectedVersion, long currentVersion)
        : base($"Security stream version conflict for {securityId}. Expected {expectedVersion}, actual {currentVersion}.")
    {
        SecurityId = securityId;
        ExpectedVersion = expectedVersion;
        CurrentVersion = currentVersion;
    }

    public Guid SecurityId { get; }
    public long ExpectedVersion { get; }
    public long CurrentVersion { get; }

    /// <summary>
    /// True when the append tried to CREATE a stream that already had events — the shape a repeated
    /// ingest of the same security takes.
    /// </summary>
    public bool IsAlreadyCreated => ExpectedVersion == 0 && CurrentVersion > 0;
}

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
