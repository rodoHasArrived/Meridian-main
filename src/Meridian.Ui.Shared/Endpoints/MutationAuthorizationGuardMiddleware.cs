using Meridian.Identity;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Enforces every mutating route's declared authorization from in front of minimal-API argument
/// binding, and refuses a mutating route that declares nothing.
/// <para>
/// The endpoint filters attached by <c>RequirePermission</c>/<c>RequireAnyPermission</c> run only
/// after binding has parsed the request body and resolved the handler's services, so a caller
/// holding zero permissions could learn validation outcomes (a binding 400) or trip service
/// resolution before authorization was ever consulted. This middleware reads the same
/// <see cref="EndpointAuthorizationMetadata"/> those filters record and answers 401/403 before any
/// body byte is deserialized, mirroring the filters' own response bodies exactly. The filters stay
/// attached as the declarative, host-independent layer, which means a host composed without this
/// guard still fails closed once binding completes.
/// </para>
/// <para>
/// Omission fails closed: a mutating route carrying no authorization declaration at all is refused
/// outright unless it explicitly states why it needs none —
/// <see cref="EndpointPermissionlessMutationMetadata"/> for deliberately permissionless seams
/// (login, the first-account bootstrap, 410 Gone tombstones) or
/// <see cref="EndpointIndependentAuthenticationMetadata"/> for routes that authenticate their
/// caller by their own mechanism (provider webhooks, the portal grant exchange, the loopback
/// lifecycle token). An empty-permission declaration (the <c>RequireAuthenticatedSession</c>
/// family) passes through to its own filter, which owns the session-kind nuance a permission
/// snapshot cannot express. Reads are deliberately out of scope; their posture is owned by the
/// read-declaration lane.
/// </para>
/// </summary>
public sealed class MutationAuthorizationGuardMiddleware
{
    private readonly RequestDelegate _next;

    public MutationAuthorizationGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        if (!HttpMethods.IsPost(method) &&
            !HttpMethods.IsPut(method) &&
            !HttpMethods.IsPatch(method) &&
            !HttpMethods.IsDelete(method))
        {
            await _next(context);
            return;
        }

        // WebApplication inserts routing ahead of user middleware, so the selected endpoint is
        // already available here. An unroutable request has nothing to guard and keeps its 404
        // semantics.
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            await _next(context);
            return;
        }

        // Guard exactly the surface the authorization ratchets sweep: endpoints that declare the
        // request's mutating method. A method mismatch selects routing's 405 fallback endpoint,
        // which carries no authorization declaration and must keep answering 405, not 403.
        var methodMetadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        if (methodMetadata is null ||
            !methodMetadata.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (endpoint.Metadata.GetMetadata<EndpointPermissionlessMutationMetadata>() is not null ||
            endpoint.Metadata.GetMetadata<EndpointIndependentAuthenticationMetadata>() is not null)
        {
            await _next(context);
            return;
        }

        var declaration = endpoint.Metadata.GetMetadata<EndpointAuthorizationMetadata>();
        if (declaration is null)
        {
            // Undeclared means refused, not merely unguarded. The declaration ratchet keeps the
            // mapped surface at zero undeclared mutating routes, so this branch only ever answers
            // a route added without any authorization decision at all.
            await ApiProblemDetails.Forbidden(
                    context,
                    "This operation declares no authorization requirement and is refused fail-closed.")
                .ExecuteAsync(context);
            return;
        }

        if (declaration.Permissions.Count == 0)
        {
            await _next(context);
            return;
        }

        // The items snapshot populated by the upstream authenticators is the cheap common path.
        // LoginSessionMiddleware exempts the /api/auth prefix before validating a cookie, so the
        // account-administration routes there resolve their own session; mirror that resolution
        // chain the way AccountAdministrationGuardMiddleware does before refusing the caller.
        if (!EndpointAuthorization.TryGetPermissions(context, out var held))
        {
            var token = context.Request.Cookies[LoginSessionMiddleware.SessionCookieName];
            var profile = string.IsNullOrWhiteSpace(token)
                ? null
                : context.RequestServices.GetService<LoginSessionService>()?.GetSessionProfile(token);
            if (profile is null)
            {
                await ApiProblemDetails.Unauthorized(context).ExecuteAsync(context);
                return;
            }

            held = profile.Permissions;
        }

        var allowed = declaration.RequireAll
            ? HoldsAllPermissions(held, declaration.Permissions)
            : HoldsAnyPermission(held, declaration.Permissions);
        if (!allowed)
        {
            await ApiProblemDetails.Forbidden(context).ExecuteAsync(context);
            return;
        }

        await _next(context);
    }

    private static bool HoldsAllPermissions(UserPermission held, IReadOnlyList<UserPermission> permissions)
    {
        foreach (var permission in permissions)
        {
            if ((held & permission) != permission)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HoldsAnyPermission(UserPermission held, IReadOnlyList<UserPermission> permissions)
    {
        foreach (var permission in permissions)
        {
            if ((held & permission) == permission)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Extension methods for registering the pre-binding mutation authorization guard.
/// </summary>
public static class MutationAuthorizationGuardMiddlewareExtensions
{
    /// <summary>
    /// Guards every mutating route's declared authorization before request binding. Register
    /// after session, API-key, and CSRF middleware — and after any middleware that contributes
    /// the request's permissions snapshot — so the declared decision is made on the same
    /// principal the endpoint filters would see.
    /// </summary>
    public static IApplicationBuilder UseMutationAuthorizationGuard(this IApplicationBuilder app)
        => app.UseMiddleware<MutationAuthorizationGuardMiddleware>();
}
