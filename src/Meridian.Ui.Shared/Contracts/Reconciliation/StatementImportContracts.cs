namespace Meridian.Ui.Shared.Contracts.Reconciliation;

public sealed record StatementImportSummaryDto(
    string ImportId,
    string Broker,
    string StatementDate,
    string ImportedAtUtc,
    int RawRowCount,
    int NormalizedRowCount);

public sealed record ReconciliationCaseSummaryDto(
    string CaseId,
    string ImportId,
    string Status,
    string Reason,
    decimal Confidence,
    string Rationale,
    string CreatedAtUtc);

public sealed record ReconciliationQueueAccountStatusDto(
    Guid AccountId,
    string AccountCode,
    string QueueState,
    int UnresolvedBreakCount,
    bool SignOffReady,
    string NextBestAction,
    string BlockerReason,
    IReadOnlyList<string> EvidenceLinks);

public sealed record StatementRunSummaryDto(
    string RunId,
    string ImportId,
    string StartedAtUtc,
    string CompletedAtUtc,
    int PositionMatches,
    int CashMatches,
    int TransactionMatches,
    int OpenExceptionCount);

public sealed record StatementRunExceptionDto(
    string BreakId,
    string RunId,
    string ImportId,
    string SourceReference,
    string BreakCode,
    string Category,
    decimal Delta,
    decimal Tolerance,
    bool ToleranceBreached,
    string CreatedAtUtc,
    string Status);
