using Meridian.Contracts.Api;
using Meridian.Ui.Services.Contracts;
using Meridian.Wpf.Contracts;

namespace Meridian.Wpf.Services;

/// <summary>
/// Adapts the shared UI API client to the desktop shell's status contract.
/// </summary>
public sealed class ApiStatusService : IStatusService
{
    private readonly IRemoteWorkstationClient _remoteClient;

    public ApiStatusService(IRemoteWorkstationClient remoteClient)
    {
        _remoteClient = remoteClient ?? throw new ArgumentNullException(nameof(remoteClient));
    }

    public string ServiceUrl => _remoteClient.BaseUrl;

    public Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default)
        => _remoteClient.GetStatusAsync(ct);

    public Task<ApiResponse<StatusResponse>> GetStatusWithResponseAsync(CancellationToken ct = default)
        => _remoteClient.GetStatusWithResponseAsync(ct);

    public Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default)
        => _remoteClient.CheckHealthAsync(ct);
}
