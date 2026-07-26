using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Contracts.Reconciliation;

public interface IReconciliationApiService
{
    Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(CancellationToken ct = default);
    Task<StatementRunDto?> CreateStatementRunAsync(StatementRunCreateDto request, CancellationToken ct = default);
    Task<StatementRunDto?> GetStatementRunAsync(string runId, CancellationToken ct = default);
    Task<StatementRunValidationDto?> GetStatementRunValidationAsync(string runId, CancellationToken ct = default);
    Task<IReadOnlyList<StatementRunBreakDto>?> ListStatementRunBreaksAsync(string runId, CancellationToken ct = default);
    Task<StatementRunDto?> ReconcileStatementRunAsync(string runId, StatementRunReconcileRequestDto request, CancellationToken ct = default);
    Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StatementBreakDto>> ListOpenStatementBreaksAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StatementBreakDto>>([]);
    Task<StatementBreakDispositionResultDto> DispositionStatementBreakAsync(
        string breakId,
        StatementBreakDispositionRequestDto request,
        string authenticatedActor,
        CancellationToken ct = default)
        => Task.FromResult(new StatementBreakDispositionResultDto(
            StatementBreakDispositionOutcomeDto.NotConfigured,
            breakId,
            null,
            null,
            request.CommandId,
            request.ExpectedVersion,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "Statement break disposition service is not configured."));
    Task<IReadOnlyList<StatementBreakDispositionAuditEntryDto>?> GetStatementBreakAuditHistoryAsync(
        string breakId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StatementBreakDispositionAuditEntryDto>?>(null);
    Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(CancellationToken ct = default);
}
