using Meridian.Contracts.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Middleware that enforces session-based authentication.
/// <list type="bullet">
///   <item>Health probes (/healthz, /readyz, /livez) are always exempt.</item>
///   <item>The login page (/login) and auth API endpoints (/api/auth/*) are exempt when authentication is configured.</item>
///   <item>Unauthenticated API requests receive a 401 JSON response.</item>
///   <item>Unauthenticated browser (non-/api) requests are redirected to /login.</item>
///   <item>Authentication is optional in Development/Test and required elsewhere by default.</item>
/// </list>
/// </summary>
public sealed class LoginSessionMiddleware
{
    /// <summary>Name of the HTTP-only session cookie set after successful login.</summary>
    public const string SessionCookieName = "mdc-session";

    /// <summary>
    /// Key for the authenticated username stored in <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
    /// Set by the middleware after successful session validation.
    /// </summary>
    public const string CurrentUserKey = "CurrentUser";

    /// <summary>
    /// Key for the authenticated user's <see cref="Meridian.Contracts.Auth.UserRole"/> stored in
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
    /// </summary>
    public const string CurrentUserRoleKey = "CurrentUserRole";

    /// <summary>
    /// Key for the authenticated user's <see cref="Meridian.Contracts.Auth.UserPermission"/> flags
    /// stored in <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
    /// </summary>
    public const string CurrentUserPermissionsKey = "CurrentUserPermissions";

    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/healthz",
        "/readyz",
        "/livez"
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

        // Fail closed outside optional mode when authentication credentials are missing
        if (!sessionService.IsConfigured)
        {
            if (sessionService.AllowAnonymousWhenUnconfigured)
            {
                await _next(context);
                return;
            }

            await WriteAuthenticationConfigurationErrorAsync(context, path);
            return;
        }

        // Exempt the login page and all auth API endpoints
        if (trimmedPath.Equals("/login", StringComparison.OrdinalIgnoreCase) ||
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
                context.Items[CurrentUserPermissionsKey] = profile.Permissions;
                await _next(context);
                return;
            }
        }

        // Unauthenticated request — differentiate API from browser
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"error":"Unauthorized. Please sign in via the login page."}""");
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
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"error":"Authentication is required but not configured. Set MDC_USERNAME and MDC_PASSWORD or configure MDC_AUTH_MODE=optional for local development."}""");
            return;
        }

        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(
            "Authentication is required but not configured. Set MDC_USERNAME and MDC_PASSWORD or configure MDC_AUTH_MODE=optional for local development.");
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
