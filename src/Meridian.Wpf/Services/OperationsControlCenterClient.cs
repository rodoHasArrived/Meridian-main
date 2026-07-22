using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

public interface IOperationsControlCenterClient
{
    Task<OperationsApprovalPolicyMatrixDto?> GetApprovalPolicyMatrixAsync(CancellationToken ct = default);

    Task<OperationsCloseCalendarDto?> GetCloseCalendarAsync(CancellationToken ct = default);
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
}
