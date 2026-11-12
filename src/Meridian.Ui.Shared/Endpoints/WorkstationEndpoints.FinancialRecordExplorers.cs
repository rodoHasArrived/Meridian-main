using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapFinancialRecordExplorerEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        MapWorkstationTableViewEndpoints(group, jsonOptions);
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
            var requestedViewId = query?.ViewId;
            var personalViews = Array.Empty<FinancialRecordExplorerSavedViewDto>();
            if (context.RequestServices.GetService<IFinancialRecordExplorerSavedViewStore>() is { } viewStore &&
                TryResolveCurrentUser(context, out var actor))
            {
                try
                {
                    personalViews = (await viewStore.LoadPersonalViewsAsync(tenantId, actor, explorerId, context.RequestAborted).ConfigureAwait(false))
                        .Where(view => !view.IsReadOnly).Select(ToExplorerPersonalView).ToArray();
                    var selected = personalViews.FirstOrDefault(view => view.ViewId == query?.ViewId);
                    if (selected is not null)
                        query = query! with
                        {
                            // The read service knows system and legacy views. Apply this authorized
                            // personal view as query operands, preserving its identity for the response.
                            ViewId = string.Empty,
                            SearchText = context.Request.Query.ContainsKey("searchText") ? query.SearchText : selected.SearchText,
                            Filters = query.Filters?.Count > 0 ? query.Filters : selected.Filters
                        };
                }
                catch (NotSupportedException) { /* A legacy custom store remains readable; new personal writes return unavailable. */ }
            }
            var explorer = await service
                .GetExplorerAsync(
                    explorerId,
                    tenantId,
                    EndpointAuthorization.ResolveCompanyId(context),
                    query,
                    ResolveExplorerReadScope(context),
                    // The report families the explorer enriches with are not tenant-partitioned at
                    // their source -- ListRecords returns every tenant's workflow records -- so the
                    // permission scope above decides which families load and this decides which of
                    // their records this caller may see. Built with RequireBoundScope, the same
                    // context the reporting routes serve these records under head-on.
                    BuildReportAccessQueryContext(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            if (explorer is not null)
            {
                var views = explorer.SavedViews.Select(view => view with
                {
                    IsReadOnly = true,
                    Origin = view.IsSystem ? "System view" : "Legacy tenant view — save a personal copy"
                }).Concat(personalViews).Select(view => string.IsNullOrWhiteSpace(requestedViewId) ? view : view with { IsActive = view.ViewId == requestedViewId }).ToArray();
                explorer = explorer with { SavedViews = views };
            }
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
                .GetRecordAsync(
                    explorerId,
                    recordId,
                    tenantId,
                    EndpointAuthorization.ResolveCompanyId(context),
                    ResolveExplorerReadScope(context),
                    // Same bound context as the list route: the drill-in is one row of that explorer,
                    // so a record the list filtered out must not be reachable by asking for it.
                    BuildReportAccessQueryContext(context),
                    context.RequestAborted)
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
            HttpContext context) =>
        {
            try
            {
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Unauthorized();
                }

                if (!FinancialRecordExplorerReadService.IsKnownExplorerId(explorerId))
                    return Results.NotFound();
                if (!CanReadFinancialRecordExplorer(context, explorerId))
                    return EndpointHelpers.Forbidden();
                if (!TryResolveCurrentUser(context, out var actor))
                    return Results.Unauthorized();
                var store = context.RequestServices.GetService<IFinancialRecordExplorerSavedViewStore>();
                if (store is null)
                    return Results.Problem("Personal view persistence is unavailable.", statusCode: 501);
                if (request.Filters?.Count > 50 || request.ColumnIds?.Count > 100 ||
                    request.Filters?.Any(filter => filter is not null && string.IsNullOrWhiteSpace(filter.FilterId)) == true)
                    return Results.BadRequest(new { error = "Saved-view filters require an identity and presentation settings must remain within supported limits." });
                var normalizedFilters = FinancialRecordExplorerReadService.NormalizeFilters(request.Filters, maximumCount: 50);
                var normalizedColumns = FinancialRecordExplorerReadService.NormalizeColumnIds(request.ColumnIds, maximumCount: 100);
                var filters = normalizedFilters.GroupBy(filter => filter.FilterId)
                    .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty);
                var saved = await store.SavePersonalViewAsync(tenantId, actor, explorerId,
                    new WorkstationTableViewSaveRequest(request.Label, request.SearchText?.Trim() ?? string.Empty, normalizedColumns.Count > 0 ? normalizedColumns : ["__all__"], "compact", Filters: filters)
                    { Description = request.Description, ExplorerFilters = normalizedFilters },
                    context.RequestAborted).ConfigureAwait(false);
                return Results.Json(ToExplorerPersonalView(saved), jsonOptions);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (NotSupportedException) { return Results.Problem("Personal view persistence is unavailable.", statusCode: 501); }
        })
        .WithName("SaveWorkstationFinancialRecordExplorerView").RequireAuthenticatedSession()
        .Produces<FinancialRecordExplorerSavedViewDto>(200)
        .Produces(400)
        .Produces(404);
    }

    private static FinancialRecordExplorerSavedViewDto ToExplorerPersonalView(WorkstationTableViewDto view)
        => new(view.ViewId, view.Label, view.Description ?? view.Origin ?? "My view", IsSystem: false, IsActive: false,
            Filters: view.ExplorerFilters ?? (view.Filters ?? new Dictionary<string, string>()).Select(filter => new FinancialRecordExplorerFilterDto(filter.Key, filter.Key, filter.Value)).ToArray(),
            SearchText: view.SearchText, ColumnIds: view.ColumnIds.Where(column => column != "__all__").ToArray())
        { IsReadOnly = view.IsReadOnly, Origin = view.Origin };

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
    /// Ledger and portfolio are projections of strategy-run detail and answer only to the strategy
    /// permissions, matching the run-ledger, trial-balance and journal routes that serve the same
    /// runs directly. Security-instrument is run-derived too but is the Security Master coverage
    /// surface, so it answers to either basis. Report-line provenance is built from the report-pack
    /// workflow instead and answers only to the reporting permissions.
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

        // Ledger and portfolio build entirely from StrategyRunReadService -- trial balances, positions,
        // run identifiers and proof links. GetRunLedger, GetRunLedgerTrialBalance and
        // GetRunLedgerJournal serve that same data directly and admit only the strategy permissions,
        // so an operations or Security Master permission is not a claim on it. Grouping these with
        // security-instrument was what let ViewSecurityMaster alone read strategy-run financial
        // records.
        if (normalized.Equals(FinancialRecordExplorerReadService.LedgerExplorerId, StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(FinancialRecordExplorerReadService.PortfolioExplorerId, StringComparison.OrdinalIgnoreCase))
        {
            return EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewStrategies,
                UserPermission.ManageStrategies);
        }

        // Security-instrument has two bases and answers to either: its rows are the Security Master
        // references a strategy run touched, so a strategy permission admits it, and it is the
        // Security Master coverage surface, so a Security Master permission does too. Everything each
        // row is decorated with is projected separately by ResolveExplorerReadScope.
        if (normalized.Equals(FinancialRecordExplorerReadService.SecurityInstrumentExplorerId, StringComparison.OrdinalIgnoreCase))
        {
            return EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewSecurityMaster,
                UserPermission.ModifySecurityMaster,
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
                UserPermission.AdminMaintenance),
            // GetRunLedger and the other run routes. This decorates security-instrument rows with the
            // identity of the run they came from; the explorer admits Security Master callers on their
            // own basis, and that basis is not a claim on which strategy run touched an instrument.
            Strategy: EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewStrategies,
                UserPermission.ManageStrategies));
}
