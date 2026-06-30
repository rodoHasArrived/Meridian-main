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
    /// </summary>
    public static RouteHandlerBuilder RequireFundProfileTenantScope(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter(EnforceFundProfileScopeAsync);
        return builder;
    }

    private static async ValueTask<object?> EnforceFundProfileScopeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var fundProfileId = httpContext.Request.Query[FundProfileQueryKey].ToString();
        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            var guard = httpContext.RequestServices.GetService<IFundProfileTenantGuard>();
            if (guard is not null)
            {
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(httpContext);
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

        return await next(context).ConfigureAwait(false);
    }
}
