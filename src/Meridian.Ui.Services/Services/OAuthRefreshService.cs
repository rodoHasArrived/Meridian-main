using System.Threading;
using System.Timers;

namespace Meridian.Ui.Services;

/// <summary>
/// Background service that automatically refreshes OAuth tokens before they expire.
/// Monitors credential metadata and proactively refreshes tokens based on configured thresholds.
/// </summary>
public sealed class OAuthRefreshService : IDisposable
{
    private static readonly Lazy<OAuthRefreshService> _instance = new(() => new OAuthRefreshService());
    /// <summary>
    /// Gets the singleton instance of the OAuth refresh service.
    /// </summary>
    public static OAuthRefreshService Instance => _instance.Value;

    private readonly CredentialService _credentialService;
    private readonly System.Timers.Timer _refreshTimer;
    private readonly System.Timers.Timer _expirationCheckTimer;
    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// Default refresh threshold in minutes before token expiration.
    /// </summary>
    public int RefreshThresholdMinutes { get; set; } = 5;

    /// <summary>
    /// Interval between expiration checks in milliseconds.
    /// </summary>
    public int CheckIntervalMs { get; set; } = 60000; // 1 minute

    /// <summary>
    /// Event raised when a token is successfully refreshed.
    /// </summary>
    public event EventHandler<TokenRefreshEventArgs>? TokenRefreshed;

    /// <summary>
    /// Event raised when a token refresh fails.
    /// </summary>
    public event EventHandler<TokenRefreshFailedEventArgs>? TokenRefreshFailed;

    /// <summary>
    /// Event raised when a token is about to expire and cannot be auto-refreshed.
    /// </summary>
    public event EventHandler<TokenExpirationWarningEventArgs>? TokenExpirationWarning;

    private OAuthRefreshService()
    {
        _credentialService = new CredentialService();

        _refreshTimer = new System.Timers.Timer(CheckIntervalMs);
        _refreshTimer.Elapsed += OnRefreshTimerElapsed;
        _refreshTimer.AutoReset = true;

        _expirationCheckTimer = new System.Timers.Timer(CheckIntervalMs);
        _expirationCheckTimer.Elapsed += OnExpirationCheckElapsed;
        _expirationCheckTimer.AutoReset = true;

        // Subscribe to credential service events
        _credentialService.CredentialExpiring += OnCredentialExpiring;
    }

    /// <summary>
    /// Starts the automatic OAuth token refresh service.
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _refreshTimer.Start();
        _expirationCheckTimer.Start();

