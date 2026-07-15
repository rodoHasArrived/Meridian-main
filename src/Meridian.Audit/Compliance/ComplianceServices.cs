using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Identity.Auth;
using Meridian.Storage.Archival;

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
    private static readonly JsonSerializerOptions PersistenceOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly ConcurrentQueue<AuditEvent> _events = new();

    // The append sequence (read tail hash → compute hash → persist → enqueue) must be atomic.
    // ConcurrentQueue makes each individual operation thread-safe, but two concurrent
    // callers would otherwise read the same predecessor hash and chain off it, silently
    // forking the tamper-evident hash chain and breaking VerifyIntegrity.
    private readonly Lock _appendLock = new();

    // When set, every appended event is durably persisted (and the chain is rehydrated from disk on
    // construction) so compliance history survives a restart. Null keeps the service purely
    // in-memory for hosts and tests that do not configure a durable audit path.
    private readonly string? _persistencePath;

    /// <summary>
    /// Creates a purely in-memory audit log. Events are lost on restart; use the
    /// <see cref="ImmutableAuditLogService(string)"/> overload for durable, compliance-grade storage.
    /// </summary>
    public ImmutableAuditLogService()
    {
    }

    /// <summary>
    /// Creates a durable audit log backed by an append-only JSONL file at
    /// <paramref name="persistencePath"/>. Existing events are rehydrated on construction so the
    /// tamper-evident hash chain continues from the last persisted event rather than an empty log.
    /// </summary>
    public ImmutableAuditLogService(string persistencePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistencePath);
        _persistencePath = persistencePath;
        LoadPersistedEvents();
    }

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

        // Persist before publishing to the in-memory queue: a failed durable write must not leave an
        // event visible in memory but absent from disk, which would fork the chain after a restart.
        Persist(hashed);
        _events.Enqueue(hashed);
        return hashed;
    }

    private void Persist(AuditEvent evt)
    {
        if (_persistencePath is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_persistencePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = JsonSerializer.Serialize(evt, PersistenceOptions);

        // Copy-on-write append (temp file → fsync → atomic rename → directory fsync) so a crash
        // mid-write can never leave a torn line that would corrupt the tamper-evident chain. The
        // append is already serialized by _appendLock, so blocking on the async primitive here does
        // not add contention; compliance events are low-volume and privileged.
        AtomicFileWriter.AppendLinesAsync(_persistencePath, [payload], CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private void LoadPersistedEvents()
    {
        if (_persistencePath is null || !File.Exists(_persistencePath))
        {
            return;
        }

        // Stream lines lazily so startup memory stays bounded as the audit log grows.
        foreach (var line in File.ReadLines(_persistencePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            AuditEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize<AuditEvent>(line, PersistenceOptions);
            }
            catch (JsonException)
            {
                // Tolerate a torn trailing line from a legacy non-atomic write; a corrupt interior
                // line simply breaks the chain, which VerifyIntegrity then reports as tampering.
                continue;
            }

            if (evt is not null)
            {
                _events.Enqueue(evt);
            }
        }
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
