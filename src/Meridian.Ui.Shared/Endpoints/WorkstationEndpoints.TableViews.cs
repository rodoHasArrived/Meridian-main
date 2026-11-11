using System.Text.Json;
using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapWorkstationTableViewEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        MapWorkstationTableExportEndpoint(group, jsonOptions);
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationTableViews), async (
            string tableId, [FromServices] IFinancialRecordExplorerSavedViewStore store, HttpContext context) =>
        {
            if (!CanReadWorkstationTable(context, tableId))
                return EndpointHelpers.Forbidden();
            if (!TryResolveRequiredTenantId(context, out var tenantId) || !TryResolveCurrentUser(context, out var actor))
                return Results.Unauthorized();
            try
            {
                var views = await store.LoadPersonalViewsAsync(tenantId, actor, tableId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(views, jsonOptions);
            }
            catch (NotSupportedException)
            {
                return Results.Problem("Personal table views are unavailable for this host.", statusCode: 501);
            }
        }).WithName("GetWorkstationTableViews").RequireAuthenticatedSession()
            .Produces<IReadOnlyList<WorkstationTableViewDto>>(200).Produces(403).Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationTableViews), async (
            string tableId, WorkstationTableViewSaveRequest request,
            [FromServices] IFinancialRecordExplorerSavedViewStore store, HttpContext context) =>
        {
            if (!CanReadWorkstationTable(context, tableId))
                return EndpointHelpers.Forbidden();
            if (!TryResolveRequiredTenantId(context, out var tenantId) || !TryResolveCurrentUser(context, out var actor))
                return Results.Unauthorized();
            try
            {
                var saved = await store.SavePersonalViewAsync(tenantId, actor, tableId, request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(saved, jsonOptions);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (NotSupportedException)
            {
                return Results.Problem("Personal table views are unavailable for this host.", statusCode: 501);
            }
        }).WithName("SaveWorkstationTableView").RequireAuthenticatedSession()
            .Produces<WorkstationTableViewDto>(200).Produces(400).Produces(403).Produces(501);
    }

    private static void MapWorkstationTableExportEndpoint(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationTableExport), async (
            string tableId, WorkstationTableExportRequest request, HttpContext context) =>
        {
            if (!CanReadWorkstationTable(context, tableId))
                return EndpointHelpers.Forbidden();
            if (!TryResolveRequiredTenantId(context, out _) || !TryResolveCurrentUser(context, out _))
                return Results.Unauthorized();
            var dataExport = await TryExportWorkstationDataTableAsync(tableId, request, context, jsonOptions).ConfigureAwait(false);
            if (dataExport is not null)
                return dataExport;
            var auditExport = await TryExportExecutionAuditAsync(tableId, request, context, jsonOptions).ConfigureAwait(false);
            if (auditExport is not null)
                return auditExport;
            if (tableId != "strategy-runs")
                return Results.Problem("A complete export adapter is not registered for this source.", statusCode: 501);
            var columns = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["strategy"] = "Strategy",
                ["mode"] = "Mode",
                ["engine"] = "Engine",
                ["status"] = "Status",
                ["pnl"] = "P&L (retained run denomination)",
                ["sharpe"] = "Sharpe",
                ["drawdown"] = "Maximum drawdown (amount)",
                ["updated"] = "Updated (UTC)",
                ["run-id"] = "Run ID"
                ,
                ["promotion"] = "Promotion state"
            };
            if (request.ColumnIds is null || request.ColumnIds.Count is 0 or > 20 ||
                request.ColumnIds.Any(column => !columns.ContainsKey(column)) ||
                request.ColumnIds.Distinct(StringComparer.Ordinal).Count() != request.ColumnIds.Count ||
                request.SearchText?.Length > 1000 || request.SortDirection is not (null or "asc" or "desc") ||
                request.SortColumnId is not null && !columns.ContainsKey(request.SortColumnId) || request.Filters?.Count > 0)
                return Results.BadRequest(new { error = "Unknown columns or invalid export query." });
            var service = context.RequestServices.GetService<StrategyRunReadService>();
            if (service is null)
                return Results.Problem("Strategy run service is unavailable.", statusCode: 501);
            var scope = ResolveStrategyRunReadScope(context);
            // This overload deliberately reads the complete authorized population, without the history endpoint's 500-row cap.
            var snapshot = await service.GetRunsAsync(strategyId: null, runType: null, scope, context.RequestAborted).ConfigureAwait(false);
            var search = request.SearchText?.Trim() ?? string.Empty;
            var selected = snapshot.Where(run => search.Length == 0 ||
                StrategyRunTableSearchText(run).Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
            var metrics = await service.CompareRunsAsync(selected.Select(run => run.RunId), scope, context.RequestAborted).ConfigureAwait(false);
            var byId = metrics.ToDictionary(run => run.RunId, StringComparer.Ordinal);
            if (selected.Any(run => !byId.TryGetValue(run.RunId, out var current) || current.LastUpdatedAt != run.LastUpdatedAt))
                return Results.Conflict(new { error = "The retained run population changed while preparing the export. Refresh and export again." });
            object? Cell(StrategyRunSummary row, string columnId) => columnId switch
            {
                "strategy" => row.StrategyName,
                "mode" => row.Mode.ToString(),
                "engine" => row.Engine.ToString(),
                "status" => row.Status.ToString(),
                "pnl" => row.NetPnl,
                "sharpe" => byId.GetValueOrDefault(row.RunId)?.SharpeRatio,
                "drawdown" => byId.GetValueOrDefault(row.RunId)?.MaxDrawdown,
                "updated" => row.LastUpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                "run-id" => row.RunId,
                "promotion" => row.Promotion?.State.ToString(),
                _ => null
            };
            IEnumerable<StrategyRunSummary> ordered = selected;
            if (request.SortColumnId is not null)
                ordered = request.SortDirection == "desc"
                    ? selected.OrderByDescending(row => Cell(row, request.SortColumnId)).ThenBy(row => row.RunId, StringComparer.Ordinal)
                    : selected.OrderBy(row => Cell(row, request.SortColumnId)).ThenBy(row => row.RunId, StringComparer.Ordinal);
            var asOf = DateTimeOffset.UtcNow;
            string[] metadataHeaders = ["Context · Source", "Context · Run ID", "Context · Source updated UTC", "Context · Exported at UTC", "Context · Denomination"];
            var lines = new List<string> { string.Join(',', request.ColumnIds.Select(column => CsvCell(columns[column])).Concat(metadataHeaders.Select(CsvCell))) };
            // Stable identity and snapshot time are explicit metadata columns, not a second unaudited source of financial truth.
            foreach (var row in ordered)
                lines.Add(string.Join(',', request.ColumnIds.Select(column => CsvCell(Cell(row, column))).Concat(
                new[] { "Retained authorized strategy-run results", row.RunId, row.LastUpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), asOf.ToString("O", CultureInfo.InvariantCulture), "Unavailable in source contract" }.Select(CsvCell))));
            return Results.Json(new WorkstationTableExportDto($"strategy-runs-{asOf:yyyyMMddTHHmmssZ}.csv", string.Join("\r\n", lines), selected.Length, asOf), jsonOptions);
        }).WithName("ExportWorkstationTable").DeclareNonMutating("Exports the complete authorized retained table population without changing domain records.")
            .RequireAuthenticatedSession()
            .Produces<WorkstationTableExportDto>(200).Produces(400).Produces(403).Produces(501);
    }

    private static string CsvCell(object? value)
    {
        if (value is null)
            return string.Empty;
        var text = value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty;
        if (value is string && text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r')
            text = "'" + text;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? '"' + text.Replace("\"", "\"\"", StringComparison.Ordinal) + '"' : text;
    }

    private static string StrategyRunTableSearchText(StrategyRunSummary run)
        => $"{run.StrategyName.Trim()} {run.Mode.ToString().ToLowerInvariant()} {run.Engine} {run.Status}";

    private static bool CanReadWorkstationTable(HttpContext context, string tableId)
        => tableId switch
        {
            "operator-inbox" => EndpointAuthorization.HasAnyPermission(context,
                UserPermission.ViewTrades, UserPermission.ViewStrategies, UserPermission.ManageStrategies,
                UserPermission.ViewDirectLending, UserPermission.ManageDirectLending,
                UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance),
            "portfolio-positions" => EndpointAuthorization.HasAnyPermission(context,
                UserPermission.ViewTrades, UserPermission.ViewStrategies, UserPermission.ManageStrategies),
            "portfolio-brokerage-accounts" or "portfolio-brokerage-positions" =>
                EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewTrades),
            "trading-positions" or "trading-orders" or "trading-fills" or "trading-execution-audit" or "trading-execution-blotter" or
            "trading-broker-order-discrepancies" or "trading-broker-affected-runs" => EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewTrades),
            "accounting-capital-command-centers" or "accounting-capital-investor-accounts" =>
                EndpointAuthorization.HasAnyPermission(context, UserPermission.AdminMaintenance, UserPermission.ManageDirectLending),
            "accounting-payment-intents" => EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewDirectLending),
            "accounting-reconciliation-breaks" or "margin-prime-summary" or "margin-account-positions" or "accounting-statement-schedules" or
            "accounting-external-gl-providers" or "accounting-external-gl-mappings" or "accounting-external-gl-reconciliation" or
            "accounting-approval-workflows" or "accounting-break-audit-differences" or "close-checklist-history" => EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewDirectLending,
                UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance),
            "accounting-period-comparison" or "accounting-ledger-search" or "accounting-mark-preview" or
            "accounting-payment-evidence-workflows" or "accounting-private-capital-events" or "accounting-private-capital-activity" or
            "accounting-private-capital-subledgers" or "accounting-private-capital-subledger" or "accounting-private-capital-ledger-impacts" or
            "accounting-private-capital-report-outputs" or "accounting-prepared-deadlines" => EndpointAuthorization.HasAnyPermission(context,
                UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports),
            "accounting-audit-integrity" => EndpointAuthorization.HasAnyPermission(context, UserPermission.AdminMaintenance),
            "strategy-runs" => EndpointAuthorization.HasAnyPermission(context,
                UserPermission.ViewStrategies, UserPermission.ManageStrategies),
            "strategy-comparison" or "strategy-position-diff" or "strategy-parameter-diff" or "strategy-promotion-history" or
            "strategy-plot-studies" or "strategy-plot-moments" or "strategy-plot-observations" or "strategy-scatter-mark-coordinates" =>
                EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewStrategies, UserPermission.ManageStrategies),
            "reporting-export-profiles" or "reporting-private-capital-events" or "reporting-capital-subledgers" or
            "reporting-released-artifacts" or "reporting-run-audit" or "reporting-series-revisions" or "reporting-access-grants" or "reporting-delivery-history" => EndpointAuthorization.HasAnyPermission(context,
                UserPermission.ViewReporting, UserPermission.ManageReporting),
            "data-provider-health" or "provider-evidence-age-data" or "data-backfill-queue" or "data-remediation-queue" or "data-export-packages" =>
                EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewHistoricalData, UserPermission.ViewDiagnostics, UserPermission.ManageStorage),
            "data-corporate-action-source-facts" or "data-corporate-action-queue" => EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewCorporateActions),
            "data-provider-capabilities" => EndpointAuthorization.HasAnyPermission(context,
                UserPermission.ViewConfig, UserPermission.ViewDiagnostics, UserPermission.ManageProviders, UserPermission.AdminMaintenance),
            "data-query-results" => EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewHistoricalData),
            "data-upload-preview" or "data-workbook-preview" => EndpointAuthorization.HasAnyPermission(context,
                UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster),
            "data-provider-rate-limits" => EndpointAuthorization.HasAnyPermission(context,
                UserPermission.ViewConfig, UserPermission.ViewDiagnostics, UserPermission.ManageProviders, UserPermission.AdminMaintenance),
            "data-ingestion-operations" or "backfill-coverage-evidence" => EndpointAuthorization.HasAnyPermission(context,
                UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill),
            "data-maintenance-executions" => EndpointAuthorization.HasAnyPermission(context, UserPermission.AdminMaintenance),
            "close-task-progress-data" or "close-checklist-progress-data" => EndpointAuthorization.HasAnyPermission(context,
                UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports),
            "provider-verification-events" => EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageCredentials),
            "account-balance-history" => EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewTrades),
            "break-occurrence-age-data" => EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewDirectLending,
                UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance),
            _ => FinancialRecordExplorerReadService.IsKnownExplorerId(tableId) && CanReadFinancialRecordExplorer(context, tableId)
        };
}
