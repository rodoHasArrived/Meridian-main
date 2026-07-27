using Meridian.Domain.Events;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Abstraction over the persistent event deduplication ledger.
/// Implementations must be thread-safe and survive application restarts.
/// </summary>
/// <remarks>
/// The reservation flow is the safe path for callers that persist events to a durable sink:
/// <see cref="TryReserveAsync"/> claims an identity in memory only, <see cref="CommitDurableAsync"/>
/// persists it as durability-confirmed (version 2) after the primary sink flushed, and
/// <see cref="Release"/> abandons the claim when persistence fails. Identities must never be
/// durably recorded before the sink flush they describe, otherwise a crash between the dedup
/// write and the sink flush would suppress the WAL replay of an event that was never stored.
/// </remarks>
public interface IDedupStore
{
    /// <summary>Gets the total number of events checked for duplicates.</summary>
    long TotalChecked { get; }

    /// <summary>Gets the total number of duplicate events detected.</summary>
    long TotalDuplicates { get; }

    /// <summary>Loads persisted dedup state from durable storage on startup.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="evt"/> has been seen before and records it if new.
    /// </summary>
    /// <remarks>
    /// Legacy live-ingress admission check: a miss eagerly records the identity as a
    /// legacy (version 1) entry, i.e. without sink-durability confirmation, so entries created
    /// here are untrusted during WAL recovery. Callers that persist events durably should use
    /// <see cref="TryReserveAsync"/> / <see cref="CommitDurableAsync"/> instead.
    /// </remarks>
    ValueTask<bool> IsDuplicateAsync(MarketEvent evt, CancellationToken ct = default);

    /// <summary>
    /// Attempts to claim the identity of <paramref name="evt"/> for exclusive persistence.
    /// </summary>
    /// <remarks>
    /// Pending reservations are memory-only; nothing is persisted until
    /// <see cref="CommitDurableAsync"/>. In <see cref="DedupLookupScope.LiveIngress"/> scope any
    /// unexpired committed entry suppresses the event; in
    /// <see cref="DedupLookupScope.WalRecovery"/> scope only durability-confirmed (version 2)
    /// entries suppress it, so legacy identities are replayed rather than trusted.
    /// A successful claim must be balanced by exactly one commit or release.
    /// </remarks>
    ValueTask<DedupReservationResult> TryReserveAsync(
        MarketEvent evt,
        DedupLookupScope scope,
        CancellationToken ct = default);

    /// <summary>
    /// Durably commits held reservations as durability-confirmed (version 2) identities and
    /// flushes the backing store.
    /// </summary>
    /// <remarks>
    /// Call only after the primary sink flushed the corresponding events. On failure the pending
    /// reservations remain held so the caller can retry the commit without re-appending the sink.
    /// Reservations whose token is no longer held are skipped (and logged), never re-claimed.
    /// </remarks>
    Task CommitDurableAsync(IReadOnlyList<DedupReservation> reservations, CancellationToken ct = default);

    /// <summary>
    /// Releases a pending reservation so the identity can be claimed again.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the reservation was held by exactly this token and has been released;
    /// <c>false</c> when the token is stale (already committed, released, or re-claimed), in
    /// which case the current holder — if any — is left untouched.
    /// </returns>
    bool Release(in DedupReservation reservation);

    /// <summary>Flushes in-memory state to durable backing storage.</summary>
    Task FlushAsync(CancellationToken ct = default);

    /// <summary>Compacts the backing store by discarding expired entries.</summary>
    Task CompactAsync(CancellationToken ct = default);
}
