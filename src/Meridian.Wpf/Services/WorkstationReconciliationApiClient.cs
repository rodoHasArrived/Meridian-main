using Meridian.Contracts.Workstation;
using Meridian.Contracts.Operations;
using Meridian.Ui.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;

namespace Meridian.Wpf.Services;

public sealed record WorkstationReconciliationActionResult(
    bool Success,
    string? ErrorMessage,
    ReconciliationBreakQueueItem? Item)
{
    public VerifiedOperationOutcome? Outcome { get; init; }
}

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
        => (await _apiClient.GetWithResponseAsync<List<StatementRunSummaryDto>>(Meridian.Contracts.Api.UiApiRoutes.ReconciliationStatementRuns, ct).ConfigureAwait(false)).DataOrLoggedNull("Get statement runs") ?? [];

    public async Task<StatementRunSummaryDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<StatementRunSummaryDto>(
            Meridian.Contracts.Api.UiApiRoutes.WithParam(Meridian.Contracts.Api.UiApiRoutes.ReconciliationStatementRunById, "runId", runId),
            ct).ConfigureAwait(false)).DataOrLoggedNull("Get statement run");

    public async Task<IReadOnlyList<StatementRunExceptionDto>> GetStatementExceptionsAsync(CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<List<StatementRunExceptionDto>>(Meridian.Contracts.Api.UiApiRoutes.ReconciliationStatementExceptions, ct).ConfigureAwait(false)).DataOrLoggedNull("Get statement exceptions") ?? [];

    public async Task<IReadOnlyList<StatementBreakDto>> GetOpenStatementBreaksAsync(CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<List<StatementBreakDto>>(Meridian.Contracts.Api.UiApiRoutes.ReconciliationStatementBreaks, ct).ConfigureAwait(false)).DataOrLoggedNull("Get open statement breaks") ?? [];

    public async Task<IReadOnlyList<ReconciliationCaseSummaryDto>> GetOpenReconciliationCasesAsync(CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<List<ReconciliationCaseSummaryDto>>(Meridian.Contracts.Api.UiApiRoutes.ReconciliationOpenCases, ct).ConfigureAwait(false)).DataOrLoggedNull("Get open reconciliation cases") ?? [];

    public async Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> GetReconciliationQueueStatusAsync(CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<List<ReconciliationQueueAccountStatusDto>>(Meridian.Contracts.Api.UiApiRoutes.ReconciliationQueueStatus, ct).ConfigureAwait(false)).DataOrLoggedNull("Get reconciliation queue status") ?? [];

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
        Task<Meridian.Contracts.Api.ApiResponse<ReconciliationCaseworkOperationResult>> responseTask)
    {
        var response = await responseTask.ConfigureAwait(false);
        if (!response.Success || response.Data is null)
        {
            return new WorkstationReconciliationActionResult(false, response.ErrorMessage, null);
        }

        var operation = response.Data;
        var succeeded = operation.Outcome.State is
            OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings;
        return new WorkstationReconciliationActionResult(
            succeeded,
            succeeded
                ? null
                : operation.Error
                    ?? operation.Outcome.Issues.FirstOrDefault()?.Message
                    ?? $"Reconciliation operation ended in {operation.Outcome.State}.",
            operation.Item)
        {
            Outcome = operation.Outcome
        };
    }
}
