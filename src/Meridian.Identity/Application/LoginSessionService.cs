using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Identity.Auth;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Hosting;

namespace Meridian.Identity;

/// <summary>
/// Session store for username/password authentication with role-based access control.
/// User accounts are resolved via <see cref="UserProfileRegistry"/> from the governed account
/// store or hashed environment bootstrap records.
/// Authentication defaults to optional in Development/Test and required elsewhere.
/// Use MDC_AUTH_MODE=optional|required|auto to override the default environment-based mode.
/// </summary>
public sealed class LoginSessionService
{
    /// <summary>Session lifetime; exposed so the auth endpoint can set a matching cookie expiry.</summary>
    public static readonly TimeSpan SessionDuration = TimeSpan.FromHours(8);

    private const int MaximumFailedAttempts = 5;
    // Keep unauthenticated login state bounded even when callers supply distinct usernames.
    private const int MaximumTrackedFailedAttemptWindows = 1_024;
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IHostEnvironment environment;
    private readonly UserProfileRegistry profileRegistry;
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly ConcurrentDictionary<string, FailedAttemptWindow> _failedAttempts = new(StringComparer.Ordinal);
    private readonly object _failedAttemptGate = new();
    private readonly object _persistenceGate = new();
    private readonly string? _sessionStorePath;

    public LoginSessionService(
        IHostEnvironment environment,
        UserProfileRegistry profileRegistry,
        LoginSessionStoreOptions? options = null)
    {
        this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        this.profileRegistry = profileRegistry ?? throw new ArgumentNullException(nameof(profileRegistry));
        _sessionStorePath = ResolveStorePath(options);
        LoadSessions();
    }

    /// <summary>
    /// Returns <see langword="true"/> when at least one user account is configured.
    /// </summary>
    public bool IsConfigured => profileRegistry.IsConfigured;

    private AuthenticationMode Mode => AuthenticationModeResolver.Resolve(environment);

    public bool AllowAnonymousWhenUnconfigured => Mode == AuthenticationMode.Optional;

    /// <summary>
    /// Validates the supplied credentials and creates a new session token on success.
    /// Returns <see langword="null"/> when the credentials are invalid or no accounts are
    /// configured.
    /// </summary>
    public string? CreateSession(string username, string password)
    {
        var result = TryCreateSession(username, password, "local");
        return result.Status == LoginAttemptStatus.Succeeded ? result.Token : null;
    }

    /// <summary>
    /// Applies the owned login-attempt policy and creates a session when credentials are valid.
    /// The caller supplies a stable client discriminator, normally the remote IP address.
    /// </summary>
    public LoginAttemptResult TryCreateSession(string username, string password, string clientKey)
    {
        var attemptKey = BuildAttemptKey(username, clientKey);
        var now = DateTimeOffset.UtcNow;
        var throttle = GetPreAuthenticationThrottle(attemptKey, now);
        if (throttle is not null)
        {
            return throttle;
        }

        var profile = profileRegistry.Authenticate(username, password);
        if (profile is null)
        {
            var failed = RegisterFailedAttempt(attemptKey, now);
            if (failed.LockedUntil > now)
            {
                return LoginAttemptResult.Locked(failed.LockedUntil.Value - now);
            }

            return LoginAttemptResult.Invalid(MaximumFailedAttempts - failed.Count);
        }

        lock (_failedAttemptGate)
        {
            _failedAttempts.TryRemove(attemptKey, out _);
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[HashToken(token)] = new SessionEntry(
            profile.Username,
            DateTimeOffset.UtcNow + SessionDuration);
        PruneExpiredSessions();
        PersistSessions();
        return LoginAttemptResult.Succeeded(token);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the token corresponds to a valid, non-expired session.
    /// </summary>
    public bool ValidateSession(string token)
    {
        var tokenHash = HashToken(token);
        if (!_sessions.TryGetValue(tokenHash, out var entry))
            return false;

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(tokenHash, out _);
            PersistSessions();
            return false;
        }

        var profile = profileRegistry.GetProfile(entry.Username);
        if (profile is null)
        {
            _sessions.TryRemove(tokenHash, out _);
            PersistSessions();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the <see cref="UserProfile"/> for the given session token, or
    /// <see langword="null"/> when the token is missing or expired.
    /// </summary>
    public UserProfile? GetSessionProfile(string token)
    {
        var tokenHash = HashToken(token);
        if (!_sessions.TryGetValue(tokenHash, out var entry))
            return null;

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(tokenHash, out _);
            PersistSessions();
            return null;
        }

        var profile = profileRegistry.GetProfile(entry.Username);
        if (profile is null)
        {
            _sessions.TryRemove(tokenHash, out _);
            PersistSessions();
        }

        return profile;
    }

    /// <summary>
    /// Removes the session associated with the given token (logout).
    /// </summary>
    public void RemoveSession(string token)
    {
        if (_sessions.TryRemove(HashToken(token), out _))
        {
            PersistSessions();
        }
    }

    public int RevokeSessionsForUser(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return 0;
        }

        var removed = 0;
        foreach (var (token, entry) in _sessions)
        {
            if (entry.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase) &&
                _sessions.TryRemove(token, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            PersistSessions();
        }

        return removed;
    }

    public int RevokeAllSessions()
    {
        var removed = _sessions.Count;
        _sessions.Clear();
        PersistSessions();
        return removed;
    }

    private void PruneExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (token, entry) in _sessions)
        {
            if (entry.ExpiresAt <= now)
                _sessions.TryRemove(token, out _);
        }
    }

    private static string BuildAttemptKey(string username, string clientKey)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{username.Trim().ToUpperInvariant()}|{clientKey.Trim().ToUpperInvariant()}")));

