using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Wpf.Services;

/// <summary>
/// Adapts the shared UI API client to the desktop shell's status contract.
/// </summary>
public sealed class ApiStatusService : IStatusService
{
    private readonly ApiClientService _apiClientService;

    public ApiStatusService(ApiClientService apiClientService)
    {
        _apiClientService = apiClientService ?? throw new ArgumentNullException(nameof(apiClientService));
    }

    public string ServiceUrl => _apiClientService.BaseUrl;

    public Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default)
        => _apiClientService.UiApi.GetStatusAsync(ct);

    public Task<ApiResponse<StatusResponse>> GetStatusWithResponseAsync(CancellationToken ct = default)
        => _apiClientService.UiApi.GetWithResponseAsync<StatusResponse>(UiApiRoutes.Status, ct);

    public Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default)
        => _apiClientService.CheckHealthAsync(ct);
}
