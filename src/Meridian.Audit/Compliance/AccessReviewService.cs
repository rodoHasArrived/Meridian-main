using Meridian.Identity;
using Meridian.Identity.Auth;

namespace Meridian.Audit.Compliance;

/// <summary>
/// Provides authoritative effective-role reads and role-removal mutations for access reviews.
/// </summary>
public interface IAccessRoleAssignmentStore
{
    Task<IReadOnlyList<string>> GetAssignedRolesAsync(
        string actorId,
        CancellationToken ct = default);

    Task RemoveRolesAsync(
        string actorId,
        IReadOnlyList<string> roles,
        string performedBy,
        string correlationId,
        CancellationToken ct = default);
}

/// <summary>
/// Applies dormant-access remediation through the canonical user-account store. Meridian accounts
/// have one base role, so removing it demotes the account to ReadOnly and disables the account in
/// the same atomic identity mutation; disabled accounts have no effective role assignment.
/// </summary>
public sealed class UserAccountAccessRoleAssignmentStore(IUserAccountStore accounts)
    : IAccessRoleAssignmentStore
{
    private readonly IUserAccountStore _accounts =
        accounts ?? throw new ArgumentNullException(nameof(accounts));

    public async Task<IReadOnlyList<string>> GetAssignedRolesAsync(
        string actorId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        var account = (await _accounts.GetAccountsAsync(ct).ConfigureAwait(false))
            .SingleOrDefault(candidate =>
                string.Equals(candidate.Username, actorId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"User account '{actorId}' was not found.");

        return account.IsDisabled ? [] : [account.Role];
    }

    public async Task RemoveRolesAsync(
        string actorId,
        IReadOnlyList<string> roles,
        string performedBy,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentException.ThrowIfNullOrWhiteSpace(performedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var account = (await _accounts.GetAccountsAsync(ct).ConfigureAwait(false))
            .SingleOrDefault(candidate =>
                string.Equals(candidate.Username, actorId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"User account '{actorId}' was not found.");
        if (account.IsDisabled || !roles.Contains(account.Role, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        await _accounts.UpsertAsync(
            new UserAccountUpsertRequestDto(
                Username: account.Username,
                Role: nameof(UserRole.ReadOnly),
                RoleProfileName: null,
                PermissionNames: null,
                NewPassword: null,
                PasswordHash: null,
                IsDisabled: true,
                PasswordResetRequired: account.PasswordResetRequired,
                RequestedBy: performedBy.Trim(),
                Rationale: "Verified dormant-access remediation: remove standing role and disable account.",
                CorrelationId: correlationId.Trim(),
                CompanyId: account.CompanyId),
            performedBy.Trim(),
            ct).ConfigureAwait(false);
    }
}

public sealed class AccessReviewService
{
    private static readonly TimeSpan DormancyThreshold = TimeSpan.FromDays(90);

    private readonly IAccessRoleAssignmentStore _roleStore;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _reviewGate = new(1, 1);
    private readonly object _reviewsLock = new();
    private readonly List<AccessReviewRecord> _reviews = [];

    public AccessReviewService(
        IAccessRoleAssignmentStore roleStore,
        TimeProvider? timeProvider = null)
    {
        _roleStore = roleStore ?? throw new ArgumentNullException(nameof(roleStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Assesses dormant access without mutating permissions or claiming that remediation occurred.
    /// </summary>
    public async Task<AccessReviewAssessment> AssessDormantPermissionsAsync(
        string actorId,
        string reviewedBy,
        DateTimeOffset lastUsedAtUtc,
        CancellationToken ct = default)
    {
        ValidateActors(actorId, reviewedBy);
        var roles = NormalizeRoles(
            await _roleStore.GetAssignedRolesAsync(actorId.Trim(), ct).ConfigureAwait(false));
        return BuildAssessment(actorId.Trim(), reviewedBy.Trim(), lastUsedAtUtc, roles);
    }

    /// <summary>
    /// Applies dormant-role remediation and records only removals proven by authoritative readback.
    /// Once mutation begins, verification is non-cancellable so a committed or partial change is not
    /// reported as an unqualified cancellation.
    /// </summary>
    public async Task<AccessReviewRecord> ApplyDormantPermissionRemediationAsync(
        string actorId,
        string reviewedBy,
        DateTimeOffset lastUsedAtUtc,
        CancellationToken ct = default)
    {
        ValidateActors(actorId, reviewedBy);
        await _reviewGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var normalizedActor = actorId.Trim();
            var normalizedReviewer = reviewedBy.Trim();
            var rolesBefore = NormalizeRoles(
                await _roleStore.GetAssignedRolesAsync(normalizedActor, ct).ConfigureAwait(false));
            var assessment = BuildAssessment(
                normalizedActor,
                normalizedReviewer,
                lastUsedAtUtc,
                rolesBefore);
            var now = _timeProvider.GetUtcNow();
            if (!assessment.IsDormant || assessment.CandidateRoles.Count == 0)
            {
                return Retain(new AccessReviewRecord(
                    ReviewId: $"access-{Guid.NewGuid():N}",
                    ReviewedAtUtc: now,
                    ReviewedBy: normalizedReviewer,
                    ActorId: normalizedActor,
                    RolesBefore: rolesBefore,
                    RolesAfter: rolesBefore,
                    RemovedRoles: [],
                    Outcome: AccessReviewOutcome.NoActionRequired,
                    Reason: assessment.IsDormant
                        ? "No remediation was required because the actor has no effective roles."
                        : "No remediation was applied because the actor is not dormant."));
            }

            var reviewId = $"access-{Guid.NewGuid():N}";
            Exception? mutationFailure = null;
            try
            {
                await _roleStore.RemoveRolesAsync(
                    normalizedActor,
                    assessment.CandidateRoles,
                    normalizedReviewer,
                    reviewId,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A store can fail after a partial or committed mutation. Readback below is the only
                // authority for outcome language and for the RemovedRoles evidence field.
                mutationFailure = ex;
            }

            IReadOnlyList<string> rolesAfter;
            try
            {
                rolesAfter = NormalizeRoles(
                    await _roleStore.GetAssignedRolesAsync(
                        normalizedActor,
                        CancellationToken.None).ConfigureAwait(false));
            }
            catch (Exception verificationFailure)
            {
                return Retain(new AccessReviewRecord(
                    ReviewId: reviewId,
                    ReviewedAtUtc: _timeProvider.GetUtcNow(),
                    ReviewedBy: normalizedReviewer,
                    ActorId: normalizedActor,
                    RolesBefore: rolesBefore,
                    RolesAfter: null,
                    RemovedRoles: [],
                    Outcome: AccessReviewOutcome.VerificationFailed,
                    Reason: "Remediation outcome could not be verified; no role removal is claimed.",
                    FailureCode: $"verification:{verificationFailure.GetType().Name}"));
            }

            var removedRoles = rolesBefore
                .Except(rolesAfter, StringComparer.OrdinalIgnoreCase)
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var expectedRemovals = assessment.CandidateRoles.Count;
            var outcome = removedRoles.Length == expectedRemovals
                ? AccessReviewOutcome.RemediationApplied
                : removedRoles.Length > 0
                    ? AccessReviewOutcome.RemediationPartiallyApplied
                    : AccessReviewOutcome.RemediationFailed;
            var reason = BuildOutcomeReason(
                outcome,
                removedRoles.Length,
                expectedRemovals,
                mutationFailure is not null);

            return Retain(new AccessReviewRecord(
                ReviewId: reviewId,
                ReviewedAtUtc: _timeProvider.GetUtcNow(),
                ReviewedBy: normalizedReviewer,
                ActorId: normalizedActor,
                RolesBefore: rolesBefore,
                RolesAfter: rolesAfter,
                RemovedRoles: removedRoles,
                Outcome: outcome,
                Reason: reason,
                FailureCode: mutationFailure is null
                    ? null
                    : $"mutation:{mutationFailure.GetType().Name}"));
        }
        finally
        {
            _reviewGate.Release();
        }
    }

    public IReadOnlyList<AccessReviewRecord> GetReviews()
    {
        lock (_reviewsLock)
        {
            return _reviews.ToArray();
        }
    }

    private AccessReviewAssessment BuildAssessment(
        string actorId,
        string reviewedBy,
        DateTimeOffset lastUsedAtUtc,
        IReadOnlyList<string> assignedRoles)
    {
        var now = _timeProvider.GetUtcNow();
        var isDormant = lastUsedAtUtc < now.Subtract(DormancyThreshold);
        var candidates = isDormant ? assignedRoles.ToArray() : [];
        return new AccessReviewAssessment(
            AssessmentId: $"assessment-{Guid.NewGuid():N}",
            AssessedAtUtc: now,
            ReviewedBy: reviewedBy,
            ActorId: actorId,
            IsDormant: isDormant,
            AssignedRoles: assignedRoles,
            CandidateRoles: candidates,
            Reason: isDormant
                ? "Assessment only: dormant roles are eligible for remediation; no permissions were changed."
                : "Assessment only: the actor is active; no permissions were changed.");
    }

    private AccessReviewRecord Retain(AccessReviewRecord review)
    {
        lock (_reviewsLock)
        {
            _reviews.Add(review);
        }

        return review;
    }

    private static IReadOnlyList<string> NormalizeRoles(IEnumerable<string> roles)
        => roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildOutcomeReason(
        AccessReviewOutcome outcome,
        int removedCount,
        int expectedCount,
        bool storeReportedFailure)
        => outcome switch
        {
            AccessReviewOutcome.RemediationApplied when storeReportedFailure =>
                $"Remediation was applied and verified for {removedCount} dormant role(s), despite a mutation error being reported.",
            AccessReviewOutcome.RemediationApplied =>
                $"Remediation was applied and verified for {removedCount} dormant role(s).",
            AccessReviewOutcome.RemediationPartiallyApplied =>
                $"Remediation was partially applied and verified: {removedCount} of {expectedCount} dormant role(s) were removed.",
            _ => "Remediation failed verification: no dormant roles were removed."
        };

    private static void ValidateActors(string actorId, string reviewedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);
    }
}