    private FailedAttemptWindow RegisterFailedAttempt(string attemptKey, DateTimeOffset now)
    {
        lock (_failedAttemptGate)
        {
            PruneExpiredFailedAttempts(now);
            if (_failedAttempts.TryGetValue(attemptKey, out var existing))
            {
                var updated = existing.Register(now);
                _failedAttempts[attemptKey] = updated;
                return updated;
            }

            if (_failedAttempts.Count >= MaximumTrackedFailedAttemptWindows)
            {
                return FailedAttemptWindow.CapacityLocked(GetCapacityRetryAtNoLock(now));
            }

            var first = FailedAttemptWindow.First(now);
            _failedAttempts[attemptKey] = first;
            return first;
        }
    }

    private LoginAttemptResult? GetPreAuthenticationThrottle(string attemptKey, DateTimeOffset now)
    {
        lock (_failedAttemptGate)
        {
            PruneExpiredFailedAttempts(now);
            if (_failedAttempts.TryGetValue(attemptKey, out var current))
            {
                return current.LockedUntil > now
                    ? LoginAttemptResult.Locked(current.LockedUntil.Value - now)
                    : null;
            }

            // Do not perform password verification for an untracked target after the bounded
            // state is saturated. Returning a deterministic retry window is deliberately
            // fail-closed: the previous zero-count fallback allowed unlimited target churn.
            return _failedAttempts.Count >= MaximumTrackedFailedAttemptWindows
                ? LoginAttemptResult.Locked(GetCapacityRetryAtNoLock(now) - now)
                : null;
        }
    }

    private DateTimeOffset GetCapacityRetryAtNoLock(DateTimeOffset now)
    {
        var retryAt = _failedAttempts.Values
            .Select(window => window.ExpiresAt)
            .DefaultIfEmpty(now + AttemptWindow)
            .Min();
        return retryAt > now ? retryAt : now + TimeSpan.FromSeconds(1);
    }

    private void PruneExpiredFailedAttempts(DateTimeOffset now)
    {
        foreach (var (attemptKey, window) in _failedAttempts)
        {
            if (window.ExpiresAt <= now)
            {
                _failedAttempts.TryRemove(attemptKey, out _);
            }
        }
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));

    private static string? ResolveStorePath(LoginSessionStoreOptions? options)
    {
        if (!string.IsNullOrWhiteSpace(options?.Path))
        {
            return Path.GetFullPath(options.Path);
        }

        var configured = Environment.GetEnvironmentVariable(LoginSessionStoreOptions.PathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return null;
    }

    private void LoadSessions()
    {
        if (_sessionStorePath is null || !File.Exists(_sessionStorePath))
        {
            return;
        }

        var persisted = JsonSerializer.Deserialize<PersistedSession[]>(
            File.ReadAllText(_sessionStorePath),
            JsonOptions) ?? [];
        var now = DateTimeOffset.UtcNow;
        foreach (var session in persisted.Where(candidate => candidate.ExpiresAt > now))
        {
            if (!string.IsNullOrWhiteSpace(session.TokenHash) &&
                !string.IsNullOrWhiteSpace(session.Username))
            {
                _sessions[session.TokenHash] = new SessionEntry(session.Username, session.ExpiresAt);
            }
        }
    }

    private void PersistSessions()
    {
        if (_sessionStorePath is null)
        {
            return;
        }

        lock (_persistenceGate)
        {
            var now = DateTimeOffset.UtcNow;
            var persisted = _sessions
                .Where(pair => pair.Value.ExpiresAt > now)
                .OrderBy(pair => pair.Value.Username, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new PersistedSession(pair.Key, pair.Value.Username, pair.Value.ExpiresAt))
                .ToArray();
            AtomicFileWriter.Write(_sessionStorePath, JsonSerializer.Serialize(persisted, JsonOptions));
        }
    }

    private sealed record SessionEntry(
        string Username,
        DateTimeOffset ExpiresAt);

    private sealed record PersistedSession(string TokenHash, string Username, DateTimeOffset ExpiresAt);

    private sealed record FailedAttemptWindow(int Count, DateTimeOffset WindowStartedAt, DateTimeOffset? LockedUntil)
    {
        public static FailedAttemptWindow First(DateTimeOffset now) => new(1, now, null);

        public static FailedAttemptWindow CapacityLocked(DateTimeOffset retryAt)
            => new(MaximumFailedAttempts, retryAt - AttemptWindow, retryAt);

        public DateTimeOffset ExpiresAt => LockedUntil ?? WindowStartedAt + AttemptWindow;

        public FailedAttemptWindow Register(DateTimeOffset now)
        {
            if (now - WindowStartedAt >= AttemptWindow)
            {
                return First(now);
            }

            var nextCount = Count + 1;
            return nextCount >= MaximumFailedAttempts
                ? new FailedAttemptWindow(nextCount, WindowStartedAt, now + LockoutDuration)
                : this with { Count = nextCount };
        }
    }
}

public sealed record LoginSessionStoreOptions(string? Path = null)
{
    public const string PathEnvironmentVariable = "MDC_SESSION_STORE_PATH";
}

public enum LoginAttemptStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut
}

public sealed record LoginAttemptResult(
    LoginAttemptStatus Status,
    string? Token,
    int RemainingAttempts,
    TimeSpan? RetryAfter)
{
    public static LoginAttemptResult Succeeded(string token) => new(LoginAttemptStatus.Succeeded, token, 5, null);
    public static LoginAttemptResult Invalid(int remainingAttempts) => new(LoginAttemptStatus.InvalidCredentials, null, Math.Max(0, remainingAttempts), null);
    public static LoginAttemptResult Locked(TimeSpan retryAfter) => new(LoginAttemptStatus.LockedOut, null, 0, retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter);
}
