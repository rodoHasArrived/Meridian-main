using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class IntercompanyOperationsEndpoints
{
    public static void MapIntercompanyOperationsEndpoints(this WebApplication app, JsonSerializerOptions options)
    {
        app.MapGet("/api/ledger/intercompany/capabilities", (HttpContext context) =>
        {
            var session = !EndpointAuthorization.IsNonSessionPrincipal(context);
            var structure = session && EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageFundStructure, UserPermission.AdminMaintenance);
            var ledger = session && EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);
            return Results.Json(new
            {
                canManageAgreements = structure,
                canManageLifecycle = structure,
                canManageLedger = ledger,
                canViewLedger = EndpointAuthorization.HasAnyPermission(context,
                    UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            }, options);
        }).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope();

        app.MapGet("/api/fund-structure/entities/{entityId:guid}/timeline", async (Guid entityId,
            Guid organizationId, DateTimeOffset? asOf, HttpContext context) =>
        {
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var service = context.RequestServices.GetService<EntityTimelineService>();
            if (service is null)
                return Results.Problem("Entity timeline authority is unavailable.", statusCode: 503);
            try
            {
                var timeline = await service.GetAsync(scope.TenantId!, organizationId, entityId,
                    asOf ?? DateTimeOffset.UtcNow, context.RequestAborted, scope.Actor ?? throw new UnauthorizedAccessException("An authenticated actor is required.")).ConfigureAwait(false);
                var canAttest = !EndpointAuthorization.IsNonSessionPrincipal(context)
                    && EndpointAuthorization.HasPermission(context, UserPermission.ManageCompliance)
                    && await EndpointAuthorization.HasScopedPermissionAsync(context, UserPermission.ManageCompliance,
                        AccessScopeKindDto.LegalEntity, entityId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(timeline with { CanAttestExternalLifecycleReview = canAttest }, options);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        }).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
          .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports,
              UserPermission.ManageFundStructure, UserPermission.AdminMaintenance);

        app.MapGet("/api/ledger/intercompany/proposals", async (Guid sourceBookId, HttpContext context) =>
        {
            if (!await BookAllowedAsync(context, sourceBookId).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<IIntercompanyProposalStore>();
            if (store is null)
                return Unavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            return Results.Json(await store.ListAsync(tenant.TenantId!, sourceBookId, context.RequestAborted).ConfigureAwait(false), options);
        }).RequireWorkstationTenantCompanyScope().RequireAnyPermission(UserPermission.ViewLedgerReports,
            UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        app.MapGet("/api/ledger/intercompany/balance-schedule", async (Guid ledgerBookId, DateOnly? asOf, HttpContext context) =>
        {
            if (!await BookAllowedAsync(context, ledgerBookId).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var service = context.RequestServices.GetService<IntercompanyBalanceScheduleService>();
            if (service is null)
                return Unavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                return Results.Json(await service.GetAsync(tenant.TenantId, tenant.CompanyId, null, ledgerBookId,
                    asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), context.RequestAborted).ConfigureAwait(false), options);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        }).RequireWorkstationTenantCompanyScope().RequireAnyPermission(UserPermission.ViewLedgerReports,
            UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        app.MapGet("/api/ledger/intercompany/proposals/{proposalId:guid}/refresh-preview", async (Guid proposalId, HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IIntercompanyProposalStore>();
            var service = context.RequestServices.GetService<IntercompanyProposalDeliveryService>();
            if (store is null || service is null)
                return Unavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var proposal = await store.GetAsync(tenant.TenantId!, proposalId, context.RequestAborted).ConfigureAwait(false);
            if (proposal is null || !await BookAllowedAsync(context, proposal.Source.SourceBookId).ConfigureAwait(false)
                || proposal.TargetBookId is not { } target || !await BookAllowedAsync(context, target).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            try
            { return Results.Json(await service.RefreshPreviewAsync(tenant.TenantId!, proposalId, context.RequestAborted).ConfigureAwait(false), options); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            { return Results.Conflict(new { error = error.Message }); }
        }).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
          .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        app.MapPost("/api/ledger/intercompany/proposals/{proposalId:guid}/refresh", async (Guid proposalId,
            RefreshIntercompanyProposalRequest request, HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IIntercompanyProposalStore>();
            var service = context.RequestServices.GetService<IntercompanyProposalDeliveryService>();
            if (store is null || service is null)
                return Unavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var proposal = await store.GetAsync(tenant.TenantId!, proposalId, context.RequestAborted).ConfigureAwait(false);
            if (proposal is null || !await BookAllowedAsync(context, proposal.Source.SourceBookId).ConfigureAwait(false)
                || proposal.TargetBookId is not { } target || !await BookAllowedAsync(context, target).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();
            try
            { return Results.Json(await service.RefreshAsync(tenant.TenantId!, proposalId, request, actor, context.RequestAborted).ConfigureAwait(false), options); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            { return Results.Conflict(new { error = error.Message }); }
        }).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
          .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
          .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Read-only computation: the body carries selections only, so it is a POST without being a mutation.
        app.MapPost("/api/ledger/intercompany/accruals/preview", async (IntercompanyAccrualRequestDto request, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IntercompanyAccrualDraftService>();
            if (service is null)
                return AccrualUnavailable();
            if (!await BookAllowedAsync(context, request.SourceBookId).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                return Results.Json(await service.PreviewAsync(tenant.TenantId!, tenant.CompanyId, actor, request,
                    context.RequestAborted).ConfigureAwait(false), options);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (Npgsql.NpgsqlException) { return AccrualUnavailable(); }
        }).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
          .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
          .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Creates a draft in the source book's review queue. It does not submit, approve or post.
        app.MapPost("/api/ledger/intercompany/accruals/drafts", async (CreateIntercompanyAccrualDraftRequest request, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IntercompanyAccrualDraftService>();
            if (service is null)
                return AccrualUnavailable();
            if (request.Accrual is null || !await BookAllowedAsync(context, request.Accrual.SourceBookId).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                return Results.Json(await service.CreateDraftAsync(tenant.TenantId!, tenant.CompanyId, actor, request,
                    context.RequestAborted).ConfigureAwait(false), options);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            { return Results.Conflict(new { error = error.Message }); }
            catch (Npgsql.NpgsqlException) { return AccrualUnavailable(); }
        }).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
          .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
          .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapGet("/api/ledger/intercompany/breaks", async (Guid sourceBookId, DateOnly? asOf, HttpContext context) =>
        {
            if (!await BookAllowedAsync(context, sourceBookId).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var delivery = context.RequestServices.GetService<IntercompanyProposalDeliveryService>();
            if (delivery is null)
                return Unavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            return Results.Json(await delivery.BreaksAsync(tenant.TenantId!, sourceBookId,
                asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), context.RequestAborted).ConfigureAwait(false), options);
        }).RequireWorkstationTenantCompanyScope().RequireAnyPermission(UserPermission.ViewLedgerReports,
            UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        app.MapPost("/api/ledger/intercompany/proposals/{proposalId:guid}/retry", async (Guid proposalId,
            RetryIntercompanyProposalRequest request, HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IIntercompanyProposalStore>();
            if (store is null)
                return Unavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var proposal = await store.GetAsync(tenant.TenantId!, proposalId, context.RequestAborted).ConfigureAwait(false);
            if (proposal is null || !await BookAllowedAsync(context, proposal.Source.SourceBookId).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();
            try
            {
                await store.RetryAsync(tenant.TenantId!, proposalId, request, actor, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(await store.GetAsync(tenant.TenantId!, proposalId, context.RequestAborted).ConfigureAwait(false), options);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            { return Results.Conflict(new { error = error.Message }); }
        }).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
          .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
          .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost("/api/ledger/intercompany/proposals/{proposalId:guid}/link", async (Guid proposalId,
            LinkIntercompanyCounterpartRequest request, HttpContext context) =>
        {
            var delivery = context.RequestServices.GetService<IntercompanyProposalDeliveryService>();
            var store = context.RequestServices.GetService<IIntercompanyProposalStore>();
            if (delivery is null || store is null)
                return Unavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var proposal = await store.GetAsync(tenant.TenantId!, proposalId, context.RequestAborted).ConfigureAwait(false);
            if (proposal is null || !await BookAllowedAsync(context, proposal.Source.SourceBookId).ConfigureAwait(false)
                || !await BookAllowedAsync(context, request.TargetBookId).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();
            try
            {
                await delivery.LinkAsync(tenant.TenantId!, proposalId, request, actor, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(await store.GetAsync(tenant.TenantId!, proposalId, context.RequestAborted).ConfigureAwait(false), options);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            { return Results.Conflict(new { error = error.Message }); }
        }).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
          .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
          .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static IResult AccrualUnavailable() => Results.Problem("Governed intercompany accrual authority is unavailable.", statusCode: 503);

    private static IResult Unavailable() => Results.Problem("Durable intercompany proposal delivery is unavailable.", statusCode: 503);

    private static async Task<bool> BookAllowedAsync(HttpContext context, Guid bookId)
    {
        var journals = context.RequestServices.GetService<ILedgerJournalStore>();
        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (journals is null || registry is null)
            return false;
        var book = await journals.GetLedgerBookAsync(bookId, context.RequestAborted).ConfigureAwait(false);
        var owner = book is null ? null : await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return owner is not null && owner.IsHeldBy(scope.TenantId)
            && (owner.CompanyId is null || string.Equals(owner.CompanyId, scope.CompanyId, StringComparison.OrdinalIgnoreCase));
    }
}
