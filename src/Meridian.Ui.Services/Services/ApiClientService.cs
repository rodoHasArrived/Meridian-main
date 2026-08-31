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
    private static readonly Lazy<ApiClientService> _instance = new(
        () => new ApiClientService(HttpClientFactoryProvider.CompatibilityFactory));

    private readonly object _configurationGate = new();
    private IHttpClientFactory _httpClientFactory;
    private EndpointSession _session;
    private readonly HttpClient _apiProxyClient;
    private readonly HttpClient _backfillProxyClient;
    private readonly UiApiClient _uiApiClient;
    private int _disposed;

    // Use centralized JSON options to avoid duplication across services
    private static JsonSerializerOptions JsonOptions => DesktopJsonOptions.Api;

    /// <summary>
    /// Gets the singleton instance of the ApiClientService.
    /// </summary>
    public static ApiClientService Instance => _instance.Value;

    /// <summary>
    /// Creates a host-owned API client service backed by the registered HTTP client factory.
    /// </summary>
    public ApiClientService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _session = EndpointSession.Create(
            _httpClientFactory,
            ApiEndpointDefaults.LocalApiBaseUrl,
            timeoutSeconds: 30,
            backfillTimeoutMinutes: 60,
            revision: 0);
        _apiProxyClient = CreateRoutingClient(this, useBackfillClient: false);
        _backfillProxyClient = CreateRoutingClient(this, useBackfillClient: true);
        _uiApiClient = new UiApiClient(_apiProxyClient, ApiEndpointDefaults.LocalApiBaseUrl, JsonOptions);
    }

    /// <summary>
    /// Gets the current base URL for the service.
    /// </summary>
    public string BaseUrl => Volatile.Read(ref _session).BaseUrl;

    /// <summary>
    /// Returns one immutable endpoint snapshot. Callers that need multiple settings for a
    /// request must use this snapshot so configuration cannot mix generations.
    /// </summary>
    public ApiEndpointConfiguration Configuration => Volatile.Read(ref _session).Configuration;

    /// <summary>
    /// Shared UI API client for status/config endpoints.
    /// </summary>
    public UiApiClient UiApi
    {
        get
        {
            ThrowIfDisposed();
            return _uiApiClient;
        }
    }

    /// <summary>
    /// Gets whether the client is configured with a non-default URL.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Volatile.Read(ref _session).BaseUrl);

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

        var newUrl = (settings.ServiceUrl ?? ApiEndpointDefaults.LocalApiBaseUrl).TrimEnd('/');
        var newTimeout = settings.ServiceTimeoutSeconds > 0 ? settings.ServiceTimeoutSeconds : 30;
        var newBackfillTimeout = settings.BackfillTimeoutMinutes > 0 ? settings.BackfillTimeoutMinutes : 60;

        ServiceUrlChangedEventArgs? change = null;
        lock (_configurationGate)
        {
            ThrowIfDisposed();
            var current = _session;
            var urlChanged = !string.Equals(current.BaseUrl, newUrl, StringComparison.OrdinalIgnoreCase);
            if (!urlChanged
                && current.TimeoutSeconds == newTimeout
                && current.BackfillTimeoutMinutes == newBackfillTimeout)
            {
                return;
            }

            var replacement = EndpointSession.Create(
                _httpClientFactory,
                newUrl,
                newTimeout,
                newBackfillTimeout,
                checked(current.Revision + 1));
            Volatile.Write(ref _session, replacement);
            _uiApiClient.UpdateBaseUrl(replacement.BaseUrl);
            current.Retire();

            if (urlChanged)
            {
                change = new ServiceUrlChangedEventArgs
                {
                    OldUrl = current.BaseUrl,
                    NewUrl = replacement.BaseUrl
                };
            }
        }

        if (change is not null)
        {
            // Cookie domains do not include ports. Remove the prior endpoint's session after
            // publishing the complete replacement generation so no request can combine its URL,
            // client, timeout, or CSRF lookup with fields from another generation.
            ApiClientSession.Clear(change.OldUrl);
            ServiceUrlChanged?.Invoke(this, change);
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

    /// <summary>
    /// Rebinds the compatibility singleton to the host-owned factory without changing its
    /// public identity. Existing consumers retain a complete old generation; subsequent calls
    /// use clients created by the host factory.
    /// </summary>
    internal void AttachHttpClientFactory(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        lock (_configurationGate)
        {
            ThrowIfDisposed();
            if (ReferenceEquals(_httpClientFactory, httpClientFactory))
                return;

            var current = _session;
            _httpClientFactory = httpClientFactory;
            var replacement = EndpointSession.Create(
                _httpClientFactory,
                current.BaseUrl,
                current.TimeoutSeconds,
                current.BackfillTimeoutMinutes,
                checked(current.Revision + 1));
            Volatile.Write(ref _session, replacement);
            current.Retire();
        }
    }

    /// <summary>
    /// Gets a shared HTTP client configured for long-running backfill operations.
    /// The stable proxy routes each request through one captured endpoint generation.
    /// </summary>
    public HttpClient GetBackfillClient()
    {
        ThrowIfDisposed();
        return _backfillProxyClient;
    }

    /// <summary>
    /// Performs a GET request to the specified endpoint. Returns null ONLY for 404 (not found)
    /// and — for legacy compatibility — on other failures, which are now logged. New callers
    /// must use <see cref="GetWithResponseAsync{T}"/> so failures stay distinguishable from
    /// absent data; the CI caller ratchet (check-apiclient-callers.py) blocks new call sites.
    /// </summary>
    public async Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class
    {
        using var lease = AcquireSession();
        var url = BuildUrl(lease.Session.BaseUrl, endpoint);
        return await SendAsync<T>(
            "GET",
            url,
            () => lease.Session.ApiClient.GetAsync(url, ct),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs a GET request and returns the raw response.
    /// </summary>
    public async Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default) where T : class
    {
        using var lease = AcquireSession();
        var url = BuildUrl(lease.Session.BaseUrl, endpoint);
        return await SendWithResponseAsync<T>(
            () => lease.Session.ApiClient.GetAsync(url, ct),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs a POST request with JSON body. Returns null ONLY for 404 (not found) and — for
    /// legacy compatibility — on other failures, which are now logged. New callers must use
    /// <see cref="PostWithResponseAsync{T}"/> so failures stay distinguishable from absent data;
    /// the CI caller ratchet (check-apiclient-callers.py) blocks new call sites.
    /// </summary>
    public async Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default) where T : class
    {
        using var lease = AcquireSession();
        var url = BuildUrl(lease.Session.BaseUrl, endpoint);
        return await SendAsync<T>(
            "POST",
            url,
            () => lease.Session.ApiClient.SendAsync(
                CreateMutationRequest(lease.Session.BaseUrl, HttpMethod.Post, url, CreateJsonContent(body)),
                ct),
            ct).ConfigureAwait(false);
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
        using var lease = AcquireSession();
        var url = BuildUrl(lease.Session.BaseUrl, endpoint);
        var client = ReferenceEquals(customClient, _backfillProxyClient)
            ? lease.Session.BackfillClient
            : customClient ?? lease.Session.ApiClient;
        return await SendWithResponseAsync<T>(
            () => client.SendAsync(
                CreateMutationRequest(lease.Session.BaseUrl, HttpMethod.Post, url, CreateJsonContent(body)),
                ct),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a DELETE request and returns the API response.
    /// </summary>
    public async Task<ApiResponse<T>> DeleteWithResponseAsync<T>(
        string endpoint,
        CancellationToken ct = default) where T : class
    {
        using var lease = AcquireSession();
        var url = BuildUrl(lease.Session.BaseUrl, endpoint);
        return await SendWithResponseAsync<T>(
            () => lease.Session.ApiClient.SendAsync(
                CreateMutationRequest(lease.Session.BaseUrl, HttpMethod.Delete, url, content: null),
                ct),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a mutating request that echoes the server-issued CSRF cookie as the
    /// X-CSRF-Token header. The server's CookieCsrfMiddleware requires the header on
    /// session-authenticated POST/PUT/PATCH/DELETE calls under /api; before a login
    /// session exists there is no CSRF cookie and the header is simply omitted.
    /// </summary>
    private static HttpRequestMessage CreateMutationRequest(
        string baseUrl,
        HttpMethod method,
        string url,
        HttpContent? content)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        AddCsrfHeader(request, baseUrl);
        return request;
    }

    private static void AddCsrfHeader(HttpRequestMessage request, string baseUrl)
    {
        var csrfToken = ApiClientSession.GetCsrfToken(baseUrl);
        if (!string.IsNullOrWhiteSpace(csrfToken))
        {
            request.Headers.TryAddWithoutValidation(ApiClientSession.CsrfHeaderName, csrfToken);
        }
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
        using var lease = AcquireSession();
        var url = BuildUrl(lease.Session.BaseUrl, UiApiRoutes.AuthApiLogin);
        try
        {
            using var response = await lease.Session.ApiClient.PostAsync(
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
        using var lease = AcquireSession();
        try
        {
            var url = BuildUrl(lease.Session.BaseUrl, UiApiRoutes.AuthApiLogout);
            using var response = await lease.Session.ApiClient.SendAsync(
                CreateMutationRequest(lease.Session.BaseUrl, HttpMethod.Post, url, content: null), ct);
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
            ApiClientSession.Clear(lease.Session.BaseUrl);
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
            using var response = await send().ConfigureAwait(false);
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
            using var response = await send().ConfigureAwait(false);
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
            var status = await _uiApiClient.GetStatusAsync(ct).ConfigureAwait(false);
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

    private static string BuildUrl(string baseUrl, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return baseUrl;

        var path = endpoint.StartsWith('/') ? endpoint : $"/{endpoint}";
        return $"{baseUrl}{path}";
    }

    private EndpointSessionLease AcquireSession()
    {
        while (true)
        {
            ThrowIfDisposed();
            var session = Volatile.Read(ref _session);
            if (session.TryAcquire(out var lease))
                return lease;
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static HttpClient CreateRoutingClient(ApiClientService owner, bool useBackfillClient)
        => new(new EndpointRoutingHandler(owner, useBackfillClient), disposeHandler: true)
        {
            // The captured endpoint generation owns the authoritative timeout. Leaving the
            // proxy unbounded prevents a second, stale timeout from racing configuration.
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

    public void Dispose()
    {
        EndpointSession session;
        lock (_configurationGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            session = _session;
        }

        session.Retire();
        _apiProxyClient.Dispose();
        _backfillProxyClient.Dispose();
    }

    private sealed class EndpointRoutingHandler(
        ApiClientService owner,
        bool useBackfillClient) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            using var lease = owner.AcquireSession();
            var relativePath = request.RequestUri?.PathAndQuery ?? string.Empty;
            var targetUri = BuildUrl(lease.Session.BaseUrl, relativePath);
            using var forwarded = await CloneRequestAsync(request, targetUri, cancellationToken)
                .ConfigureAwait(false);

            if (forwarded.Method != HttpMethod.Get
                && forwarded.Method != HttpMethod.Head
                && forwarded.Method != HttpMethod.Options
                && forwarded.Method != HttpMethod.Trace)
            {
                AddCsrfHeader(forwarded, lease.Session.BaseUrl);
            }

            var client = useBackfillClient
                ? lease.Session.BackfillClient
                : lease.Session.ApiClient;
            var response = await client.SendAsync(forwarded, cancellationToken).ConfigureAwait(false);
            response.RequestMessage = request;
            return response;
        }

        private static async Task<HttpRequestMessage> CloneRequestAsync(
            HttpRequestMessage source,
            string targetUri,
            CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(source.Method, targetUri)
            {
                Version = source.Version,
                VersionPolicy = source.VersionPolicy
            };

            foreach (var header in source.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (source.Content is not null)
            {
                var bytes = await source.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                clone.Content = new ByteArrayContent(bytes);
                foreach (var header in source.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }

    private sealed class EndpointSession : IDisposable
    {
        private readonly object _leaseGate = new();
        private int _leaseCount;
        private bool _acceptingLeases = true;
        private int _disposed;

        private EndpointSession(
            string baseUrl,
            int timeoutSeconds,
            int backfillTimeoutMinutes,
            long revision,
            HttpClient apiClient,
            HttpClient backfillClient)
        {
            BaseUrl = baseUrl;
            TimeoutSeconds = timeoutSeconds;
            BackfillTimeoutMinutes = backfillTimeoutMinutes;
            Revision = revision;
            ApiClient = apiClient;
            BackfillClient = backfillClient;
            Configuration = new ApiEndpointConfiguration(
                baseUrl,
                timeoutSeconds,
                backfillTimeoutMinutes,
                revision);
        }

        public string BaseUrl { get; }
        public int TimeoutSeconds { get; }
        public int BackfillTimeoutMinutes { get; }
        public long Revision { get; }
        public HttpClient ApiClient { get; }
        public HttpClient BackfillClient { get; }
        public ApiEndpointConfiguration Configuration { get; }

        public static EndpointSession Create(
            IHttpClientFactory factory,
            string baseUrl,
            int timeoutSeconds,
            int backfillTimeoutMinutes,
            long revision)
        {
            var apiClient = factory.CreateClient(HttpClientNames.ApiClient);
            HttpClient? backfillClient = null;
            try
            {
                apiClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                backfillClient = factory.CreateClient(HttpClientNames.BackfillClient);
                backfillClient.Timeout = TimeSpan.FromMinutes(backfillTimeoutMinutes);
                return new EndpointSession(
                    baseUrl,
                    timeoutSeconds,
                    backfillTimeoutMinutes,
                    revision,
                    apiClient,
                    backfillClient);
            }
            catch
            {
                apiClient.Dispose();
                backfillClient?.Dispose();
                throw;
            }
        }

        public bool TryAcquire(out EndpointSessionLease lease)
        {
            lock (_leaseGate)
            {
                if (!_acceptingLeases)
                {
                    lease = null!;
                    return false;
                }

                checked
                { _leaseCount++; }
                lease = new EndpointSessionLease(this);
                return true;
            }
        }

        public void Retire()
        {
            var dispose = false;
            lock (_leaseGate)
            {
                _acceptingLeases = false;
                dispose = _leaseCount == 0;
            }

            if (dispose)
                Dispose();
        }

        public void Release()
        {
            var dispose = false;
            lock (_leaseGate)
            {
                _leaseCount--;
                dispose = !_acceptingLeases && _leaseCount == 0;
            }

            if (dispose)
                Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            ApiClient.Dispose();
            BackfillClient.Dispose();
        }
    }

    private sealed class EndpointSessionLease(EndpointSession session) : IDisposable
    {
        private EndpointSession? _session = session;

        public EndpointSession Session => _session
            ?? throw new ObjectDisposedException(nameof(EndpointSessionLease));

        public void Dispose()
            => Interlocked.Exchange(ref _session, null)?.Release();
    }
}

/// <summary>
/// Immutable request-generation settings published atomically by <see cref="ApiClientService"/>.
/// </summary>
public sealed record ApiEndpointConfiguration(
    string BaseUrl,
    int TimeoutSeconds,
    int BackfillTimeoutMinutes,
    long Revision);

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
