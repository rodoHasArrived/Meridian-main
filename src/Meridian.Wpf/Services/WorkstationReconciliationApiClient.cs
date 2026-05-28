using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Contracts.Reconciliation;

namespace Meridian.Wpf.Services;

public sealed record WorkstationReconciliationActionResult(
    bool Success,
    string? ErrorMessage,
    ReconciliationBreakQueueItem? Item);

public interface IWorkstationReconciliationApiClient
{
    Task<ReconciliationCalibrationSummaryDto?> GetCalibrationSummaryAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetBreakQueueAsync(CancellationToken ct = default);

    Task<IReadOnlyList<StatementRunSummaryDto>> GetStatementRunsAsync(CancellationToken ct = default);

    Task<StatementRunSummaryDto?> GetStatementRunAsync(string runId, CancellationToken ct = default);

    Task<IReadOnlyList<StatementRunExceptionDto>> GetStatementExceptionsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<StatementBreakDto>> GetOpenStatementBreaksAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationCaseSummaryDto>> GetOpenReconciliationCasesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> GetReconciliationQueueStatusAsync(CancellationToken ct = default);

    Task<ReconciliationRunDetail?> GetLatestRunDetailAsync(string runId, CancellationToken ct = default);

    Task<ReconciliationRunDetail?> GetRunDetailAsync(string reconciliationRunId, CancellationToken ct = default);

    Task<WorkstationReconciliationActionResult> ReviewBreakAsync(
        string breakId,
        ReviewReconciliationBreakRequest request,
        CancellationToken ct = default);

    Task<WorkstationReconciliationActionResult> ResolveBreakAsync(
        string breakId,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct = default);
}

public sealed class WorkstationReconciliationApiClient : IWorkstationReconciliationApiClient
{
    private readonly Meridian.Ui.Services.ApiClientService _apiClient;

    public WorkstationReconciliationApiClient(Meridian.Ui.Services.ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<ReconciliationCalibrationSummaryDto?> GetCalibrationSummaryAsync(CancellationToken ct = default)
        => _apiClient.UiApi.GetReconciliationCalibrationSummaryAsync(ct);

    public async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetBreakQueueAsync(CancellationToken ct = default)
        => await _apiClient.UiApi.GetReconciliationBreakQueueAsync(ct).ConfigureAwait(false)
        ?? [];

    public async Task<IReadOnlyList<StatementRunSummaryDto>> GetStatementRunsAsync(CancellationToken ct = default)
        => await _apiClient.GetAsync<List<StatementRunSummaryDto>>(Meridian.Contracts.Api.UiApiRoutes.ReconciliationStatementRuns, ct).ConfigureAwait(false) ?? [];

    public Task<StatementRunSummaryDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
        => _apiClient.GetAsync<StatementRunSummaryDto>(
            Meridian.Contracts.Api.UiApiRoutes.WithParam(Meridian.Contracts.Api.UiApiRoutes.ReconciliationStatementRunById, "runId", runId),
            ct);

    public async Task<IReadOnlyList<StatementRunExceptionDto>> GetStatementExceptionsAsync(CancellationToken ct = default)
        => await _apiClient.GetAsync<List<StatementRunExceptionDto>>(Meridian.Contracts.Api.UiApiRoutes.ReconciliationStatementExceptions, ct).ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<StatementBreakDto>> GetOpenStatementBreaksAsync(CancellationToken ct = default)
        => await _apiClient.GetAsync<List<StatementBreakDto>>(Meridian.Contracts.Api.UiApiRoutes.ReconciliationStatementBreaks, ct).ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<ReconciliationCaseSummaryDto>> GetOpenReconciliationCasesAsync(CancellationToken ct = default)
        => await _apiClient.GetAsync<List<ReconciliationCaseSummaryDto>>(Meridian.Contracts.Api.UiApiRoutes.ReconciliationOpenCases, ct).ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> GetReconciliationQueueStatusAsync(CancellationToken ct = default)
        => await _apiClient.GetAsync<List<ReconciliationQueueAccountStatusDto>>(Meridian.Contracts.Api.UiApiRoutes.ReconciliationQueueStatus, ct).ConfigureAwait(false) ?? [];

    public Task<ReconciliationRunDetail?> GetLatestRunDetailAsync(string runId, CancellationToken ct = default)
        => _apiClient.UiApi.GetLatestRunReconciliationAsync(runId, ct);

    public Task<ReconciliationRunDetail?> GetRunDetailAsync(string reconciliationRunId, CancellationToken ct = default)
        => _apiClient.UiApi.GetReconciliationRunAsync(reconciliationRunId, ct);

    public Task<WorkstationReconciliationActionResult> ReviewBreakAsync(
        string breakId,
        ReviewReconciliationBreakRequest request,
        CancellationToken ct = default)
        => ToActionResultAsync(_apiClient.UiApi.ReviewReconciliationBreakAsync(breakId, request, ct));

    public Task<WorkstationReconciliationActionResult> ResolveBreakAsync(
        string breakId,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct = default)
        => ToActionResultAsync(_apiClient.UiApi.ResolveReconciliationBreakAsync(breakId, request, ct));

    private static async Task<WorkstationReconciliationActionResult> ToActionResultAsync(
        Task<Meridian.Contracts.Api.ApiResponse<ReconciliationBreakQueueItem>> responseTask)
    {
        var response = await responseTask.ConfigureAwait(false);

        return response.Success
            ? new WorkstationReconciliationActionResult(true, null, response.Data)
            : new WorkstationReconciliationActionResult(false, response.ErrorMessage, null);
    }
}
