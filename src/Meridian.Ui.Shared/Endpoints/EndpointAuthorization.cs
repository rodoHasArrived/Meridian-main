using Meridian.Contracts.Auth;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Centralized authorization helpers for workstation endpoints that rely on
/// LoginSessionMiddleware session data instead of ad hoc caller-supplied fields.
/// </summary>
internal static class EndpointAuthorization
{
    internal static bool HasPermission(HttpContext context, UserPermission required)
        => TryGetPermissions(context, out var permissions) &&
           (permissions & required) == required;

    internal static bool HasAnyPermission(HttpContext context, params UserPermission[] required)
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

    internal static bool TryGetPermissions(HttpContext context, out UserPermission permissions)
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

    internal static bool TryResolveActor(HttpContext context, out string actor)
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

    internal static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> Require(
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
}
