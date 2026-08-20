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
/// Declares that a route outside the safe methods changes no state -- a read whose query does not
/// fit in a URL, not a command. Only the read-only-role method cap consults this; it does not
/// substitute for the route's own permission, which still decides who may call it.
/// <para>
/// Applied per route after reading the handler, never by convention: an unmarked non-safe route
/// stays refused for a read-only principal, so a wrong omission costs a capability while a wrong
/// marking would reopen the legacy mutations the cap exists to close.
/// </para>
/// </summary>
public sealed class EndpointNonMutatingMetadata
{
    public EndpointNonMutatingMetadata(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Why this route changes nothing, e.g. "bounded single SELECT-family statement".</summary>
    public string Reason { get; }
}

/// <summary>
/// Centralized authorization helpers for workstation endpoints that rely on
/// LoginSessionMiddleware session data instead of ad hoc caller-supplied fields.
/// </summary>
public static class EndpointAuthorization
{
    /// <summary>
    /// True when a principal carrying <paramref name="role"/> is attempting to change state and the
    /// role is not entitled to. A read-only role means a read-only client whichever posture
    /// established it -- an API key with no configured role, an API key configured ReadOnly, or an
    /// optional-mode anonymous operator -- and permission names alone cannot enforce that, because a
    /// few legacy mutations are declared with view-grade permissions those roles hold. One definition
    /// shared by both non-session principals, so the two cannot drift apart: they already did once,
    /// and the anonymous path silently allowed what the key path refused.
    /// </summary>
    internal static bool IsReadOnlyRoleMutation(HttpContext context, UserRole role)
    {
        if (!IsReadOnlyRole(role))
        {
            return false;
        }

        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return false;
        }

        return !AllowsReadOnlyRoleNonSafeMethod(context.GetEndpoint());
    }

    /// <summary>
    /// True when the selected endpoint may be called by a read-only role despite its method, for
    /// either of two distinct reasons: it changes no state (<see cref="EndpointNonMutatingMetadata"/>),
    /// or it requires the one action grant those roles legitimately hold.
    /// </summary>
    private static bool AllowsReadOnlyRoleNonSafeMethod(Endpoint? endpoint)
        => endpoint is not null &&
           (endpoint.Metadata.GetMetadata<EndpointNonMutatingMetadata>() is not null ||
            DeclaresReadOnlyRoleActionGrant(endpoint));

    /// <summary>
    /// True when the selected endpoint declares an action grant a read-only role legitimately holds,
    /// so the method rule above must stand aside. <see cref="UserPermission.ExportData"/> is that
    /// grant today: <see cref="UserRole.Analysis"/> and <see cref="UserRole.Executive"/> are granted
    /// it outright, and the export routes are POSTs because they take a request body, not because
    /// they mutate governed state. Enumerated rather than inferred from the permission's name -- the
    /// exemption is a deliberate decision per grant, not a naming convention.
    ///
    /// <para>Read from the endpoint's own declaration rather than the request path, so the exemption
    /// tracks what a route requires instead of what its URL looks like. Routing populates the
    /// endpoint before this middleware runs; when it has not (no endpoint selected, or an
    /// unroutable request) there is no declaration to defer to and the method rule applies -- the
    /// exemption fails closed, never the protection.</para>
    /// </summary>
    private static bool DeclaresReadOnlyRoleActionGrant(Endpoint? endpoint)
        => endpoint?.Metadata.GetMetadata<EndpointAuthorizationMetadata>() is { } metadata &&
           metadata.Permissions.Contains(UserPermission.ExportData);

    /// <summary>
    /// The built-in roles whose permission sets carry no Manage, Modify, Execute or Admin permission
    /// at all -- only view grants plus, for two of them, <see cref="UserPermission.ExportData"/>.
    /// Restricting them to safe methods therefore takes away nothing they were meant to do once the
    /// export exemption above is applied; it only closes the legacy routes that mutate while
    /// declaring a view permission. Keyed on the role rather than on its name, so a role that gains a
    /// command permission later stops being covered by this rule rather than being silently
    /// restricted by it.
    /// </summary>
    private static bool IsReadOnlyRole(UserRole role)
        => role is UserRole.ReadOnly or UserRole.Analysis or UserRole.Executive;

