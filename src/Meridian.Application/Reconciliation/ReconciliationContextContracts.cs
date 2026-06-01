using Meridian.Contracts.Workstation;

namespace Meridian.Application.Reconciliation;

public sealed record DataIntegrationIngestionRequest(
    string SourceKind,
    string SourcePath,
    string? MappingProfileId);

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
    IReadOnlyList<ExternalStatementCaseRecord> Cases);

public interface IReconciliationCaseIntakeService
{
    Task<ReconciliationCaseIntakeResult> IntakeAsync(
        ReconciliationCaseIntakeRequest request,
        CancellationToken cancellationToken = default);
}
