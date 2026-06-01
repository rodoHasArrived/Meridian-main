using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;

namespace Meridian.Application.Reconciliation;

public sealed record DataIntegrationIngestionRequest(
    string SourceKind,
    string SourcePath,
    string? MappingProfileId);

public sealed record StatementReconciliationValidationRequest(
    string SourceKind,
    string SourcePath,
    string? MappingProfileId);

public interface IStatementReconciliationValidationService
{
    Task<string> ValidateAsync(
        StatementReconciliationValidationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DataIntegrationIngestionResult(
    string ImportId,
    string SourceKind,
    string SourcePath,
    int ImportedRowCount);

public interface IDataIntegrationIngestionService
{
    Task<DataIntegrationIngestionResult> IngestAsync(
        DataIntegrationIngestionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ReconciliationCaseIntakeRequest(
    string SourceKind,
    string SourcePath,
    string? MappingProfileId);

public sealed record ReconciliationCaseIntakeResult(
    string ImportId,
    string SourceKind,
    string SourcePath,
    int RowCount,
    int MatchCount,
    IReadOnlyList<ReconciliationCase> Cases);

public interface IReconciliationCaseIntakeService
{
    Task<ReconciliationCaseIntakeResult> IntakeAsync(
        ReconciliationCaseIntakeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record StatementRunWorkflowResult(
    CanonicalStatementImport Import,
    IReadOnlyList<ReconciliationBreakRecord> Breaks,
    IReadOnlyList<ReconciliationCase> Cases);

public interface IStatementRunWorkflowService
{
    Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken cancellationToken = default);
    Task<StatementRunWorkflowResult> CreateAsync(StatementRunRequest request, CancellationToken cancellationToken = default);
    Task<StatementRunWorkflowResult?> GetAsync(string runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(CancellationToken cancellationToken = default);
}
