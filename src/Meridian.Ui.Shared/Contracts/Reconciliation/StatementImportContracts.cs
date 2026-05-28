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
    string CreatedAtUtc,
    string? Assignee = null,
    string Priority = "Normal",
    string? SlaPolicyId = null,
    string? SlaDueAtUtc = null,
    string? SlaWarningAtUtc = null,
    string? SlaBreachedAtUtc = null,
    string SlaState = "OnTrack",
    string? AgeBand = null,
    double BusinessAgeHours = 0,
    string? RootCauseCode = null,
    string? ResolutionCode = null,
    string? ResolutionNote = null,
    string? SignedOffBy = null,
    string? SignedOffAtUtc = null,
    string? ReopenedBy = null,
    string? ReopenedAtUtc = null,
    string? ReopenReason = null,
    long Version = 0);

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
