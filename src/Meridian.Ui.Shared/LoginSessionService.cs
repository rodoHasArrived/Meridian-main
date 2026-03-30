using System.Collections.Concurrent;
using System.Security.Cryptography;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.Hosting;

namespace Meridian.Ui.Shared;

/// <summary>
/// In-memory session store for username/password authentication with role-based access control.
/// User accounts are resolved via <see cref="UserProfileRegistry"/> which supports both
/// the legacy single-user environment variable pattern (MDC_USERNAME / MDC_PASSWORD) and the
/// multi-user pattern (MDC_USERS JSON array).
/// Authentication defaults to optional in Development/Test and required elsewhere.
/// Use MDC_AUTH_MODE=optional|required|auto to override the default environment-based mode.
/// </summary>
public sealed class LoginSessionService(IHostEnvironment environment, UserProfileRegistry profileRegistry)
{
    /// <summary>Session lifetime; exposed so the auth endpoint can set a matching cookie expiry.</summary>
    internal static readonly TimeSpan SessionDuration = TimeSpan.FromHours(8);

    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();

    /// <summary>
    /// Returns <see langword="true"/> when at least one user account is configured.
    /// </summary>
    public bool IsConfigured => profileRegistry.IsConfigured;

    internal AuthenticationMode Mode => AuthenticationModeResolver.Resolve(environment);

    internal bool AllowAnonymousWhenUnconfigured => Mode == AuthenticationMode.Optional;

    /// <summary>
    /// Validates the supplied credentials and creates a new session token on success.
    /// Returns <see langword="null"/> when the credentials are invalid or no accounts are
    /// configured.
    /// </summary>
    public string? CreateSession(string username, string password)
    {
        var profile = profileRegistry.Authenticate(username, password);
        if (profile is null)
            return null;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = new SessionEntry(profile.Username, profile.Role, DateTimeOffset.UtcNow + SessionDuration);
        PruneExpiredSessions();
        return token;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the token corresponds to a valid, non-expired session.
    /// </summary>
    public bool ValidateSession(string token)
    {
        if (!_sessions.TryGetValue(token, out var entry))
            return false;

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
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
        if (!_sessions.TryGetValue(token, out var entry))
            return null;

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        return new UserProfile(entry.Username, entry.Role);
    }

    /// <summary>
    /// Removes the session associated with the given token (logout).
    /// </summary>
    public void RemoveSession(string token) => _sessions.TryRemove(token, out _);

    private void PruneExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (token, entry) in _sessions)
        {
            if (entry.ExpiresAt <= now)
                _sessions.TryRemove(token, out _);
        }
    }

    private sealed record SessionEntry(string Username, Contracts.Auth.UserRole Role, DateTimeOffset ExpiresAt);
}
