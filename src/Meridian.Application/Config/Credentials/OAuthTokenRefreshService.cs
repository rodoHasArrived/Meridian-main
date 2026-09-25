using System.Collections.Concurrent;
using Meridian.DataIntegration.Credentials;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Meridian.Core.Logging;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Application.Config.Credentials;

/// <summary>
/// Background service for automatically refreshing OAuth tokens before they expire.
/// Supports extensible provider registration for different OAuth implementations.
/// </summary>
public sealed class OAuthTokenRefreshService : IAsyncDisposable
{
    private readonly ILogger _log;
    private readonly HttpClient _httpClient;
    private readonly CredentialExpirationConfig _config;
    private readonly ConcurrentDictionary<string, OAuthToken> _tokens = new();
    private readonly ConcurrentDictionary<string, OAuthProviderConfig> _providerConfigs = new();
    private readonly string _tokenPersistencePath;
    private readonly IOAuthTokenVault _vault;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _initializationSync = new();
    private readonly object _lifecycleSync = new();
    private Task? _initialization;
    private Task? _stopping;

    private CancellationTokenSource? _cts;
    private Task? _refreshLoop;

    // Events for monitoring
    public event Action<string, OAuthToken>? OnTokenRefreshed;
    public event Action<string, string>? OnRefreshFailed;
    public event Action<string, TimeSpan>? OnTokenExpiringSoon;

    public OAuthTokenRefreshService(
        string dataRoot,
        CredentialExpirationConfig? config = null,
        HttpClient? httpClient = null,
        ILogger? logger = null,
        IOAuthTokenVault? vault = null)
    {
        _log = logger ?? LoggingSetup.ForContext<OAuthTokenRefreshService>();
        _vault = vault ?? new FileProviderCredentialStore(dataRoot);
        _config = config ?? new CredentialExpirationConfig();
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _tokenPersistencePath = Path.Combine(dataRoot, ".mdc", "oauth_tokens.json");
    }

