using Meridian.Identity;
using Meridian.Identity.Auth;

namespace Meridian.Wpf.Services;

/// <summary>
/// Holds the authenticated desktop operator for the current WPF process.
/// Credentials stay hash-backed through <see cref="UserProfileRegistry"/>.
/// </summary>
public sealed class DesktopAuthenticationSession(LoginSessionService loginSessionService)
{
    private string? _sessionToken;

    public bool IsConfigured => loginSessionService.IsConfigured;

    public bool CanContinueWithoutCredentials =>
        !loginSessionService.IsConfigured && loginSessionService.AllowAnonymousWhenUnconfigured;

    public bool IsAuthenticationRequired =>
        loginSessionService.IsConfigured || !loginSessionService.AllowAnonymousWhenUnconfigured;

    public bool IsAuthenticated =>
        !string.IsNullOrWhiteSpace(_sessionToken) &&
        loginSessionService.ValidateSession(_sessionToken);

    public bool IsAnonymousDevelopmentSession { get; private set; }

    public UserProfile? CurrentUser =>
        string.IsNullOrWhiteSpace(_sessionToken)
            ? null
            : loginSessionService.GetSessionProfile(_sessionToken);

    public string CurrentActor =>
        CurrentUser?.Username ??
        (IsAnonymousDevelopmentSession ? "local-development" : string.Empty);

    public UserRole? CurrentRole => CurrentUser?.Role;

    public UserPermission? CurrentPermissions => CurrentUser?.Permissions;

    /// <summary>
    /// Client-side defense-in-depth permission check for the desktop shell. Fails closed: unless
    /// the environment explicitly permits continuing without credentials (unconfigured local
    /// development), a resolved operator profile that grants <paramref name="permission"/> is
    /// required. This prevents an unauthenticated Production session from being treated as fully
    /// privileged. Server-side authorization remains authoritative in all cases.
    /// </summary>
    public bool HasPermission(UserPermission permission)
    {
        if (CanContinueWithoutCredentials)
        {
            // Unconfigured local development: gating defers to the authentication gates so the
            // local shell is not blocked.
            return true;
        }

        var current = CurrentPermissions;
        return current is not null && (current.Value & permission) == permission;
    }

    public DesktopSignInResult SignIn(string username, string password)
    {
        if (!IsConfigured)
        {
            return DesktopSignInResult.Failed(IsAuthenticationRequired
                ? "No Meridian desktop users are configured. Set MDC_USERS with passwordHash values before launching this environment."
                : "No Meridian desktop users are configured for this local session.");
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return DesktopSignInResult.Failed("Enter a Meridian username and password.");
        }

        var token = loginSessionService.CreateSession(username.Trim(), password);
        if (string.IsNullOrWhiteSpace(token))
        {
            return DesktopSignInResult.Failed("The username or password does not match a configured Meridian user.");
        }

        _sessionToken = token;
        IsAnonymousDevelopmentSession = false;
        var profile = loginSessionService.GetSessionProfile(token);
        return profile is null
            ? DesktopSignInResult.Failed("Meridian created a desktop session but could not resolve the user profile.")
            : DesktopSignInResult.SignedIn(profile);
    }

    public DesktopSignInResult ContinueWithoutCredentials()
    {
        if (!CanContinueWithoutCredentials)
        {
            return DesktopSignInResult.Failed("This Meridian environment requires a configured user account.");
        }

        _sessionToken = null;
        IsAnonymousDevelopmentSession = true;
        return DesktopSignInResult.AnonymousDevelopment();
    }

    public void SignOut()
    {
        if (!string.IsNullOrWhiteSpace(_sessionToken))
        {
            loginSessionService.RemoveSession(_sessionToken);
        }

        _sessionToken = null;
        IsAnonymousDevelopmentSession = false;
    }
}

public sealed record DesktopSignInResult(
    bool Succeeded,
    string Message,
    UserProfile? Profile,
    bool IsAnonymousDevelopmentSession)
{
    public static DesktopSignInResult SignedIn(UserProfile profile)
        => new(true, $"Signed in as {profile.Username} ({profile.Role}).", profile, false);

    public static DesktopSignInResult AnonymousDevelopment()
        => new(true, "Continuing without credentials in local development mode.", null, true);

    public static DesktopSignInResult Failed(string message)
        => new(false, message, null, false);
}
