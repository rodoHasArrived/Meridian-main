using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Services;

public sealed record WorkstationReconciliationActionResult(
    bool Success,
    string? ErrorMessage,
    ReconciliationBreakQueueItem? Item);

public interface IWorkstationReconciliationApiClient
{
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
}

public sealed class WorkstationReconciliationApiClient : IWorkstationReconciliationApiClient
{
    private readonly Meridian.Ui.Services.ApiClientService _apiClient;

    public WorkstationReconciliationApiClient(Meridian.Ui.Services.ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetBreakQueueAsync(CancellationToken ct = default)
        => await _apiClient.GetAsync<List<ReconciliationBreakQueueItem>>("/api/workstation/reconciliation/break-queue", ct).ConfigureAwait(false)
        ?? [];

    public Task<ReconciliationRunDetail?> GetLatestRunDetailAsync(string runId, CancellationToken ct = default)
        => _apiClient.GetAsync<ReconciliationRunDetail>($"/api/workstation/runs/{Uri.EscapeDataString(runId)}/reconciliation", ct);

    public Task<ReconciliationRunDetail?> GetRunDetailAsync(string reconciliationRunId, CancellationToken ct = default)
        => _apiClient.GetAsync<ReconciliationRunDetail>($"/api/workstation/reconciliation/runs/{Uri.EscapeDataString(reconciliationRunId)}", ct);

    public Task<WorkstationReconciliationActionResult> ReviewBreakAsync(
        string breakId,
        ReviewReconciliationBreakRequest request,
        CancellationToken ct = default)
        => PostActionAsync(
            $"/api/workstation/reconciliation/break-queue/{Uri.EscapeDataString(breakId)}/review",
            request,
            ct);

    public Task<WorkstationReconciliationActionResult> ResolveBreakAsync(
        string breakId,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct = default)
        => PostActionAsync(
            $"/api/workstation/reconciliation/break-queue/{Uri.EscapeDataString(breakId)}/resolve",
            request,
            ct);

    private async Task<WorkstationReconciliationActionResult> PostActionAsync(
        string endpoint,
        object request,
        CancellationToken ct)
    {
        var response = await _apiClient
            .PostWithResponseAsync<ReconciliationBreakQueueItem>(endpoint, request, ct)
            .ConfigureAwait(false);

        return response.Success
            ? new WorkstationReconciliationActionResult(true, null, response.Data)
            : new WorkstationReconciliationActionResult(false, response.ErrorMessage, null);
    }
}
