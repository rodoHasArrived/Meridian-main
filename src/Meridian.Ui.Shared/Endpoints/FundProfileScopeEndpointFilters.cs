using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoint filter that applies the fund-profile tenant ownership gate (SEC-005 slice 3) to fund-scoped
/// read routes. When a request carries a <c>fundProfileId</c> query value that the authoritative
/// <see cref="IFundProfileTenantGuard"/> reports as owned by a different tenant, the route is refused with
/// <c>403</c> before any fund-partitioned data is loaded.
///
/// <para>This is the read-side counterpart to the governed-write guard already applied on the Security
/// Master workbench field-edit route. It is read-only and <b>fail-open</b>: a blank fund, a caller with no
/// tenant scope, a fund the registry does not positively attribute to another tenant, or an unavailable
/// registry all pass through — the single-company-per-deployment boundary remains the control, and a
/// legitimate read is never blocked. It only bites in a future multi-tenant, shared-datastore deployment.
/// See <c>docs/security/security-remediation-backlog.md</c> (SEC-005).</para>
/// </summary>
public static class FundProfileScopeEndpointFilters
{
    private const string FundProfileQueryKey = "fundProfileId";

    /// <summary>
    /// Adds the fund-profile tenant ownership gate to a fund-scoped read endpoint. The gate reads the
    /// <c>fundProfileId</c> query value and denies only a fund positively owned by another tenant.
    ///
    /// <para>Pass the route's read permission(s) so the ownership check runs <b>only</b> for callers who can
    /// already read this route. Endpoint filters execute before the route delegate's own permission check,
    /// so without this an unauthorized caller would receive the ownership 403 for a foreign fund but fall
    /// through to the handler's plain 403 for an own/unbound fund — distinguishing cross-tenant ownership to
    /// callers who should see neither. When no permissions are supplied the check always runs (the route has
    /// no read gate to defer to).</para>
    /// </summary>
    public static RouteHandlerBuilder RequireFundProfileTenantScope(
        this RouteHandlerBuilder builder,
        params UserPermission[] readPermissions)
    {
        builder.AddEndpointFilter((context, next) => EnforceFundProfileScopeAsync(context, next, readPermissions));
        return builder;
    }

    private static async ValueTask<object?> EnforceFundProfileScopeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        UserPermission[] readPermissions)
    {
        var httpContext = context.HttpContext;

        // Defer to the route's own permission 403 for callers who cannot read it: evaluating ownership here
        // would otherwise let an unauthorized caller distinguish a foreign fund (this filter's 403) from an
        // own/unbound fund (the handler's 403), leaking cross-tenant ownership.
        if (readPermissions.Length > 0 && !EndpointAuthorization.HasAnyPermission(httpContext, readPermissions))
        {
            return await next(context).ConfigureAwait(false);
        }

        var fundProfileIds = httpContext.Request.Query[FundProfileQueryKey];
        if (fundProfileIds.Count > 0)
        {
            var guard = httpContext.RequestServices.GetService<IFundProfileTenantGuard>();
            if (guard is not null)
            {
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(httpContext);

                // Evaluate EVERY supplied fundProfileId value, not the joined StringValues: a polluted
                // query such as ?fundProfileId=foreign&fundProfileId=mine would otherwise join to
                // "foreign,mine" (which the guard fails open on) while the handler's string parameter binds
                // to a single value — a gate bypass. Deny if any supplied scope is positively foreign.
                foreach (var fundProfileId in fundProfileIds)
                {
                    if (string.IsNullOrWhiteSpace(fundProfileId))
                    {
                        continue;
                    }

                    var decision = await guard
                        .EvaluateAsync(tenant, fundProfileId, httpContext.RequestAborted)
                        .ConfigureAwait(false);
                    if (!decision.IsAllowed)
                    {
                        return Results.Problem(
                            "The requested fund profile is not accessible to the current tenant.",
                            statusCode: StatusCodes.Status403Forbidden);
                    }
                }
            }
        }

        return await next(context).ConfigureAwait(false);
    }
}
