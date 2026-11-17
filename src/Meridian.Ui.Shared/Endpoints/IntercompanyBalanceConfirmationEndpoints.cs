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
/// Reciprocal intercompany balance confirmation. Balances come from posted journals only; each side acts through its
/// own book, which the service checks against the caller's tenant and company. Advisory: nothing here gates close.
/// </summary>
public static class IntercompanyBalanceConfirmationEndpoints
{
    private const string Route = "/api/ledger/intercompany/confirmations";

    public static void MapIntercompanyBalanceConfirmationEndpoints(this WebApplication app, JsonSerializerOptions endpointOptions)
    {
        // Balances travel as exact decimal strings, as the obligation monitor's do, so no client rounds them.
        var options = new JsonSerializerOptions(endpointOptions);
        options.Converters.Insert(0, new IntercompanyDecimalJsonConverter());
        app.MapGet(Route, (Guid ledgerBookId, DateOnly asOf, HttpContext context) => ExecuteAsync(context, options,
            (service, tenant, company, _) => service.SummaryAsync(tenant, company, ledgerBookId, asOf, context.RequestAborted)))
            .RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        app.MapGet(Route + "/{confirmationId:guid}", async (Guid confirmationId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IntercompanyBalanceConfirmationService>();
            if (service is null)
                return Unavailable();
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var confirmation = await service.GetAsync(scope.TenantId!, scope.CompanyId, confirmationId, context.RequestAborted).ConfigureAwait(false);
            return confirmation is null ? Results.NotFound() : Results.Json(confirmation, options);
        }).RequireWorkstationTenantCompanyScope()
          .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        // Pins both sides' current posted balances. Nothing is entered by the operator except the profile and date.
        app.MapPost(Route, (ProposeIntercompanyBalanceConfirmationRequest request, HttpContext context) => ExecuteAsync(context, options,
            (service, tenant, company, actor) => service.ProposeAsync(tenant, company, actor!, request, context.RequestAborted), requiresActor: true))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(Route + "/{confirmationId:guid}/actions", (Guid confirmationId, IntercompanyBalanceConfirmationActionRequest request,
            HttpContext context) => ExecuteAsync(context, options,
            (service, tenant, company, actor) => service.ActAsync(tenant, company, actor!, confirmationId, request, context.RequestAborted), requiresActor: true))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static async Task<IResult> ExecuteAsync<T>(HttpContext context, JsonSerializerOptions options,
        Func<IntercompanyBalanceConfirmationService, string, string?, string?, Task<T>> operation, bool requiresActor = false)
    {
        var service = context.RequestServices.GetService<IntercompanyBalanceConfirmationService>();
        if (service is null)
            return Unavailable();
        string? actor = null;
        if (requiresActor && !EndpointAuthorization.TryResolveActor(context, out actor))
            return Results.Unauthorized();
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        try
        {
            return Results.Json(await operation(service, scope.TenantId!, scope.CompanyId, actor).ConfigureAwait(false), options);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or Meridian.Contracts.Tenancy.TenantScopeRejectedException)
        { return EndpointHelpers.Forbidden(); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { return Results.Conflict(new { error = error.Message }); }
    }

    private static IResult Unavailable() => Results.Problem("Intercompany balance confirmation is unavailable.", statusCode: 503);
}