    /// <summary>Loads and migrates retained tokens asynchronously before synchronous token inspection.</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        lock (_initializationSync)
        {
            if (_initialization is null || _initialization.IsFaulted || _initialization.IsCanceled)
                _initialization = LoadPersistedTokensAsync();
            return _initialization.WaitAsync(ct);
        }
    }

    private void RequireInitialized()
    {
        lock (_initializationSync)
        {
            if (_initialization is not { IsCompletedSuccessfully: true })
                throw new InvalidOperationException("Await InitializeAsync before inspecting OAuth tokens.");
        }
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
        lock (_lifecycleSync)
        {
            if (_stopping is not null || _refreshLoop is { IsCompleted: false })
                return;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _refreshLoop = RefreshLoopAsync(_cts.Token);
        }
        _log.Information("OAuth token refresh service started");
    }

    /// <summary>Stops the refresh loop and clears its lifecycle state even after failure.</summary>
    public Task StopAsync(CancellationToken ct = default)
    {
        CancellationTokenSource? source = null;
        Task? loop = null;
        TaskCompletionSource? completion = null;
        Task stopping;
        lock (_lifecycleSync)
        {
            if (_stopping is not null)
                stopping = _stopping;
            else if (_cts is null)
                return Task.CompletedTask;
            else
            {
                source = _cts;
                loop = _refreshLoop;
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                stopping = _stopping = completion.Task;
            }
        }
        if (completion is not null)
            _ = StopLoopAsync(source!, loop, completion);
        return stopping.WaitAsync(ct);
    }

    private async Task StopLoopAsync(CancellationTokenSource source, Task? loop, TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            // Cancellation callbacks and asynchronous completion run outside the state lock.
            source.Cancel();
            if (loop is not null)
                await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception ex) { failure = ex; }
        finally
        {
            source.Dispose();
            lock (_lifecycleSync)
            {
                _cts = null;
                _refreshLoop = null;
                _stopping = null;
            }
        }
        if (failure is null)
            completion.TrySetResult();
        else
            completion.TrySetException(failure);
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
        await InitializeAsync(ct).ConfigureAwait(false);
        await PersistTokenAsync(providerName, token, ct).ConfigureAwait(false);
        _log.Debug("Stored OAuth token for {Provider}, expires at {ExpiresAt}", providerName, token.ExpiresAt);
    }

    /// <summary>
    /// Gets the current OAuth token for a provider.
    /// </summary>
    public OAuthToken? GetToken(string providerName)
    {
        RequireInitialized();
        return _tokens.TryGetValue(providerName, out var token) ? token : null;
    }

    /// <summary>
    /// Gets all stored OAuth tokens with their status.
    /// </summary>
    public IReadOnlyDictionary<string, (OAuthToken Token, TokenStatus Status)> GetAllTokens()
    {
        RequireInitialized();
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
        await InitializeAsync(ct).ConfigureAwait(false);
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
        await InitializeAsync(ct).ConfigureAwait(false);
        await PersistTokenAsync(providerName, null, ct).ConfigureAwait(false);
        _log.Information("Removed OAuth token for {Provider}", providerName);
    }

    private async Task PersistTokenAsync(string providerName, OAuthToken? token, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                await _vault.SaveOAuthTokenAsync(providerName, token, ct).ConfigureAwait(false);
            }
            catch
            {
                // Audit can fail after the encrypted mutation commits. Match the durable
                // record before propagating failure, and evict if recovery cannot be read.
                _tokens.TryRemove(providerName, out _);
                var retained = await _vault.ReadOAuthTokensAsync(CancellationToken.None).ConfigureAwait(false);
                if (retained.TryGetValue(providerName, out var persisted))
                    _tokens[providerName] = persisted;
                throw;
            }
            if (token is null)
                _tokens.TryRemove(providerName, out _);
            else
                _tokens[providerName] = token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        try
        {
            await InitializeAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            _log.Error("OAuth token refresh initialization failed ({FailureType}); initialization can be retried", ex.GetType().Name);
            return;
        }
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
                _log.Error("Error in OAuth token refresh loop ({FailureType})", ex.GetType().Name);
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
            // A removal or manual replacement may have completed while this refresh waited.
            if (!_tokens.TryGetValue(providerName, out currentToken))
                return new OAuthRefreshResult(false, Error: $"No token stored for provider: {providerName}");
            if (!currentToken.CanRefresh)
                return new OAuthRefreshResult(false, Error: "Token cannot be refreshed (no refresh token or refresh token expired)");
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
                // Provider bodies and reason phrases can echo bearer tokens or client secrets.
                var error = $"Token refresh failed: HTTP {(int)response.StatusCode}.";
                OnRefreshFailed?.Invoke(providerName, error);
                return new OAuthRefreshResult(false, Error: error, RefreshedAt: DateTimeOffset.UtcNow);
            }

            // Once rotation succeeds remotely, lifecycle cancellation must not discard the
            // response or cancel the replacement token's durable commit.
            var responseContent = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
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

            // The provider may already have invalidated the old refresh token. Retain the new
            // token in memory even if durable storage fails, but never acknowledge that failure as success.
            _tokens[providerName] = newToken;
            await _vault.SaveOAuthTokenAsync(providerName, newToken, CancellationToken.None).ConfigureAwait(false);

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
            const string error = "Token refresh failed.";
            OnRefreshFailed?.Invoke(providerName, error);
            _log.Error("OAuth token refresh failed for {Provider} ({FailureType})",
                providerName, ex.GetType().Name);
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

    private async Task LoadPersistedTokensAsync()
    {
        try
        {
            await LegacyCredentialFileMigration.MigrateAsync(_tokenPersistencePath, async (json, ct) =>
            {
                var tokens = JsonSerializer.Deserialize<Dictionary<string, OAuthToken>>(json)
                    ?? throw new InvalidOperationException("Legacy OAuth token snapshot is invalid.");
                await _vault.ImportOAuthTokensAsync(tokens, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
            var retained = await _vault.ReadOAuthTokensAsync().ConfigureAwait(false);
            _tokens.Clear();
            foreach (var pair in retained)
                _tokens[pair.Key] = pair.Value;
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to initialize encrypted OAuth persistence ({ExceptionType})", ex.GetType().Name);
            throw new InvalidOperationException("Encrypted OAuth persistence could not be initialized.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Task? initialization;
        lock (_initializationSync)
            initialization = _initialization;
        if (initialization is not null)
        {
            try
            { await initialization.ConfigureAwait(false); }
            catch (InvalidOperationException) when (initialization.IsFaulted) { }
        }
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
