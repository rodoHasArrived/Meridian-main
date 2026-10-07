using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapClosePreparationEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerCloseManagementTemplates, async (Guid sourceWorkflowId, HttpContext context) =>
            await ExecuteClosePreparationAsync(context, async (service, close) =>
            {
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, close, sourceWorkflowId).ConfigureAwait(false);
                if (!scope.IsAccessible)
                    return CloseWorkflowScopeDenied();
                if (scope.Plan is null)
                    return Results.NotFound();
                return Results.Json(await service.ListTemplatesAsync(sourceWorkflowId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }, mutation: false).ConfigureAwait(false))
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports);

        app.MapGet(UiApiRoutes.LedgerCloseManagementTemplate, async (Guid templateId, int version, HttpContext context) =>
            await ExecuteClosePreparationAsync(context, async (service, close) =>
            {
                var template = await service.GetTemplateAsync(templateId, version, context.RequestAborted).ConfigureAwait(false);
                if (template is null)
                    return Results.NotFound();
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, close, template.SourceWorkflowId).ConfigureAwait(false);
                return scope.IsAccessible && scope.Plan is not null ? Results.Json(template, jsonOptions) : CloseWorkflowScopeDenied();
            }, mutation: false).ConfigureAwait(false))
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports);

        app.MapPost(UiApiRoutes.LedgerCloseManagementTemplates, async (CaptureClosePlanTemplateRequestDto request, HttpContext context) =>
            await ExecuteClosePreparationAsync(context, async (service, close) =>
            {
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, close, request.SourceWorkflowId).ConfigureAwait(false);
                if (!scope.IsAccessible)
                    return CloseWorkflowScopeDenied();
                if (scope.Plan is null)
                    return Results.NotFound();
                var actor = ResolveMutationActor(context, string.Empty);
                var result = await service.CaptureTemplateAsync(request with
                {
                    Actor = actor,
                    ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
                }, actor, scope.TenantContext.TenantId!, scope.TenantContext.CompanyId!, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }).ConfigureAwait(false))
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
            .RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerCloseManagementPreparationPreview, async (PreviewClosePreparationRequestDto request, HttpContext context) =>
            await ExecuteClosePreparationAsync(context, async (service, close) =>
            {
                var template = await service.GetTemplateAsync(request.TemplateId, request.TemplateVersion, context.RequestAborted).ConfigureAwait(false);
                if (template is null)
                    return Results.NotFound();
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, close, template.SourceWorkflowId).ConfigureAwait(false);
                if (!scope.IsAccessible || scope.Plan is null || !await CanAccessPreparationBookAsync(context, request.TargetLedgerBookId).ConfigureAwait(false))
                    return CloseWorkflowScopeDenied();
                var actor = ResolveMutationActor(context, string.Empty);
                return Results.Json(await service.PreviewAsync(request with
                {
                    Actor = actor,
                    ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
                }, actor, scope.TenantContext.TenantId!, scope.TenantContext.CompanyId!, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false))
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
            .RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerCloseManagementPreparationCreate, async (CreatePreparedClosePlanRequestDto request, HttpContext context) =>
            await ExecuteClosePreparationAsync(context, async (service, close) =>
            {
                var preview = await service.GetPreviewAsync(request.PreviewId, context.RequestAborted).ConfigureAwait(false);
                if (preview is null)
                    return Results.NotFound();
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, close, preview.SourceWorkflowId).ConfigureAwait(false);
                if (!scope.IsAccessible || scope.Plan is null || !await CanAccessPreparationBookAsync(context, preview.TargetBook.LedgerBookId).ConfigureAwait(false))
                    return CloseWorkflowScopeDenied();
                var actor = ResolveMutationActor(context, string.Empty);
                return Results.Json(await service.CreateAsync(request with
                {
                    Actor = actor,
                    ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
                }, actor, scope.TenantContext.TenantId!, scope.TenantContext.CompanyId!, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false))
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
            .RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static async Task<IResult> ExecuteClosePreparationAsync(HttpContext context,
        Func<IAccountingClosePreparationService, IAccountingCloseManagementService, Task<IResult>> execute,
        bool mutation = true)
    {
        if (mutation ? !HasLedgerMutationPermission(context) : !HasLedgerReadPermission(context))
            return EndpointHelpers.Forbidden();
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!HasAccountingPackageTenantScope(tenant) || (mutation && !TryResolveActor(context, out _)))
            return CloseWorkflowScopeDenied();
        if (context.RequestServices.GetService<ILedgerBookService>() is null || context.RequestServices.GetService<ILedgerJournalStore>() is null)
            return ServiceUnavailable("Authoritative ledger book and period services are required to prepare a close plan.");
        var service = context.RequestServices.GetService<IAccountingClosePreparationService>();
        var close = ResolveAccountingCloseManagementService(context);
        if (service is null || close is null)
            return ServiceUnavailable("Close preparation is unavailable.");
        try
        { return await execute(service, close).ConfigureAwait(false); }
        catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [ex.Message] }); }
        catch (ClosePreparationPreviewStaleException ex) { return Results.Conflict(new { error = ex.Message, code = "PREPARATION_PREVIEW_STALE" }); }
        catch (ClosePreparationRecoveryRequiredException ex) { return Results.Conflict(new { error = ex.Message, code = "PREPARATION_RECOVERY_REQUIRED" }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (LedgerBookServiceException ex) { return MapServiceException(ex); }
    }

    private static async Task<bool> CanAccessPreparationBookAsync(HttpContext context, Guid bookId)
    {
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        var books = context.RequestServices.GetRequiredService<ILedgerBookService>();
        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (registry is null)
            return false;
        var book = await books.GetBookAsync(bookId, context.RequestAborted).ConfigureAwait(false);
        if (book is null)
            return false;
        var owner = await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
        if (owner is null || !CloseWorkflowOwnerMatches(owner, tenant))
            return false;
        var guard = context.RequestServices.GetService<IFundProfileTenantGuard>();
        return guard is null || (await guard.EvaluateAsync(tenant, book.FundProfileId, context.RequestAborted).ConfigureAwait(false)).IsAllowed;
    }
}
