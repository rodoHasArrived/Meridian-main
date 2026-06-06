using System.Collections.Concurrent;

namespace Meridian.Audit.Compliance;

public interface ICompliancePolicyEngine
{
    (bool Allowed, string Reason) Evaluate(ActorContext actor, ComplianceActionRequest request);
}

public sealed class CompliancePolicyEngine : ICompliancePolicyEngine
{
    private static readonly Dictionary<SensitiveAction, string[]> RequiredRoles = new()
    {
        [SensitiveAction.RuleEdit] = ["RulesAdmin"],
        [SensitiveAction.BreakClosure] = ["ReconciliationOfficer"],
        [SensitiveAction.PaymentRelease] = ["TreasuryOperator"],
        [SensitiveAction.OverrideApproval] = ["OverrideApprover"]
    };

    public (bool Allowed, string Reason) Evaluate(ActorContext actor, ComplianceActionRequest request)
    {
        if (!RequiredRoles.TryGetValue(request.Action, out var requiredRoles))
        {
            return (false, "Unknown action.");
        }

        if (!actor.Roles.Intersect(requiredRoles).Any())
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
}

public sealed class ImmutableAuditLogService
{
    private readonly ConcurrentQueue<AuditEvent> _events = new();

    public AuditEvent Append(ActorContext actor, ComplianceActionRequest request)
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
