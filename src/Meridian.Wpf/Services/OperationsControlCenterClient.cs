using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

public interface IOperationsControlCenterClient
{
    Task<OperationsApprovalPolicyMatrixDto?> GetApprovalPolicyMatrixAsync(CancellationToken ct = default);

    Task<OperationsCloseCalendarDto?> GetCloseCalendarAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the continuity workflow list, or <see langword="null"/> when the workstation API
    /// call failed — callers that surface workflow state must not render an outage as an empty
    /// queue.
    /// </summary>
    Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>?> GetWorkflowsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the continuity workflow detail, or <see langword="null"/> when the workflow does
    /// not exist or the workstation API call failed.
    /// </summary>
    Task<OperationsContinuityWorkflowDto?> GetWorkflowAsync(Guid workflowId, CancellationToken ct = default);
}

public sealed class OperationsControlCenterClient : IOperationsControlCenterClient
{
    private readonly ApiClientService _apiClient;

    public OperationsControlCenterClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<OperationsApprovalPolicyMatrixDto?> GetApprovalPolicyMatrixAsync(CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<OperationsApprovalPolicyMatrixDto>(
            UiApiRoutes.OperationsContinuityApprovalPolicyMatrix,
            ct).ConfigureAwait(false)).DataOrLoggedNull("Get approval policy matrix");

    public async Task<OperationsCloseCalendarDto?> GetCloseCalendarAsync(CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<OperationsCloseCalendarDto>(
            UiApiRoutes.OperationsContinuityCloseCalendar,
            ct).ConfigureAwait(false)).DataOrLoggedNull("Get close calendar");

    public async Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>?> GetWorkflowsAsync(CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<List<OperationsContinuityWorkflowSummaryDto>>(
            UiApiRoutes.OperationsContinuity,
            ct).ConfigureAwait(false)).DataOrLoggedNull("Get operations continuity workflows");

    public async Task<OperationsContinuityWorkflowDto?> GetWorkflowAsync(Guid workflowId, CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<OperationsContinuityWorkflowDto>(
            $"{UiApiRoutes.OperationsContinuity}/{workflowId:D}",
            ct).ConfigureAwait(false)).DataOrLoggedNull("Get operations continuity workflow");
}
