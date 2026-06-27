using System.Collections.Concurrent;
using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Durable lifecycle state for a governed Security Master passport revision
/// (<see cref="SecurityMasterRevisionStateDto"/>: Draft → Submitted → Approved → Published / Rejected).
/// The store is the authority a publish consults to confirm a revision was actually approved through
/// the governed gate, so an arbitrary revision id can never trigger downstream publish side effects.
/// </summary>
public interface ISecurityMasterRevisionStore
{
    /// <summary>Creates a new <see cref="SecurityMasterRevisionStateDto.Draft"/> revision for a security.</summary>
    Task<SecurityMasterRevisionRecord> CreateDraftAsync(Guid securityId, string actor, CancellationToken ct = default);

    /// <summary>
    /// Creates a Draft revision carrying the field-edit metadata (path, effective date, justification)
    /// so a later publish can emit the correct effective-from and changed-field set for downstream
    /// (period-aware / restatement) impact analysis instead of defaulting to publish time.
    /// </summary>
    Task<SecurityMasterRevisionRecord> CreateDraftAsync(
        Guid securityId,
        string actor,
        string fieldPath,
        DateTimeOffset fieldEffectiveFrom,
        string fieldJustification,
        CancellationToken ct = default);

    /// <summary>Returns the revision, or <c>null</c> when no revision with that id exists.</summary>
    Task<SecurityMasterRevisionRecord?> GetAsync(Guid revisionId, CancellationToken ct = default);

    /// <summary>
    /// Atomically transitions a revision from <paramref name="expected"/> to <paramref name="next"/>.
    /// Throws <see cref="SecurityMasterRevisionStateException"/> when the revision is missing or its
    /// current state is not <paramref name="expected"/> (a concurrent or out-of-order transition).
    /// When <paramref name="workflowIdForSubmit"/> is supplied on a Draft→Submitted transition, it is
    /// durably bound to the revision so approval can be restricted to that same workflow lane.
    /// </summary>
    Task<SecurityMasterRevisionRecord> TransitionAsync(
        Guid revisionId,
        SecurityMasterRevisionStateDto expected,
        SecurityMasterRevisionStateDto next,
        string actor,
        Guid? workflowIdForSubmit = null,
        CancellationToken ct = default);
}

/// <summary>Durable record of a passport revision's governed lifecycle state.</summary>
public sealed record SecurityMasterRevisionRecord(
    Guid RevisionId,
    Guid SecurityId,
    SecurityMasterRevisionStateDto State,
    string Actor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? WorkflowId = null,
    string? FieldPath = null,
    DateTimeOffset? FieldEffectiveFrom = null,
    string? FieldJustification = null);

/// <summary>
/// Raised when a revision lifecycle transition is attempted out of order — e.g. publishing a revision
/// that was never approved, or approving one that was never submitted. Endpoints map this to HTTP 409.
/// </summary>
public sealed class SecurityMasterRevisionStateException : Exception
{
    public SecurityMasterRevisionStateException(Guid revisionId, string detail)
        : base($"Security Master revision '{revisionId:D}' cannot transition: {detail}")
    {
        RevisionId = revisionId;
    }

    public Guid RevisionId { get; }
}

/// <summary>
/// Process-local revision lifecycle store. Mirrors the in-memory posture of
/// <c>SecurityMasterConflictService</c>; the durable Postgres-backed implementation is wired in the
/// subsequent slice. State transitions are compare-and-set so concurrent lifecycle calls are safe.
/// </summary>
public sealed class InMemorySecurityMasterRevisionStore : ISecurityMasterRevisionStore
{
    private readonly ConcurrentDictionary<Guid, SecurityMasterRevisionRecord> _revisions = new();

    public Task<SecurityMasterRevisionRecord> CreateDraftAsync(Guid securityId, string actor, CancellationToken ct = default)
        => CreateDraftCore(securityId, actor, fieldPath: null, fieldEffectiveFrom: null, fieldJustification: null);

    public Task<SecurityMasterRevisionRecord> CreateDraftAsync(
        Guid securityId,
        string actor,
        string fieldPath,
        DateTimeOffset fieldEffectiveFrom,
        string fieldJustification,
        CancellationToken ct = default)
        => CreateDraftCore(securityId, actor, fieldPath, fieldEffectiveFrom, fieldJustification);

    private Task<SecurityMasterRevisionRecord> CreateDraftCore(
        Guid securityId,
        string actor,
        string? fieldPath,
        DateTimeOffset? fieldEffectiveFrom,
        string? fieldJustification)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new SecurityMasterRevisionRecord(
            RevisionId: Guid.NewGuid(),
            SecurityId: securityId,
            State: SecurityMasterRevisionStateDto.Draft,
            Actor: actor,
            CreatedAt: now,
            UpdatedAt: now,
            WorkflowId: null,
            FieldPath: fieldPath,
            FieldEffectiveFrom: fieldEffectiveFrom,
            FieldJustification: fieldJustification);
        _revisions[record.RevisionId] = record;
        return Task.FromResult(record);
    }

    public Task<SecurityMasterRevisionRecord?> GetAsync(Guid revisionId, CancellationToken ct = default)
    {
        _revisions.TryGetValue(revisionId, out var record);
        return Task.FromResult(record);
    }

    public Task<SecurityMasterRevisionRecord> TransitionAsync(
        Guid revisionId,
        SecurityMasterRevisionStateDto expected,
        SecurityMasterRevisionStateDto next,
        string actor,
        Guid? workflowIdForSubmit = null,
        CancellationToken ct = default)
    {
        if (!_revisions.TryGetValue(revisionId, out var existing))
        {
            throw new SecurityMasterRevisionStateException(
                revisionId, $"no such revision (expected state {expected}).");
        }

        if (existing.State != expected)
        {
            throw new SecurityMasterRevisionStateException(
                revisionId, $"expected state {expected} but the revision is {existing.State}.");
        }

        // Bind the submitting workflow only on the Draft→Submitted transition; other transitions
        // preserve the existing binding so approval can be restricted to the same lane.
        var workflowId = next == SecurityMasterRevisionStateDto.Submitted && workflowIdForSubmit.HasValue
            ? workflowIdForSubmit
            : existing.WorkflowId;
        var updated = existing with { State = next, Actor = actor, UpdatedAt = DateTimeOffset.UtcNow, WorkflowId = workflowId };

        // Compare-and-set: only the caller whose view still matches the stored record wins; a
        // concurrent transition that already advanced the revision loses and is rejected.
        if (!_revisions.TryUpdate(revisionId, updated, existing))
        {
            throw new SecurityMasterRevisionStateException(
                revisionId, $"the revision changed concurrently (expected state {expected}).");
        }

        return Task.FromResult(updated);
    }
}
