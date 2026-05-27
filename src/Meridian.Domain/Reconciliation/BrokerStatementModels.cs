namespace Meridian.Domain.Reconciliation;

public sealed record BrokerStatementImportRequest(string Broker, string SourcePath, DateOnly StatementDate);

public sealed record BrokerStatementValidationResult(bool IsValid, IReadOnlyList<string> Errors, int RowCount);

public sealed record BrokerStatementImportResult(CanonicalStatementImport Import, IReadOnlyList<CanonicalStatementRow> Rows);

public sealed record MatchOutcome(
    string RowChecksum,
    string OutcomeType,
    string LinkedEntityId,
    decimal Confidence,
    string Rationale);

public interface IBrokerStatementService
{
    Task<BrokerStatementValidationResult> ValidateAsync(BrokerStatementImportRequest request, CancellationToken ct = default);
    Task<BrokerStatementImportResult> ImportAsync(BrokerStatementImportRequest request, CancellationToken ct = default);
}

public interface IReconciliationCaseService
{
    Task<IReadOnlyList<ReconciliationCase>> CreateOpenCasesAsync(string importId, IReadOnlyList<MatchOutcome> outcomes, CancellationToken ct = default);
    Task<ReconciliationCase> UpdateStatusAsync(string caseId, string toStatus, string note, CancellationToken ct = default);
    Task<ReconciliationCase> AssignAsync(string caseId, string assignee, string note, CancellationToken ct = default);
    Task<ReconciliationCase> AddCommentAsync(string caseId, string subject, string body, string actor, string? parentCommentId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationCase>> ListOpenCasesAsync(CancellationToken ct = default);
}
