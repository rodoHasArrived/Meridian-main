using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    public static void MapEntityControlEndpoints(this WebApplication app, JsonSerializerOptions options)
    {
        var group = app.MapGroup("/api/ledger/books/{bookId:guid}/controls")
            .RequireAuthenticatedSession()
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageLedgerReports);
        group.MapGet("", async (Guid bookId, HttpContext context) =>
        {
            if (!await HasControlBookAccessAsync(bookId, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<ILedgerBookControlStore>();
            return store is null ? ServiceUnavailable() : Results.Json(await store.GetBookControlsAsync(bookId, context.RequestAborted), options);
        }).RequireWorkstationTenantCompanyScope();
        group.MapPost("/preflight", async (Guid bookId, HttpContext context) =>
        {
            if (!await HasControlBookAccessAsync(bookId, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<ILedgerBookControlStore>();
            if (store is null)
                return ServiceUnavailable();
            try
            { return Results.Json(await store.PrepareBookControlsAsync(bookId, context.RequestAborted), options); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or Meridian.Ledger.LedgerValidationException) { return Results.Conflict(new { error = ex.Message }); }
        }).RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant();
        group.MapPost("/activate", async (Guid bookId, ActivateLedgerBookControlsRequestDto request, HttpContext context) =>
        {
            if (!TryResolveActor(context, out var actor) || !await HasControlBookAccessAsync(bookId, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<ILedgerBookControlStore>();
            if (store is null)
                return ServiceUnavailable();
            try
            {
                return Results.Json(await store.ActivateBookControlsAsync(bookId, request.ExpectedVersion, request.PreflightDigest,
                actor, request.Reason, request.EvidenceReference, context.RequestAborted), options);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or Meridian.Ledger.LedgerValidationException) { return Results.Conflict(new { error = ex.Message }); }
        }).RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost("/api/ledger/controlled-close/authorizations", async (ControlledCloseAuthorizationRequestDto request, HttpContext context) =>
        {
            if (!TryResolveActor(context, out var actor))
                return EndpointHelpers.Forbidden();
            var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var owner = registry is null ? null : await registry.ResolveAsync(request.FundProfileId, context.RequestAborted);
            if (owner is null || !CloseWorkflowOwnerMatches(owner, tenant))
                return EndpointHelpers.Forbidden();
            var service = context.RequestServices.GetService<ManualJournalEntryWorkbenchService>();
            if (service is null)
                return ServiceUnavailable();
            try
            {
                return Results.Json(await service.AuthorizeControlledCloseAsync(request with { TenantId = tenant.TenantId, CompanyId = tenant.CompanyId },
                actor, context.RequestAborted), options);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or Meridian.Ledger.LedgerValidationException) { return Results.Conflict(new { error = ex.Message }); }
        }).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageLedgerReports)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        var entities = app.MapGroup("/api/fund-structure/entities/{entityId:guid}/lifecycle")
            .RequireAuthenticatedSession()
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageFundStructure, UserPermission.ManageCompliance);
        entities.MapGet("", async (Guid entityId, long? revision, HttpContext context) =>
        {
            if (!await HasLifecycleEntityAccessAsync(context, [entityId]))
                return EndpointHelpers.Forbidden();
            var service = context.RequestServices.GetService<IEntityLifecycleService>();
            return service is null ? ServiceUnavailable() : Results.Json(await service.GetAsync(entityId, revision, context.RequestAborted), options);
        }).RequireWorkstationTenantCompanyScope();
        entities.MapGet("/history", async (Guid entityId, HttpContext context) =>
        {
            if (!await HasLifecycleEntityAccessAsync(context, [entityId]))
                return EndpointHelpers.Forbidden();
            var service = context.RequestServices.GetService<IEntityLifecycleService>();
            return service is null ? ServiceUnavailable() : Results.Json(await service.GetHistoryAsync(entityId, context.RequestAborted), options);
        }).RequireWorkstationTenantCompanyScope();
        entities.MapPost("/review-preview", async (Guid entityId, AppendEntityLifecycleRevisionRequestDto request, HttpContext context) =>
        {
            if (request.EntityId != entityId || request.ExternalReview is not null)
                return Results.BadRequest(new { error = "Prepare the exact selected entity timeline before adding external review." });
            if (request.Timeline is null || request.Timeline.Count == 0 || request.ExpectedRevision < 0
                || string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.EvidenceReference))
                return Results.BadRequest(new { error = "A complete timeline, reason and evidence reference are required for review preparation." });
            if (!await HasLifecycleEntityAccessAsync(context, request.Timeline.Where(s => s.SuccessorEntityId.HasValue)
                .Select(s => s.SuccessorEntityId!.Value).Append(entityId)))
                return EndpointHelpers.Forbidden();
            var caller = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var access = context.RequestServices.GetService<IIntercompanyAgreementAccessAuthority>();
            if (access is null || !await access.CanAccessEntitiesAsync(new(caller.TenantId!, caller.Actor!), [entityId], context.RequestAborted))
                return EndpointHelpers.Forbidden();
            var lifecycle = context.RequestServices.GetService<IEntityLifecycleService>();
            if (lifecycle is null)
                return ServiceUnavailable();
            var current = await lifecycle.GetAsync(entityId, ct: context.RequestAborted);
            if ((current?.Revision ?? 0) != request.ExpectedRevision)
                return Results.Conflict(new { error = "Lifecycle history changed; reload before preparing external review." });
            try
            {
                return Results.Json(new { submittedHash = EntityLifecycleExternalReviewIntegrity.SubmittedHash(request) }, options);
            }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        }).RequireWorkstationTenantCompanyScope();
        entities.MapPost("/revisions", async (Guid entityId, AppendEntityLifecycleRevisionRequestDto request,
            HttpContext context) =>
        {
            if (request.EntityId != entityId)
                return Results.BadRequest(new { error = "Entity route and revision identity must agree." });
            if (!TryResolveActor(context, out var actor))
                return EndpointHelpers.Forbidden();
            if (request.ExternalReview is not null && !EndpointAuthorization.HasPermission(context, UserPermission.ManageCompliance))
                return EndpointHelpers.Forbidden();
            if (request.ExternalReview is null && !EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageFundStructure, UserPermission.AdminMaintenance))
                return EndpointHelpers.Forbidden();
            if (request.Timeline is null || !await HasLifecycleEntityAccessAsync(context,
                request.Timeline.Where(s => s.SuccessorEntityId.HasValue).Select(s => s.SuccessorEntityId!.Value).Append(entityId),
                request.ExternalReview is not null ? UserPermission.ManageCompliance : null))
                return EndpointHelpers.Forbidden();
            var service = context.RequestServices.GetService<IEntityLifecycleService>();
            if (service is null)
                return ServiceUnavailable();
            try
            { return Results.Json(await service.TransitionAsync(request, actor, context.RequestAborted), options); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or Meridian.Ledger.LedgerValidationException) { return Results.Conflict(new { error = ex.Message }); }
        }).RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static async Task<bool> HasControlBookAccessAsync(Guid bookId, HttpContext context)
    {
        var ledger = context.RequestServices.GetService<ILedgerJournalStore>();
        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (ledger is null || registry is null)
            return false;
        var book = await ledger.GetLedgerBookAsync(bookId, context.RequestAborted).ConfigureAwait(false);
        var owner = book is null ? null : await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
        return owner is not null && CloseWorkflowOwnerMatches(owner, HttpContextWorkstationTenantContextAccessor.Resolve(context));
    }

    private static async Task<bool> HasLifecycleEntityAccessAsync(HttpContext http, IEnumerable<Guid> entityIds,
        UserPermission? reviewPermission = null)
    {
        var ids = entityIds.Distinct().ToArray();
        var caller = HttpContextWorkstationTenantContextAccessor.Resolve(http);
        var access = http.RequestServices.GetService<IIntercompanyAgreementAccessAuthority>();
        if (access is null && http.RequestServices.GetService<Meridian.Storage.FundStructure.IFundStructureStore>() is { } nodes)
            access = new HttpContextIntercompanyAgreementAccessAuthority(new HttpContextAccessor { HttpContext = http }, nodes);
        if (access is null || string.IsNullOrWhiteSpace(caller.TenantId) || string.IsNullOrWhiteSpace(caller.Actor)
            || !await access.CanAccessEntitiesAsync(new(caller.TenantId, caller.Actor), ids, http.RequestAborted))
            return false;
        if (!reviewPermission.HasValue)
            return true;
        var granted = await EndpointAuthorization.AuthorizeScopedManyAsync(http, reviewPermission.Value,
            AccessScopeKindDto.LegalEntity, ids, http.RequestAborted);
        return ids.All(granted.Contains);
    }
}
