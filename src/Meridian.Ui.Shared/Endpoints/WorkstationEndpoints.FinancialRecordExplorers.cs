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
            // Unknown before unauthorized, matching the record route below: the per-explorer guard
            // answers false for an id it does not recognise, so without this an unknown id reads as a
            // permission refusal to a caller who is in fact permitted every explorer that exists.
            if (!FinancialRecordExplorerReadService.IsKnownExplorerId(explorerId))
            {
                return Results.NotFound(new { error = $"Unknown financial record explorer '{explorerId}'." });
            }

            if (!TryResolveRequiredTenantId(context, out var tenantId))
            {
                return Results.Unauthorized();
            }

            if (!CanReadFinancialRecordExplorer(context, explorerId))
            {
                return EndpointHelpers.Forbidden();
            }

            var query = BuildFinancialRecordExplorerQuery(context);
            var explorer = await service
                .GetExplorerAsync(explorerId, tenantId, query, ResolveExplorerReadScope(context), context.RequestAborted)
                .ConfigureAwait(false);
            return explorer is null
                ? Results.NotFound(new { error = $"Unknown financial record explorer '{explorerId}'." })
                : Results.Json(explorer, jsonOptions);
        })
        .WithName("GetWorkstationFinancialRecordExplorer").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.ViewStrategies, UserPermission.ManageStrategies, UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.AdminMaintenance)
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

            var record = await service
                .GetRecordAsync(explorerId, recordId, tenantId, ResolveExplorerReadScope(context), context.RequestAborted)
                .ConfigureAwait(false);
            return record is null
                ? Results.NotFound(new { error = $"Unknown financial record '{recordId}'." })
                : Results.Json(record, jsonOptions);
        })
        .WithName("GetWorkstationFinancialRecordExplorerRecord").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.ViewStrategies, UserPermission.ManageStrategies, UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.AdminMaintenance)
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
    /// Each explorer is authorized by the family its own builder reads. The route declaration admits
    /// the union, which is wider than any single explorer, so the decision has to be made here.
    /// <para>
    /// Ledger, portfolio and security-instrument are projections of strategy-run detail -- all three
    /// read StrategyRunReadService -- over fund records, so they answer to the operations and
    /// security-master set that serves those records directly, or to the strategy permissions the
    /// run-ledger, trial-balance and journal routes use for the same runs. Report-line provenance is
    /// built from the report-pack workflow instead and answers only to the reporting permissions.
    /// </para>
    /// <para>
    /// The operations set was previously applied before the id was examined, which let a caller
    /// holding only ViewSecurityMaster read report-pack lines, approvals and delivery history. That
    /// was not an access this surface deliberately granted: before this wave the route carried no
    /// declaration and no guard at all, so every session read all four. Scoping the set to the
    /// explorers whose records it serves is therefore the first decision made here, not a narrowing
    /// of one already taken.
    /// </para>
    /// </summary>
    private static bool CanReadFinancialRecordExplorer(HttpContext context, string explorerId)
    {
        var normalized = (explorerId ?? string.Empty).Trim();

        if (normalized.Equals(FinancialRecordExplorerReadService.LedgerExplorerId, StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(FinancialRecordExplorerReadService.PortfolioExplorerId, StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(FinancialRecordExplorerReadService.SecurityInstrumentExplorerId, StringComparison.OrdinalIgnoreCase))
        {
            return EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewDirectLending,
                UserPermission.ViewSecurityMaster,
                UserPermission.ManageDirectLending,
                UserPermission.ModifySecurityMaster,
                UserPermission.AdminMaintenance,
                UserPermission.ViewStrategies,
                UserPermission.ManageStrategies);
        }

        if (normalized.Equals(FinancialRecordExplorerReadService.ReportLineProvenanceExplorerId, StringComparison.OrdinalIgnoreCase))
        {
            return EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewReporting,
                UserPermission.ManageReporting);
        }

        return false;
    }

    /// <summary>
    /// Which sibling families the caller may see enriched into an explorer row. Admission to the
    /// explorer is not a claim on the families it decorates rows with: the security-instrument
    /// explorer's rows are the Security Master references a strategy run touched, so a strategy
    /// permission admits it, but the passport, AssetOperations detail and readiness, journal proofs,
    /// report-pack usage, and direct-lending health each answer to their own family.
    /// <para>
    /// Each set is the one its direct route declares, so a caller sees through the explorer exactly
    /// what it could fetch head-on. Keeping them literally equal is the point: a decoration that
    /// admits more callers than the route serving the same data is the leak this resolves.
    /// </para>
    /// </summary>
    private static FinancialRecordExplorerReadScope ResolveExplorerReadScope(HttpContext context)
        => new(
            Reporting: EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewReporting,
                UserPermission.ManageReporting),
            DirectLending: EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewDirectLending,
                UserPermission.ManageDirectLending),
            // GetSecurityMasterWorkstationInstrumentPassport.
            SecurityMaster: EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewSecurityMaster,
                UserPermission.ModifySecurityMaster),
            // GetWorkstationAssetOperations.
            AssetOperations: EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewTrades,
                UserPermission.ViewDirectLending,
                UserPermission.ManageDirectLending,
                UserPermission.ViewSecurityMaster,
                UserPermission.ModifySecurityMaster,
                UserPermission.AdminMaintenance));
}
