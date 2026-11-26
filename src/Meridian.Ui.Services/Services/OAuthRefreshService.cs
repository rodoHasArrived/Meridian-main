using System.Threading;
using Meridian.Ui.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Ui.Services;

/// <summary>
/// Background service that automatically refreshes OAuth tokens before they expire.
/// Monitors credential metadata and proactively refreshes tokens based on configured thresholds.
/// Construct it with the host's real <see cref="ICredentialService"/>; the shared no-op
/// <see cref="CredentialService"/> never yields a token to refresh.
/// </summary>
public sealed class OAuthRefreshService : IDisposable
{
    private const string RefreshTokenSuffix = ".refresh";
    private static readonly TimeSpan WrapperFailureLogThrottleWindow = TimeSpan.FromMinutes(1);

    private readonly ICredentialService _credentialService;
    private readonly ILogger<OAuthRefreshService> _logger;
    private readonly object _lifecycleSync = new();
    private readonly object _refreshSync = new();
    private readonly Dictionary<string, TaskCompletionSource<bool>> _refreshes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _lastWrapperFailureLogByOperation = new(StringComparer.Ordinal);
    private readonly object _wrapperFailureSync = new();
    private int _wrapperFailureCount;
    private (CancellationTokenSource Cancellation, Task Loop)? _schedule;
    private Task _stopping = Task.CompletedTask;
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
    /// <summary>
    /// Event raised when a wrapper-level timer operation fails.
    /// </summary>
    public event EventHandler<OAuthWrapperFailureEventArgs>? WrapperOperationFailed;

    /// <summary>
    /// Count of wrapper-level failures observed by safe background wrappers.
    /// </summary>
    public int WrapperFailureCount => Volatile.Read(ref _wrapperFailureCount);

    public OAuthRefreshService(ICredentialService credentialService, ILogger<OAuthRefreshService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(credentialService);
        _credentialService = credentialService;
        _logger = logger ?? NullLogger<OAuthRefreshService>.Instance;

        // Subscribe to credential service events
        _credentialService.CredentialExpiring += OnCredentialExpiring;
    }

