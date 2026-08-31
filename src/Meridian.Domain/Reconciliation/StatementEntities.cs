namespace Meridian.Domain.Reconciliation;

/// <summary>
/// Exact accounting authority bound to a retained statement import. The source account remains on
/// the statement request; this scope identifies the reporting fund profile, ledger book, ledger
/// period, and as-of date that may consume the statement during close and certified reporting.
/// </summary>
public sealed record StatementAccountingScope(
    string FundProfileId,
    Guid LedgerBookId,
    Guid AccountingPeriodId,
    DateOnly AsOfDate);

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
    public string CanonicalArtifactHash { get; init; } = SourceChecksum;
    public string DuplicateKey { get; init; } = string.Empty;
    public StatementAccountingScope? AccountingScope { get; init; }
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
    string RawChecksum)
{
    /// <summary>ISO 4217 currency of the row's monetary amounts. Defaults to USD when unmapped.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>Settlement date when the source provides one; otherwise null.</summary>
    public DateOnly? SettlementDate { get; init; }

    /// <summary>Fees or commission carried on the row, when present.</summary>
    public decimal? FeesCommission { get; init; }

    /// <summary>The broker/custodian transaction identifier, used for exact transaction matching.</summary>
    public string? ExternalTransactionId { get; init; }
}

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
}

public sealed record ReconciliationCaseCommentThread(string ThreadId, string Subject, IReadOnlyList<ReconciliationCaseComment> Comments);

public sealed record ReconciliationCaseComment(string CommentId, string Body, string Actor, DateTimeOffset CreatedAtUtc, string? ParentCommentId = null);

public sealed record ReconciliationCaseAuditEvent(string EventId, string EventType, DateTimeOffset OccurredAtUtc, string Actor, string Detail);

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

    /// <summary>
    /// Optional machine-readable classification qualifying how the break should be governed.
    /// <see langword="null"/> for an ordinary break. See <see cref="ReconciliationBreakClassifications"/>.
    /// </summary>
    public string? Classification { get; init; }
}

/// <summary>
/// Machine-readable break classifications carried on <see cref="ReconciliationBreakRecord.Classification"/>.
/// </summary>
public static class ReconciliationBreakClassifications
{
    /// <summary>
    /// Statement transaction matching ran against an empty internal ledger-transaction population, so
    /// every statement movement is structurally unmatched. Breaks carrying this classification are
    /// informational only: they stay visible for operator review but must not block close outputs.
    /// </summary>
    public const string InternalTransactionPopulationUnavailable =
        "internal-transaction-population-unavailable";
}
