using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
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

            if (!CanReadFinancialRecordExplorer(context, explorerId))
            {
                return EndpointHelpers.Forbidden();
            }

            var query = BuildFinancialRecordExplorerQuery(context);
            var explorer = await service.GetExplorerAsync(explorerId, tenantId, query, context.RequestAborted).ConfigureAwait(false);
            return explorer is null
                ? Results.NotFound(new { error = $"Unknown financial record explorer '{explorerId}'." })
                : Results.Json(explorer, jsonOptions);
        })
        .WithName("GetWorkstationFinancialRecordExplorer").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.ViewStrategies, UserPermission.ManageStrategies, UserPermission.AdminMaintenance)
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

            if (!CanReadFinancialRecordExplorer(context, explorerId))
            {
                return EndpointHelpers.Forbidden();
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
        .WithName("GetWorkstationFinancialRecordExplorerRecord").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.ViewStrategies, UserPermission.ManageStrategies, UserPermission.AdminMaintenance)
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
        .WithName("SaveWorkstationFinancialRecordExplorerView").RequireAuthenticatedSession()
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

    /// <summary>
    /// The route admits strategy readers because the ledger and portfolio explorers are projections
    /// of strategy-run detail -- the same data the run-ledger, trial-balance and journal routes serve
    /// under ViewStrategies, so refusing it here while serving it there would break the drill-in
    /// links between them. The other two explorers have no such second door: security-instrument is
    /// security-master data and report-line-provenance is reporting data, so a caller holding only a
    /// strategy permission is refused those rather than admitted to all four by a widened route.
    /// </summary>
    private static bool CanReadFinancialRecordExplorer(HttpContext context, string explorerId)
    {
        if (EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewDirectLending,
                UserPermission.ViewSecurityMaster,
                UserPermission.ManageDirectLending,
                UserPermission.ModifySecurityMaster,
                UserPermission.AdminMaintenance))
        {
            return true;
        }

        var normalized = (explorerId ?? string.Empty).Trim();
        var isRunBacked =
            normalized.Equals(FinancialRecordExplorerReadService.LedgerExplorerId, StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(FinancialRecordExplorerReadService.PortfolioExplorerId, StringComparison.OrdinalIgnoreCase);

        return isRunBacked &&
               EndpointAuthorization.HasAnyPermission(
                   context,
                   UserPermission.ViewStrategies,
                   UserPermission.ManageStrategies);
    }
}
