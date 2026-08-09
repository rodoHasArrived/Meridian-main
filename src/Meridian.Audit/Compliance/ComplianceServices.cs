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

    private readonly IComplianceApprovalResolver _approvalResolver;
    private readonly TimeProvider _timeProvider;

    public CompliancePolicyEngine(
        IComplianceApprovalResolver approvalResolver,
        TimeProvider? timeProvider = null)
    {
        _approvalResolver = approvalResolver ?? throw new ArgumentNullException(nameof(approvalResolver));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

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

        if (request.Action is SensitiveAction.PaymentRelease or SensitiveAction.OverrideApproval)
        {
            if (!actor.MfaSatisfied)
            {
                return (false, "Step-up requirement failed: MFA required.");
            }

            if (string.IsNullOrWhiteSpace(request.ApprovalRequestId))
            {
                return (false, "Step-up requirement failed: authoritative approval request required.");
            }

            var approvalRequest = _approvalResolver.Resolve(request.ApprovalRequestId);
            if (approvalRequest is null)
            {
                return (false, "Step-up requirement failed: approval request not found.");
            }

            if (!IsBoundToRequest(approvalRequest, request))
            {
                return (false, "Step-up requirement failed: approval evidence does not match the requested object.");
            }

            if (approvalRequest.ExpiresAtUtc <= _timeProvider.GetUtcNow())
            {
                return (false, "Step-up requirement failed: approval request expired.");
            }

            if (string.Equals(
                    approvalRequest.RequestedByActorId,
                    actor.ActorId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Segregation of duties violation: requester cannot execute their own approved action.");
            }

            if (approvalRequest.Decisions.Any(decision => !decision.Approved))
            {
                return (false, "Step-up requirement failed: approval request was rejected.");
            }

            if (approvalRequest.Decisions.Any(decision =>
                    string.IsNullOrWhiteSpace(decision.ApprovedByActorId) ||
                    string.Equals(
                        decision.ApprovedByActorId,
                        approvalRequest.RequestedByActorId,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        decision.ApprovedByActorId,
                        actor.ActorId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "Segregation of duties violation: approval actors must be independent.");
            }

            var approvers = approvalRequest.Decisions
                .Where(decision => decision.Approved)
                .Select(decision => decision.ApprovedByActorId)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            if (approvers.Count() < 2)
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

    private static bool IsBoundToRequest(
        ComplianceApprovalRequestRecord approval,
        ComplianceActionRequest request)
        => approval.Action == request.Action
           && string.Equals(approval.ObjectType, request.ObjectType, StringComparison.OrdinalIgnoreCase)
           && string.Equals(approval.ObjectId, request.ObjectId, StringComparison.Ordinal)
           && string.Equals(approval.EntityId, NormalizeOptional(request.EntityId), StringComparison.Ordinal);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

    // Set when a persisted record fails to parse while loading. A tamper-evident log must surface
    // that corruption rather than silently dropping the record and reporting the shortened prefix as
    // valid, so VerifyIntegrity fails closed. Written only during construction (single-threaded).
    private bool _persistedRecordCorrupt;

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
    /// <remarks>
    /// Assumes a single writer per <paramref name="persistencePath"/>; the host registers this as a
    /// singleton. The in-process append lock does not coordinate separate processes (or instances)
    /// writing the same file, and because each instance chains off its own in-memory tail, concurrent
    /// writers on a shared path would fork the chain regardless of file locking. Multi-writer support
    /// (re-reading the durable tail under a cross-process lock) is a separate design, out of scope here.
    /// </remarks>
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
                // A persisted record that no longer parses is corruption or tampering, not something
                // to silently drop from a tamper-evident log. Flag it so VerifyIntegrity reports the
                // log invalid even when the surviving records still chain among themselves (e.g. a
                // truncated trailing record whose absence a plain prefix would hide). The records
                // that did load are still enqueued so GetAll can expose them for investigation.
                _persistedRecordCorrupt = true;
                continue;
            }

            if (evt is null)
            {
                // A non-blank line that deserializes to null (e.g. the JSON literal `null`) is a
                // removed or corrupt record rather than a valid event — Deserialize returns null
                // without throwing, so flag it like a parse failure instead of silently dropping it.
                _persistedRecordCorrupt = true;
                continue;
            }

            _events.Enqueue(evt);
        }
    }

    public IReadOnlyList<AuditEvent> GetAll() => _events.ToArray();

    public bool VerifyIntegrity()
    {
        // A record that failed to parse on load broke the durable chain; the surviving in-memory
        // records may still chain among themselves, so report the corruption explicitly here.
        if (_persistedRecordCorrupt)
        {
            return false;
        }

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
