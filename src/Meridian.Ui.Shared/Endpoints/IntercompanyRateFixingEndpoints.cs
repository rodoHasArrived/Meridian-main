using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Reviewed index fixings for floating-rate intercompany accrual (rule <c>intercompany-accrual-v2</c>). An operator
/// records what a publisher showed; a second person approves it before any accrual can use it. Tenant-scoped.
/// </summary>
public static class IntercompanyRateFixingEndpoints
{
    private const string Route = "/api/ledger/intercompany/rate-fixings";

    public static void MapIntercompanyRateFixingEndpoints(this WebApplication app, JsonSerializerOptions endpointOptions)
    {
        // Rates travel as exact decimal strings so no client rounds them.
        var options = new JsonSerializerOptions(endpointOptions);
        options.Converters.Insert(0, new IntercompanyDecimalJsonConverter());

        app.MapGet(Route, (string indexReference, DateOnly from, DateOnly to, HttpContext context) => ExecuteAsync(context, options,
            (service, tenant, _) => service.ListAsync(tenant, indexReference, from, to, context.RequestAborted)))
            .RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        app.MapPost(Route, (ProposeIntercompanyRateFixingRequest request, HttpContext context) => ExecuteAsync(context, options,
            (service, tenant, actor) => service.ProposeAsync(tenant, actor!, request, context.RequestAborted), requiresActor: true))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(Route + "/{fixingId:guid}/decisions", (Guid fixingId, DecideIntercompanyRateFixingRequest request, HttpContext context)
            => ExecuteAsync(context, options,
                (service, tenant, actor) => service.DecideAsync(tenant, actor!, fixingId, request, context.RequestAborted), requiresActor: true))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static async Task<IResult> ExecuteAsync<T>(HttpContext context, JsonSerializerOptions options,
        Func<IntercompanyRateFixingService, string, string?, Task<T>> operation, bool requiresActor = false)
    {
        var service = context.RequestServices.GetService<IntercompanyRateFixingService>();
        if (service is null)
            return Results.Problem("Intercompany rate fixings are unavailable.", statusCode: 503);
        string? actor = null;
        if (requiresActor && !EndpointAuthorization.TryResolveActor(context, out actor))
            return Results.Unauthorized();
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        try
        {
            return Results.Json(await operation(service, scope.TenantId!, actor).ConfigureAwait(false), options);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or Meridian.Contracts.Tenancy.TenantScopeRejectedException)
        { return EndpointHelpers.Forbidden(); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { return Results.Conflict(new { error = error.Message }); }
    }
}
