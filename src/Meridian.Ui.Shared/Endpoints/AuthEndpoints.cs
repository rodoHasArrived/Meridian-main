using Meridian.Application.Auth;
using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering login/logout authentication endpoints.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Maps the login page, login API, logout API, and current-user info endpoints.
    /// <list type="bullet">
    ///   <item>GET  /login            – returns the login page HTML.</item>
    ///   <item>POST /api/auth/login   – validates credentials, sets a session cookie.</item>
    ///   <item>POST /api/auth/logout  – invalidates the session cookie.</item>
    ///   <item>GET  /api/auth/me      – returns the current user's username, role, and permissions.</item>
    /// </list>
    /// </summary>
    public static void MapAuthEndpoints(this WebApplication app)
    {
        // GET /login – serve the login page
        app.MapGet(UiApiRoutes.AuthLoginPage, (HttpContext context) =>
        {
            var returnUrl = context.Request.Query["returnUrl"].ToString();
            var hasError = context.Request.Query.ContainsKey("error");
            var html = HtmlTemplateGenerator.Login(returnUrl, hasError);
            return Results.Content(html, "text/html");
        }).ExcludeFromDescription();

        // POST /api/auth/login – authenticate and issue a session cookie
        app.MapPost(UiApiRoutes.AuthApiLogin, async (HttpContext context, LoginSessionService sessionService) =>
        {
            string? username, password, returnUrl;

            if (context.Request.HasJsonContentType())
            {
                var body = await context.Request.ReadFromJsonAsync<LoginRequest>();
                username = body?.Username;
                password = body?.Password;
                returnUrl = body?.ReturnUrl;
            }
            else
            {
                var form = await context.Request.ReadFormAsync();
                username = form["username"].ToString();
                password = form["password"].ToString();
                returnUrl = form["returnUrl"].ToString();
            }

            if (!sessionService.IsConfigured && !sessionService.AllowAnonymousWhenUnconfigured)
            {
                if (context.Request.HasJsonContentType())
                {
                    return Results.Json(
                        new
                        {
                            error = "Authentication is required but not configured. Set MDC_USERNAME and MDC_PASSWORD or configure MDC_AUTH_MODE=optional for local development."
                        },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                return Results.Text(
                    "Authentication is required but not configured. Set MDC_USERNAME and MDC_PASSWORD or configure MDC_AUTH_MODE=optional for local development.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                if (context.Request.HasJsonContentType())
                    return Results.BadRequest(new { error = "Username and password are required." });

                var errorRedirect = BuildLoginRedirect(returnUrl, error: true);
                return Results.Redirect(errorRedirect);
            }

            var token = sessionService.CreateSession(username, password);
            if (token is null)
            {
                if (context.Request.HasJsonContentType())
                    return Results.Json(
                        new { error = "Invalid username or password." },
                        statusCode: StatusCodes.Status401Unauthorized);

                var errorRedirect = BuildLoginRedirect(returnUrl, error: true);
                return Results.Redirect(errorRedirect);
            }

            context.Response.Cookies.Append(
                LoginSessionMiddleware.SessionCookieName,
                token,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow + LoginSessionService.SessionDuration
                });

            if (context.Request.HasJsonContentType())
            {
                var profile = sessionService.GetSessionProfile(token);
                return Results.Ok(new
                {
                    success = true,
                    username = profile?.Username,
                    role = profile?.Role.ToString(),
                    roleProfileName = profile?.RoleProfileName,
                    permissions = profile?.Permissions.ToString(),
                    permissionNames = profile is null
                        ? Array.Empty<string>()
                        : RolePermissions.GetPermissionNames(profile.Permissions).ToArray()
                });
            }

            var redirect = string.IsNullOrWhiteSpace(returnUrl) ? "/workstation/" : returnUrl;
            return Results.Redirect(redirect);
        }).ExcludeFromDescription();

        // POST /api/auth/logout – invalidate session and redirect to login
        app.MapPost(UiApiRoutes.AuthApiLogout, (HttpContext context, LoginSessionService sessionService) =>
        {
            var token = context.Request.Cookies[LoginSessionMiddleware.SessionCookieName];
            if (!string.IsNullOrWhiteSpace(token))
                sessionService.RemoveSession(token);

            context.Response.Cookies.Delete(LoginSessionMiddleware.SessionCookieName);
            return Results.Redirect(UiApiRoutes.AuthLoginPage);
        }).ExcludeFromDescription();

        // GET /api/auth/me – return the current user's identity and role
        app.MapGet(UiApiRoutes.AuthApiMe, (HttpContext context, LoginSessionService sessionService) =>
        {
            var profile = ResolveCurrentProfile(context, sessionService);
            if (profile is null)
                return Results.Json(new { error = "Not authenticated." }, statusCode: StatusCodes.Status401Unauthorized);

            return Results.Ok(new
            {
                username = profile.Username,
                role = profile.Role.ToString(),
                roleProfileName = profile.RoleProfileName,
                permissions = profile.Permissions.ToString(),
                permissionNames = RolePermissions.GetPermissionNames(profile.Permissions).ToArray()
            });
        }).WithName("GetCurrentUser")
          .WithSummary("Returns the currently authenticated user's profile, role, and permissions.")
          .Produces(StatusCodes.Status200OK)
          .Produces(StatusCodes.Status401Unauthorized);

        app.MapGet(UiApiRoutes.AuthApiRoles, async (IRolePermissionProfileStore roleProfileStore, CancellationToken ct) =>
                Results.Ok(await roleProfileStore.GetCatalogAsync(ct).ConfigureAwait(false)))
            .WithName("GetRolePermissionCatalog")
            .WithSummary("Returns built-in and custom roles, permissions, and permission metadata for role configuration surfaces.")
            .Produces<RolePermissionCatalogDto>(StatusCodes.Status200OK);

        app.MapPost(
                UiApiRoutes.AuthApiRoleProfiles,
                async (
                    HttpContext context,
                    LoginSessionService sessionService,
                    IRolePermissionProfileStore roleProfileStore,
                    RolePermissionProfileUpsertRequestDto request,
                    CancellationToken ct) =>
                {
                    var currentProfile = ResolveCurrentProfile(context, sessionService);
                    UserPermission currentPermissions;
                    var actor = currentProfile?.Username;
                    if (currentProfile is not null)
                    {
                        currentPermissions = currentProfile.Permissions;
                    }
                    else if (EndpointAuthorization.TryGetPermissions(context, out var contextPermissions))
                    {
                        currentPermissions = contextPermissions;
                        actor = EndpointAuthorization.TryResolveActor(context, out var resolvedActor)
                            ? resolvedActor
                            : request.RequestedBy;
                    }
                    else
                    {
                        return Results.Unauthorized();
                    }

                    if ((currentPermissions & UserPermission.ManageUsers) != UserPermission.ManageUsers)
                    {
                        return EndpointHelpers.Forbidden();
                    }

                    try
                    {
                        var result = await roleProfileStore
                            .UpsertAsync(request, actor ?? request.RequestedBy, ct)
                            .ConfigureAwait(false);
                        return Results.Ok(result);
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(new { error = ex.Message });
                    }
                })
            .WithName("UpsertRolePermissionProfile")
            .WithSummary("Creates or updates a governed custom role profile with audit evidence.")
            .Produces<RolePermissionProfileUpsertResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        app.MapGet(
                UiApiRoutes.AuthApiAccessAssignments,
                async (
                    HttpContext context,
                    LoginSessionService sessionService,
                    IScopedAccessAssignmentService scopedAccess,
                    string? principalId,
                    AccessScopeKindDto? scopeKind,
                    Guid? scopeId,
                    bool? includeRevoked,
                    CancellationToken ct) =>
                {
                    var currentProfile = ResolveCurrentProfile(context, sessionService);
                    var currentPermissions = currentProfile?.Permissions
                        ?? (EndpointAuthorization.TryGetPermissions(context, out var contextPermissions)
                            ? contextPermissions
                            : UserPermission.None);
                    if (currentPermissions == UserPermission.None)
                    {
                        return Results.Unauthorized();
                    }

                    if ((currentPermissions & UserPermission.ManageUsers) != UserPermission.ManageUsers)
                    {
                        return EndpointHelpers.Forbidden();
                    }

                    var query = new UserAccessAssignmentQueryDto(
                        principalId,
                        scopeKind,
                        scopeId,
                        includeRevoked.GetValueOrDefault());
                    var result = await scopedAccess.QueryAsync(query, ct).ConfigureAwait(false);
                    return Results.Ok(result);
                })
            .WithName("ListScopedAccessAssignments")
            .WithSummary("Lists governed scoped access assignments for identity and access administration.")
            .Produces<IReadOnlyList<UserAccessAssignmentDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        app.MapPost(
                UiApiRoutes.AuthApiAccessAssignments,
                async (
                    HttpContext context,
                    LoginSessionService sessionService,
                    IScopedAccessAssignmentService scopedAccess,
                    UserAccessAssignmentCreateRequestDto request,
                    CancellationToken ct) =>
                {
                    var auth = ResolveManageUsersActor(context, sessionService, request.RequestedBy);
                    if (auth.StatusCode is not null)
                    {
                        return auth.StatusCode.Value switch
                        {
                            StatusCodes.Status401Unauthorized => Results.Unauthorized(),
                            _ => EndpointHelpers.Forbidden()
                        };
                    }

                    try
                    {
                        var result = await scopedAccess.CreateAsync(request, auth.Actor, ct).ConfigureAwait(false);
                        return Results.Created(UiApiRoutes.AuthApiAccessAssignments, result);
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(new { error = ex.Message });
                    }
                })
            .WithName("CreateScopedAccessAssignment")
            .WithSummary("Creates a governed scoped access assignment with audit evidence.")
            .Produces<UserAccessAssignmentMutationResultDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        app.MapPost(
                UiApiRoutes.AuthApiAccessAssignmentRevoke,
                async (
                    Guid assignmentId,
                    HttpContext context,
                    LoginSessionService sessionService,
                    IScopedAccessAssignmentService scopedAccess,
                    UserAccessAssignmentRevokeRequestDto request,
                    CancellationToken ct) =>
                {
                    if (assignmentId != request.AssignmentId)
                    {
                        return Results.BadRequest(new { error = "Route assignment id must match the request body." });
                    }

                    var auth = ResolveManageUsersActor(context, sessionService, request.RequestedBy);
                    if (auth.StatusCode is not null)
                    {
                        return auth.StatusCode.Value switch
                        {
                            StatusCodes.Status401Unauthorized => Results.Unauthorized(),
                            _ => EndpointHelpers.Forbidden()
                        };
                    }

                    try
                    {
                        var result = await scopedAccess.RevokeAsync(request, auth.Actor, ct).ConfigureAwait(false);
                        return Results.Ok(result);
                    }
                    catch (KeyNotFoundException ex)
                    {
                        return Results.NotFound(new { error = ex.Message });
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.Conflict(new { error = ex.Message });
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(new { error = ex.Message });
                    }
                })
            .WithName("RevokeScopedAccessAssignment")
            .WithSummary("Revokes a governed scoped access assignment using optimistic concurrency.")
            .Produces<UserAccessAssignmentMutationResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    private static string BuildLoginRedirect(string? returnUrl, bool error)
    {
        var url = "/login";
        if (error)
            url += "?error=1";
        if (!string.IsNullOrWhiteSpace(returnUrl))
            url += $"{(error ? "&" : "?")}returnUrl={Uri.EscapeDataString(returnUrl)}";
        return url;
    }

    /// <summary>
    /// Resolves the <see cref="UserProfile"/> for the current request by first checking the session
    /// cookie and falling back to values placed in <see cref="HttpContext.Items"/> by the middleware.
    /// Returns <see langword="null"/> when the request is unauthenticated.
    /// </summary>
    private static UserProfile? ResolveCurrentProfile(HttpContext context, LoginSessionService sessionService)
    {
        var token = context.Request.Cookies[LoginSessionMiddleware.SessionCookieName];
        if (!string.IsNullOrWhiteSpace(token))
            return sessionService.GetSessionProfile(token);

        // Fall back to items populated by LoginSessionMiddleware during the current request
        var ctxUser = context.Items[LoginSessionMiddleware.CurrentUserKey] as string;
        var ctxRole = context.Items[LoginSessionMiddleware.CurrentUserRoleKey] as UserRole?;
        var ctxPerms = context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] as UserPermission?;

        if (ctxUser is null || ctxRole is null)
            return null;

        return new UserProfile(
            ctxUser,
            ctxRole.Value,
            PermissionOverride: ctxPerms);
    }

    private static ManageUsersActor ResolveManageUsersActor(
        HttpContext context,
        LoginSessionService sessionService,
        string requestedBy)
    {
        var currentProfile = ResolveCurrentProfile(context, sessionService);
        UserPermission currentPermissions;
        var actor = currentProfile?.Username;
        if (currentProfile is not null)
        {
            currentPermissions = currentProfile.Permissions;
        }
        else if (EndpointAuthorization.TryGetPermissions(context, out var contextPermissions))
        {
            currentPermissions = contextPermissions;
            actor = EndpointAuthorization.TryResolveActor(context, out var resolvedActor)
                ? resolvedActor
                : requestedBy;
        }
        else
        {
            return new ManageUsersActor(string.Empty, StatusCodes.Status401Unauthorized);
        }

        if ((currentPermissions & UserPermission.ManageUsers) != UserPermission.ManageUsers)
        {
            return new ManageUsersActor(actor ?? requestedBy, StatusCodes.Status403Forbidden);
        }

        return new ManageUsersActor(actor ?? requestedBy, null);
    }

    private sealed record ManageUsersActor(string Actor, int? StatusCode);
}

/// <summary>Login request body for JSON clients.</summary>
internal sealed record LoginRequest(string? Username, string? Password, string? ReturnUrl);
