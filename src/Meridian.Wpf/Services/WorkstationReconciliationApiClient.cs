using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Services;

public sealed record WorkstationReconciliationActionResult(
    bool Success,
    string? ErrorMessage,
    ReconciliationBreakQueueItem? Item);

public interface IWorkstationReconciliationApiClient
{
    Task<ReconciliationCalibrationSummaryDto?> GetCalibrationSummaryAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetBreakQueueAsync(CancellationToken ct = default);

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

    Task<WorkstationReconciliationActionResult> AssignBreakAsync(string breakId, ReconciliationAssignRequest request, CancellationToken ct = default);

    Task<WorkstationReconciliationActionResult> TransitionBreakAsync(string breakId, ReconciliationStatusTransitionRequest request, CancellationToken ct = default);

    Task<WorkstationReconciliationActionResult> AddCommentAsync(string breakId, ReconciliationCommentMutationRequest request, CancellationToken ct = default);

    Task<WorkstationReconciliationActionResult> SetRootCauseAsync(string breakId, ReconciliationTaxonomyRequest request, CancellationToken ct = default);

    Task<WorkstationReconciliationActionResult> SetResolutionAsync(string breakId, ReconciliationTaxonomyRequest request, CancellationToken ct = default);

    Task<WorkstationReconciliationActionResult> SignOffAsync(string breakId, ReconciliationSignOffRequest request, CancellationToken ct = default);

    Task<WorkstationReconciliationActionResult> ReopenAsync(string breakId, ReconciliationReopenRequest request, CancellationToken ct = default);
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

    public Task<WorkstationReconciliationActionResult> AssignBreakAsync(string breakId, ReconciliationAssignRequest request, CancellationToken ct = default)
        => ToActionResultAsync(_apiClient.UiApi.AssignReconciliationBreakAsync(breakId, request, ct));

    public Task<WorkstationReconciliationActionResult> TransitionBreakAsync(string breakId, ReconciliationStatusTransitionRequest request, CancellationToken ct = default)
        => ToActionResultAsync(_apiClient.UiApi.TransitionReconciliationBreakAsync(breakId, request, ct));

    public Task<WorkstationReconciliationActionResult> AddCommentAsync(string breakId, ReconciliationCommentMutationRequest request, CancellationToken ct = default)
        => ToActionResultAsync(_apiClient.UiApi.AddReconciliationBreakCommentAsync(breakId, request, ct));

    public Task<WorkstationReconciliationActionResult> SetRootCauseAsync(string breakId, ReconciliationTaxonomyRequest request, CancellationToken ct = default)
        => ToActionResultAsync(_apiClient.UiApi.SetReconciliationBreakRootCauseAsync(breakId, request, ct));

    public Task<WorkstationReconciliationActionResult> SetResolutionAsync(string breakId, ReconciliationTaxonomyRequest request, CancellationToken ct = default)
        => ToActionResultAsync(_apiClient.UiApi.SetReconciliationBreakResolutionAsync(breakId, request, ct));

    public Task<WorkstationReconciliationActionResult> SignOffAsync(string breakId, ReconciliationSignOffRequest request, CancellationToken ct = default)
        => ToActionResultAsync(_apiClient.UiApi.SignOffReconciliationBreakAsync(breakId, request, ct));

    public Task<WorkstationReconciliationActionResult> ReopenAsync(string breakId, ReconciliationReopenRequest request, CancellationToken ct = default)
        => ToActionResultAsync(_apiClient.UiApi.ReopenReconciliationBreakAsync(breakId, request, ct));

    private static async Task<WorkstationReconciliationActionResult> ToActionResultAsync(
        Task<Meridian.Contracts.Api.ApiResponse<ReconciliationBreakQueueItem>> responseTask)
    {
        var response = await responseTask.ConfigureAwait(false);

        return response.Success
            ? new WorkstationReconciliationActionResult(true, null, response.Data)
            : new WorkstationReconciliationActionResult(false, response.ErrorMessage, null);
    }
}
