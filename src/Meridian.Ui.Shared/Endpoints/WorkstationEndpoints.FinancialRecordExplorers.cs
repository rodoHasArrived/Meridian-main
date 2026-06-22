using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapFinancialRecordExplorerEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationFinancialRecordExplorer), async (
            string explorerId,
            [FromServices] FinancialRecordExplorerReadService service,
            HttpContext context) =>
        {
            if (!TryResolveRequiredTenantId(context, out var tenantId))
            {
                return Results.Unauthorized();
            }

            var query = BuildFinancialRecordExplorerQuery(context);
            var explorer = await service.GetExplorerAsync(explorerId, tenantId, query, context.RequestAborted).ConfigureAwait(false);
            return explorer is null
                ? Results.NotFound(new { error = $"Unknown financial record explorer '{explorerId}'." })
                : Results.Json(explorer, jsonOptions);
        })
        .WithName("GetWorkstationFinancialRecordExplorer")
        .Produces<FinancialRecordExplorerDto>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationFinancialRecordExplorerRecord), async (
            string explorerId,
            string recordId,
            [FromServices] FinancialRecordExplorerReadService service,
            HttpContext context) =>
        {
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
        .Produces(404);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationFinancialRecordExplorerSavedViews), async (
            string explorerId,
            FinancialRecordExplorerSavedViewSaveRequestDto request,
            [FromServices] FinancialRecordExplorerReadService service,
            HttpContext context) =>
        {
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
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("SaveWorkstationFinancialRecordExplorerView")
        .Produces<FinancialRecordExplorerSavedViewDto>(200)
        .Produces(400)
        .Produces(404);
    }

    private static FinancialRecordExplorerQueryDto? BuildFinancialRecordExplorerQuery(HttpContext context)
    {
        var query = context.Request.Query;
        var viewId = query["viewId"].FirstOrDefault()?.Trim() ?? string.Empty;
        var searchText = query["searchText"].FirstOrDefault()?.Trim() ?? string.Empty;
        var filters = query["filter"]
            .Select(ParseFinancialRecordExplorerFilter)
            .Where(static filter => filter is not null)
            .Cast<FinancialRecordExplorerFilterDto>()
            .ToArray();

        return string.IsNullOrWhiteSpace(viewId) &&
               string.IsNullOrWhiteSpace(searchText) &&
               filters.Length == 0
            ? null
            : new FinancialRecordExplorerQueryDto(viewId, searchText, filters);
    }

    private static FinancialRecordExplorerFilterDto? ParseFinancialRecordExplorerFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            return null;
        }

        var filterId = value[..separator].Trim();
        var filterValue = value[(separator + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(filterId) || string.IsNullOrWhiteSpace(filterValue))
        {
            return null;
        }

        return new FinancialRecordExplorerFilterDto(filterId, filterId, filterValue);
    }
}
