using Meridian.Contracts.Configuration;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Middleware that enforces session-based authentication.
/// <list type="bullet">
///   <item>Health probes (/healthz, /readyz, /livez) are always exempt.</item>
///   <item>The initial-account bootstrap surface (/setup/account, /api/auth/bootstrap) is exempt
///     while no account exists; those endpoints gate themselves on the loopback-only, one-use
///     MDC_BOOTSTRAP_TOKEN.</item>
///   <item>The login page (/login) and auth API endpoints (/api/auth/*) are exempt when authentication is configured.</item>
///   <item>Unauthenticated API requests receive a 401 JSON response.</item>
///   <item>Unauthenticated browser (non-/api) requests are redirected to /login.</item>
///   <item>Authentication is optional in Development/Test and required elsewhere by default.</item>
/// </list>
/// </summary>
public sealed class LoginSessionMiddleware
{
    private const string LocalShutdownTokenEnvironmentVariable = "MDC_SHUTDOWN_TOKEN";
    private const string AnonymousRoleEnvironmentVariable = "MDC_ANONYMOUS_ROLE";
    private const string AnonymousTenantEnvironmentVariable = "MDC_ANONYMOUS_TENANT";

    /// <summary>Actor recorded for optional-mode callers so audit trails are attributed.</summary>
    internal const string AnonymousLocalActor = "local-operator";

    /// <summary>
    /// Marks a request whose principal came from optional mode rather than a validated login session.
    /// </summary>
    internal const string AnonymousPrincipalKey = "CurrentUserIsAnonymous";

    /// <summary>
    /// Marks the anonymous local operator established only by the isolated, explicitly requested
    /// demo runtime. Read-only workstation bootstrap routes may opt into this principal without
    /// treating every optional-auth caller as a validated login session.
    /// </summary>
    internal const string DemoLocalOperatorPrincipalKey = "CurrentUserIsDemoLocalOperator";

    private const string LocalShutdownTokenHeader = "X-Meridian-Shutdown-Token";

    /// <summary>Name of the HTTP-only session cookie set after successful login.</summary>
    public const string SessionCookieName = "mdc-session";

    /// <summary>
    /// Key for the authenticated username stored in <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
    /// Set by the middleware after successful session validation.
    /// </summary>
    public const string CurrentUserKey = "CurrentUser";

    /// <summary>
    /// Key for the authenticated user's <see cref="Meridian.Identity.Auth.UserRole"/> stored in
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
    /// </summary>
    public const string CurrentUserRoleKey = "CurrentUserRole";

    /// <summary>
    /// Key for the authenticated user's role-profile name stored in
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
    /// </summary>
    public const string CurrentUserRoleProfileNameKey = "CurrentUserRoleProfileName";

    /// <summary>
    /// Key for the authenticated user's company id stored in
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
    /// </summary>
    public const string CurrentUserCompanyIdKey = "CurrentUserCompanyId";

    /// <summary>
    /// Key for the tenant scope resolved for the authenticated request.
    /// Until tenant ids diverge from company ids, this carries the authenticated company id.
    /// </summary>
    public const string CurrentTenantIdKey = "CurrentTenantId";

