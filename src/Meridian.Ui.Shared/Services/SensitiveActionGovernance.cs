using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Application.Composition;
using Meridian.Contracts.Integrity;
using Meridian.Identity.Auth;
using Meridian.FSharp.Operations;

namespace Meridian.Ui.Shared.Services;

public enum SensitiveActionType
{
    RuleEdit,
    BreakClosure,
    PaymentRelease,
    OverrideApproval
}

public sealed record AccessContext(
    string Actor,
    UserRole Role,
    string Team,
    string Entity,
    string SourceIp,
    string DeviceId,
    string CorrelationId,
    bool MfaSatisfied,
    IReadOnlyCollection<string>? SecondaryApprovers = null);

public sealed record PolicyDecision(bool Allowed, string Reason, bool RequiresDualApproval, bool RequiresPrivilegedRole, bool RequiresMfa);

public sealed class SensitiveActionPolicyEngine
{
    public PolicyDecision Evaluate(SensitiveActionType action, AccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var decision = SensitiveActionPolicyInterop.Evaluate(new SensitiveActionPolicyInput
        {
            Action = action.ToString(),
            Actor = context.Actor,
            Role = context.Role,
            Team = context.Team,
            MfaSatisfied = context.MfaSatisfied,
            SecondaryApprovers = context.SecondaryApprovers?.ToArray() ?? []
        });

        return new PolicyDecision(
            decision.Allowed,
            decision.Reason,
            decision.RequiresDualApproval,
            decision.RequiresPrivilegedRole,
            decision.RequiresMfa);
    }
}

public sealed record ImmutableAuditEvent(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Actor,
    SensitiveActionType Action,
    string ObjectId,
    string BeforeJson,
    string AfterJson,
    string SourceIp,
    string DeviceId,
    string CorrelationId,
    string PreviousHash,
    string Hash);

/// <summary>
/// In-memory test/demo audit chain. Production composition uses the durable
/// <c>Meridian.Audit.Compliance.ImmutableAuditLogService</c> instead.
/// </summary>
public sealed class ImmutableAuditLogService : INonProductionOnlyService
{
    private readonly ConcurrentQueue<ImmutableAuditEvent> _events = new();

    public ImmutableAuditEvent Append(SensitiveActionType action, AccessContext context, string objectId, object? beforeState, object? afterState)
    {
        var sequence = _events.Count + 1;
        var previousHash = _events.LastOrDefault()?.Hash ?? "GENESIS";
        var beforeJson = JsonSerializer.Serialize(beforeState);
        var afterJson = JsonSerializer.Serialize(afterState);
        var payload = $"{sequence}|{context.Actor}|{action}|{objectId}|{beforeJson}|{afterJson}|{context.SourceIp}|{context.DeviceId}|{context.CorrelationId}|{previousHash}";
        var hash = Sha256Digest.ComputeUtf8(payload);

        var ev = new ImmutableAuditEvent(
            sequence,
            DateTimeOffset.UtcNow,
            context.Actor,
            action,
            objectId,
            beforeJson,
            afterJson,
            context.SourceIp,
            context.DeviceId,
            context.CorrelationId,
            previousHash,
            hash);

        _events.Enqueue(ev);
        return ev;
    }

    public IReadOnlyList<ImmutableAuditEvent> GetAll() => _events.ToArray();

    public bool VerifyIntegrity()
    {
        var snapshot = _events.ToArray();
        var previousHash = "GENESIS";
        foreach (var item in snapshot)
        {
            var payload = $"{item.Sequence}|{item.Actor}|{item.Action}|{item.ObjectId}|{item.BeforeJson}|{item.AfterJson}|{item.SourceIp}|{item.DeviceId}|{item.CorrelationId}|{previousHash}";
            var expected = Sha256Digest.ComputeUtf8(payload);
            if (!string.Equals(expected, item.Hash, StringComparison.Ordinal) ||
                !string.Equals(item.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                return false;
            }

            previousHash = item.Hash;
        }

        return true;
    }
}
