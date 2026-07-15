using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

/// <summary>
/// SEC-005 slice 4c-ii: supplies the storage layer (which must not depend on ASP.NET Core) with the
/// caller's server-resolved tenant for the current request, so the Postgres ledger store can apply
/// tenant read predicates without threading a caller-tenant argument through every read method.
/// Depends only on the singleton-safe <see cref="IHttpContextAccessor"/> (AsyncLocal-backed), so it is
/// safe to inject into the singleton ledger store without a captive scoped dependency. Returns null
/// outside a request, or when the session has no tenant (fail-open to the deployment boundary).
/// </summary>
public sealed class WorkstationFundScopeTenantAccessor : IFundScopeTenantAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WorkstationFundScopeTenantAccessor(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    public string? ResolveCallerTenant()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        var context = HttpContextWorkstationTenantContextAccessor.Resolve(httpContext);
        return context.HasTenantScope ? context.TenantId : null;
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

    public static RouteHandlerBuilder RequireWorkstationTenantScope(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter(RequireTenantScope);
        builder.WithMetadata(new WorkstationTenantScopeMetadata());
        return builder;
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

/// <summary>
/// SEC-005 slice 4c-iii deployment/startup switch for the fund-scoped write tenant gate — read once at
/// startup from the environment during DI registration, so changing it requires a restart. Off by default: a
/// tenantless authenticated session (the legacy <c>MDC_USERNAME</c> admin) can still create/evaluate
/// fund-scoped accounting artifacts, but the write is logged for detection. A shared multi-tenant
/// deployment sets <c>MERIDIAN_FUND_SCOPED_WRITE_TENANT_REQUIRED=true</c> to fail closed instead.
/// </summary>
public sealed record FundScopedWriteTenantOptions(bool Enforce)
{
    public static readonly FundScopedWriteTenantOptions Disabled = new(Enforce: false);
}

/// <summary>
/// SEC-005 slice 4c-iii: gates fund-scoped <b>write</b>/evaluate routes on a server-resolved tenant.
/// Detection-first — a tenantless caller is refused with <c>403</c> only when
/// <see cref="FundScopedWriteTenantOptions.Enforce"/> is set (a deployment opt-in); otherwise the write
/// proceeds and is logged so operators can see whether any real deployment still relies on the tenantless
/// admin profile before enforcement is enabled. A tenant-scoped caller always proceeds, so single-company
/// deployments (where the session tenant is populated) are unaffected. The read side stays fail-open;
/// this is the write-side counterpart. See <c>docs/security/security-remediation-backlog.md</c> (SEC-005).
/// </summary>
public static class FundScopedWriteTenantEndpointFilters
{
    private const string MissingTenantScopeMessage =
        "A tenant-scoped session is required for fund-scoped writes.";

    public static RouteHandlerBuilder RequireFundScopedWriteTenant(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter(EnforceFundScopedWriteTenantAsync);
        return builder;
    }

    private static async ValueTask<object?> EnforceFundScopedWriteTenantAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(httpContext);
        var options = httpContext.RequestServices.GetService<FundScopedWriteTenantOptions>()
                      ?? FundScopedWriteTenantOptions.Disabled;

        switch (FundScopedWriteTenantGate.Decide(tenant.HasTenantScope, options.Enforce))
        {
            case FundScopedWriteTenantDecision.Allow:
                return await next(context).ConfigureAwait(false);

            case FundScopedWriteTenantDecision.Deny:
                return Results.Problem(MissingTenantScopeMessage, statusCode: StatusCodes.Status403Forbidden);

            default:
                // Detection-first: record the tenantless fund-scoped write without blocking it. Structured
                // fields only (no interpolation) per the repo logging convention.
                httpContext.RequestServices
                    .GetService<ILoggerFactory>()?
                    .CreateLogger("Meridian.Security.FundScopedWriteTenant")
                    .LogWarning(
                        "SEC-005 4c-iii: tenantless session performed a fund-scoped write; enforcement is off. Actor={Actor}, Path={Path}",
                        tenant.Actor ?? "(unknown)",
                        httpContext.Request.Path.Value);
                return await next(context).ConfigureAwait(false);
        }
    }
}
