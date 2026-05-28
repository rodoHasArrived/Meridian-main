namespace Meridian.Domain.Reconciliation;

public sealed record CanonicalStatementImport(
    string ImportId,
    string Broker,
    DateOnly StatementDate,
    DateTimeOffset ImportedAtUtc,
    string SourcePath,
    string SourceChecksum,
    int RawRowCount,
    int NormalizedRowCount)
{
    public string SourceInstitution { get; init; } = string.Empty;
    public string FundAccountId { get; init; } = string.Empty;
    public string ExternalAccountId { get; init; } = string.Empty;
    public DateOnly StatementPeriodStart { get; init; } = StatementDate;
    public DateOnly StatementPeriodEnd { get; init; } = StatementDate;
    public string OriginalFileName { get; init; } = string.Empty;
    public string MappingProfileId { get; init; } = string.Empty;
    public string ToleranceProfileId { get; init; } = string.Empty;
    public string ImportedBy { get; init; } = "system";
    public string SourceFileHash { get; init; } = SourceChecksum;
    public string DuplicateKey { get; init; } = string.Empty;
}

public sealed record CanonicalStatementRow(
    string ImportId,
    int SourceRowNumber,
    string Account,
    string Symbol,
    decimal Quantity,
    decimal Price,
    decimal CashAmount,
    string ActivityType,
    DateOnly TradeDate,
    string RawChecksum);

public sealed record ReconciliationCase(
    string CaseId,
    string ImportId,
    string Status,
    string Reason,
    decimal Confidence,
    string Rationale,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<ReconciliationCaseHistoryEntry> History)
{
    public string Owner { get; init; } = "unassigned";
    public string Priority { get; init; } = "Normal";
    public DateTimeOffset? DueAtUtc { get; init; }
    public DateTimeOffset? SlaBreachedAtUtc { get; init; }
    public IReadOnlyList<ReconciliationCaseCommentThread> CommentThreads { get; init; } = [];
    public IReadOnlyList<ReconciliationCaseAuditEvent> AuditEvents { get; init; } = [];
    public ReconciliationResolutionMetadata? Resolution { get; init; }
    public DateTimeOffset LastUpdatedAtUtc { get; init; } = CreatedAtUtc;
    public string LastUpdatedBy { get; init; } = "system";
}

public sealed record ReconciliationCaseCommentThread(string ThreadId, string Subject, IReadOnlyList<ReconciliationCaseComment> Comments);

public sealed record ReconciliationCaseComment(string CommentId, string Body, string Actor, DateTimeOffset CreatedAtUtc, string? ParentCommentId = null);

public sealed record ReconciliationCaseAuditEvent(string EventId, string EventType, DateTimeOffset OccurredAtUtc, string Actor, string Detail);

public sealed record ReconciliationResolutionMetadata(string ResolutionCode, string Summary, string ResolvedBy, DateTimeOffset ResolvedAtUtc, string? SignedOffBy = null, DateTimeOffset? SignedOffAtUtc = null);

public sealed record ReconciliationCaseHistoryEntry(
    DateTimeOffset TimestampUtc,
    string FromStatus,
    string ToStatus,
    string Note)
{
    public string Actor { get; init; } = "system";
    public string? EvidenceId { get; init; }
}


public sealed record StatementReconciliationRun(
    string RunId,
    string ImportId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int PositionMatches,
    int CashMatches,
    int TransactionMatches,
    int OpenExceptionCount);

public sealed record ReconciliationBreakRecord(
    string BreakId,
    string RunId,
    string ImportId,
    string SourceReference,
    string BreakCode,
    string Category,
    decimal Delta,
    decimal Tolerance,
    bool ToleranceBreached,
    DateTimeOffset CreatedAtUtc,
    string Status);
