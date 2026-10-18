using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Storage abstraction for operator-supplied per-security override values. Implementations
/// must be safe to call concurrently; the canonical implementation is backed by Postgres
/// and uses upsert semantics keyed by security id.
/// </summary>
public interface IOperatorOverridesStore
{
    Task<OperatorOverridesDto?> GetAsync(Guid securityId, CancellationToken ct = default);

    Task<OperatorOverridesDto> PatchAsync(
        Guid securityId,
        OperatorOverridesPatchRequest request,
        string updatedBy,
        CancellationToken ct = default,
        long? expectedCanonicalVersion = null);

    /// <summary>
    /// Records a reviewer's Approved/Rejected decision on the current override overlay, stamping the
    /// reviewer identity and time and appending a durable audit entry. Throws
    /// <see cref="InvalidOperationException"/> when no override row exists or it is not Pending, and
    /// <see cref="ArgumentException"/> when the decision is not Approved/Rejected or the reviewer is blank.
    /// </summary>
    Task<OperatorOverridesDto> RecordApprovalDecisionAsync(
        Guid securityId,
        OperatorOverrideDecision decision,
        CancellationToken ct = default);
}

/// <summary>
/// Internal store command for an operator-override approval decision. Unlike the public
/// <see cref="OperatorOverrideDecisionRequest"/>, it carries the <see cref="Reviewer"/> identity that
/// the API layer derives from the authenticated principal before calling the store.
/// </summary>
public sealed record OperatorOverrideDecision(
    SecurityOverrideApprovalStatusDto Decision,
    string Reviewer,
    string? Comment = null)
{
    /// <summary>
    /// True only when the decision re-records a decision an independent reviewer already made
    /// (a compensating revert or a discard restored the overlay to values that reviewer decided).
    /// Restorations are exempt from the self-approval rule because they assert no new review;
    /// the governance log records the flag. API callers never set it.
    /// </summary>
    public bool RestoresPriorDecision { get; init; }
}

/// <summary>The independent-review rule for operator-override decisions.</summary>
public static class OperatorOverrideReviewRules
{
    /// <summary>
    /// Whether <paramref name="reviewer"/> is the actor who staged the overlay's current values.
    /// Actor identities compare case-insensitively after trimming.
    /// </summary>
    public static bool IsSelfApproval(string? stagedBy, string? reviewer)
        => !string.IsNullOrWhiteSpace(stagedBy)
           && !string.IsNullOrWhiteSpace(reviewer)
           && string.Equals(stagedBy.Trim(), reviewer.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fails when <paramref name="decision"/> approves values the reviewer staged themselves. A
    /// rejection (withdrawing one's own staged values) and a restoration of a prior independent
    /// decision are allowed.
    /// </summary>
    public static void EnsureIndependentReview(Guid securityId, OperatorOverridesDto overlay, OperatorOverrideDecision decision)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Decision == SecurityOverrideApprovalStatusDto.Approved
            && !decision.RestoresPriorDecision
            && IsSelfApproval(overlay.UpdatedBy, decision.Reviewer))
        {
            throw new OperatorOverrideSelfApprovalException(securityId, decision.Reviewer);
        }
    }
}

/// <summary>Raised when a reviewer attempts to approve operator-override values they staged.</summary>
public sealed class OperatorOverrideSelfApprovalException : InvalidOperationException
{
    public OperatorOverrideSelfApprovalException(Guid securityId, string reviewer)
        : base($"Operator overrides for security '{securityId:D}' were staged by '{reviewer}', who cannot also approve them; an independent reviewer must decide.")
    {
        SecurityId = securityId;
        Reviewer = reviewer;
    }

    public Guid SecurityId { get; }

    public string Reviewer { get; }
}

/// <summary>
/// Thrown when an overlay patch was submitted for a canonical Security Master version that no
/// longer matches the stream version observed while the patch transaction held the stream lock.
/// </summary>
public sealed class OperatorOverrideCanonicalVersionConflictException : InvalidOperationException
{
    public OperatorOverrideCanonicalVersionConflictException(Guid securityId, long expectedVersion, long currentVersion)
        : base($"Security stream version conflict for {securityId}. Expected {expectedVersion}, actual {currentVersion}.")
    {
        SecurityId = securityId;
        ExpectedVersion = expectedVersion;
        CurrentVersion = currentVersion;
    }

    public Guid SecurityId { get; }
    public long ExpectedVersion { get; }
    public long CurrentVersion { get; }
}
