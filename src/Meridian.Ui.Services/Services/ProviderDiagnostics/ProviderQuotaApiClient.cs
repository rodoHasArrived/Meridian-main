using Meridian.Contracts.Api;

namespace Meridian.Ui.Services.ProviderDiagnostics;

public interface IProviderQuotaApiClient
{
    Task<ProviderQuotaResponse> ReadAsync(string providerId, CancellationToken ct = default);
    Task<ProviderQuotaResponse> SaveAsync(string providerId, ProviderQuotaEditDto edit, CancellationToken ct = default);
    Task<ProviderRateLimitHistoryResponse> ReadHistoryAsync(string providerId, CancellationToken ct = default);
}

/// <summary>Reads and saves account quota limits through the authoritative host.</summary>
public sealed class ProviderQuotaApiClient(ApiClientService api) : IProviderQuotaApiClient
{
    public async Task<ProviderQuotaResponse> ReadAsync(string providerId, CancellationToken ct = default)
        => HostResponse.RequireData(await api.GetWithResponseAsync<ProviderQuotaResponse>(
            QuotaRoute(providerId), ct).ConfigureAwait(false));

    public async Task<ProviderQuotaResponse> SaveAsync(string providerId, ProviderQuotaEditDto edit, CancellationToken ct = default)
        => HostResponse.RequireData(await api.PutWithResponseAsync<ProviderQuotaResponse>(
            QuotaRoute(providerId), edit, ct).ConfigureAwait(false));

    public async Task<ProviderRateLimitHistoryResponse> ReadHistoryAsync(string providerId, CancellationToken ct = default)
        => HostResponse.RequireData(await api.GetWithResponseAsync<ProviderRateLimitHistoryResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.ProviderRateLimitHistory, "providerName", providerId), ct).ConfigureAwait(false));

    private static string QuotaRoute(string providerId)
        => UiApiRoutes.WithParam(UiApiRoutes.ProviderQuota, "providerId", providerId);
}
