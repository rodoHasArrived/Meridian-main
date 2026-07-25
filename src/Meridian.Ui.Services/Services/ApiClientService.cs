using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Centralized HTTP client service for communicating with the Meridian core service.
/// Provides configurable service URL, retry logic, and health monitoring.
/// </summary>
public sealed class ApiClientService : IDisposable
{
    private static readonly Lazy<ApiClientService> _instance = new(() => new ApiClientService());
    private HttpClient _httpClient;
    private volatile HttpClient? _backfillHttpClient;
    private readonly object _backfillClientLock = new();
    private string _baseUrl;
    private int _timeoutSeconds;
    private int _backfillTimeoutMinutes;
    private bool _disposed;
    private UiApiClient _uiApiClient;

    // Use centralized JSON options to avoid duplication across services
    private static JsonSerializerOptions JsonOptions => DesktopJsonOptions.Api;

    /// <summary>
    /// Gets the singleton instance of the ApiClientService.
    /// </summary>
    public static ApiClientService Instance => _instance.Value;

    private ApiClientService()
    {
        _baseUrl = ApiEndpointDefaults.LocalApiBaseUrl;
        _timeoutSeconds = 30;
        _backfillTimeoutMinutes = 60;
        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        _httpClient = HttpClientFactoryProvider.CreateClient(HttpClientNames.ApiClient);
        _uiApiClient = new UiApiClient(_httpClient, _baseUrl, JsonOptions);
    }

    /// <summary>
    /// Gets the current base URL for the service.
    /// </summary>
    public string BaseUrl => _baseUrl;

    /// <summary>
    /// Shared UI API client for status/config endpoints.
    /// </summary>
    public UiApiClient UiApi => _uiApiClient;

    /// <summary>
    /// Gets whether the client is configured with a non-default URL.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl);

    /// <summary>
    /// Event raised when the service URL changes.
    /// </summary>
    public event EventHandler<ServiceUrlChangedEventArgs>? ServiceUrlChanged;

    /// <summary>
    /// Configures the API client with settings from the app configuration.
    /// </summary>
    public void Configure(AppSettings? settings)
    {
        if (settings == null)
            return;

        var newUrl = settings.ServiceUrl ?? ApiEndpointDefaults.LocalApiBaseUrl;
        var newTimeout = settings.ServiceTimeoutSeconds > 0 ? settings.ServiceTimeoutSeconds : 30;
        var newBackfillTimeout = settings.BackfillTimeoutMinutes > 0 ? settings.BackfillTimeoutMinutes : 60;

        var urlChanged = !string.Equals(_baseUrl, newUrl, StringComparison.OrdinalIgnoreCase);
        var timeoutChanged = _timeoutSeconds != newTimeout;
        var backfillTimeoutChanged = _backfillTimeoutMinutes != newBackfillTimeout;

        if (urlChanged || timeoutChanged || backfillTimeoutChanged)
        {
            var oldUrl = _baseUrl;
            _baseUrl = newUrl.TrimEnd('/');
            _timeoutSeconds = newTimeout;
            _backfillTimeoutMinutes = newBackfillTimeout;
            _uiApiClient.UpdateBaseUrl(_baseUrl);

            lock (_backfillClientLock)
            {
                if (_backfillHttpClient != null)
                {
                    _backfillHttpClient.Timeout = TimeSpan.FromMinutes(_backfillTimeoutMinutes);
                }
            }

            // Recreate HTTP client with new timeout
            var oldClient = _httpClient;
            _httpClient = CreateHttpClient(_timeoutSeconds);
            oldClient.Dispose();
            _uiApiClient = new UiApiClient(_httpClient, _baseUrl, JsonOptions);

            if (urlChanged)
            {
                // Cookie domains do not include ports. Remove the prior endpoint's session
                // before using the replacement URL so a localhost port change cannot reuse it.
                ApiClientSession.Clear(oldUrl);

                ServiceUrlChanged?.Invoke(this, new ServiceUrlChangedEventArgs
                {
                    OldUrl = oldUrl,
                    NewUrl = _baseUrl
                });
            }
        }
    }

    /// <summary>
    /// Configures the API client with a specific URL.
    /// </summary>
    public void Configure(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60)
    {
        Configure(new AppSettings
        {
            ServiceUrl = serviceUrl,
            ServiceTimeoutSeconds = timeoutSeconds,
            BackfillTimeoutMinutes = backfillTimeoutMinutes
        });
    }

    private static HttpClient CreateHttpClient(int timeoutSeconds)
    {
        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        var client = HttpClientFactoryProvider.CreateClient(HttpClientNames.ApiClient);
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return client;
    }

