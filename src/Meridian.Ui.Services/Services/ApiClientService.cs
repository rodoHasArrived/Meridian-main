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
        _baseUrl = "http://localhost:8080";
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

        var newUrl = settings.ServiceUrl ?? "http://localhost:8080";
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
    /// Performs a GET request to the specified endpoint.
    /// </summary>
    public async Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Performs a GET request and returns the raw response.
    /// </summary>
    public async Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
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

            var data = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<T>(json, JsonOptions);
            return new ApiResponse<T>
            {
                Success = true,
                StatusCode = (int)response.StatusCode,
                Data = data
            };
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
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
    /// Performs a POST request with JSON body.
    /// </summary>
    public async Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);
        try
        {
            var content = body != null
                ? new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
                : null;

            var response = await _httpClient.PostAsync(url, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Performs a POST request and returns the full response.
    /// </summary>
    public async Task<ApiResponse<T>> PostWithResponseAsync<T>(
        string endpoint,
        object? body = null,
        CancellationToken ct = default,
        HttpClient? customClient = null) where T : class
    {
        var url = BuildUrl(endpoint);
        var client = customClient ?? _httpClient;

        try
        {
            var content = body != null
                ? new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
                : null;

            var response = await client.PostAsync(url, content, ct);
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

            var data = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<T>(json, JsonOptions);
            return new ApiResponse<T>
            {
                Success = true,
                StatusCode = (int)response.StatusCode,
                Data = data
            };
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
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
    /// Sends a DELETE request and returns the API response.
    /// </summary>
    public async Task<ApiResponse<T>> DeleteWithResponseAsync<T>(
        string endpoint,
        CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);

        try
        {
            var response = await _httpClient.DeleteAsync(url, ct);
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

            var data = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<T>(json, JsonOptions);
            return new ApiResponse<T>
            {
                Success = true,
                StatusCode = (int)response.StatusCode,
                Data = data
            };
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
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
