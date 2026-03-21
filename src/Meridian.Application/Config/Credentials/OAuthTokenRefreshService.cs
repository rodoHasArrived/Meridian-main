using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Infrastructure.Http;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Application.Config.Credentials;

/// <summary>
/// Background service for automatically refreshing OAuth tokens before they expire.
/// Supports extensible provider registration for different OAuth implementations.
/// </summary>
public sealed class OAuthTokenRefreshService : IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<OAuthTokenRefreshService>();
    private readonly HttpClient _httpClient;
    private readonly CredentialExpirationConfig _config;
    private readonly ConcurrentDictionary<string, OAuthToken> _tokens = new();
    private readonly ConcurrentDictionary<string, OAuthProviderConfig> _providerConfigs = new();
    private readonly string _tokenPersistencePath;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _refreshLoop;

    // Events for monitoring
    public event Action<string, OAuthToken>? OnTokenRefreshed;
    public event Action<string, string>? OnRefreshFailed;
    public event Action<string, TimeSpan>? OnTokenExpiringSoon;

    public OAuthTokenRefreshService(
        string dataRoot,
        CredentialExpirationConfig? config = null,
        HttpClient? httpClient = null)
    {
        _config = config ?? new CredentialExpirationConfig();
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _tokenPersistencePath = Path.Combine(dataRoot, ".mdc", "oauth_tokens.json");
        LoadPersistedTokens();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        var client = HttpClientFactoryProvider.CreateClient(HttpClientNames.OAuthTokenRefresh);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Meridian/1.6.1");
        return client;
    }

    /// <summary>
    /// Starts the background token refresh loop.
    /// </summary>
    public void Start()
    {
        if (_refreshLoop != null)
            return;

        _cts = new CancellationTokenSource();
        _refreshLoop = RefreshLoopAsync(_cts.Token);
        _log.Information("OAuth token refresh service started");
    }

    /// <summary>
    /// Stops the background refresh loop.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_cts == null)
            return;

        _cts.Cancel();

        if (_refreshLoop != null)
        {
            try
            { await _refreshLoop; }
            catch (OperationCanceledException) { }
        }

        _cts.Dispose();
        _cts = null;
        _refreshLoop = null;

        _log.Information("OAuth token refresh service stopped");
    }

    /// <summary>
    /// Registers an OAuth provider configuration.
    /// </summary>
    public void RegisterProvider(OAuthProviderConfig providerConfig)
    {
        ArgumentNullException.ThrowIfNull(providerConfig);
        _providerConfigs[providerConfig.ProviderName] = providerConfig;
        _log.Debug("Registered OAuth provider: {Provider}", providerConfig.ProviderName);
    }

    /// <summary>
    /// Stores an OAuth token for a provider.
    /// </summary>
    public async Task StoreTokenAsync(string providerName, OAuthToken token, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        _tokens[providerName] = token;
        await PersistTokensAsync(ct);
        _log.Debug("Stored OAuth token for {Provider}, expires at {ExpiresAt}", providerName, token.ExpiresAt);
    }

    /// <summary>
    /// Gets the current OAuth token for a provider.
    /// </summary>
    public OAuthToken? GetToken(string providerName)
    {
        return _tokens.TryGetValue(providerName, out var token) ? token : null;
    }

    /// <summary>
    /// Gets all stored OAuth tokens with their status.
    /// </summary>
    public IReadOnlyDictionary<string, (OAuthToken Token, TokenStatus Status)> GetAllTokens()
    {
        return _tokens.ToDictionary(
            kvp => kvp.Key,
            kvp => (kvp.Value, GetTokenStatus(kvp.Value))
        );
    }

    /// <summary>
    /// Manually triggers a token refresh for a provider.
    /// </summary>
    public async Task<OAuthRefreshResult> RefreshTokenAsync(string providerName, CancellationToken ct = default)
    {
        if (!_tokens.TryGetValue(providerName, out var currentToken))
        {
            return new OAuthRefreshResult(false, Error: $"No token stored for provider: {providerName}");
        }

        if (!_providerConfigs.TryGetValue(providerName, out var providerConfig))
        {
            return new OAuthRefreshResult(false, Error: $"No provider configuration for: {providerName}");
        }

        return await RefreshTokenInternalAsync(providerName, currentToken, providerConfig, ct);
    }

    /// <summary>
    /// Removes stored token for a provider.
    /// </summary>
    public async Task RemoveTokenAsync(string providerName, CancellationToken ct = default)
    {
        _tokens.TryRemove(providerName, out _);
        await PersistTokensAsync(ct);
        _log.Information("Removed OAuth token for {Provider}", providerName);
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
                await CheckAndRefreshTokensAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error in OAuth token refresh loop");
            }
        }
    }

    private async Task CheckAndRefreshTokensAsync(CancellationToken ct)
    {
        foreach (var (providerName, token) in _tokens)
        {
            if (!_providerConfigs.TryGetValue(providerName, out var providerConfig))
                continue;

            // Check if token needs refresh
            if (ShouldRefreshToken(token))
            {
                _log.Information("Auto-refreshing token for {Provider} (expires at {ExpiresAt})",
                    providerName, token.ExpiresAt);

                var result = await RefreshTokenInternalAsync(providerName, token, providerConfig, ct);

                if (!result.Success)
                {
                    _log.Warning("Failed to auto-refresh token for {Provider}: {Error}",
                        providerName, result.Error);
                }
            }
            else if (IsExpiringSoon(token))
            {
                var timeUntilExpiration = token.ExpiresAt - DateTimeOffset.UtcNow;
                OnTokenExpiringSoon?.Invoke(providerName, timeUntilExpiration);
            }
        }
    }

    private bool ShouldRefreshToken(OAuthToken token)
    {
        if (!token.CanRefresh)
            return false;
        if (token.IsExpired)
            return true;

        var daysUntilExpiration = (token.ExpiresAt - DateTimeOffset.UtcNow).TotalDays;
        return daysUntilExpiration <= _config.AutoRefreshDaysBeforeExpiration;
    }

    private bool IsExpiringSoon(OAuthToken token)
    {
        var daysUntilExpiration = (token.ExpiresAt - DateTimeOffset.UtcNow).TotalDays;
        return daysUntilExpiration <= _config.WarnDaysBeforeExpiration && !token.IsExpired;
    }

    private async Task<OAuthRefreshResult> RefreshTokenInternalAsync(
        string providerName,
        OAuthToken currentToken,
        OAuthProviderConfig providerConfig,
        CancellationToken ct)
    {
        if (!currentToken.CanRefresh)
        {
            return new OAuthRefreshResult(false, Error: "Token cannot be refreshed (no refresh token or refresh token expired)");
        }

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Build refresh request
            var tokenEndpoint = providerConfig.TokenEndpoint;
            if (string.IsNullOrEmpty(tokenEndpoint))
            {
                return new OAuthRefreshResult(false, Error: "Token endpoint not configured");
            }

            var requestBody = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = currentToken.RefreshToken!,
                ["client_id"] = providerConfig.ClientId
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = requestBody
            };

            // Add client secret if configured (for confidential clients)
            if (!string.IsNullOrEmpty(providerConfig.ClientSecret))
            {
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{providerConfig.ClientId}:{providerConfig.ClientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                var error = $"Token refresh failed: {response.StatusCode} - {errorContent}";
                OnRefreshFailed?.Invoke(providerName, error);
                return new OAuthRefreshResult(false, Error: error, RefreshedAt: DateTimeOffset.UtcNow);
            }

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseContent);

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                return new OAuthRefreshResult(false, Error: "Invalid token response", RefreshedAt: DateTimeOffset.UtcNow);
            }

            var newToken = new OAuthToken(
                AccessToken: tokenResponse.AccessToken,
                TokenType: tokenResponse.TokenType ?? "Bearer",
                ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn ?? 3600),
                RefreshToken: tokenResponse.RefreshToken ?? currentToken.RefreshToken,
                Scope: tokenResponse.Scope,
                IssuedAt: DateTimeOffset.UtcNow
            );

            _tokens[providerName] = newToken;
            await PersistTokensAsync();

            OnTokenRefreshed?.Invoke(providerName, newToken);
            _log.Information("Successfully refreshed OAuth token for {Provider}, new expiration: {ExpiresAt}",
                providerName, newToken.ExpiresAt);

            return new OAuthRefreshResult(
                Success: true,
                Token: newToken,
                RefreshedAt: DateTimeOffset.UtcNow,
                NextRefreshAt: newToken.ExpiresAt.AddDays(-_config.AutoRefreshDaysBeforeExpiration)
            );
        }
        catch (Exception ex)
        {
            var error = $"Token refresh exception: {ex.Message}";
            OnRefreshFailed?.Invoke(providerName, error);
            _log.Error(ex, "Exception during OAuth token refresh for {Provider}", providerName);
            return new OAuthRefreshResult(false, Error: error, RefreshedAt: DateTimeOffset.UtcNow);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static TokenStatus GetTokenStatus(OAuthToken token)
    {
        if (token.IsExpired)
            return TokenStatus.Expired;
        if (token.IsExpiringSoon)
            return TokenStatus.ExpiringSoon;
        return TokenStatus.Valid;
    }

    private void LoadPersistedTokens()
    {
        try
        {
            if (!File.Exists(_tokenPersistencePath))
                return;

            var json = File.ReadAllText(_tokenPersistencePath);
            var tokens = JsonSerializer.Deserialize<Dictionary<string, OAuthToken>>(json);

            if (tokens != null)
            {
                foreach (var kvp in tokens)
                {
                    _tokens[kvp.Key] = kvp.Value;
                }
            }

            _log.Debug("Loaded OAuth tokens for {Count} providers", _tokens.Count);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load persisted OAuth tokens");
        }
    }

    private async Task PersistTokensAsync(CancellationToken ct = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_tokenPersistencePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var tokens = _tokens.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var json = JsonSerializer.Serialize(tokens, new JsonSerializerOptions { WriteIndented = true });

            await AtomicFileWriter.WriteAsync(_tokenPersistencePath, json, ct);
            _log.Debug("Persisted OAuth tokens for {Count} providers", tokens.Count);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to persist OAuth tokens");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await PersistTokensAsync();
        _refreshLock.Dispose();
        _httpClient.Dispose();
    }

    /// <summary>
    /// Token status enumeration for display purposes.
    /// </summary>
    public enum TokenStatus : byte
    {
        Valid,
        ExpiringSoon,
        Expired,
        Refreshing
    }

    /// <summary>
    /// Internal class for deserializing OAuth token responses.
    /// </summary>
    private sealed class OAuthTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }
}
