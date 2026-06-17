using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Request-scoped operator and tenant context resolved from authenticated workstation middleware.
/// </summary>
public sealed record WorkstationTenantContext(
    string? TenantId,
    string? CompanyId,
    string? Actor,
    string? RoleProfileName,
    UserPermission Permissions)
{
    public bool HasTenantScope => !string.IsNullOrWhiteSpace(TenantId);
}

public interface IWorkstationTenantContextAccessor
{
    bool TryGetCurrent(out WorkstationTenantContext context);

    WorkstationTenantContext GetRequired();
}

public sealed class HttpContextWorkstationTenantContextAccessor : IWorkstationTenantContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextWorkstationTenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool TryGetCurrent(out WorkstationTenantContext context)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            context = new WorkstationTenantContext(null, null, null, null, UserPermission.None);
            return false;
        }

        context = Resolve(httpContext);
        return !string.IsNullOrWhiteSpace(context.Actor) || context.HasTenantScope;
    }

    public WorkstationTenantContext GetRequired()
    {
        if (TryGetCurrent(out var context) && context.HasTenantScope)
        {
            return context;
        }

        throw new InvalidOperationException("A tenant-scoped workstation request context is required.");
    }

    private static string? ResolveTenantId(HttpContext context)
        => ResolveStringItem(context, LoginSessionMiddleware.CurrentTenantIdKey);

    public static WorkstationTenantContext Resolve(HttpContext httpContext)
    {
        var actor = EndpointAuthorization.TryResolveActor(httpContext, out var resolvedActor)
            ? resolvedActor
            : null;
        var companyId = EndpointAuthorization.ResolveCompanyId(httpContext);
        var tenantId = ResolveTenantId(httpContext) ?? companyId;
        var roleProfileName = ResolveStringItem(httpContext, LoginSessionMiddleware.CurrentUserRoleProfileNameKey);
        EndpointAuthorization.TryGetPermissions(httpContext, out var permissions);

        return new WorkstationTenantContext(tenantId, companyId, actor, roleProfileName, permissions);
    }

    private static string? ResolveStringItem(HttpContext context, string key)
    {
        if (context.Items.TryGetValue(key, out var value) &&
            value is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        return null;
    }
}

public sealed class WorkstationTenantScopeMetadata;

public static class WorkstationTenantScopeEndpointFilters
{
    private const string MissingTenantScopeMessage = "A tenant-scoped workstation request context is required.";

    public static RouteGroupBuilder RequireWorkstationTenantScope(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(RequireTenantScope);
        group.WithMetadata(new WorkstationTenantScopeMetadata());
        return group;
    }

    private static ValueTask<object?> RequireTenantScope(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context.HttpContext);
        if (tenantContext.HasTenantScope)
        {
            return next(context);
        }

        return ValueTask.FromResult<object?>(Results.Problem(
            MissingTenantScopeMessage,
            statusCode: StatusCodes.Status403Forbidden));
    }
}
