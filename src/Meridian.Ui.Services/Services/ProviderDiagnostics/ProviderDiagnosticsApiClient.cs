using Meridian.Contracts.Api;

namespace Meridian.Ui.Services.ProviderDiagnostics;

/// <summary>
/// Reads provider registration and current rate-limit evidence from the shared workstation API.
/// </summary>
public interface IProviderDiagnosticsApiClient
{
    Task<ProviderCatalogResponse> GetCatalogAsync(CancellationToken ct = default);
    Task<ProviderRateLimitsResponse> GetRateLimitsAsync(CancellationToken ct = default);
    Task<ProviderConnectionHealthResponse> GetConnectionHealthAsync(CancellationToken ct = default);
}

/// <summary>
/// Thin API adapter for provider accounting surfaces. Missing responses fail closed instead of
/// producing synthetic healthy registration or request-capacity state.
/// </summary>
public sealed class ProviderDiagnosticsApiClient : IProviderDiagnosticsApiClient
{
    private readonly ApiClientService _apiClient;

    public ProviderDiagnosticsApiClient()
        : this(ApiClientService.Instance)
    {
    }

    internal ProviderDiagnosticsApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<ProviderCatalogResponse> GetCatalogAsync(CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<ProviderCatalogResponse>(UiApiRoutes.ProviderCatalog, ct)).DataOrLoggedNull("Get provider catalog")
            ?? throw new InvalidOperationException("Provider catalog registration evidence was unavailable.");

    public async Task<ProviderRateLimitsResponse> GetRateLimitsAsync(CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<ProviderRateLimitsResponse>(UiApiRoutes.ProviderRateLimits, ct)).DataOrLoggedNull("Get provider rate limits")
            ?? throw new InvalidOperationException("Current provider rate-limit evidence was unavailable.");

    public async Task<ProviderConnectionHealthResponse> GetConnectionHealthAsync(CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<ProviderConnectionHealthResponse>(UiApiRoutes.ProviderHealth, ct)).DataOrLoggedNull("Get provider connection health")
            ?? throw new InvalidOperationException("Current provider connection diagnostics were unavailable.");
}
