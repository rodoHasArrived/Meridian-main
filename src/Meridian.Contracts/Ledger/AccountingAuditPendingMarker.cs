namespace Meridian.Contracts.Ledger;

/// <summary>
/// A declared-but-unconfirmed audit append: the event a mutation is about to be audited with,
/// recorded durably <b>before</b> the mutation runs.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> The accounting configuration and manual-journal lifecycles save the
/// mutation and then, as a separate operation, append the audit event. An append that fails after the
/// mutation commits leaves a perfectly valid hash chain that simply omits the mutation — and
/// tamper-evidence over a record that was never written is not tamper-evidence. The chain proves
/// nobody edited what is there; it says nothing about what is missing.</para>
///
/// <para><b>Why a marker rather than a shared transaction.</b> The configuration store and the audit
/// store are separate interfaces, separately resolved, and in the file posture they are separate
/// artifacts on disk. There is no transaction to share. A marker written first turns an invisible
/// gap into a detectable one: an outstanding marker is proof that a mutation-and-audit pair was
/// interrupted, and the retained hashes say which side of it landed.</para>
/// </remarks>
/// <param name="AuditEvent">The fully-formed event the completed mutation is to be audited with.</param>
/// <param name="DeclaredAtUtc">When the intent was recorded, for operator diagnostics.</param>
public sealed record AccountingAuditPendingMarker(
    AccountingActionAuditEventDto AuditEvent,
    DateTimeOffset DeclaredAtUtc);

/// <summary>How an outstanding marker was resolved.</summary>
public enum AccountingAuditRecoveryOutcome
{
    /// <summary>No marker was outstanding.</summary>
    Nothing,

    /// <summary>The audit event was already retained; the marker was stale and has been cleared.</summary>
    AlreadyAudited,

    /// <summary>The mutation had landed without its audit event, which has now been appended.</summary>
    AuditReplayed,

    /// <summary>The mutation never landed, so there was nothing to audit and the marker was discarded.</summary>
    MutationDiscarded,
}

/// <summary>What a recovery pass found and did.</summary>
public sealed record AccountingAuditRecoveryResult(
    AccountingAuditRecoveryOutcome Outcome,
    Guid? AuditEventId = null,
    string? Detail = null);

/// <summary>
/// Raised when an outstanding marker cannot be resolved because the retained state matches neither
/// the mutation's before-state nor its after-state.
/// </summary>
/// <remarks>
/// Deliberately an incident rather than a guess. Reaching here means something changed the workspace
/// between the interrupted mutation and this recovery, so neither replaying the audit event nor
/// discarding it states the truth — and quietly picking one would put a false record into a log whose
/// only purpose is being trustworthy.
/// </remarks>
public sealed class AccountingAuditRecoveryException : Exception
{
    public AccountingAuditRecoveryException(Guid auditEventId, string message)
        : base(message)
    {
        AuditEventId = auditEventId;
    }

    public Guid AuditEventId { get; }
}

/// <summary>
/// Durable storage for the pending-audit marker. One marker at a time: the accounting mutation paths
/// serialize on the store they write to, so a second outstanding marker would itself be a defect.
/// </summary>
public interface IAccountingAuditPendingMarkerStore
{
    /// <summary>The outstanding marker, or null when the last mutation-and-audit pair completed.</summary>
    Task<AccountingAuditPendingMarker?> ReadAsync(CancellationToken ct = default);

    /// <summary>Records the intent to audit, before the mutation it describes runs.</summary>
    Task WriteAsync(AccountingAuditPendingMarker marker, CancellationToken ct = default);

    /// <summary>
    /// Clears the marker for <paramref name="auditEventId"/>. A no-op when the outstanding marker is
    /// for a different event, so a late clear cannot erase a newer intent.
    /// </summary>
    Task ClearAsync(Guid auditEventId, CancellationToken ct = default);
}