    /// <summary>
    /// Key for the authenticated user's <see cref="Meridian.Identity.Auth.UserPermission"/> flags
    /// stored in <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
    /// </summary>
    public const string CurrentUserPermissionsKey = "CurrentUserPermissions";

    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/healthz",
        "/ready",
        "/readyz",
        "/live",
        "/livez",
        "/startup",
        "/startupz"
    };

    private readonly RequestDelegate _next;

    public LoginSessionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, LoginSessionService sessionService)
    {
        var path = context.Request.Path.Value ?? "";
        var trimmedPath = path.TrimEnd('/');

        // Exempt health probes
        if (ExemptPaths.Contains(trimmedPath))
        {
            await _next(context);
            return;
        }

        if (IsLifecycleTokenRequest(context, trimmedPath))
        {
            await _next(context);
            return;
        }

        // Fail closed outside optional mode when authentication credentials are missing
        if (!sessionService.IsConfigured)
        {
            if (sessionService.AllowAnonymousWhenUnconfigured)
            {
                // Optional mode means this deployment has no accounts at all -- the demo and local
                // development posture. Such a caller has no authorization context, so every governed
                // route refuses it, which is correct by default: "authentication is optional" must not
                // silently become "authorization is absent". A deployment that genuinely wants an
                // anonymous operator to work the surface -- the demo runtime does -- opts in by naming
                // the role that operator carries, and nothing is granted without that explicit choice.
                // A request carrying an API key is judged by the key's own role downstream, so an
                // unusable anonymous posture must not decide it: without this, a typo in an
                // MDC_ANONYMOUS_ROLE nobody is using would disable every independently configured
                // API-key client with a 503 they cannot act on.
                if (!TryResolveAnonymousRole(out var anonymousRole) &&
                    !ApiKeyMiddleware.IsApiKeyCandidate(context))
                {
                    await WriteAnonymousRoleConfigurationErrorAsync(context, path);
                    return;
                }

                if (anonymousRole is { } role)
                {
                    // The same read-only contract the API-key principal carries: naming ReadOnly as
                    // the anonymous role must not let an unauthenticated caller drive the legacy
                    // mutations that are declared with view-grade permissions ReadOnly holds. The two
                    // postures share one rule so this cannot diverge again.
                    //
                    // It binds only to requests that will actually be served as this principal. Two
                    // are not, and both are decided further down the pipeline than this branch runs:
                    // a request carrying an API key is judged by the key's own role in
                    // ApiKeyMiddleware, which runs after this middleware, so rejecting it here would
                    // let a read-only anonymous posture silently disable every key mutation; and the
                    // initial-account bootstrap is gated by its own loopback and one-use token
                    // checks, which are stronger than a role and must stay reachable or a fresh
                    // install with an anonymous role can never create its first account.
                    if (!ApiKeyMiddleware.IsApiKeyCandidate(context) &&
                        !IsInitialAccountBootstrapRequest(trimmedPath) &&
                        EndpointAuthorization.IsReadOnlyRoleMutation(role, context.Request.Method))
                    {
                        await ApiProblemDetails.Forbidden(
                                context,
                                $"The {UserRole.ReadOnly} anonymous role allows only GET, HEAD, and OPTIONS requests. Set {AnonymousRoleEnvironmentVariable} to a role that authorizes this command endpoint.")
                            .ExecuteAsync(context);
                        return;
                    }

                    context.Items[CurrentUserKey] = AnonymousLocalActor;
                    context.Items[CurrentUserRoleKey] = role;
                    context.Items[CurrentUserPermissionsKey] = RolePermissions.For(role);

                    // A session takes its tenant from the account's company; an anonymous caller has
                    // no account to take one from. Optional local development therefore names its own
                    // tenant explicitly, while the supported demo workspace falls back to its seeded
                    // tenant/company pair. With neither, the caller keeps a role but no scope and the
                    // tenant-scoped workstation stays refused.
                    if (ResolveAnonymousTenant() is { } configuredTenant)
                    {
                        context.Items[CurrentUserCompanyIdKey] = configuredTenant;
                        context.Items[CurrentTenantIdKey] = configuredTenant;
                    }
                    else if (DemoWorkspaceLayout.IsDemoModeRequested(Array.Empty<string>()))
                    {
                        context.Items[CurrentUserCompanyIdKey] = DemoTenantBlueprint.CompanyId;
                        context.Items[CurrentTenantIdKey] = DemoTenantBlueprint.TenantId;
                        context.Items[DemoLocalOperatorPrincipalKey] = true;
                    }

                    // Marks this principal as anonymous rather than a validated login session, so a
                    // deployment that also configures MDC_API_KEY still has its key enforced -- the
                    // API-key middleware exempts sessions, and an anonymous caller is not one.
                    context.Items[AnonymousPrincipalKey] = true;
                }

                await _next(context);
                return;
            }

            // The initial-account bootstrap surface must stay reachable while no account
            // exists, or a fresh install can never create its first login. The endpoints
            // fail closed on their own: loopback-only, one-use MDC_BOOTSTRAP_TOKEN, and
            // refusal once any account exists.
            if (IsInitialAccountBootstrapRequest(trimmedPath))
            {
                await _next(context);
                return;
            }

            // API-key clients authenticate downstream via ApiKeyMiddleware, not sessions.
            if (ApiKeyMiddleware.IsApiKeyCandidate(context))
            {
                await _next(context);
                return;
            }

            await WriteAuthenticationConfigurationErrorAsync(context, path);
            return;
        }

        // Exempt the login page and all auth API endpoints
        if (trimmedPath.Equals("/login", StringComparison.OrdinalIgnoreCase) ||
            trimmedPath.Equals("/setup/account", StringComparison.OrdinalIgnoreCase) ||
            trimmedPath.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Validate session cookie and attach the user profile to the request context
        var token = context.Request.Cookies[SessionCookieName];
        if (!string.IsNullOrWhiteSpace(token))
        {
            var profile = sessionService.GetSessionProfile(token);
            if (profile is not null)
            {
                context.Items[CurrentUserKey] = profile.Username;
                context.Items[CurrentUserRoleKey] = profile.Role;
                if (!string.IsNullOrWhiteSpace(profile.RoleProfileName))
                {
                    context.Items[CurrentUserRoleProfileNameKey] = profile.RoleProfileName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(profile.CompanyId))
                {
                    var companyId = profile.CompanyId.Trim();
                    context.Items[CurrentUserCompanyIdKey] = companyId;
                    context.Items[CurrentTenantIdKey] = companyId;
                }

                context.Items[CurrentUserPermissionsKey] = profile.Permissions;
                CookieCsrfProtection.EnsureCsrfCookie(
                    context,
                    CookieCsrfProtection.ShouldUseSecureCookies(context),
                    LoginSessionService.SessionDuration);
                await _next(context);
                return;
            }
        }

        // Defer to the API-key middleware when API-key auth is configured and the caller
        // presented a key: out-of-band API clients authenticate with X-Api-Key, not sessions.
        if (ApiKeyMiddleware.IsApiKeyCandidate(context))
        {
            await _next(context);
            return;
        }

        // Unauthenticated request — differentiate API from browser
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await ApiProblemDetails.Unauthorized(
                    context,
                    "Sign in using the login page before accessing this resource.")
                .ExecuteAsync(context);
        }
        else
        {
            var returnUrl = path + context.Request.QueryString.ToString();
            context.Response.Redirect(
                $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }
    }

    private static async Task WriteAuthenticationConfigurationErrorAsync(HttpContext context, string path)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await ApiProblemDetails.ServiceUnavailable(
                    context,
                    "authentication",
                    "Authentication is required but is not configured. Configure governed users or explicitly enable optional authentication for local development.")
                .ExecuteAsync(context);
            return;
        }

        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(
            "Authentication is required but not configured. Set MDC_USERS with passwordHash values or configure MDC_AUTH_MODE=optional for local development.");
    }

    /// <summary>
    /// Resolves the role an unauthenticated caller carries in optional mode. Unset yields no role at
    /// all, so the governed surface keeps refusing anonymous callers; a value naming no known role
    /// fails closed rather than silently applying a different permission set than was configured.
    /// </summary>
    private static bool TryResolveAnonymousRole(out UserRole? role)
    {
        role = null;
        var configured = Environment.GetEnvironmentVariable(AnonymousRoleEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return true;
        }

        if (ApiKeyMiddleware.TryParseRoleName(configured, out var parsed))
        {
            role = parsed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tenant and company authority an optional-mode caller carries, or null when the deployment has
    /// named none. This is a deployment-chosen identifier matched against stored records rather than
    /// a member of a closed set, so non-empty values are normalized but not enum-validated here.
    /// </summary>
    private static string? ResolveAnonymousTenant()
    {
        var configured = Environment.GetEnvironmentVariable(AnonymousTenantEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
    }

    private static async Task WriteAnonymousRoleConfigurationErrorAsync(HttpContext context, string path)
    {
        var detail =
            $"{AnonymousRoleEnvironmentVariable} is set to a value that is not a known role. "
            + "Set it to a valid role, or unset it to leave anonymous callers without authorization.";

        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await ApiProblemDetails.ServiceUnavailable(context, "authentication", detail).ExecuteAsync(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(detail);
    }

    private static bool IsInitialAccountBootstrapRequest(string trimmedPath)
        => trimmedPath.Equals("/setup/account", StringComparison.OrdinalIgnoreCase)
        || trimmedPath.Equals("/api/auth/bootstrap", StringComparison.OrdinalIgnoreCase);

    private static bool IsLifecycleTokenRequest(HttpContext context, string trimmedPath)
    {
        if (!trimmedPath.Equals("/api/system/lifecycle", StringComparison.OrdinalIgnoreCase) &&
            !trimmedPath.Equals("/api/system/shutdown", StringComparison.OrdinalIgnoreCase) &&
            !trimmedPath.StartsWith("/api/system/shutdown/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is not null && !IPAddress.IsLoopback(remoteIp))
            return false;

        var expected = Environment.GetEnvironmentVariable(LocalShutdownTokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(expected))
            return false;

        var supplied = context.Request.Headers[LocalShutdownTokenHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(supplied))
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return suppliedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}

/// <summary>
/// Extension methods for registering the login session middleware.
/// </summary>
public static class LoginSessionMiddlewareExtensions
{
    /// <summary>
    /// Adds session-based authentication middleware.
    /// Authentication is optional in Development/Test and required elsewhere by default.
    /// </summary>
    public static IApplicationBuilder UseLoginSessionAuthentication(this IApplicationBuilder app)
        => app.UseMiddleware<LoginSessionMiddleware>();
}