        // Perform initial check
        _ = CheckAndRefreshTokensAsync();
    }

    /// <summary>
    /// Stops the automatic OAuth token refresh service.
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _refreshTimer.Stop();
        _expirationCheckTimer.Stop();
    }

    /// <summary>
    /// Gets the status of all OAuth tokens.
    /// </summary>
    public List<OAuthTokenStatus> GetTokenStatuses()
    {
        var statuses = new List<OAuthTokenStatus>();
        var credentials = _credentialService.GetAllCredentialsWithMetadata();

        foreach (var cred in credentials.Where(c => c.IsOAuthToken))
        {
            var providerId = cred.Resource.Replace($"{CredentialService.OAuthTokenResource}.", "");
            var now = DateTime.UtcNow;
            var isExpired = cred.ExpiresAt.HasValue && cred.ExpiresAt.Value <= now;
            var isExpiringSoon = cred.ExpiresAt.HasValue &&
                                 cred.ExpiresAt.Value > now &&
                                 (cred.ExpiresAt.Value - now).TotalHours < 24;

            var status = new OAuthTokenStatus
            {
                ProviderId = providerId,
                DisplayName = providerId, // Use provider ID as display name
                ExpiresAt = cred.ExpiresAt,
                LastRefreshedAt = null, // Not tracked in CredentialWithMetadata
                CanAutoRefresh = cred.CanAutoRefresh,
                IsExpired = isExpired,
                IsExpiringSoon = isExpiringSoon
            };

            if (cred.ExpiresAt.HasValue)
            {
                status.TimeRemaining = cred.ExpiresAt.Value - now;
            }

            statuses.Add(status);
        }

        return statuses;
    }

    /// <summary>
    /// Manually triggers a refresh for a specific provider.
    /// </summary>
    public async Task<bool> RefreshTokenAsync(string providerId, CancellationToken ct = default)
    {
        try
        {
            var result = await _credentialService.RefreshOAuthTokenAsync(providerId);
            if (result.Success)
            {
                TokenRefreshed?.Invoke(this, new TokenRefreshEventArgs(providerId, DateTime.UtcNow));
            }
            else
            {
                TokenRefreshFailed?.Invoke(this, new TokenRefreshFailedEventArgs(providerId, "Refresh failed"));
            }
            return result.Success;
        }
        catch (Exception ex)
        {
            TokenRefreshFailed?.Invoke(this, new TokenRefreshFailedEventArgs(providerId, ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Enables or disables auto-refresh for a specific provider.
    /// </summary>
    public async Task SetAutoRefreshAsync(string providerId, bool enabled, CancellationToken ct = default)
    {
        var resource = $"{CredentialService.OAuthTokenResource}.{providerId}";
        await _credentialService.UpdateMetadataAsync(resource, m => m.AutoRefreshEnabled = enabled);
    }

    private void OnRefreshTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // Fire-and-forget the async work, with proper exception handling in the async method
        _ = SafeCheckAndRefreshTokensAsync();
    }

    private void OnExpirationCheckElapsed(object? sender, ElapsedEventArgs e)
    {
        // Fire-and-forget the async work, with proper exception handling in the async method
        _ = SafeCheckExpiringTokensAsync();
    }

    private async Task SafeCheckAndRefreshTokensAsync(CancellationToken ct = default)
    {
        try
        {
            await CheckAndRefreshTokensAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OAuthRefreshService] Error in refresh timer: {ex.Message}");
        }
    }

    private async Task SafeCheckExpiringTokensAsync(CancellationToken ct = default)
    {
        try
        {
            await CheckExpiringTokensAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OAuthRefreshService] Error in expiration check timer: {ex.Message}");
        }
    }

    private async Task CheckAndRefreshTokensAsync(CancellationToken ct = default)
    {
        var credentials = _credentialService.GetAllCredentialsWithMetadata();
        var oauthTokens = credentials.Where(c => c.IsOAuthToken);

        foreach (var token in oauthTokens)
        {
            if (!token.CanAutoRefresh || !token.ExpiresAt.HasValue)
                continue;

            var remaining = token.ExpiresAt.Value - DateTime.UtcNow;
            if (remaining.TotalMinutes <= RefreshThresholdMinutes && remaining.TotalSeconds > 0)
            {
                var providerId = token.Resource.Replace($"{CredentialService.OAuthTokenResource}.", "");
                try
                {
                    var result = await _credentialService.RefreshOAuthTokenAsync(providerId);
                    if (result.Success)
                    {
                        TokenRefreshed?.Invoke(this, new TokenRefreshEventArgs(providerId, DateTime.UtcNow));
                    }
                    else
                    {
                        TokenRefreshFailed?.Invoke(this, new TokenRefreshFailedEventArgs(providerId, "Refresh request failed"));
                    }
                }
                catch (Exception ex)
                {
                    TokenRefreshFailed?.Invoke(this, new TokenRefreshFailedEventArgs(providerId, ex.Message));
                }
            }
        }
    }

    private Task CheckExpiringTokensAsync()
    {
        var credentials = _credentialService.GetAllCredentialsWithMetadata();
        var oauthTokens = credentials.Where(c => c.IsOAuthToken);

        foreach (var token in oauthTokens)
        {
            if (!token.ExpiresAt.HasValue)
                continue;

            var remaining = token.ExpiresAt.Value - DateTime.UtcNow;

            // Warn about tokens expiring in less than 1 hour that can't auto-refresh
            if (remaining.TotalHours <= 1 && remaining.TotalSeconds > 0 && !token.CanAutoRefresh)
            {
                var providerId = token.Resource.Replace($"{CredentialService.OAuthTokenResource}.", "");
                TokenExpirationWarning?.Invoke(this, new TokenExpirationWarningEventArgs(
                    providerId,
                    token.ExpiresAt.Value,
                    token.CanAutoRefresh));
            }
        }

        return Task.CompletedTask;
    }

    private void OnCredentialExpiring(object? sender, CredentialExpirationEventArgs e)
    {
        // Handle credential expiration notifications from the credential service
        if (e.Resource.Contains(CredentialService.OAuthTokenResource))
        {
            var providerId = e.Resource.Replace($"{CredentialService.OAuthTokenResource}.", "");
            var metadata = _credentialService.GetMetadata(e.Resource);

            TokenExpirationWarning?.Invoke(this, new TokenExpirationWarningEventArgs(
                providerId,
                e.ExpiresAt,
                metadata?.AutoRefreshEnabled ?? false));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _refreshTimer.Dispose();
        _expirationCheckTimer.Dispose();
        _credentialService.CredentialExpiring -= OnCredentialExpiring;
    }
}

/// <summary>
/// Status information for an OAuth token.
/// </summary>
public sealed class OAuthTokenStatus
{
    public string ProviderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public TimeSpan? TimeRemaining { get; set; }
    public DateTime? LastRefreshedAt { get; set; }
    public bool CanAutoRefresh { get; set; }
    public bool IsExpired { get; set; }
    public bool IsExpiringSoon { get; set; }

    public string StatusDisplay
    {
        get
        {
            if (IsExpired)
                return "Expired";
            if (IsExpiringSoon)
                return CanAutoRefresh ? "Expiring soon (auto-refresh enabled)" : "Expiring soon";
            if (!ExpiresAt.HasValue)
                return "Valid (no expiration)";
            return "Valid";
        }
    }

    public string TimeRemainingDisplay
    {
        get
        {
            if (!TimeRemaining.HasValue)
                return "N/A";
            if (TimeRemaining.Value.TotalSeconds <= 0)
                return "Expired";
            if (TimeRemaining.Value.TotalDays >= 1)
                return $"{(int)TimeRemaining.Value.TotalDays}d {TimeRemaining.Value.Hours}h";
            if (TimeRemaining.Value.TotalHours >= 1)
                return $"{(int)TimeRemaining.Value.TotalHours}h {TimeRemaining.Value.Minutes}m";
            return $"{TimeRemaining.Value.Minutes}m {TimeRemaining.Value.Seconds}s";
        }
    }
}

/// <summary>
/// Event args for successful token refresh.
/// </summary>
public sealed class TokenRefreshEventArgs : EventArgs
{
    public string ProviderId { get; }
    public DateTime RefreshedAt { get; }

    public TokenRefreshEventArgs(string providerId, DateTime refreshedAt)
    {
        ProviderId = providerId;
        RefreshedAt = refreshedAt;
    }
}

/// <summary>
/// Event args for failed token refresh.
/// </summary>
public sealed class TokenRefreshFailedEventArgs : EventArgs
{
    public string ProviderId { get; }
    public string ErrorMessage { get; }

    public TokenRefreshFailedEventArgs(string providerId, string errorMessage)
    {
        ProviderId = providerId;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Event args for token expiration warning.
/// </summary>
public sealed class TokenExpirationWarningEventArgs : EventArgs
{
    public string ProviderId { get; }
    public DateTime ExpiresAt { get; }
    public bool CanAutoRefresh { get; }
    public TimeSpan TimeRemaining => ExpiresAt - DateTime.UtcNow;

    public TokenExpirationWarningEventArgs(string providerId, DateTime expiresAt, bool canAutoRefresh)
    {
        ProviderId = providerId;
        ExpiresAt = expiresAt;
        CanAutoRefresh = canAutoRefresh;
    }
}
