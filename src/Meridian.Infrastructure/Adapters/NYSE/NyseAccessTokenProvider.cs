using System.Net.Http.Headers;
using System.Text.Json;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.NYSE;

/// <summary>
/// Shared OAuth2 access-token provider for the NYSE Direct adapter. Owns token acquisition,
/// caching, and expiry, and produces authenticated REST clients/requests. Extracted from
/// <see cref="NYSEDataSource"/> so the streaming and historical concerns share a single token
/// cache instead of each re-implementing authentication.
/// </summary>
internal sealed class NyseAccessTokenProvider : IDisposable
{
    private readonly NYSEOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public NyseAccessTokenProvider(NYSEOptions options, IHttpClientFactory httpClientFactory, ILogger log)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _log = log;
    }

    /// <summary>The currently cached bearer token, if any (used for the WebSocket handshake header).</summary>
    public string? AccessToken => _accessToken;

    /// <summary>
    /// Ensures a valid (non-expired) access token is cached, obtaining a fresh one via the
    /// OAuth2 client-credentials flow when needed. Refreshes 5 minutes before expiry.
    /// </summary>
    public async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
        {
            return;
        }

        await _authLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            {
                return;
            }

            var apiKey = _options.ResolveApiKey();
            var apiSecret = _options.ResolveApiSecret();

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                throw new InvalidOperationException("NYSE API credentials not configured");
            }

            // OAuth2 client credentials flow
            var authContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ResolveClientId() ?? apiKey,
                ["client_secret"] = apiSecret
            });

            using var authRequest = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
            {
                Content = authContent
            };

            using var httpClient = CreateHttpClient();
            using var response = await httpClient.SendAsync(authRequest, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var tokenResponse = JsonSerializer.Deserialize<NYSETokenResponse>(json);

            _accessToken = tokenResponse?.AccessToken
                ?? throw new InvalidOperationException("Failed to obtain NYSE access token");
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            _log.Debug("Obtained NYSE access token, expires at {Expiry}", _tokenExpiry);
        }
        finally
        {
            _authLock.Release();
        }
    }

    /// <summary>Applies the cached bearer token to the given request, if a token is available.</summary>
    public void AddAuthHeader(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
    }

    /// <summary>
    /// Creates a named <see cref="HttpClient"/> via <see cref="IHttpClientFactory"/>, setting the
    /// dynamic <see cref="NYSEOptions.EffectiveBaseUrl"/> as the base address. The shared handler
    /// pool (connection lifetime, retry pipeline) is managed entirely by the factory.
    /// </summary>
    public HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientNames.NYSE);
        // BaseAddress is dynamic per NYSEOptions; it must be applied to the lightweight
        // HttpClient wrapper. The underlying HttpMessageHandler/connection pool is
        // owned by IHttpClientFactory and survives beyond any single HttpClient instance.
        client.BaseAddress = new Uri(_options.EffectiveBaseUrl);
        return client;
    }

    public void Dispose() => _authLock.Dispose();
}
