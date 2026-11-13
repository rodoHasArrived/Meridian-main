using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    public static void MapConsolidationTransitionEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/ledger/consolidation/transitions")
            .RequireAuthenticatedSession()
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageLedgerReports);
        group.MapGet("", async (Guid organizationId, Guid ownershipRootId, HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IConsolidationTransitionStore>();
            if (store is null)
                return ServiceUnavailable();
            try
            {
                var rows = await store.ListAsync(organizationId, ownershipRootId, context.RequestAborted).ConfigureAwait(false);
                foreach (var row in rows)
                    if (!await TransitionBooksAccessibleAsync(context, row.Request).ConfigureAwait(false))
                        return EndpointHelpers.Forbidden();
                return Results.Json(rows, jsonOptions);
            }
            catch (TenantScopeRejectedException) { return EndpointHelpers.Forbidden(); }
        }).RequireWorkstationTenantCompanyScope();
        group.MapGet("/previews/{previewId:guid}", async (Guid previewId, HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IConsolidationTransitionStore>();
            if (store is null)
                return ServiceUnavailable();
            try
            {
                var preview = await store.GetPreviewAsync(previewId, context.RequestAborted).ConfigureAwait(false);
                if (preview is null)
                    return Results.NotFound();
                if (!await TransitionBooksAccessibleAsync(context, preview.Request).ConfigureAwait(false))
                    return EndpointHelpers.Forbidden();
                return Results.Json(preview, jsonOptions);
            }
            catch (TenantScopeRejectedException) { return EndpointHelpers.Forbidden(); }
        }).RequireWorkstationTenantCompanyScope();
        group.MapPost("/preview", async (ConsolidationTransitionRequestDto request, HttpContext context) =>
        {
            if (!TryResolveActor(context, out var actor) || !await TransitionBooksAccessibleAsync(context, request).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var service = context.RequestServices.GetService<ConsolidationTransitionService>();
            if (service is null)
                return ServiceUnavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                return Results.Json(await service.PreviewAsync(request, tenant.TenantId!, tenant.CompanyId, actor,
                    context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        }).RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
        group.MapPost("/activate", async (ApproveConsolidationTransitionRequestDto request, HttpContext context) =>
        {
            if (!TryResolveActor(context, out var actor))
                return EndpointHelpers.Forbidden();
            var service = context.RequestServices.GetService<ConsolidationTransitionService>();
            var store = context.RequestServices.GetService<IConsolidationTransitionStore>();
            if (service is null || store is null)
                return ServiceUnavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                var preview = await store.GetPreviewAsync(request.PreviewId, context.RequestAborted).ConfigureAwait(false);
                if (preview is null)
                    return Results.NotFound();
                if (!await TransitionBooksAccessibleAsync(context, preview.Request).ConfigureAwait(false))
                    return EndpointHelpers.Forbidden();
                return Results.Json(await service.ActivateAsync(request, tenant.TenantId!, tenant.CompanyId, actor,
                    context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        }).RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static async Task<bool> TransitionBooksAccessibleAsync(HttpContext context, ConsolidationTransitionRequestDto request)
        => request is not null && await CanAccessAccountBindingBookAsync(context, request.PreviousEliminationBookId).ConfigureAwait(false)
            && await CanAccessAccountBindingBookAsync(context, request.NewEliminationBookId).ConfigureAwait(false);
}
