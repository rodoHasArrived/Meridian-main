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

public sealed record StatementSourceRowReference(
    string StatementRunId,
    int SourceRowNumber,
    string SourceRowHash,
    IReadOnlyDictionary<string, string> RawSnapshot);

public sealed record StatementSecurityReference(
    string StatementRunId,
    int SourceRowNumber,
    string SourceRowHash,
    IReadOnlyDictionary<string, string> RawSnapshot,
    string? SecurityId,
    string? UnresolvedIdentifier,
    string Currency);

public sealed record StatementPosition(
    string StatementRunId,
    int SourceRowNumber,
    string SourceRowHash,
    IReadOnlyDictionary<string, string> RawSnapshot,
    string AccountId,
    string ExternalAccountId,
    string? SecurityId,
    string? UnresolvedIdentifier,
    string Currency,
    decimal Quantity,
    decimal Price,
    decimal MarketValue,
    DateOnly TradeDate,
    DateOnly? SettlementDate);

public sealed record StatementCashBalance(
    string StatementRunId,
    int SourceRowNumber,
    string SourceRowHash,
    IReadOnlyDictionary<string, string> RawSnapshot,
    string AccountId,
    string ExternalAccountId,
    string Currency,
    decimal Amount,
    DateOnly TradeDate,
    DateOnly? SettlementDate);

public sealed record StatementTransaction(
    string StatementRunId,
    int SourceRowNumber,
    string SourceRowHash,
    IReadOnlyDictionary<string, string> RawSnapshot,
    string AccountId,
    string ExternalAccountId,
    string? SecurityId,
    string? UnresolvedIdentifier,
    string Currency,
    decimal Quantity,
    decimal Price,
    decimal MarketValue,
    DateOnly TradeDate,
    DateOnly? SettlementDate,
    decimal Amount,
    decimal FeesCommission,
    string TransactionType,
    string? ExternalReference);

public sealed record NormalizedStatementImportResult(
    string ImportId,
    string SourceKind,
    string SourcePath,
    int RowCount,
    IReadOnlyList<StatementPosition> Positions,
    IReadOnlyList<StatementCashBalance> CashBalances,
    IReadOnlyList<StatementTransaction> Transactions,
    IReadOnlyList<StatementSecurityReference> Securities,
    IReadOnlyList<StatementSourceRowReference> SourceRows);

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
    public IReadOnlyList<string> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<ReconciliationCaseDecisionNote> DecisionNotes { get; init; } = [];
    public DateTimeOffset LastUpdatedAtUtc { get; init; } = CreatedAtUtc;
    public string LastUpdatedBy { get; init; } = "system";
    public string Disposition { get; init; } = "Open";
    public int AgingDays { get; init; } = 0;
    public IReadOnlyList<ReconciliationCaseAttachment> Attachments { get; init; } = [];
    public ReconciliationBreakExplanation? BreakExplanation { get; init; }
    public string? BreakId { get; init; }
    public long Version { get; init; }
    public string? DispositionTransactionId { get; init; }
}

public sealed record ReconciliationCaseCommentThread(string ThreadId, string Subject, IReadOnlyList<ReconciliationCaseComment> Comments);

public sealed record ReconciliationCaseComment(string CommentId, string Body, string Actor, DateTimeOffset CreatedAtUtc, string? ParentCommentId = null);

public sealed record ReconciliationCaseAuditEvent(
    string EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string Detail,
    IReadOnlyList<string>? EvidenceReferences = null,
    string? Rationale = null,
    string? TransactionId = null,
    long Version = 0,
    string? PreviousHash = null,
    string? EntryHash = null);

public sealed record ReconciliationResolutionMetadata(string ResolutionCode, string Summary, string ResolvedBy, DateTimeOffset ResolvedAtUtc, string? SignedOffBy = null, DateTimeOffset? SignedOffAtUtc = null);

public sealed record ReconciliationCaseAttachment(
    string AttachmentId,
    string EvidenceKind,
    string SourceSystem,
    string SourceReference,
    string ContentHash,
    string? Route,
    DateTimeOffset AttachedAtUtc);

public sealed record ReconciliationBreakExplanation(
    string Summary,
    IReadOnlyList<string> SourceSystems,
    string ProbableCause,
    string LedgerImpact,
    string SuggestedNextAction,
    string RequiredSignoffRole,
    IReadOnlyList<string> EvidenceLinks);

public sealed record ReconciliationCaseDecisionNote(
    string DecisionNoteId,
    string Actor,
    DateTimeOffset CreatedAtUtc,
    string Note,
    IReadOnlyList<string> EvidenceReferences);

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
    int OpenExceptionCount)
{
    public string ToleranceProfileId { get; init; } = string.Empty;
    public int ToleranceProfileVersion { get; init; }
}

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
    string Status)
{
    public string? EvidenceLink { get; init; }
    public long Version { get; init; }
    public string? Disposition { get; init; }
    public string? DispositionActor { get; init; }
    public string? DispositionRationale { get; init; }
    public IReadOnlyList<string> DispositionEvidenceLinks { get; init; } = [];
    public string? DispositionEvidenceHash { get; init; }
    public DateTimeOffset? DisposedAtUtc { get; init; }
    public string? DispositionTransactionId { get; init; }
    public string? SupersedingBreakId { get; init; }
}
