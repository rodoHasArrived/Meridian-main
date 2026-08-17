using Meridian.Identity.Auth;
using Meridian.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Marker metadata recorded on endpoints that attach a permission gate through
/// <see cref="EndpointAuthorization.RequirePermission{TBuilder}"/> or
/// <see cref="EndpointAuthorization.RequireAnyPermission{TBuilder}"/>. Coverage tests inspect this
/// metadata to prove every mapped route declares an explicit authorization requirement.
/// </summary>
public sealed class EndpointAuthorizationMetadata
{
    public EndpointAuthorizationMetadata(IReadOnlyList<UserPermission> permissions, bool requireAll)
    {
        Permissions = permissions;
        RequireAll = requireAll;
    }

    /// <summary>The permission flags evaluated by the attached authorization filter.</summary>
    public IReadOnlyList<UserPermission> Permissions { get; }

    /// <summary>
    /// <see langword="true"/> when every permission in <see cref="Permissions"/> is required;
    /// <see langword="false"/> when any single permission grants access.
    /// </summary>
    public bool RequireAll { get; }
}

/// <summary>
/// Declares that a read route is deliberately open to any authenticated workstation session,
/// with the reason stated where reviewers and the read-declaration ratchet can see it.
/// <para>
/// The read surface is governed risk-based rather than uniformly: reads exposing account-scoped,
/// position, or PII-bearing data must declare a permission via
/// <see cref="EndpointAuthorization.RequirePermission{TBuilder}"/>, while broad workstation reads
/// — reference data, catalogs, health — declare openness explicitly through this marker instead
/// of by omission. A read route carrying neither is undeclared debt tracked by the ratchet.
/// </para>
/// </summary>
public sealed class EndpointOpenReadMetadata
{
    public EndpointOpenReadMetadata(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Why this read is safe for any authenticated session, e.g. "static reference data".</summary>
    public string Reason { get; }
}

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

    public static IReadOnlyList<string> ResolveReportGroupPrincipalIds(HttpContext context)
    {
        var groups = new List<string>();
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleKey, out var rawRole) &&
            rawRole is UserRole role)
        {
            groups.Add(role.ToString());
        }

        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleProfileNameKey, out var rawProfile) &&
            rawProfile is string profileName &&
            !string.IsNullOrWhiteSpace(profileName))
        {
            groups.Add(profileName.Trim());
        }

        return groups
            .Where(static group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? ResolveCompanyId(HttpContext context)
    {
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserCompanyIdKey, out var rawCompanyId) &&
            rawCompanyId is string companyId &&
            !string.IsNullOrWhiteSpace(companyId))
        {
            return companyId.Trim();
        }

        return null;
    }

    public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> Require(
        UserPermission permission)
        => (context, next) =>
        {
            if (!TryGetPermissions(context.HttpContext, out _))
            {
                return ValueTask.FromResult<object?>(
                    ApiProblemDetails.Unauthorized(context.HttpContext));
            }

            if (HasPermission(context.HttpContext, permission))
            {
                return next(context);
            }

            return ValueTask.FromResult<object?>(
                EndpointHelpers.Forbidden(context.HttpContext));
        };

    public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RequireAny(
        params UserPermission[] permissions)
        => (context, next) =>
        {
            if (!TryGetPermissions(context.HttpContext, out _))
            {
                return ValueTask.FromResult<object?>(
                    ApiProblemDetails.Unauthorized(context.HttpContext));
            }

            if (HasAnyPermission(context.HttpContext, permissions))
            {
                return next(context);
            }

            return ValueTask.FromResult<object?>(
                EndpointHelpers.Forbidden(context.HttpContext));
        };

    /// <summary>
    /// Attaches a single-permission authorization filter to the route (or group) and records
    /// <see cref="EndpointAuthorizationMetadata"/> so the requirement is discoverable in endpoint metadata.
    /// This is the canonical way to gate a mutation or sensitive read route.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, UserPermission permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(Require(permission));
        builder.WithMetadata(new EndpointAuthorizationMetadata(new[] { permission }, requireAll: true));
        return builder;
    }

    /// <summary>
    /// Attaches an any-of-permissions authorization filter to the route (or group) and records
    /// <see cref="EndpointAuthorizationMetadata"/> so the requirement is discoverable in endpoint metadata.
    /// Use for read routes that should accept either a view or a manage permission.
    /// </summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params UserPermission[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(RequireAny(permissions));
        builder.WithMetadata(new EndpointAuthorizationMetadata(permissions, requireAll: false));
        return builder;
    }

    /// <summary>
    /// Requires an authenticated workstation session without demanding a specific permission, and
    /// declares that requirement as metadata (an empty permission list with
    /// <see cref="EndpointAuthorizationMetadata.RequireAll"/> false).
    /// <para>
    /// For mutations whose whole scope is the caller's own session state — workflow presets,
    /// saved views, first-run acknowledgements, the local desktop launcher. Demanding a business
    /// permission for a user's own UI state would be false precision: the real requirement is
    /// "who are you", and this declaration says exactly that instead of leaving the route
    /// undeclared. Routes that mutate shared or governed state must declare a real permission.
    /// </para>
    /// </summary>
    public static TBuilder RequireAuthenticatedSession<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter((context, next) =>
            TryGetPermissions(context.HttpContext, out _)
                ? next(context)
                : ValueTask.FromResult<object?>(ApiProblemDetails.Unauthorized(context.HttpContext)));
        builder.WithMetadata(new EndpointAuthorizationMetadata(Array.Empty<UserPermission>(), requireAll: false));
        return builder;
    }

    /// <summary>
    /// Marks a read route as deliberately open to any authenticated session. See
    /// <see cref="EndpointOpenReadMetadata"/> for when this is the right declaration and when a
    /// permission is required instead.
    /// </summary>
    public static TBuilder DeclareOpenRead<TBuilder>(this TBuilder builder, string reason)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new EndpointOpenReadMetadata(reason));
        return builder;
    }

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
            return new ScopedAuthorizationDecisionDto(
                IsAllowed: false,
                Actor: actor,
                RequiredPermission: required,
                ScopeKind: scopeKind,
                ScopeId: scopeId,
                Reason: "Scoped authorization service unavailable; scoped access was denied.");
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
