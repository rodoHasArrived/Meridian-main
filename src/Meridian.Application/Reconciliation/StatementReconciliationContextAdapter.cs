namespace Meridian.Application.Reconciliation;

public sealed class StatementReconciliationContextAdapter(StatementReconciliationService service)
    : IStatementReconciliationValidationService, IDataIntegrationIngestionService, IReconciliationCaseIntakeService
{
    public Task<string> ValidateAsync(
        StatementReconciliationValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return service.ValidateAsync(
            request.SourceKind,
            request.SourcePath,
            request.MappingProfileId,
            cancellationToken);
    }

    public async Task<DataIntegrationIngestionResult> IngestAsync(
        DataIntegrationIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var import = await service.ImportAsync(request.SourceKind, request.SourcePath, cancellationToken).ConfigureAwait(false);
        return new DataIntegrationIngestionResult(import.ImportId, import.SourceKind, import.SourcePath, import.RowCount);
    }

    public async Task<ReconciliationCaseIntakeResult> IntakeAsync(
        ReconciliationCaseIntakeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var intake = await service.CreateExternalStatementCasesAsync(
            request.SourceKind,
            request.SourcePath,
            request.MappingProfileId,
            cancellationToken).ConfigureAwait(false);

        return new ReconciliationCaseIntakeResult(
            intake.ImportId,
            intake.SourceKind,
            intake.SourcePath,
            intake.RowCount,
            intake.MatchCount,
            intake.Cases);
    }
}
