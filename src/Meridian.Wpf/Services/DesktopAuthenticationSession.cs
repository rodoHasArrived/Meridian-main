using Meridian.Identity;
using Meridian.Identity.Auth;

namespace Meridian.Wpf.Services;

/// <summary>
/// Supplies the actor a governed desktop write is recorded against. Narrow on purpose: view models
/// that stamp an audit field need only this, and depending on it rather than the whole session keeps
/// them testable without standing up a real login session.
/// </summary>
public interface IDesktopActorSource
{
    /// <summary>
    /// Returns the actor to record as the author of a write, or <c>false</c> when this process has
    /// nobody it can honestly name — in which case the caller must refuse the write rather than
    /// substitute a placeholder.
    /// </summary>
    bool TryGetAuthenticatedActor(out string actor);
}

/// <summary>
/// Supplies the active desktop operator and the permissions granted to that operator. Governed
/// in-process writes use this seam because no HTTP authorization filter runs between a WPF view
/// model and the shared application service.
/// </summary>
public interface IDesktopAuthorizationSource : IDesktopActorSource
{
    /// <summary>
    /// Resolves the actor only when the active, non-expired desktop session grants
    /// <paramref name="permission"/>. Callers use this single boundary check for audit attribution
    /// and authorization rather than composing independent actor and permission probes.
    /// </summary>
    bool TryAuthorize(UserPermission permission, out string actor);
}

/// <summary>
/// Holds the authenticated desktop operator for the current WPF process.
/// Credentials stay hash-backed through <see cref="UserProfileRegistry"/>.
/// </summary>
public sealed class DesktopAuthenticationSession(LoginSessionService loginSessionService) : IDesktopAuthorizationSource
{
    private const string AnonymousRoleEnvironmentVariable = "MDC_ANONYMOUS_ROLE";
    private string? _sessionToken;

    public event EventHandler? SignedOut;

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

    /// <summary>
    /// Resolves the actor to record as the author of a governed write, or returns <c>false</c> when
    /// this process has nobody it can honestly name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="CurrentActor"/>, which resolves a username from the session token
    /// alone. A token that has expired or been revoked still yields a profile there, so
    /// <see cref="CurrentActor"/> can name an operator whose session no longer validates — fine for
    /// pre-filling a text box, not for stamping an audit field. This gates on
    /// <see cref="IsAuthenticated"/>, which validates the session, so a caller cannot attribute a
    /// write to an operator who is no longer signed in.
    /// </para>
    /// <para>
    /// The unconfigured local-development session is a deliberate exception: it has no credentials to
    /// validate, so it reports its own identity rather than borrowing an operator's.
    /// </para>
    /// </remarks>
    public bool TryGetAuthenticatedActor(out string actor)
    {
        if (IsAuthenticated && CurrentUser?.Username is { Length: > 0 } username)
        {
            actor = username;
            return true;
        }

        if (IsAnonymousDevelopmentSession && CanContinueWithoutCredentials)
        {
            actor = "local-development";
            return true;
        }

        actor = string.Empty;
        return false;
    }

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
            var configuredAnonymousRole = Environment.GetEnvironmentVariable(AnonymousRoleEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configuredAnonymousRole))
            {
                // Unconfigured local development with no declared anonymous role keeps the existing
                // fail-open posture. Governed writes separately require the explicit anonymous
                // development session so merely launching the process does not authorize a write.
                return true;
            }

            // The browser uses the shared name-only role parser for MDC_ANONYMOUS_ROLE. Mirror it
            // here so a typo (or a numeric enum value such as "0") cannot grant desktop authority.
            if (!RolePermissions.TryParseRoleName(configuredAnonymousRole, out var anonymousRole))
            {
                return false;
            }

            var anonymousPermissions = RolePermissions.For(anonymousRole);
            return (anonymousPermissions & permission) == permission;
        }

        // CurrentUser is a projection, not proof that the token still validates. Check the live
        // session before using its permission set so expiry or revocation fails closed.
        if (!IsAuthenticated)
        {
            return false;
        }

        var current = CurrentPermissions;
        return current is not null && (current.Value & permission) == permission;
    }

    public bool TryAuthorize(UserPermission permission, out string actor)
    {
        if (HasPermission(permission) && TryGetAuthenticatedActor(out actor))
        {
            return true;
        }

        actor = string.Empty;
        return false;
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
        SignedOut?.Invoke(this, EventArgs.Empty);
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
