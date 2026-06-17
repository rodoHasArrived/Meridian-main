using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapFinancialRecordExplorerEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationFinancialRecordExplorer), async (
            string explorerId,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetService<FinancialRecordExplorerReadService>();
            if (service is null)
            {
                return Results.Problem(
                    "Financial record explorer service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveRequiredTenantId(context, out var tenantId))
            {
                return Results.Unauthorized();
            }

            var explorer = await service.GetExplorerAsync(explorerId, tenantId, context.RequestAborted).ConfigureAwait(false);
            return explorer is null
                ? Results.NotFound(new { error = $"Unknown financial record explorer '{explorerId}'." })
                : Results.Json(explorer, jsonOptions);
        })
        .WithName("GetWorkstationFinancialRecordExplorer")
        .Produces<FinancialRecordExplorerDto>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationFinancialRecordExplorerRecord), async (
            string explorerId,
            string recordId,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetService<FinancialRecordExplorerReadService>();
            if (service is null)
            {
                return Results.Problem(
                    "Financial record explorer service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!FinancialRecordExplorerReadService.IsKnownExplorerId(explorerId))
            {
                return Results.NotFound(new { error = $"Unknown financial record explorer '{explorerId}'." });
            }

            if (!TryResolveRequiredTenantId(context, out var tenantId))
            {
                return Results.Unauthorized();
            }

            var record = await service.GetRecordAsync(explorerId, recordId, tenantId, context.RequestAborted).ConfigureAwait(false);
            return record is null
                ? Results.NotFound(new { error = $"Unknown financial record '{recordId}'." })
                : Results.Json(record, jsonOptions);
        })
        .WithName("GetWorkstationFinancialRecordExplorerRecord")
        .Produces<FinancialRecordExplorerSelectedRecordDto>(200)
        .Produces(404)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationFinancialRecordExplorerSavedViews), async (
            string explorerId,
            FinancialRecordExplorerSavedViewSaveRequestDto request,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetService<FinancialRecordExplorerReadService>();
            if (service is null)
            {
                return Results.Problem(
                    "Financial record explorer service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Unauthorized();
                }

                var savedView = await service.SaveViewAsync(explorerId, tenantId, request, context.RequestAborted).ConfigureAwait(false);
                return savedView is null
                    ? Results.NotFound(new { error = $"Unknown financial record explorer '{explorerId}'." })
                    : Results.Json(savedView, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("SaveWorkstationFinancialRecordExplorerView")
        .Produces<FinancialRecordExplorerSavedViewDto>(200)
        .Produces(400)
        .Produces(404)
        .Produces(501);
    }
}