    /// <summary>
    /// True when the request is served by a principal that is not an operator session: an API key,
    /// or the optional-mode anonymous actor. Both carry a role's permission snapshot without a person
    /// behind it, so a permission check alone cannot tell them apart from a signed-in operator.
    /// <para>
    /// Session-owned authority must consult this rather than the snapshot. Naming a broad role in
    /// <c>MDC_ANONYMOUS_ROLE</c> or <c>MDC_API_KEY_ROLE</c> is a deployment convenience for reaching
    /// the read surface; it is not a grant of account administration or of another operator's scoped
    /// assignments, and treating it as one lets an unauthenticated caller administer the deployment.
    /// </para>
    /// </summary>
    internal static bool IsNonSessionPrincipal(HttpContext context)
        => context.Items.ContainsKey(ApiKeyMiddleware.ApiKeyPrincipalKey) ||
           context.Items.ContainsKey(LoginSessionMiddleware.AnonymousPrincipalKey);

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
    /// Requires a resolved actor and permission snapshot from a validated workstation session,
    /// without demanding a specific business permission, and declares that requirement as metadata
    /// (an empty permission list with
    /// <see cref="EndpointAuthorizationMetadata.RequireAll"/> false).
    /// <para>
    /// For UI-state operations deliberately limited to signed-in workstation operators — workflow
    /// presets, saved views, first-run acknowledgements, the local desktop launcher. This filter
    /// rejects API-key and optional-auth anonymous principals. It does not itself prove record
    /// ownership; routes that mutate shared or governed state must also declare the appropriate
    /// permission and scope.
    /// </para>
    /// </summary>
    public static TBuilder RequireAuthenticatedSession<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter((context, next) =>
            TryResolveActor(context.HttpContext, out _) &&
            TryGetPermissions(context.HttpContext, out _) &&
            !context.HttpContext.Items.ContainsKey(ApiKeyMiddleware.ApiKeyPrincipalKey) &&
            !context.HttpContext.Items.ContainsKey(LoginSessionMiddleware.AnonymousPrincipalKey)
                ? next(context)
                : ValueTask.FromResult<object?>(ApiProblemDetails.Unauthorized(context.HttpContext)));
        builder.WithMetadata(new EndpointAuthorizationMetadata(Array.Empty<UserPermission>(), requireAll: false));
        return builder;
    }

    /// <summary>
    /// Requires a validated workstation session, except that safe reads from an explicitly scoped
    /// optional-mode local operator are also accepted. This narrow exception keeps local/demo
    /// workspaces bootstrappable without letting an anonymous principal satisfy session-owned
    /// mutation filters. API-key principals are always refused.
    /// </summary>
    public static TBuilder RequireAuthenticatedSessionOrScopedLocalOperatorRead<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter((context, next) =>
        {
            var httpContext = context.HttpContext;
            var isApiKey = httpContext.Items.ContainsKey(ApiKeyMiddleware.ApiKeyPrincipalKey);
            var isAnonymous = httpContext.Items.ContainsKey(LoginSessionMiddleware.AnonymousPrincipalKey);
            var isValidatedSession = !isApiKey && !isAnonymous;
            var carriesLocalScope =
                httpContext.Items.TryGetValue(LoginSessionMiddleware.CurrentTenantIdKey, out var rawTenant) &&
                rawTenant is string tenant &&
                !string.IsNullOrWhiteSpace(tenant) &&
                httpContext.Items.TryGetValue(LoginSessionMiddleware.CurrentUserCompanyIdKey, out var rawCompany) &&
                rawCompany is string company &&
                !string.IsNullOrWhiteSpace(company);
            var isScopedLocalOperatorRead =
                !isApiKey &&
                isAnonymous &&
                (httpContext.Items.ContainsKey(LoginSessionMiddleware.DemoLocalOperatorPrincipalKey) || carriesLocalScope) &&
                (HttpMethods.IsGet(httpContext.Request.Method) || HttpMethods.IsHead(httpContext.Request.Method));

            return TryResolveActor(httpContext, out _) &&
                   TryGetPermissions(httpContext, out _) &&
                   (isValidatedSession || isScopedLocalOperatorRead)
                ? next(context)
                : ValueTask.FromResult<object?>(ApiProblemDetails.Unauthorized(httpContext));
        });
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

    /// <summary>
    /// Records <see cref="EndpointNonMutatingMetadata"/> on a route outside the safe methods, so the
    /// read-only-role cap stands aside for a read that simply needs a request body.
    /// </summary>
    public static TBuilder DeclareNonMutating<TBuilder>(this TBuilder builder, string reason)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new EndpointNonMutatingMetadata(reason));
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

        // API keys and the optional local operator are distinct principal kinds, not users whose
        // literal names happen to be "api-key" or "local-operator". Never pass either synthetic
        // actor through case-insensitive User-assignment lookup. Preserve only the explicit global
        // overrides the scoped service itself recognizes.
        var isApiKeyPrincipal = context.Items.ContainsKey(ApiKeyMiddleware.ApiKeyPrincipalKey);
        if (IsNonSessionPrincipal(context))
        {
            var principalLabel = isApiKeyPrincipal ? "API-key" : "Anonymous";
            var hasGlobalOverride = HasScopedGlobalOverride(globalPermissions);
            return new ScopedAuthorizationDecisionDto(
                IsAllowed: hasGlobalOverride,
                Actor: actor,
                RequiredPermission: required,
                ScopeKind: scopeKind,
                ScopeId: scopeId,
                Reason: hasGlobalOverride
                    ? $"The {principalLabel} principal carries a global scoped-access override."
                    : $"{principalLabel} principals cannot inherit user-scoped access assignments.");
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

    public static async Task<IReadOnlySet<Guid>> AuthorizeScopedManyAsync(
        HttpContext context,
        UserPermission required,
        AccessScopeKindDto scopeKind,
        IReadOnlyCollection<Guid> scopeIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scopeIds);
        var requestedScopeIds = scopeIds.ToHashSet();
        if (requestedScopeIds.Count == 0 ||
            !TryResolveActor(context, out var actor) ||
            !TryGetPermissions(context, out var globalPermissions))
        {
            return new HashSet<Guid>();
        }

        // As with the single-scope helper, synthetic non-session principals never inherit User
        // assignments belonging to a human with the same actor name. Only an explicit global
        // scoped-access override admits the requested ids.
        if (context.Items.ContainsKey(ApiKeyMiddleware.ApiKeyPrincipalKey) ||
            context.Items.ContainsKey(LoginSessionMiddleware.AnonymousPrincipalKey))
        {
            return HasScopedGlobalOverride(globalPermissions)
                ? requestedScopeIds
                : new HashSet<Guid>();
        }

        var service = context.RequestServices.GetService(typeof(IScopedAuthorizationService)) as IScopedAuthorizationService;
        if (service is null)
        {
            return new HashSet<Guid>();
        }

        var decisions = await service
            .AuthorizeManyAsync(actor, required, scopeKind, requestedScopeIds, globalPermissions, ct)
            .ConfigureAwait(false);
        return decisions
            .Where(pair => pair.Value.IsAllowed && requestedScopeIds.Contains(pair.Key))
            .Select(pair => pair.Key)
            .ToHashSet();
    }

    public static async Task<bool> HasScopedPermissionAsync(
        HttpContext context,
        UserPermission required,
        AccessScopeKindDto scopeKind,
        Guid? scopeId,
        CancellationToken ct = default)
        => (await AuthorizeScopedAsync(context, required, scopeKind, scopeId, ct).ConfigureAwait(false)).IsAllowed;

    private static bool HasScopedGlobalOverride(UserPermission permissions)
        => (permissions & UserPermission.AdminMaintenance) == UserPermission.AdminMaintenance ||
           (permissions & UserPermission.ManageUsers) == UserPermission.ManageUsers;
}
