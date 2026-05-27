using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Auth;

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
    private static readonly IReadOnlyDictionary<SensitiveActionType, UserPermission> PermissionMatrix =
        new Dictionary<SensitiveActionType, UserPermission>
        {
            [SensitiveActionType.RuleEdit] = UserPermission.ModifyConfig,
            [SensitiveActionType.BreakClosure] = UserPermission.ManageDirectLending,
            [SensitiveActionType.PaymentRelease] = UserPermission.ManageDirectLending,
            [SensitiveActionType.OverrideApproval] = UserPermission.AdminMaintenance
        };

    private static readonly HashSet<UserRole> PrivilegedRoles = [UserRole.Admin, UserRole.Developer, UserRole.Executive];

    public PolicyDecision Evaluate(SensitiveActionType action, AccessContext context)
    {
        if (!PermissionMatrix.TryGetValue(action, out var required))
        {
            return new PolicyDecision(false, "Unknown action.", false, false, false);
        }

        if (!RolePermissions.HasPermission(context.Role, required))
        {
            return new PolicyDecision(false, $"Role {context.Role} lacks {required} permission.", false, false, false);
        }

        // Segregation-of-duties: accounting users cannot approve overrides on their own entity.
        if (action is SensitiveActionType.OverrideApproval &&
            context.Role is UserRole.Accounting &&
            context.Team.Equals("accounting", StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyDecision(false, "Segregation-of-duties blocked override approval for accounting actor.", true, true, true);
        }

        var dualApproval = action is SensitiveActionType.PaymentRelease or SensitiveActionType.OverrideApproval;
        var privilegedRole = action is SensitiveActionType.OverrideApproval;
        var requiresMfa = action is SensitiveActionType.PaymentRelease or SensitiveActionType.OverrideApproval;

        if (privilegedRole && !PrivilegedRoles.Contains(context.Role))
        {
            return new PolicyDecision(false, "Privileged role is required.", dualApproval, true, requiresMfa);
        }

        if (dualApproval)
        {
            var secondary = context.SecondaryApprovers ?? [];
            if (secondary.Count < 1 || secondary.Contains(context.Actor, StringComparer.OrdinalIgnoreCase))
            {
                return new PolicyDecision(false, "Dual approval requires a distinct secondary approver.", true, privilegedRole, requiresMfa);
            }
        }

        if (requiresMfa && !context.MfaSatisfied)
        {
            return new PolicyDecision(false, "MFA requirement not satisfied.", dualApproval, privilegedRole, true);
        }

        return new PolicyDecision(true, "Approved.", dualApproval, privilegedRole, requiresMfa);
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

public sealed class ImmutableAuditLogService
{
    private readonly ConcurrentQueue<ImmutableAuditEvent> _events = new();

    public ImmutableAuditEvent Append(SensitiveActionType action, AccessContext context, string objectId, object? beforeState, object? afterState)
    {
        var sequence = _events.Count + 1;
        var previousHash = _events.LastOrDefault()?.Hash ?? "GENESIS";
        var beforeJson = JsonSerializer.Serialize(beforeState);
        var afterJson = JsonSerializer.Serialize(afterState);
        var payload = $"{sequence}|{context.Actor}|{action}|{objectId}|{beforeJson}|{afterJson}|{context.SourceIp}|{context.DeviceId}|{context.CorrelationId}|{previousHash}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

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
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
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
