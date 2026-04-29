using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

public interface IWorkstationOperatorInboxApiClient
{
    Task<OperatorInboxDto?> GetInboxAsync(Guid? fundAccountId = null, CancellationToken ct = default);
}

public sealed class WorkstationOperatorInboxApiClient : IWorkstationOperatorInboxApiClient
{
    private readonly ApiClientService _apiClient;

    public WorkstationOperatorInboxApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<OperatorInboxDto?> GetInboxAsync(Guid? fundAccountId = null, CancellationToken ct = default)
    {
        var route = fundAccountId.HasValue
            ? UiApiRoutes.WithQuery(UiApiRoutes.WorkstationOperatorInbox, $"fundAccountId={fundAccountId.Value:D}")
            : UiApiRoutes.WorkstationOperatorInbox;

        return _apiClient.GetAsync<OperatorInboxDto>(route, ct);
    }
}
