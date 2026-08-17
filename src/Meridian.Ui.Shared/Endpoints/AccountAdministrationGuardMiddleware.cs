using Meridian.Identity;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Refuses account-administration mutations before request binding unless the caller can already
/// administer users.
/// <para>
/// The account-administration routes live under <c>/api/auth</c>, which
/// <see cref="LoginSessionMiddleware"/> exempts so that login itself can work, and their
/// <c>RequireManageUsersSession</c> endpoint filters only run after minimal-API argument binding
/// has parsed the request body. This middleware closes that gap from in front of binding: a caller
/// that cannot administer accounts is refused before any of its body bytes are deserialized, so a
/// malformed body from such a caller is answered 401/403 here rather than with a binding 400. The
/// endpoint filter remains the declarative layer, which means an admin route missing from this
/// prefix list still fails closed once binding completes.
/// </para>
/// <para>
/// Acceptance mirrors the handlers' own resolution chain exactly: a session cookie resolving to a
/// live profile, else the <see cref="HttpContext.Items"/> permissions snapshot populated by an
/// upstream authenticator, else 401 — and 403 when the resolved permissions lack
/// <see cref="UserPermission.ManageUsers"/>. Reads under the same prefixes are deliberately not
/// guarded here; their authorization posture is owned by the read-declaration lane.
/// </para>
/// </summary>
public sealed class AccountAdministrationGuardMiddleware
{
    private readonly RequestDelegate _next;

    public AccountAdministrationGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, LoginSessionService sessionService)
    {
        var trimmedPath = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
        if (!IsAccountAdministrationMutation(context.Request.Method, trimmedPath))
        {
            await _next(context);
            return;
        }

        UserPermission permissions;
        var token = context.Request.Cookies[LoginSessionMiddleware.SessionCookieName];
        var profile = string.IsNullOrWhiteSpace(token) ? null : sessionService.GetSessionProfile(token);
        if (profile is not null)
        {
            permissions = profile.Permissions;
        }
        else if (!EndpointAuthorization.TryGetPermissions(context, out permissions))
        {
            await ApiProblemDetails.Unauthorized(
                    context,
                    "Sign in using the login page before accessing this resource.")
                .ExecuteAsync(context);
            return;
        }

        if ((permissions & UserPermission.ManageUsers) != UserPermission.ManageUsers)
        {
            await ApiProblemDetails.Forbidden(context).ExecuteAsync(context);
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// True when the request is a mutation on the account-administration surface under
    /// <c>/api/auth</c>: user accounts, session revocation, role permission profiles, and scoped
    /// access assignments.
    /// </summary>
    private static bool IsAccountAdministrationMutation(string method, string trimmedPath)
    {
        if (!HttpMethods.IsPost(method) &&
            !HttpMethods.IsPut(method) &&
            !HttpMethods.IsPatch(method) &&
            !HttpMethods.IsDelete(method))
        {
            return false;
        }

        return MatchesPathSegment(trimmedPath, "/api/auth/accounts") ||
            MatchesPathSegment(trimmedPath, "/api/auth/sessions") ||
            MatchesPathSegment(trimmedPath, "/api/auth/role-profiles") ||
            MatchesPathSegment(trimmedPath, "/api/auth/access-assignments");
    }

    private static bool MatchesPathSegment(string trimmedPath, string prefix)
        => trimmedPath.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            trimmedPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Extension methods for registering the account-administration guard middleware.
/// </summary>
public static class AccountAdministrationGuardMiddlewareExtensions
{
    /// <summary>
    /// Guards account-administration mutations before request binding. Register after session,
    /// API-key, and CSRF middleware so those layers keep their existing response semantics, and
    /// after any middleware that contributes the request's permissions snapshot.
    /// </summary>
    public static IApplicationBuilder UseAccountAdministrationGuard(this IApplicationBuilder app)
        => app.UseMiddleware<AccountAdministrationGuardMiddleware>();
}
