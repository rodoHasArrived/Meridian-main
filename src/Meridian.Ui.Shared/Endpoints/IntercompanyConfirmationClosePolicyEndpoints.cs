using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Opt-in, reviewed confirmation close policies (ADR-032). Every route is scoped to a book the caller's tenant and
/// company own. A policy blocks close only once someone other than its proposer approves it.
/// </summary>
public static class IntercompanyConfirmationClosePolicyEndpoints
{
    private const string Route = "/api/ledger/intercompany/confirmation-close-policies";

    public static void MapIntercompanyConfirmationClosePolicyEndpoints(this WebApplication app, JsonSerializerOptions options)
    {
        app.MapGet(Route, (Guid ledgerBookId, HttpContext context) => ExecuteAsync(context, ledgerBookId, options,
            (service, scope, _) => service.ListAsync(scope.TenantId!, ledgerBookId, context.RequestAborted)))
            .RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        app.MapPost(Route, (ProposeIntercompanyConfirmationClosePolicyRequest request, HttpContext context) => ExecuteAsync(context, request.LedgerBookId, options,
            (service, scope, actor) => service.ProposeAsync(scope.TenantId!, scope.CompanyId, actor!, request, context.RequestAborted), requiresActor: true))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(Route + "/{policyId:guid}/decisions", (Guid policyId, Guid ledgerBookId, DecideIntercompanyConfirmationClosePolicyRequest request,
            HttpContext context) => ExecuteAsync(context, ledgerBookId, options,
                (service, scope, actor) => Scoped(service.DecideAsync(scope.TenantId!, scope.CompanyId, actor!, policyId, request, context.RequestAborted), ledgerBookId),
                requiresActor: true))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    // A decision names the book it was read under; a policy of another book is not decided through it.
    private static async Task<IntercompanyConfirmationClosePolicyDto> Scoped(Task<IntercompanyConfirmationClosePolicyDto> decision, Guid ledgerBookId)
    {
        var policy = await decision.ConfigureAwait(false);
        return policy.LedgerBookId == ledgerBookId ? policy : throw new UnauthorizedAccessException("The close policy belongs to another book.");
    }

    private static async Task<IResult> ExecuteAsync<T>(HttpContext context, Guid ledgerBookId, JsonSerializerOptions options,
        Func<IntercompanyConfirmationClosePolicyService, WorkstationTenantContext, string?, Task<T>> operation, bool requiresActor = false)
    {
        var service = context.RequestServices.GetService<IntercompanyConfirmationClosePolicyService>();
        if (service is null)
            return Results.Problem("Intercompany confirmation close policies are unavailable.", statusCode: 503);
        string? actor = null;
        if (requiresActor && !EndpointAuthorization.TryResolveActor(context, out actor))
            return Results.Unauthorized();
        if (!await IntercompanyOperationsEndpoints.BookAllowedAsync(context, ledgerBookId).ConfigureAwait(false))
            return EndpointHelpers.Forbidden();
        try
        {
            return Results.Json(await operation(service, HttpContextWorkstationTenantContextAccessor.Resolve(context), actor).ConfigureAwait(false), options);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or Meridian.Contracts.Tenancy.TenantScopeRejectedException)
        { return EndpointHelpers.Forbidden(); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { return Results.Conflict(new { error = error.Message }); }
    }
}
