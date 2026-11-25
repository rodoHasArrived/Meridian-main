namespace Meridian.Contracts.Workstation;

/// <summary>
/// An ingest amendment held because a lower-ranked source tried to overwrite a value a higher-ranked
/// source supplied (Security Master review A2). <see cref="DominatedFieldPaths"/> names the fields that
/// triggered the hold and <see cref="ConflictIds"/> the conflicts opened for it; an operator adopts the
/// held value through the conflict's governed resolution or discards the row.
/// <para>Wire shape of <c>GET /api/security-master/held-amendments</c>, shared by both workstations.</para>
/// </summary>
public sealed record SecurityMasterHeldAmendmentDto(
    Guid HeldAmendmentId,
    Guid SecurityId,
    string SourceSystem,
    string RequestJson,
    IReadOnlyList<string> DominatedFieldPaths,
    IReadOnlyList<Guid> ConflictIds,
    long HeldAtVersion,
    DateTimeOffset HeldAt,
    string Status,
    string? ClosedBy = null,
    DateTimeOffset? ClosedAt = null);

/// <summary>Held-amendment statuses accepted by the <c>status</c> filter.</summary>
public static class SecurityMasterHeldAmendmentStatusDto
{
    public const string Held = "Held";
    public const string Replayed = "Replayed";
    public const string Discarded = "Discarded";
}

/// <summary>
/// One case in the deployment-global Security Master exception queue (review D2): a conflict, an
/// identifier ambiguity or a data-quality violation, with an SLA due date and an optional assignee.
/// </summary>
public sealed record SecurityMasterExceptionCaseDto(
    Guid CaseId,
    string CaseKey,
    string Category,
    string RuleCode,
    Guid? SecurityId,
    string Severity,
    string Title,
    string Detail,
    string Status,
    string? Assignee,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset SlaDueAt,
    DateTimeOffset? ClosedAt,
    string? ClosedReason);

/// <summary>
/// A queue row from <c>GET /api/security-master/exception-cases</c>: the case plus the server's SLA
/// verdict and age, computed at read time so neither workstation re-derives them.
/// </summary>
public sealed record SecurityMasterExceptionCaseQueueItemDto(
    SecurityMasterExceptionCaseDto Row,
    bool SlaBreached,
    double AgeHours);

/// <summary>Exception-case statuses accepted by the <c>status</c> filter.</summary>
public static class SecurityMasterExceptionCaseStatusDto
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}

/// <summary>Result of <c>POST /api/security-master/exception-cases/sync</c>.</summary>
public sealed record SecurityMasterExceptionSyncResultDto(int Opened, int Refreshed, int Closed);

/// <summary>Body of <c>POST /api/security-master/exception-cases/{caseId}/assign</c>.</summary>
public sealed record AssignSecurityMasterExceptionCaseRequest(string Assignee);
