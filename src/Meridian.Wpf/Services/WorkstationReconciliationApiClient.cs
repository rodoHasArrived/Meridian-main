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

    /// <summary>
    /// Operator-facing terminal status that supplements a successful result when the verified
    /// outcome contains warnings. This is separate from <see cref="ErrorMessage"/> because a
    /// completed-with-warnings action still satisfied enough postconditions to refresh its item.
    /// </summary>
    public string? OperatorMessage { get; init; }

    public bool CompletedWithWarnings =>
        Outcome?.State == OperationTerminalState.CompletedWithWarnings;
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

    public async Task<WorkstationReconciliationActionResult> ReviewBreakAsync(
        string breakId,
        ReviewReconciliationBreakRequest request,
        CancellationToken ct = default)
    {
        return await ReviewBreakCoreAsync(breakId, request, ct).ConfigureAwait(false);
    }

    public async Task<WorkstationReconciliationActionResult> ResolveBreakAsync(
        string breakId,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct = default)
    {
        return await ResolveBreakCoreAsync(breakId, request, ct).ConfigureAwait(false);
    }

    private Task<WorkstationReconciliationActionResult> ReviewBreakCoreAsync(
        string breakId,
        ReviewReconciliationBreakRequest request,
        CancellationToken ct = default)
        => ToActionResultAsync(_apiClient.UiApi.ReviewReconciliationBreakAsync(breakId, request, ct));

    private Task<WorkstationReconciliationActionResult> ResolveBreakCoreAsync(
        string breakId,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct = default)
        => ToActionResultAsync(_apiClient.UiApi.ResolveReconciliationBreakAsync(breakId, request, ct));

    private static async Task<WorkstationReconciliationActionResult> ToActionResultAsync(
        Task<Meridian.Contracts.Api.ApiResponse<ReconciliationCaseworkOperationResult>> responseTask)
        => ToActionResult(await responseTask.ConfigureAwait(false));

    internal static WorkstationReconciliationActionResult ToActionResult(
        Meridian.Contracts.Api.ApiResponse<ReconciliationCaseworkOperationResult> response)
    {
        if (!response.Success || response.Data is null)
        {
            return new WorkstationReconciliationActionResult(false, response.ErrorMessage, null);
        }

        var operation = response.Data;
        var succeeded = operation.Outcome.State is
            OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings;
        var failureMessage = operation.Error
            ?? operation.Outcome.Issues.FirstOrDefault()?.Message
            ?? $"Reconciliation operation ended in {operation.Outcome.State}.";
        return new WorkstationReconciliationActionResult(
            succeeded,
            succeeded ? null : failureMessage,
            operation.Item)
        {
            Outcome = operation.Outcome,
            OperatorMessage = succeeded
                ? BuildOutcomeOperatorMessage(operation.Outcome)
                : failureMessage
        };
    }

    internal static string? BuildOutcomeOperatorMessage(VerifiedOperationOutcome outcome)
    {
        if (outcome.State != OperationTerminalState.CompletedWithWarnings)
        {
            return null;
        }

        var parts = new List<string>
        {
            "Reconciliation action completed with warnings."
        };
        var issues = (outcome.Issues ?? [])
            .Where(static issue => !string.IsNullOrWhiteSpace(issue.Message))
            .Select(static issue => $"{issue.Code}: {TrimSentence(issue.Message)}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (issues.Length > 0)
        {
            parts.Add($"Issues: {string.Join("; ", issues)}.");
        }

        var recovery = (outcome.Recovery ?? [])
            .Where(static action =>
                !string.IsNullOrWhiteSpace(action.Label) ||
                !string.IsNullOrWhiteSpace(action.Guidance))
            .Select(static action =>
                $"{TrimSentence(action.Label)}: {TrimSentence(action.Guidance)}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (recovery.Length > 0)
        {
            parts.Add($"Recovery: {string.Join("; ", recovery)}.");
        }

        return string.Join(" ", parts);
    }

    private static string TrimSentence(string? value)
        => value?.Trim().TrimEnd('.') ?? string.Empty;
}
