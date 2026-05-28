using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Contracts.Reconciliation;

public interface IReconciliationApiService
{
    Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(CancellationToken ct = default);
    Task<StatementRunSummaryDto?> GetStatementRunAsync(string runId, CancellationToken ct = default);
    Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StatementBreakDto>> ListOpenStatementBreaksAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StatementBreakDto>>([]);
    Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(CancellationToken ct = default);
}
