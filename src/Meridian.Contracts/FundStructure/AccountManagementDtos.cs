namespace Meridian.Contracts.FundStructure;

/// Metadata for a custodian statement batch that was ingested.
public sealed record CustodianStatementBatchDto(
    Guid BatchId,
    Guid AccountId,
    DateOnly AsOfDate,
    string CustodianName,
    string SourceFormat,
    int LineCount,
    DateTimeOffset IngestedAt,
    string LoadedBy);

/// Metadata for a bank statement batch that was ingested.
public sealed record BankStatementBatchDto(
    Guid BatchId,
    Guid AccountId,
    DateOnly StatementDate,
    string BankName,
    int LineCount,
    DateTimeOffset IngestedAt,
    string LoadedBy);

/// Header record for an account-level reconciliation run.
public sealed record AccountReconciliationRunDto(
    Guid ReconciliationRunId,
    Guid AccountId,
    DateOnly AsOfDate,
    string Status,
    int TotalChecks,
    int TotalMatched,
    int TotalBreaks,
    decimal BreakAmountTotal,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    string RequestedBy);

/// Result of a single check within a reconciliation run.
public sealed record AccountReconciliationResultDto(
    Guid ResultId,
    Guid ReconciliationRunId,
    string CheckLabel,
    bool IsMatch,
    string Category,
    string Status,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal? Variance,
    string Reason);
