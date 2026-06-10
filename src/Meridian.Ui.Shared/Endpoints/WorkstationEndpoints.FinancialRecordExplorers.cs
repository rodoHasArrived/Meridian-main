using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
                return Results.Problem("Financial record explorer service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var explorer = await service.GetExplorerAsync(explorerId, context.RequestAborted).ConfigureAwait(false);
            return explorer is null
                ? Results.NotFound(new { error = $"Financial record explorer '{explorerId}' was not found." })
                : Results.Json(explorer, jsonOptions);
        })
        .WithName("GetFinancialRecordExplorer")
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
                return Results.Problem("Financial record explorer service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var detail = await service.GetRecordAsync(explorerId, recordId, context.RequestAborted).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound(new { error = $"Financial record '{recordId}' was not found for explorer '{explorerId}'." })
                : Results.Json(detail, jsonOptions);
        })
        .WithName("GetFinancialRecordExplorerRecord")
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
                return Results.Problem("Financial record explorer service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                var savedView = await service.SaveViewAsync(explorerId, request, context.RequestAborted).ConfigureAwait(false);
                return savedView is null
                    ? Results.NotFound(new { error = $"Financial record explorer '{explorerId}' was not found." })
                    : Results.Json(savedView, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("SaveFinancialRecordExplorerSavedView")
        .Produces<FinancialRecordExplorerSavedViewDto>(200)
        .Produces(400)
        .Produces(404)
        .Produces(501);
    }
}
