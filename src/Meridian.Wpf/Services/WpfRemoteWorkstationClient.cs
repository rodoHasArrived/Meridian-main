using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF remote workstation client backed by the shared UI API client.
/// Keeps desktop health probes, typed API calls, and remote URL changes behind one seam.
/// </summary>
public sealed class WpfRemoteWorkstationClient : IRemoteWorkstationClient
{
    private static readonly Lazy<WpfRemoteWorkstationClient> _instance =
        new(() => new WpfRemoteWorkstationClient(ApiClientService.Instance));

    private readonly ApiClientService _apiClientService;
    private HttpClient _healthClient;
    private bool _disposed;

    public static WpfRemoteWorkstationClient Instance => _instance.Value;

    internal WpfRemoteWorkstationClient(ApiClientService apiClientService)
    {
        _apiClientService = apiClientService ?? throw new ArgumentNullException(nameof(apiClientService));
        _healthClient = CreateHealthClient(timeoutSeconds: 30);
    }

    public string BaseUrl => _apiClientService.BaseUrl;

    public void Configure(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);

        _apiClientService.Configure(serviceUrl, timeoutSeconds, backfillTimeoutMinutes);

        var oldClient = _healthClient;
        _healthClient = CreateHealthClient(timeoutSeconds);
        oldClient.Dispose();
    }

    public async Task<bool> CheckHealthEndpointAsync(CancellationToken ct = default)
    {
        var endpoint = $"{BaseUrl.TrimEnd('/')}/healthz";
        using var response = await _healthClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default)
        => _apiClientService.CheckHealthAsync(ct);

    public Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default)
        => _apiClientService.UiApi.GetStatusAsync(ct);

    public Task<ApiResponse<StatusResponse>> GetStatusWithResponseAsync(CancellationToken ct = default)
        => _apiClientService.UiApi.GetWithResponseAsync<StatusResponse>(UiApiRoutes.Status, ct);

    public async Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class
        => (await _apiClientService.GetWithResponseAsync<T>(endpoint, ct).ConfigureAwait(false)).DataOrLoggedNull("Get remote workstation resource");

    public Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default) where T : class
        => _apiClientService.GetWithResponseAsync<T>(endpoint, ct);

    public async Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default) where T : class
        => (await _apiClientService.PostWithResponseAsync<T>(endpoint, body, ct).ConfigureAwait(false)).DataOrLoggedNull("Post remote workstation resource");

    public Task<ApiResponse<T>> PostWithResponseAsync<T>(
        string endpoint,
        object? body = null,
        CancellationToken ct = default) where T : class
        => _apiClientService.PostWithResponseAsync<T>(endpoint, body, ct);

    public Task<ApiResponse<T>> DeleteWithResponseAsync<T>(string endpoint, CancellationToken ct = default)
        where T : class
        => _apiClientService.DeleteWithResponseAsync<T>(endpoint, ct);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _healthClient.Dispose();
        _disposed = true;
    }

    private static HttpClient CreateHealthClient(int timeoutSeconds)
    {
        var client = HttpClientFactoryProvider.CreateClient(HttpClientNames.ApiClient);
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 30);
        return client;
    }
}
