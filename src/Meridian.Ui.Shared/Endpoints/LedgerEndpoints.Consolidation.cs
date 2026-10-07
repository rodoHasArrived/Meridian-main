using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapConsolidationEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet("/api/ledger/consolidation/preview", async (Guid organizationId, Guid ownershipRootId,
            Guid eliminationBookId, Guid periodId, DateOnly asOf, HttpContext context) =>
            await ConsolidationResponseAsync(new ConsolidationRequestDto(organizationId, ownershipRootId,
                eliminationBookId, periodId, asOf), context, jsonOptions, false).ConfigureAwait(false))
            .WithName("PreviewConsolidation")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope()
            .Produces<ConsolidationViewDto>()
            .Produces(StatusCodes.Status400BadRequest);
        app.MapPost("/api/ledger/consolidation/drafts", async (ConsolidationRequestDto request, HttpContext context) =>
            await ConsolidationResponseAsync(request, context, jsonOptions, true).ConfigureAwait(false))
            .WithName("DraftConsolidation")
            .RequireAuthenticatedSession()
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .Produces<ConsolidationViewDto>();
    }

    private static async Task<IResult> ConsolidationResponseAsync(ConsolidationRequestDto request,
        HttpContext context, JsonSerializerOptions options, bool create)
    {
        if (!TryResolveActor(context, out var actor))
            return EndpointHelpers.Forbidden();
        var service = context.RequestServices.GetService<ConsolidationWorkbenchService>();
        var journalStore = context.RequestServices.GetService<ILedgerJournalStore>();
        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (service is null || journalStore is null || registry is null)
            return ServiceUnavailable();
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        try
        {
            var book = await journalStore.GetLedgerBookAsync(request.EliminationBookId, context.RequestAborted).ConfigureAwait(false);
            var owner = book is null ? null : await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
            if (owner is null || !CloseWorkflowOwnerMatches(owner, tenant))
                return EndpointHelpers.Forbidden();
            var result = create
                ? await service.CreateDraftAsync(request, actor, tenant.TenantId, tenant.CompanyId, context.RequestAborted).ConfigureAwait(false)
                : await service.GetAsync(request, tenant.TenantId, tenant.CompanyId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, options);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }
}
