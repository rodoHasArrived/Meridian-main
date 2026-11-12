using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    public static void MapConsolidationFxEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/ledger/consolidation/fx");
        group.MapGet("/rate-sets", (Func<HttpContext, Task<IResult>>)(async (HttpContext context) =>
            await WithFxStoreAsync(context, async store =>
            {
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                return Results.Json(await store.ListRateSetsAsync(tenant.TenantId!, tenant.CompanyId!, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false)))
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope();

        group.MapGet("/rate-sets/{rateSetId:guid}/revisions/{revision:int}", async (Guid rateSetId, int revision, HttpContext context) =>
            await WithFxStoreAsync(context, async store =>
            {
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await store.GetRateSetAsync(tenant.TenantId!, tenant.CompanyId!, rateSetId, revision, context.RequestAborted).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
            }).ConfigureAwait(false))
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope();

        group.MapPost("/rate-sets/approve", async (ConsolidationFxRateSetDto request, HttpContext context) =>
            await WithFxStoreAsync(context, async store =>
            {
                if (!EndpointAuthorization.TryResolveActor(context, out var actor)) return Results.Unauthorized();
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var existing = await store.GetRateSetAsync(tenant.TenantId!, tenant.CompanyId!, request.RateSetId, request.Revision, context.RequestAborted).ConfigureAwait(false);
                var approved = request with
                {
                    TenantId = tenant.TenantId!, CompanyId = tenant.CompanyId!, PreparedBy = actor,
                    ApprovedBy = actor, ApprovedAt = existing?.ApprovedAt ?? DateTimeOffset.UtcNow, ContentHash = null
                };
                await store.RetainRateSetAsync(approved, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(await store.GetRateSetAsync(tenant.TenantId!, tenant.CompanyId!, request.RateSetId, request.Revision, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageLedgerReports)
            .RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet("/books/{bookId:guid}/configuration", async (Guid bookId, DateOnly asOf, HttpContext context) =>
            await WithFxStoreAsync(context, async store =>
            {
                if (!await CanAccessPreparationBookAsync(context, bookId).ConfigureAwait(false)) return EndpointHelpers.Forbidden();
                var result = await store.GetConfigurationAsync(bookId, asOf, context.RequestAborted).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
            }).ConfigureAwait(false))
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope();

        group.MapGet("/books/{bookId:guid}/configuration/{revision:int}", async (Guid bookId, int revision, HttpContext context) =>
            await WithFxStoreAsync(context, async store =>
            {
                if (!await CanAccessPreparationBookAsync(context, bookId).ConfigureAwait(false)) return EndpointHelpers.Forbidden();
                var result = await store.GetConfigurationRevisionAsync(bookId, revision, context.RequestAborted).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
            }).ConfigureAwait(false))
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope();

        group.MapPost("/books/{bookId:guid}/configuration/approve", async (Guid bookId, ConsolidationFxBookConfigurationDto request, HttpContext context) =>
            await WithFxStoreAsync(context, async store =>
            {
                if (!EndpointAuthorization.TryResolveActor(context, out var actor)) return Results.Unauthorized();
                if (request.LedgerBookId != bookId) return Results.BadRequest(new { error = "The route and configuration book must agree." });
                if (!await CanAccessPreparationBookAsync(context, bookId).ConfigureAwait(false)) return EndpointHelpers.Forbidden();
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var existing = await store.GetConfigurationRevisionAsync(bookId, request.Revision, context.RequestAborted).ConfigureAwait(false);
                var approved = request with
                {
                    TenantId = tenant.TenantId!, CompanyId = tenant.CompanyId!, ApprovedBy = actor,
                    ApprovedAt = existing?.ApprovedAt ?? DateTimeOffset.UtcNow, ContentHash = null
                };
                await store.RetainConfigurationAsync(approved, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(await store.GetConfigurationRevisionAsync(bookId, request.Revision, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageLedgerReports)
            .RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static async Task<IResult> WithFxStoreAsync(HttpContext context, Func<IConsolidationFxStore, Task<IResult>> action)
    {
        var store = context.RequestServices.GetService<IConsolidationFxStore>();
        if (store is null) return ServiceUnavailable("Governed consolidation FX storage is unavailable.");
        try { return await action(store).ConfigureAwait(false); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (InvalidDataException) { return ServiceUnavailable("Retained FX authority is corrupt or unavailable."); }
    }
}
