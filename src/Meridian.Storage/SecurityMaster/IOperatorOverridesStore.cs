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
        CancellationToken ct = default);

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
    string? Comment = null);
