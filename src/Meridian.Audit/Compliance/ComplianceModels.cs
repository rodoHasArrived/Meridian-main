using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Meridian.Audit.Compliance;

public enum SensitiveAction
{
    RuleEdit,
    BreakClosure,
    PaymentRelease,
    OverrideApproval
}

public sealed record ActorContext(
    string ActorId,
    string[] Roles,
    string? Team,
    string? SourceIp,
    string? DeviceId,
    bool MfaSatisfied);

public sealed record ComplianceActionRequest(
    SensitiveAction Action,
    string ObjectType,
    string ObjectId,
    string? BeforeStateJson,
    string? AfterStateJson,
    string CorrelationId,
    string? EntityId,
    string? RequestedByActorId = null,
    string[]? AdditionalApproverIds = null);

public sealed record AuditEvent(
    string EventId,
    DateTimeOffset OccurredAtUtc,
    string ActorId,
    SensitiveAction Action,
    string ObjectType,
    string ObjectId,
    string? BeforeStateJson,
    string? AfterStateJson,
    string? SourceIp,
    string? DeviceId,
    string CorrelationId,
    string? EntityId,
    string Hash,
    string? PreviousHash);

public sealed record AccessReviewRecord(
    string ReviewId,
    DateTimeOffset ReviewedAtUtc,
    string ReviewedBy,
    string ActorId,
    string[] RemovedRoles,
    string Reason);

public static class AuditHash
{
    public static string Compute(AuditEvent evt)
    {
        var raw = JsonSerializer.Serialize(new
        {
            evt.EventId,
            evt.OccurredAtUtc,
            evt.ActorId,
            evt.Action,
            evt.ObjectType,
            evt.ObjectId,
            evt.BeforeStateJson,
            evt.AfterStateJson,
            evt.SourceIp,
            evt.DeviceId,
            evt.CorrelationId,
            evt.EntityId,
            evt.PreviousHash
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