    /// <summary>
    /// Starts the automatic OAuth token refresh service.
    /// </summary>
    public void Start()
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_schedule is not null)
                return;

            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CheckIntervalMs);
            var interval = TimeSpan.FromMilliseconds(CheckIntervalMs);
            var cancellation = new CancellationTokenSource();
            var previous = _stopping;
            _schedule = (cancellation, Task.Run(() => RunScheduledChecksAsync(previous, interval, cancellation.Token)));
        }
    }

    /// <summary>
    /// Stops the automatic OAuth token refresh service.
    /// </summary>
    public void Stop() => _ = StopAsync();

    /// <summary>Stops scheduled checks and waits for their current work to finish.</summary>
    public Task StopAsync()
    {
        lock (_lifecycleSync)
        {
            if (_schedule is { } schedule)
            {
                _schedule = null;
                _stopping = StopScheduleAsync(schedule.Cancellation, schedule.Loop);
            }
            return _stopping;
        }
    }

    private async Task StopScheduleAsync(CancellationTokenSource cancellation, Task loop)
    {
        using (cancellation)
        {
            try
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (AggregateException ex)
            {
                RecordWrapperFailure(nameof(StopAsync), ex);
            }
            await loop.ConfigureAwait(false);
        }
    }

    private async Task RunScheduledChecksAsync(Task previous, TimeSpan interval, CancellationToken ct)
    {
        try
        {
            await previous.ConfigureAwait(false);
            using var timer = new PeriodicTimer(interval);
            await SafeCheckAndRefreshTokensAsync(ct).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await SafeCheckAndRefreshTokensAsync(ct).ConfigureAwait(false);
                await SafeCheckExpiringTokensAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RecordWrapperFailure(nameof(RunScheduledChecksAsync), ex);
        }
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
            if (!TryGetProviderId(cred.Resource, out var providerId))
                continue;

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
    public Task<bool> RefreshTokenAsync(string providerId, CancellationToken ct = default)
        => RefreshOnceAsync(providerId, scheduledResource: null, ct);

    private Task<bool> RefreshOnceAsync(string providerId, string? scheduledResource, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<bool>(ct);
        if (string.IsNullOrWhiteSpace(providerId))
            return Task.FromResult(false);

        TaskCompletionSource<bool> completion;
        lock (_refreshSync)
        {
            if (_refreshes.TryGetValue(providerId, out var inFlight))
                return inFlight.Task.WaitAsync(ct);

            // A slow earlier provider may have delayed this scan while a manual request
            // completed. Recheck the current expiry before starting another request.
            if (scheduledResource is not null && _credentialService.GetMetadata(scheduledResource) is { } current &&
                (!current.AutoRefreshEnabled || !IsRefreshDue(current.ExpiresAt)))
                return Task.FromResult(false);

            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _refreshes.Add(providerId, completion);
        }

        _ = CompleteRefreshAsync(providerId, completion, ct);
        return completion.Task;
    }

    private async Task CompleteRefreshAsync(string providerId, TaskCompletionSource<bool> completion, CancellationToken ct)
    {
        var success = false;
        OperationCanceledException? cancellation = null;
        try
        {
            success = (await _credentialService.RefreshOAuthTokenAsync(providerId, ct).ConfigureAwait(false)).Success;
        }
        catch (OperationCanceledException ex)
        {
            cancellation = ex;
        }
        catch (Exception)
        {
            // Provider errors can contain credentials or raw response bodies.
        }

        try
        {
            if (cancellation is null)
            {
                if (success)
                    TokenRefreshed?.Invoke(this, new TokenRefreshEventArgs(providerId, DateTime.UtcNow));
                else
                    TokenRefreshFailed?.Invoke(this, new TokenRefreshFailedEventArgs(providerId, "OAuth token refresh failed."));
            }
        }
        catch (Exception ex)
        {
            RecordWrapperFailure(nameof(RefreshTokenAsync), ex);
        }
        finally
        {
            lock (_refreshSync)
            {
                _refreshes.Remove(providerId);
                if (cancellation is not null)
                    completion.TrySetCanceled(cancellation.CancellationToken);
                else
                    completion.TrySetResult(success);
            }
        }
    }

    /// <summary>
    /// Enables or disables auto-refresh for a specific provider.
    /// </summary>
    public async Task SetAutoRefreshAsync(string providerId, bool enabled, CancellationToken ct = default)
    {
        var resource = $"{_credentialService.OAuthTokenResourcePrefix}.{providerId}";
        await _credentialService.UpdateMetadataAsync(resource, m => m.AutoRefreshEnabled = enabled, ct);
    }

    private async Task SafeCheckAndRefreshTokensAsync(CancellationToken ct = default)
    {
        try
        {
            await CheckAndRefreshTokensAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RecordWrapperFailure(nameof(CheckAndRefreshTokensAsync), ex);
        }
    }

    private async Task SafeCheckExpiringTokensAsync(CancellationToken ct = default)
    {
        try
        {
            await CheckExpiringTokensAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RecordWrapperFailure(nameof(CheckExpiringTokensAsync), ex);
        }
    }

    internal Task InvokeSafeCheckAndRefreshTokensForTestsAsync(CancellationToken ct = default)
        => SafeCheckAndRefreshTokensAsync(ct);

    internal Task InvokeSafeCheckExpiringTokensForTestsAsync(CancellationToken ct = default)
        => SafeCheckExpiringTokensAsync(ct);

    private void RecordWrapperFailure(string operationName, Exception ex)
    {
        Interlocked.Increment(ref _wrapperFailureCount);

        var now = DateTime.UtcNow;
        var shouldLog = true;
        lock (_wrapperFailureSync)
        {
            if (_lastWrapperFailureLogByOperation.TryGetValue(operationName, out var lastLoggedAt) &&
                now - lastLoggedAt < WrapperFailureLogThrottleWindow)
            {
                shouldLog = false;
            }
            else
            {
                _lastWrapperFailureLogByOperation[operationName] = now;
            }
        }

        if (shouldLog)
        {
            _logger.LogError(
                ex,
                "OAuth refresh background wrapper failed for operation {OperationName}.",
                operationName);
        }

        var handlers = WrapperOperationFailed;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<OAuthWrapperFailureEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, new OAuthWrapperFailureEventArgs(operationName, ex, now, shouldLog));
            }
            catch (Exception eventEx)
            {
                _logger.LogError(
                    eventEx,
                    "OAuth refresh wrapper failure event handler threw for operation {OperationName}.",
                    operationName);
            }
        }
    }

    private async Task CheckAndRefreshTokensAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var credentials = _credentialService.GetAllCredentialsWithMetadata();
        var oauthTokens = credentials.Where(c => c.IsOAuthToken);

        foreach (var token in oauthTokens)
        {
            ct.ThrowIfCancellationRequested();
            if (token.CanAutoRefresh && IsRefreshDue(token.ExpiresAt) && TryGetProviderId(token.Resource, out var providerId))
                await RefreshOnceAsync(providerId, token.Resource, ct).ConfigureAwait(false);
        }
    }

    private bool IsRefreshDue(DateTime? expiresAt)
        => expiresAt is { } expiry && (expiry - DateTime.UtcNow).TotalMinutes <= RefreshThresholdMinutes;

    private Task CheckExpiringTokensAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var credentials = _credentialService.GetAllCredentialsWithMetadata();
        var oauthTokens = credentials.Where(c => c.IsOAuthToken);

        foreach (var token in oauthTokens)
        {
            ct.ThrowIfCancellationRequested();
            if (!token.ExpiresAt.HasValue)
                continue;

            var remaining = token.ExpiresAt.Value - DateTime.UtcNow;

            // Warn about tokens expiring in less than 1 hour that can't auto-refresh
            if (remaining.TotalHours <= 1 && remaining.TotalSeconds > 0 && !token.CanAutoRefresh
                && TryGetProviderId(token.Resource, out var providerId))
            {
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
        if (TryGetProviderId(e.Resource, out var providerId))
        {
            var metadata = _credentialService.GetMetadata(e.Resource);

            TokenExpirationWarning?.Invoke(this, new TokenExpirationWarningEventArgs(
                providerId,
                e.ExpiresAt,
                metadata?.AutoRefreshEnabled ?? false));
        }
    }

    /// <summary>
    /// Maps a stored OAuth token resource (<c>{prefix}.{providerId}</c>) to its provider id.
    /// Resources outside the prefix and companion refresh-token entries are not tokens to manage.
    /// </summary>
    private bool TryGetProviderId(string resource, out string providerId)
    {
        providerId = string.Empty;
        var prefix = _credentialService.OAuthTokenResourcePrefix + ".";
        if (string.IsNullOrEmpty(resource)
            || !resource.StartsWith(prefix, StringComparison.Ordinal)
            || resource.EndsWith(RefreshTokenSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        providerId = resource[prefix.Length..];
        return providerId.Length > 0;
    }

    public void Dispose()
    {
        lock (_lifecycleSync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        Stop();
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

/// <summary>
/// Event args for wrapper-level failures in background safe wrappers.
/// </summary>
public sealed class OAuthWrapperFailureEventArgs : EventArgs
{
    public string OperationName { get; }
    public Exception Exception { get; }
    public DateTime OccurredAtUtc { get; }
    public bool EmittedStructuredLog { get; }

    public OAuthWrapperFailureEventArgs(string operationName, Exception exception, DateTime occurredAtUtc, bool emittedStructuredLog)
    {
        OperationName = operationName;
        Exception = exception;
        OccurredAtUtc = occurredAtUtc;
        EmittedStructuredLog = emittedStructuredLog;
    }
}
