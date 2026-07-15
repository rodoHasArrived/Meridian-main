using System.Collections.Concurrent;
using Meridian.Identity.Auth;

namespace Meridian.Audit.Compliance;

public interface ICompliancePolicyEngine
{
    (bool Allowed, string Reason) Evaluate(ActorContext actor, ComplianceActionRequest request);
}

public sealed class CompliancePolicyEngine : ICompliancePolicyEngine
{
    // Sensitive actions gate on Meridian.Identity permissions so compliance gating stays aligned
    // with the platform role system (roles arrive as UserRole names from the login session).
    // The action-to-permission mapping matches Meridian.FSharp.Operations.SensitiveActionPolicy.
    private static readonly Dictionary<SensitiveAction, UserPermission> RequiredPermissions = new()
    {
        [SensitiveAction.RuleEdit] = UserPermission.ModifyConfig,
        [SensitiveAction.BreakClosure] = UserPermission.ManageDirectLending,
        [SensitiveAction.PaymentRelease] = UserPermission.ManageDirectLending,
        [SensitiveAction.OverrideApproval] = UserPermission.AdminMaintenance
    };

    public (bool Allowed, string Reason) Evaluate(ActorContext actor, ComplianceActionRequest request)
    {
        if (!RequiredPermissions.TryGetValue(request.Action, out var requiredPermission))
        {
            return (false, "Unknown action.");
        }

        if (!actor.Roles.Any(role => RoleGrants(role, requiredPermission)))
        {
            return (false, "Missing required privileged role.");
        }

        if (!string.IsNullOrWhiteSpace(request.RequestedByActorId) &&
            request.RequestedByActorId.Equals(actor.ActorId, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Segregation of duties violation: requester cannot self-approve.");
        }

        if (request.Action is SensitiveAction.PaymentRelease or SensitiveAction.OverrideApproval)
        {
            if (!actor.MfaSatisfied)
            {
                return (false, "Step-up requirement failed: MFA required.");
            }

            var approvers = request.AdditionalApproverIds ?? [];
            if (approvers.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
            {
                return (false, "Step-up requirement failed: dual approval required.");
            }
        }

        return (true, "Allowed");
    }

    private static bool RoleGrants(string role, UserPermission required)
        => Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            && RolePermissions.HasPermission(parsed, required);
}

public sealed class ImmutableAuditLogService
{
    private readonly ConcurrentQueue<AuditEvent> _events = new();

    // The append sequence (read tail hash → compute hash → enqueue) must be atomic.
    // ConcurrentQueue makes each individual operation thread-safe, but two concurrent
    // callers would otherwise read the same predecessor hash and chain off it, silently
    // forking the tamper-evident hash chain and breaking VerifyIntegrity.
    private readonly Lock _appendLock = new();

    public AuditEvent Append(ActorContext actor, ComplianceActionRequest request)
    {
        lock (_appendLock)
        {
            return AppendCore(actor, request);
        }
    }

    private AuditEvent AppendCore(ActorContext actor, ComplianceActionRequest request)
    {
        var previousHash = _events.LastOrDefault()?.Hash;
        var pending = new AuditEvent(
            EventId: $"audit-{Guid.NewGuid():N}",
            OccurredAtUtc: DateTimeOffset.UtcNow,
            ActorId: actor.ActorId,
            Action: request.Action,
            ObjectType: request.ObjectType,
            ObjectId: request.ObjectId,
            BeforeStateJson: request.BeforeStateJson,
            AfterStateJson: request.AfterStateJson,
            SourceIp: actor.SourceIp,
            DeviceId: actor.DeviceId,
            CorrelationId: request.CorrelationId,
            EntityId: request.EntityId,
            Hash: string.Empty,
            PreviousHash: previousHash);

        var hashed = pending with { Hash = AuditHash.Compute(pending) };
        _events.Enqueue(hashed);
        return hashed;
    }

    public IReadOnlyList<AuditEvent> GetAll() => _events.ToArray();

    public bool VerifyIntegrity()
    {
        string? expectedPrevious = null;
        foreach (var evt in _events)
        {
            if (!string.Equals(expectedPrevious, evt.PreviousHash, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(AuditHash.Compute(evt with { Hash = string.Empty }), evt.Hash, StringComparison.Ordinal))
            {
                return false;
            }

            expectedPrevious = evt.Hash;
        }

        return true;
    }
}

public sealed class AccessReviewService
{
    private readonly List<AccessReviewRecord> _reviews = [];

    public AccessReviewRecord ReviewDormantPermissions(string actorId, string reviewedBy, string[] currentRoles, DateTimeOffset lastUsedAtUtc)
    {
        var isDormant = lastUsedAtUtc < DateTimeOffset.UtcNow.AddDays(-90);
        var removed = isDormant ? currentRoles : [];
        var review = new AccessReviewRecord(
            ReviewId: $"access-{Guid.NewGuid():N}",
            ReviewedAtUtc: DateTimeOffset.UtcNow,
            ReviewedBy: reviewedBy,
            ActorId: actorId,
            RemovedRoles: removed,
            Reason: isDormant ? "Dormant permissions cleanup." : "No dormant permissions.");

        _reviews.Add(review);
        return review;
    }

    public IReadOnlyList<AccessReviewRecord> GetReviews() => _reviews;
}
