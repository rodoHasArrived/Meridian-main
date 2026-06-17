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

    public Task<OperationsApprovalPolicyMatrixDto?> GetApprovalPolicyMatrixAsync(CancellationToken ct = default)
        => _apiClient.GetAsync<OperationsApprovalPolicyMatrixDto>(
            UiApiRoutes.OperationsContinuityApprovalPolicyMatrix,
            ct);

    public Task<OperationsCloseCalendarDto?> GetCloseCalendarAsync(CancellationToken ct = default)
        => _apiClient.GetAsync<OperationsCloseCalendarDto>(
            UiApiRoutes.OperationsContinuityCloseCalendar,
            ct);
}
