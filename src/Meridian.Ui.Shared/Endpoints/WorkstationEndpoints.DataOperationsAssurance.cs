using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapDataOperationsAssuranceEndpoints(RouteGroupBuilder group)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationIngestionOperations), (
            HttpContext context,
            string? state,
            string? workload,
            string? provider,
            bool? resumableOnly) =>
        {
            var service = context.RequestServices.GetService<IngestionOperationsService>();
            if (!EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill))
                return EndpointHelpers.Forbidden();
            return service is null
                ? Results.Problem("Ingestion operations service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(service.GetSnapshot(state, workload, provider, resumableOnly ?? false));
        })
        .WithName("GetWorkstationIngestionOperations")
        .Produces<IngestionOperationsSnapshotDto>()
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationIngestionOperationById), (
            HttpContext context,
            string jobId) =>
        {
            var service = context.RequestServices.GetService<IngestionOperationsService>();
            if (!EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill))
                return EndpointHelpers.Forbidden();
            if (service is null)
                return Results.Problem("Ingestion operations service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var detail = service.GetDetail(jobId);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetWorkstationIngestionOperation")
        .Produces<IngestionOperationDetailDto>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationIngestionOperationAction), async (
            HttpContext context,
            string jobId,
            string action,
            IngestionOperationActionRequestDto request,
            CancellationToken ct) =>
        {
            var service = context.RequestServices.GetService<IngestionOperationsService>();
            if (!EndpointAuthorization.HasPermission(context, UserPermission.TriggerBackfill))
                return EndpointHelpers.Forbidden();
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return EndpointHelpers.Forbidden();
            if (service is null)
                return Results.Problem("Ingestion operations service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || string.IsNullOrWhiteSpace(request.Rationale))
                return Results.BadRequest(new { error = "Idempotency key and rationale are required." });
            try
            {
                var trustedScope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await service.ApplyActionAsync(
                        jobId,
                        action,
                        request,
                        actor,
                        trustedScope.TenantId!,
                        trustedScope.CompanyId!,
                        ct)
                    .ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("ApplyWorkstationIngestionOperationAction").RequirePermission(UserPermission.TriggerBackfill)
        .Produces<IngestionOperationActionResultDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationStorageAssurance), async (
            HttpContext context,
            CancellationToken ct) =>
        {
            var service = context.RequestServices.GetService<StorageAssuranceService>();
            if (service is null)
                return Results.Problem("Storage assurance service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var canView = EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewDiagnostics, UserPermission.ManageStorage);
            if (!canView)
                return EndpointHelpers.Forbidden();
            var permissions = new StorageAssurancePermissionsDto(
                true,
                EndpointAuthorization.HasPermission(context, UserPermission.ManageStorage),
                EndpointAuthorization.HasPermission(context, UserPermission.ManageStorage),
                EndpointAuthorization.HasPermission(context, UserPermission.ManageStorage) && EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance));
            return Results.Ok(await service.GetSnapshotAsync(permissions, ct).ConfigureAwait(false));
        })
        .WithName("GetWorkstationStorageAssurance")
        .Produces<StorageAssuranceSnapshotDto>()
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationStorageMaintenancePreview), async (
            HttpContext context,
            StorageMaintenancePreviewRequestDto request,
            CancellationToken ct) =>
        {
            var service = context.RequestServices.GetService<StorageAssuranceService>();
            if (service is null)
                return Results.Problem("Storage assurance service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            if (!CanPreviewStorageAction(context, request.Action))
                return EndpointHelpers.Forbidden();
            try
            {
                return Results.Ok(await service.PreviewAsync(request, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("PreviewWorkstationStorageMaintenance").RequirePermission(UserPermission.ManageStorage)
        .Produces<StorageMaintenancePreviewDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationStorageMaintenanceExecute), async (
            HttpContext context,
            StorageMaintenanceCommandRequestDto request,
            CancellationToken ct) =>
        {
            var service = context.RequestServices.GetService<StorageAssuranceService>();
            if (service is null)
                return Results.Problem("Storage assurance service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ManageStorage))
                return EndpointHelpers.Forbidden();
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return EndpointHelpers.Forbidden();
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                return Results.BadRequest(new { error = "Idempotency key is required." });
            var action = service.GetExecuteAction(request.PreviewId, request.IdempotencyKey);
            if (action.HasValue && !CanExecuteStorageAction(context, action.Value))
                return EndpointHelpers.Forbidden();
            try
            {
                var trustedScope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await service.ExecuteAsync(
                        request,
                        actor,
                        trustedScope.TenantId!,
                        trustedScope.CompanyId!,
                        ct)
                    .ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (TimeoutException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status410Gone);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("ExecuteWorkstationStorageMaintenance").RequirePermission(UserPermission.ManageStorage)
        .Produces<StorageMaintenanceResultDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status410Gone)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequireWorkstationTenantCompanyScope();
    }

    private static bool CanPreviewStorageAction(HttpContext context, StorageMaintenanceActionDto action) =>
        action != StorageMaintenanceActionDto.Cleanup
            ? EndpointAuthorization.HasPermission(context, UserPermission.ManageStorage)
            : EndpointAuthorization.HasPermission(context, UserPermission.ManageStorage) && EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance);

    private static bool CanExecuteStorageAction(HttpContext context, StorageMaintenanceActionDto action) =>
        CanPreviewStorageAction(context, action);
}
