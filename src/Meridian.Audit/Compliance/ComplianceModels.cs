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
    string? ApprovalRequestId = null,
    // Retained only for wire compatibility with older workstation clients. Policy evaluation
    // deliberately ignores these caller-authored identity claims and resolves authoritative
    // approval evidence through ApprovalRequestId instead.
    string? RequestedByActorId = null,
    string[]? AdditionalApproverIds = null);

public sealed record ComplianceApprovalRequestCommand(
    SensitiveAction Action,
    string ObjectType,
    string ObjectId,
    string CorrelationId,
    string? EntityId = null);

public sealed record ComplianceApprovalDecisionRecord(
    string ApprovalId,
    string ApprovedByActorId,
    bool Approved,
    DateTimeOffset DecidedAtUtc);

public sealed record ComplianceApprovalRequestRecord(
    string ApprovalRequestId,
    SensitiveAction Action,
    string ObjectType,
    string ObjectId,
    string? EntityId,
    string CorrelationId,
    string RequestedByActorId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<ComplianceApprovalDecisionRecord> Decisions);

internal sealed record ComplianceApprovalSnapshot(
    IReadOnlyList<ComplianceApprovalRequestRecord> Requests);

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

public enum AccessReviewOutcome
{
    NoActionRequired,
    RemediationApplied,
    RemediationPartiallyApplied,
    RemediationFailed,
    VerificationFailed
}

public sealed record AccessReviewAssessment(
    string AssessmentId,
    DateTimeOffset AssessedAtUtc,
    string ReviewedBy,
    string ActorId,
    bool IsDormant,
    IReadOnlyList<string> AssignedRoles,
    IReadOnlyList<string> CandidateRoles,
    string Reason);

public sealed record AccessReviewRecord(
    string ReviewId,
    DateTimeOffset ReviewedAtUtc,
    string ReviewedBy,
    string ActorId,
    IReadOnlyList<string> RolesBefore,
    IReadOnlyList<string>? RolesAfter,
    IReadOnlyList<string> RemovedRoles,
    AccessReviewOutcome Outcome,
    string Reason,
    string? FailureCode = null);

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