    /// <summary>
    /// Gets a shared HTTP client configured for long-running backfill operations.
    /// The client is lazily created and reused to avoid socket exhaustion.
    /// </summary>
    public HttpClient GetBackfillClient()
    {
        if (_backfillHttpClient == null)
        {
            lock (_backfillClientLock)
            {
                if (_backfillHttpClient == null)
                {
                    // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
                    _backfillHttpClient = HttpClientFactoryProvider.CreateClient(HttpClientNames.BackfillClient);
                    _backfillHttpClient.Timeout = TimeSpan.FromMinutes(_backfillTimeoutMinutes);
                }
            }
        }
        return _backfillHttpClient;
    }

    /// <summary>
    /// Performs a GET request to the specified endpoint. Returns null ONLY for 404 (not found)
    /// and — for legacy compatibility — on other failures, which are now logged. New callers
    /// must use <see cref="GetWithResponseAsync{T}"/> so failures stay distinguishable from
    /// absent data; the CI caller ratchet (check-apiclient-callers.py) blocks new call sites.
    /// </summary>
    public Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);
        return SendAsync<T>("GET", url, () => _httpClient.GetAsync(url, ct), ct);
    }

    /// <summary>
    /// Performs a GET request and returns the raw response.
    /// </summary>
    public Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);
        return SendWithResponseAsync<T>(() => _httpClient.GetAsync(url, ct), ct);
    }

    /// <summary>
    /// Performs a POST request with JSON body. Returns null ONLY for 404 (not found) and — for
    /// legacy compatibility — on other failures, which are now logged. New callers must use
    /// <see cref="PostWithResponseAsync{T}"/> so failures stay distinguishable from absent data;
    /// the CI caller ratchet (check-apiclient-callers.py) blocks new call sites.
    /// </summary>
    public Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);
        return SendAsync<T>("POST", url, () => _httpClient.SendAsync(CreateMutationRequest(HttpMethod.Post, url, CreateJsonContent(body)), ct), ct);
    }

    /// <summary>
    /// Performs a POST request and returns the full response.
    /// </summary>
    public Task<ApiResponse<T>> PostWithResponseAsync<T>(
        string endpoint,
        object? body = null,
        CancellationToken ct = default,
        HttpClient? customClient = null) where T : class
    {
        var url = BuildUrl(endpoint);
        var client = customClient ?? _httpClient;
        return SendWithResponseAsync<T>(() => client.SendAsync(CreateMutationRequest(HttpMethod.Post, url, CreateJsonContent(body)), ct), ct);
    }

    /// <summary>
    /// Sends a DELETE request and returns the API response.
    /// </summary>
    public Task<ApiResponse<T>> DeleteWithResponseAsync<T>(
        string endpoint,
        CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);
        return SendWithResponseAsync<T>(() => _httpClient.SendAsync(CreateMutationRequest(HttpMethod.Delete, url, content: null), ct), ct);
    }

    /// <summary>
    /// Builds a mutating request that echoes the server-issued CSRF cookie as the
    /// X-CSRF-Token header. The server's CookieCsrfMiddleware requires the header on
    /// session-authenticated POST/PUT/PATCH/DELETE calls under /api; before a login
    /// session exists there is no CSRF cookie and the header is simply omitted.
    /// </summary>
    private HttpRequestMessage CreateMutationRequest(HttpMethod method, string url, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        var csrfToken = ApiClientSession.GetCsrfToken(_baseUrl);
        if (!string.IsNullOrWhiteSpace(csrfToken))
        {
            request.Headers.TryAddWithoutValidation(ApiClientSession.CsrfHeaderName, csrfToken);
        }

        return request;
    }

    /// <summary>
    /// Establishes the server login session shared by every "api-client" consumer (audit
    /// finding P8). On success the server's Set-Cookie responses (mdc-session + mdc-csrf)
    /// land in <see cref="ApiClientSession.Cookies"/>, so subsequent API calls are
    /// authenticated — letting endpoints stamp the session actor over client-supplied
    /// values — and mutations carry the CSRF header. Mirrors
    /// LifecycleControlClient.AuthenticateAsync, which manages its own separate session.
    /// </summary>
    public async Task<bool> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        var url = BuildUrl(UiApiRoutes.AuthApiLogin);
        try
        {
            using var response = await _httpClient.PostAsync(
                url,
                CreateJsonContent(new { username, password, returnUrl = "/workstation/" }),
                ct);
            if (!response.IsSuccessStatusCode)
            {
                LoggingService.Instance.LogWarning(
                    $"Workstation API login failed with {(int)response.StatusCode} {response.StatusCode}.");
            }

            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Workstation API login threw.", ex);
            return false;
        }
    }

    /// <summary>
    /// Ends the server login session (best effort) and expires the locally held session
    /// cookies so no further request can ride the old session.
    /// </summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        try
        {
            var url = BuildUrl(UiApiRoutes.AuthApiLogout);
            using var response = await _httpClient.SendAsync(
                CreateMutationRequest(HttpMethod.Post, url, content: null), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogWarning(
                $"Workstation API logout failed ({ex.Message}); expiring local session cookies anyway.");
        }
        finally
        {
            ApiClientSession.Clear(_baseUrl);
        }
    }

    /// <summary>
    /// Serializes a request body to a JSON <see cref="StringContent"/>, or null when there is no body.
    /// </summary>
    private static StringContent? CreateJsonContent(object? body) =>
        body != null
            ? new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
            : null;

    /// <summary>
    /// Sends a request and deserializes a successful JSON response, returning null on any
    /// non-success status or failure (except cancellation, which is rethrown). Null is the
    /// documented result for 404 only; every other failure is logged before returning null so
    /// backend outages are no longer indistinguishable from empty data (audit finding P7).
    /// Legacy seam: new code uses <see cref="SendWithResponseAsync{T}"/>.
    /// </summary>
    private static async Task<T?> SendAsync<T>(string method, string url, Func<Task<HttpResponseMessage>> send, CancellationToken ct)
        where T : class
    {
        try
        {
            var response = await send();
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    LoggingService.Instance.LogWarning(
                        $"API {method} {url} failed with {(int)response.StatusCode} {response.StatusCode}; returning null. " +
                        "Callers on the legacy null-based seam cannot distinguish this failure from absent data.");
                }

                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"API {method} {url} threw; returning null.", ex);
            return null;
        }
    }

    /// <summary>
    /// Sends a request and wraps the outcome in an <see cref="ApiResponse{T}"/>, translating
    /// connection failures and unexpected exceptions into structured responses (cancellation is
    /// rethrown). Centralizes the try/catch/wrap pattern shared by the *WithResponseAsync methods.
    /// The request is built inside the try block so serialization errors are handled uniformly.
    /// </summary>
    private static async Task<ApiResponse<T>> SendWithResponseAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken ct) where T : class
    {
        try
        {
            var response = await send();
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new ApiResponse<T>
                {
                    Success = false,
                    StatusCode = (int)response.StatusCode,
                    ErrorMessage = json
                };
            }

            var data = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return new ApiResponse<T>
            {
                Success = true,
                StatusCode = (int)response.StatusCode,
                Data = data
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse<T>
            {
                Success = false,
                StatusCode = 0,
                ErrorMessage = $"Connection failed: {ex.Message}",
                IsConnectionError = true
            };
        }
        catch (Exception ex)
        {
            return new ApiResponse<T>
            {
                Success = false,
                StatusCode = 0,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Checks if the service is reachable.
    /// </summary>
    public async Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            var status = await _uiApiClient.GetStatusAsync(ct);
            var latencyMs = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;

            return new ServiceHealthResult
            {
                IsReachable = status != null,
                IsConnected = status?.IsConnected ?? false,
                LatencyMs = latencyMs,
                StatusCode = status != null ? 200 : 0,
                ErrorMessage = status == null ? "Service unreachable" : null
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ServiceHealthResult
            {
                IsReachable = false,
                IsConnected = false,
                LatencyMs = (float)(DateTime.UtcNow - startTime).TotalMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }

    private string BuildUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return _baseUrl;

        var path = endpoint.StartsWith('/') ? endpoint : $"/{endpoint}";
        return $"{_baseUrl}{path}";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _backfillHttpClient?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Settings for API client configuration.
/// </summary>
public sealed record AppSettings
{
    public string? ServiceUrl { get; init; }
    public int ServiceTimeoutSeconds { get; init; } = 30;
    public int BackfillTimeoutMinutes { get; init; } = 60;
}

/// <summary>
/// Event args for service URL changes.
/// </summary>
/// <remarks>
/// ApiResponse&lt;T&gt; and ServiceHealthResult are now defined in
/// Meridian.Contracts.Api.ClientModels.cs (imported via SharedModelAliases.cs)
/// </remarks>
public sealed class ServiceUrlChangedEventArgs : EventArgs
{
    public string OldUrl { get; init; } = string.Empty;
    public string NewUrl { get; init; } = string.Empty;
}
