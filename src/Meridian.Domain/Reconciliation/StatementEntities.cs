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
    IReadOnlyList<ReconciliationCaseHistoryEntry> History);

public sealed record ReconciliationCaseHistoryEntry(
    DateTimeOffset TimestampUtc,
    string FromStatus,
    string ToStatus,
    string Note);
