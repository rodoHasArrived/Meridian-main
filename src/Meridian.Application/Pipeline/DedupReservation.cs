namespace Meridian.Application.Pipeline;

/// <summary>
/// Trust scope for a dedup identity lookup.
/// </summary>
/// <remarks>
/// Persisted dedup entries are versioned:
/// <list type="bullet">
/// <item><description><b>Version 1 (legacy)</b> entries were recorded before the primary sink
/// confirmed durability for the event. They remain valid for suppressing duplicates on live
/// ingress, but they are untrusted during WAL recovery because the event they describe may never
/// have reached durable primary storage.</description></item>
/// <item><description><b>Version 2</b> entries are written only after the primary sink flushed,
/// so they confirm sink durability and may suppress WAL replay.</description></item>
/// </list>
/// </remarks>
public enum DedupLookupScope
{
    /// <summary>
    /// Live-ingress admission. Any unexpired committed entry (version 1 or version 2)
    /// suppresses the event as a duplicate.
    /// </summary>
    LiveIngress = 0,

    /// <summary>
    /// WAL recovery replay. Only durability-confirmed (version 2) entries suppress the replay.
    /// Legacy version-1 entries are untrusted here: the WAL record must be replayed to the sink,
    /// after which committing the reservation upgrades the identity to version 2.
    /// </summary>
    WalRecovery = 1
}

/// <summary>
/// Outcome classification for <see cref="IDedupStore.TryReserveAsync"/>.
/// </summary>
public enum DedupReservationStatus : byte
{
    /// <summary>
    /// The identity was unclaimed. The caller now holds a pending, memory-only reservation and
    /// must either commit it with <see cref="IDedupStore.CommitDurableAsync"/> after the primary
    /// sink flushed, or release it with <see cref="IDedupStore.Release"/> on failure.
    /// </summary>
    Reserved = 0,

    /// <summary>
    /// A committed ledger entry suppresses this identity in the requested scope.
    /// </summary>
    Duplicate = 1,

    /// <summary>
    /// Another in-flight reservation already holds this identity. The event must be suppressed,
    /// but the identity is not yet durably committed.
    /// </summary>
    PendingElsewhere = 2
}

/// <summary>
/// A pending, memory-only claim on an event identity.
/// </summary>
/// <remarks>
/// Tokens are process-local and never persisted: a crash discards every pending reservation,
/// which is safe because the corresponding WAL records are then replayed (at-least-once) and can
/// re-claim their identities. A reservation is only ever released by the exact token that holds
/// it, so a stale copy cannot release a newer holder's claim.
/// </remarks>
/// <param name="Key">The dedup identity key the reservation claims.</param>
/// <param name="Token">The process-local claim token; <c>0</c> means no reservation.</param>
public readonly record struct DedupReservation(string Key, long Token)
{
    /// <summary>True when this value represents an actual held reservation.</summary>
    public bool IsHeld => Token != 0 && Key is not null;
}

/// <summary>
/// Result of an <see cref="IDedupStore.TryReserveAsync"/> call.
/// </summary>
/// <param name="Status">How the reservation attempt resolved.</param>
/// <param name="Reservation">
/// The held reservation when <paramref name="Status"/> is
/// <see cref="DedupReservationStatus.Reserved"/>; <c>default</c> otherwise.
/// </param>
public readonly record struct DedupReservationResult(DedupReservationStatus Status, DedupReservation Reservation)
{
    /// <summary>The caller holds the reservation and must commit or release it.</summary>
    public bool IsReserved => Status == DedupReservationStatus.Reserved;

    /// <summary>The event must be suppressed (committed duplicate or pending elsewhere).</summary>
    public bool IsSuppressed => Status != DedupReservationStatus.Reserved;
}
