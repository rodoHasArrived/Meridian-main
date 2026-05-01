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
