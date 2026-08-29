using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoint filter that applies the fund-profile tenant ownership gate (SEC-005 slice 3) to fund-scoped
/// read routes. When a request carries a <c>fundProfileId</c> query value that the authoritative
/// <see cref="IFundProfileTenantGuard"/> reports as owned by a different tenant, the route is refused with
/// <c>403</c> before any fund-partitioned data is loaded.
///
/// <para>Read-side counterpart to the governed-write guard on the Security Master workbench field-edit
/// route. Its strictness follows the deployment's <see cref="TenantScopeEnforcementOptions"/>.</para>
///
/// <para>Under <see cref="TenantScopeEnforcementMode.DeploymentBoundary"/> it is <b>fail-open</b>: a
/// blank fund, a caller with no tenant scope, a fund the registry does not positively attribute to
/// another tenant, or an unavailable registry all pass — the single-company-per-deployment boundary
/// remains the control and a legitimate read is never blocked.</para>
///
/// <para>Under <see cref="TenantScopeEnforcementMode.FailClosed"/> each of those four becomes a
/// refusal, because each is a scope that could not be resolved, and the exit criterion is categorical:
/// an unresolvable scope is rejected rather than defaulted. The unavailable-registry case is the one
/// most easily missed and the most important — a gate that cannot reach its authority has not decided
/// that the caller is entitled, it has merely failed to ask.</para>
///
/// <para>The tenantless-caller refusal is evaluated <b>before</b> the absent-fund short circuit, so
/// omitting <c>fundProfileId</c> entirely is not a way past it. The routes attached to this filter
/// have no separate tenant gate behind it, and their stores treat a null tenant with no fund as no
/// filter at all — so under fail-closed the omission has to be refused here or nowhere.</para>
///
/// <para>See <c>docs/security/security-remediation-backlog.md</c> (SEC-005) and W9-GOV-008 criterion 2.</para>
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

        var failClosed = (httpContext.RequestServices.GetService<TenantScopeEnforcementOptions>()
                          ?? TenantScopeEnforcementOptions.DeploymentBoundary).IsFailClosed;

        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(httpContext);
        if (failClosed && !tenant.HasTenantScope)
        {
            // Deliberately checked BEFORE the absent-fund branch below. An unresolved caller is an
            // unresolved scope whether or not they name a fund, and leaving this until after would
            // let omitting the query string skip every check in this filter. The routes attached
            // here have no separate tenant gate to fall back on -- ListAccountingConfigurationAudit
            // is the clearest case -- and their stores read a null tenant with no fund as "no
            // filter at all", so the omission served every tenant's audit history rather than
            // being rejected. The criterion is categorical: an unresolvable scope is refused, not
            // defaulted.
            return Refuse(httpContext, "A tenant-scoped session is required for fund-scoped reads.");
        }

        var fundProfileIds = httpContext.Request.Query[FundProfileQueryKey];
        if (fundProfileIds.Count == 0)
        {
            // A caller who names no fund is asking within their own scope, which the stores narrow
            // by the tenant resolved above. There is no fund ownership left for this filter to
            // resolve. (Under the boundary posture this is reached unscoped, as before.)
            return await next(context).ConfigureAwait(false);
        }

        var guard = httpContext.RequestServices.GetService<IFundProfileTenantGuard>();
        if (guard is null)
        {
            // A gate that cannot reach its authority has not decided the caller is entitled; it has
            // failed to ask. Under the boundary posture that is tolerated because the deployment is
            // the control; under fail-closed it cannot be.
            return failClosed
                ? Refuse(httpContext, "Fund profile ownership cannot be verified.")
                : await next(context).ConfigureAwait(false);
        }

        // Evaluate EVERY supplied fundProfileId value, not the joined StringValues: a polluted
        // query such as ?fundProfileId=foreign&fundProfileId=mine would otherwise join to
        // "foreign,mine" (which the guard fails open on) while the handler's string parameter binds
        // to a single value — a gate bypass. Deny if any supplied scope is positively foreign.
        foreach (var fundProfileId in fundProfileIds)
        {
            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                // A supplied-but-blank scope is an unresolvable scope, not an absent one.
                if (failClosed)
                {
                    return Refuse(httpContext, "A fund profile scope is required for this read.");
                }

                continue;
            }

            var decision = await guard
                .EvaluateAsync(tenant, fundProfileId, httpContext.RequestAborted)
                .ConfigureAwait(false);
            if (!decision.IsAllowed)
            {
                return Refuse(httpContext, "The requested fund profile is not accessible to the current tenant.");
            }

            if (failClosed && !await IsOwnedByCallerAsync(httpContext, tenant, fundProfileId).ConfigureAwait(false))
            {
                // EvaluateAsync allows an unattributed fund by contract — it denies only a fund with
                // history exclusively under other companies. Fail-closed needs positive ownership, so
                // the registry is consulted directly: a fund nobody has claimed is refused rather than
                // served on the strength of nobody having claimed it.
                return Refuse(httpContext, "The requested fund profile is not attributed to the current tenant.");
            }
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the authoritative registry attributes <paramref name="fundProfileId"/> to the caller.
    /// </summary>
    /// <remarks>
    /// An absent registry returns false rather than true: under fail-closed, being unable to confirm
    /// ownership is not the same as confirming it, and defaulting the other way would reopen the gap
    /// on precisely the deployments whose tenancy wiring is incomplete.
    /// </remarks>
    private static async Task<bool> IsOwnedByCallerAsync(
        HttpContext httpContext,
        WorkstationTenantContext tenant,
        string fundProfileId)
    {
        var registry = httpContext.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (registry is null)
        {
            return false;
        }

        try
        {
            var ownership = await registry
                .ResolveAsync(fundProfileId, httpContext.RequestAborted)
                .ConfigureAwait(false);
            return ownership?.IsHeldBy(tenant.TenantId) == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A registry that is registered but unreachable is the unavailable-authority case, not a
            // server fault: letting it escape here returns 500 and contradicts this filter's own
            // contract, which is that an authority it cannot consult produces a refusal. Reported as
            // "not owned" so the caller gets the fail-closed 403 the mode promises.
            httpContext.RequestServices
                .GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(FundProfileScopeEndpointFilters))
                .LogWarning(
                    ex,
                    "Fund profile tenancy registry could not be consulted for {FundProfileId}; "
                    + "treating ownership as unproven.",
                    fundProfileId);
            return false;
        }
    }

    private static IResult Refuse(HttpContext httpContext, string detail)
        => Results.Problem(detail, statusCode: StatusCodes.Status403Forbidden);
}
