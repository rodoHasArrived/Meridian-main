namespace Meridian.Domain.Reconciliation;

public sealed record CanonicalStatementImport(
    string ImportId,
    string Broker,
    DateOnly StatementDate,
    DateTimeOffset ImportedAtUtc,
    string SourcePath,
    string SourceChecksum,
    int RawRowCount,
    int NormalizedRowCount);

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
    public DateTimeOffset? DueAtUtc { get; init; }
    public DateTimeOffset LastUpdatedAtUtc { get; init; } = CreatedAtUtc;
    public string LastUpdatedBy { get; init; } = "system";
}

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
