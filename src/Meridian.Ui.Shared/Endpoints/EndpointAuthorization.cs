using Meridian.Application.Auth;
using Meridian.Contracts.Auth;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Centralized authorization helpers for workstation endpoints that rely on
/// LoginSessionMiddleware session data instead of ad hoc caller-supplied fields.
/// </summary>
public static class EndpointAuthorization
{
    public static bool HasPermission(HttpContext context, UserPermission required)
        => TryGetPermissions(context, out var permissions) &&
           (permissions & required) == required;

    public static bool HasAnyPermission(HttpContext context, params UserPermission[] required)
    {
        if (!TryGetPermissions(context, out var permissions))
        {
            return false;
        }

        foreach (var permission in required)
        {
            if ((permissions & permission) == permission)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetPermissions(HttpContext context, out UserPermission permissions)
    {
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserPermissionsKey, out var rawPermissions) &&
            rawPermissions is UserPermission currentPermissions)
        {
            permissions = currentPermissions;
            return true;
        }

        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleKey, out var rawRole) &&
            rawRole is UserRole role)
        {
            permissions = RolePermissions.For(role);
            return permissions != UserPermission.None;
        }

        permissions = UserPermission.None;
        return false;
    }

    public static bool TryResolveActor(HttpContext context, out string actor)
    {
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserKey, out var currentUser) &&
            currentUser is string username &&
            !string.IsNullOrWhiteSpace(username))
        {
            actor = username;
            return true;
        }

        if (context.User.Identity?.IsAuthenticated == true &&
            !string.IsNullOrWhiteSpace(context.User.Identity.Name))
        {
            actor = context.User.Identity.Name!;
            return true;
        }

        actor = string.Empty;
        return false;
    }

    public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> Require(
        UserPermission permission)
        => (context, next) =>
        {
            if (!TryGetPermissions(context.HttpContext, out _))
            {
                return ValueTask.FromResult<object?>(Results.Unauthorized());
            }

            if (HasPermission(context.HttpContext, permission))
            {
                return next(context);
            }

            return ValueTask.FromResult<object?>(EndpointHelpers.Forbidden());
        };

    public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RequireAny(
        params UserPermission[] permissions)
        => (context, next) =>
        {
            if (!TryGetPermissions(context.HttpContext, out _))
            {
                return ValueTask.FromResult<object?>(Results.Unauthorized());
            }

            if (HasAnyPermission(context.HttpContext, permissions))
            {
                return next(context);
            }

            return ValueTask.FromResult<object?>(EndpointHelpers.Forbidden());
        };

    public static async Task<ScopedAuthorizationDecisionDto> AuthorizeScopedAsync(
        HttpContext context,
        UserPermission required,
        AccessScopeKindDto scopeKind,
        Guid? scopeId,
        CancellationToken ct = default)
    {
        if (!TryResolveActor(context, out var actor))
        {
            return new ScopedAuthorizationDecisionDto(
                IsAllowed: false,
                Actor: string.Empty,
                RequiredPermission: required,
                ScopeKind: scopeKind,
                ScopeId: scopeId,
                Reason: "No authenticated actor was resolved.");
        }

        if (!TryGetPermissions(context, out var globalPermissions))
        {
            return new ScopedAuthorizationDecisionDto(
                IsAllowed: false,
                Actor: actor,
                RequiredPermission: required,
                ScopeKind: scopeKind,
                ScopeId: scopeId,
                Reason: "No role permissions were resolved for the current actor.");
        }

        var service = context.RequestServices.GetService(typeof(IScopedAuthorizationService)) as IScopedAuthorizationService;
        if (service is null)
        {
            var isGloballyAllowed = (globalPermissions & required) == required;
            return new ScopedAuthorizationDecisionDto(
                IsAllowed: isGloballyAllowed,
                Actor: actor,
                RequiredPermission: required,
                ScopeKind: scopeKind,
                ScopeId: scopeId,
                Reason: isGloballyAllowed
                    ? "Scoped authorization service unavailable; global permission fallback granted access."
                    : "Scoped authorization service unavailable and global permission fallback denied access.");
        }

        return await service
            .AuthorizeAsync(actor, required, scopeKind, scopeId, globalPermissions, ct)
            .ConfigureAwait(false);
    }

    public static async Task<bool> HasScopedPermissionAsync(
        HttpContext context,
        UserPermission required,
        AccessScopeKindDto scopeKind,
        Guid? scopeId,
        CancellationToken ct = default)
        => (await AuthorizeScopedAsync(context, required, scopeKind, scopeId, ct).ConfigureAwait(false)).IsAllowed;
}
