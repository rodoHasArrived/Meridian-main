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
    private readonly ProviderCredentialScope? _ownershipScope;
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
        HttpClient? httpClient = null,
        ILogger? logger = null,
        IOAuthTokenVault? vault = null,
        ProviderCredentialScope? ownershipScope = null)
    {
        _log = logger ?? LoggingSetup.ForContext<OAuthTokenRefreshService>();
        _vault = vault ?? new FileProviderCredentialStore(dataRoot);
        _ownershipScope = ownershipScope;
        if (_ownershipScope is not null && _vault is not IScopedOAuthTokenVault)
            throw new InvalidOperationException("Configured OAuth vault does not support scoped ownership.");
        _config = config ?? new CredentialExpirationConfig();
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _tokenPersistencePath = Path.Combine(dataRoot, ".mdc", "oauth_tokens.json");
        try
        { LoadPersistedTokens(); }
        catch (Exception ex)
        {
            _httpClient.Dispose();
            _log.Warning("Failed to initialize encrypted OAuth persistence ({ExceptionType})", ex.GetType().Name);
            throw new InvalidOperationException("Encrypted OAuth persistence could not be initialized.");
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
        await PersistTokenAsync(providerName, token, ct).ConfigureAwait(false);
        _tokens[providerName] = token;
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
        await PersistTokenAsync(providerName, null, ct).ConfigureAwait(false);
        _tokens.TryRemove(providerName, out _);
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

            // The provider may already have invalidated the old refresh token. Retain the new
            // token in memory even if durable storage fails, but never acknowledge that failure as success.
            _tokens[providerName] = newToken;
            await PersistTokenAsync(providerName, newToken, ct).ConfigureAwait(false);

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

    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // A token file written before this change carries the umask default, and rewriting it only
    // happens on the next refresh - which may be hours away, or never if the tokens are still
    // valid. Tighten on read so an existing install is protected from startup rather than from
    // whenever the next write happens to occur.
    private void RestrictExistingTokenFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(_tokenPersistencePath);
            if ((mode & ~OwnerOnlyFileMode) == UnixFileMode.None)
            {
                return;
            }

            File.SetUnixFileMode(_tokenPersistencePath, OwnerOnlyFileMode);
            _log.Warning(
                "Persisted OAuth tokens at {TokenPath} were reachable beyond their owner ({ExposedMode}); tightened to owner-only. Treat the stored access and refresh tokens as disclosed and revoke them.",
                _tokenPersistencePath,
                mode & ~OwnerOnlyFileMode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _log.Warning("Could not verify permissions on persisted OAuth tokens at {TokenPath} ({FailureType})",
                _tokenPersistencePath, ex.GetType().Name);
        }
    }

    private Task PersistTokenAsync(string providerName, OAuthToken? token, CancellationToken ct)
        => _ownershipScope is null
            ? _vault.SaveOAuthTokenAsync(providerName, token, ct)
            : ((IScopedOAuthTokenVault)_vault).SaveScopedOAuthTokenAsync(providerName, token, _ownershipScope, ct);

    private void LoadPersistedTokens()
    {
        if (_ownershipScope is not null)
        {
            // Legacy tokens have no proven owner. Never claim them for a new connection.
            foreach (var pair in ((IScopedOAuthTokenVault)_vault).ReadScopedOAuthTokensAsync(_ownershipScope).GetAwaiter().GetResult())
                _tokens[pair.Key] = pair.Value;
            return;
        }
        // Construction fails closed if migration or encrypted persistence fails. Never replace
        // unreadable retained tokens with an empty snapshot on disposal.
        if (File.Exists(_tokenPersistencePath))
        {
            RestrictExistingTokenFile();
            var tokens = JsonSerializer.Deserialize<Dictionary<string, OAuthToken>>(File.ReadAllText(_tokenPersistencePath))
                ?? throw new InvalidOperationException("Legacy OAuth token snapshot is invalid.");
            _vault.ImportOAuthTokensAsync(tokens).ConfigureAwait(false).GetAwaiter().GetResult();
            using (var stream = new FileStream(_tokenPersistencePath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var zeros = new byte[64 * 1024];
                var remaining = stream.Length;
                while (remaining > 0)
                {
                    var count = (int)Math.Min(remaining, zeros.Length);
                    stream.Write(zeros, 0, count);
                    remaining -= count;
                }
                stream.Flush(flushToDisk: true);
            }
            File.Delete(_tokenPersistencePath);
        }
        foreach (var pair in _vault.ReadOAuthTokensAsync().ConfigureAwait(false).GetAwaiter().GetResult())
            _tokens[pair.Key] = pair.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
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
