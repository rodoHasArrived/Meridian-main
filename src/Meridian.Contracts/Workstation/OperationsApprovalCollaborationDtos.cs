namespace Meridian.Contracts.Workstation;

/// <summary>Discussion is retained workflow evidence, never an approval decision.</summary>
public sealed record OperationsApprovalCommentDto(
    string CommentId, string ApprovalId, string Author, string Body,
    IReadOnlyList<string> Mentions, DateTimeOffset CreatedAtUtc, string CommandId);

public sealed record OperationsApprovalCollaborationReceipt(
    string Actor, string CommandId, string Fingerprint);

public sealed record OperationsApprovalCollaborationDto(
    Guid WorkflowId, long Version, OperationsApprovalStateDto ApprovalState,
    string? CurrentApprovalId, string? Submitter, string? Reviewer,
    IReadOnlyList<OperationsApprovalCommentDto> Comments,
    IReadOnlyList<OperationsApprovalDto> ApprovalHistory,
    bool CanComment, bool CanReassign, string? DisabledReason);

public sealed record OperationsApprovalCommentRequestDto(
    long ExpectedVersion, string ApprovalId, string CommandId, string Actor,
    string Body, IReadOnlyList<string>? Mentions = null);

public sealed record OperationsApprovalReassignRequestDto(
    long ExpectedVersion, string ApprovalId, string CommandId, string Actor,
    string Reviewer, string Rationale);
