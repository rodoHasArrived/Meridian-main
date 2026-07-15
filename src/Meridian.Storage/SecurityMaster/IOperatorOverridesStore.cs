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
/// Records a reviewer's approve/reject decision for a security's pending operator overrides,
    /// transitioning the persisted approval status and appending to the audit trail. Returns
    /// <see langword="null"/> when no override record exists for <paramref name="securityId"/> (there
    /// is nothing to review). Implementations must persist the decision so a subsequent
    /// <see cref="GetAsync"/> reflects it.
    /// </summary>
    Task<OperatorOverridesDto?> RecordApprovalDecisionAsync(
        Guid securityId,
        OperatorOverrideApprovalDecisionRequest request,
        string reviewer,
        CancellationToken ct = default);

    /// <summary>
/// Records a reviewer's Approved/Rejected decision on the current override overlay, stamping the
    /// reviewer identity and time and appending a durable audit entry. Throws
    /// <see cref="InvalidOperationException"/> when no override row exists or it is not Pending, and
    /// <see cref="ArgumentException"/> when the decision is not Approved/Rejected or the reviewer is blank.
    /// </summary>
    Task<OperatorOverridesDto> RecordApprovalDecisionAsync(
        Guid securityId,
        OperatorOverrideDecisionRequest request,
        CancellationToken ct = default);
}
