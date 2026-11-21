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
        => WorkstationTablePermissions.TryGetValue(tableId, out var permissions)
            ? EndpointAuthorization.HasAnyPermission(context, permissions)
            : FinancialRecordExplorerReadService.IsKnownExplorerId(tableId) && CanReadFinancialRecordExplorer(context, tableId);

    /// <summary>
    /// The workstation tables that support saved views and complete export, keyed by the browser's
    /// <c>workbenchId</c>, with the permissions that may read each one (any one suffices). Each entry
    /// mirrors the permission the table's own data endpoint requires. Explorer tables not listed here
    /// fall back to the financial-record explorer allow-list.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, UserPermission[]> WorkstationTablePermissions = BuildWorkstationTablePermissions();

    private static Dictionary<string, UserPermission[]> BuildWorkstationTablePermissions()
    {
        var map = new Dictionary<string, UserPermission[]>(StringComparer.Ordinal);
        void Add(UserPermission[] permissions, params string[] tableIds)
        {
            foreach (var tableId in tableIds)
                map.Add(tableId, permissions);
        }

        Add([UserPermission.ViewTrades, UserPermission.ViewStrategies, UserPermission.ManageStrategies,
                UserPermission.ViewDirectLending, UserPermission.ManageDirectLending,
                UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance],
            "operator-inbox");
        Add([UserPermission.ViewTrades, UserPermission.ViewStrategies, UserPermission.ManageStrategies],
            "portfolio-positions");
        Add([UserPermission.ViewTrades],
            "portfolio-brokerage-accounts", "portfolio-brokerage-positions",
            // Mirrors the portfolio/trading position reads used by the instrument dossier.
            "instrument-dossier-positions",
            "trading-positions", "trading-orders", "trading-fills", "trading-execution-audit", "trading-execution-blotter",
            "trading-broker-order-discrepancies", "trading-broker-affected-runs",
            "account-balance-history",
            // Mirrors /api/execution/journal/* and /api/portfolio/cash-ladder/comparison.
            "execution-journal", "portfolio-cash-ladder-comparison");
        // Mirrors /api/fund-structure/nav-comparison.
        Add([UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance],
            "portfolio-nav-comparison");
        Add([UserPermission.AdminMaintenance, UserPermission.ManageDirectLending],
            "accounting-capital-command-centers", "accounting-capital-investor-accounts");
        Add([UserPermission.ViewDirectLending],
            "accounting-payment-intents");
        Add([UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending,
                UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance],
            "accounting-reconciliation-breaks", "margin-prime-summary", "margin-account-positions", "accounting-statement-schedules",
            "accounting-external-gl-providers", "accounting-external-gl-mappings", "accounting-external-gl-reconciliation",
            "accounting-approval-workflows", "accounting-break-audit-differences", "close-checklist-history",
            "break-occurrence-age-data");
        Add([UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports],
            "accounting-period-comparison", "accounting-period-closing-balance-comparison", "accounting-ledger-search", "accounting-mark-preview",
            // Mirrors the dossier's ledger period/journal entry reads.
            "instrument-dossier-ledger",
            "accounting-payment-evidence-workflows", "accounting-private-capital-events", "accounting-private-capital-activity",
            "accounting-private-capital-subledgers", "accounting-private-capital-subledger", "accounting-private-capital-ledger-impacts",
            "accounting-private-capital-report-outputs", "accounting-prepared-deadlines",
            "close-task-progress-data", "close-checklist-progress-data");
        // Mirrors the book receipt run reads; direct-lending permission alone does not admit them.
        Add([UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance],
            "book-income-receipts");
        Add([UserPermission.AdminMaintenance],
            "accounting-audit-integrity", "data-maintenance-executions");
        Add([UserPermission.ViewStrategies, UserPermission.ManageStrategies],
            "strategy-runs",
            "strategy-comparison", "strategy-position-diff", "strategy-parameter-diff", "strategy-promotion-history",
            "strategy-plot-studies", "strategy-plot-moments", "strategy-plot-observations", "strategy-scatter-mark-coordinates",
            // Mirrors /api/workstation/runs/{runId}/attribution/period-comparison.
            "portfolio-attribution-period-comparison");
        Add([UserPermission.ViewReporting, UserPermission.ManageReporting],
            "reporting-export-profiles", "reporting-private-capital-events", "reporting-capital-subledgers",
            "reporting-released-artifacts", "reporting-run-audit", "reporting-series-revisions", "reporting-access-grants", "reporting-delivery-history");
        Add([UserPermission.ViewHistoricalData, UserPermission.ViewDiagnostics, UserPermission.ManageStorage],
            "data-provider-health", "provider-evidence-age-data", "data-backfill-queue", "data-remediation-queue", "data-export-packages");
        Add([UserPermission.ViewCorporateActions],
            "data-corporate-action-source-facts", "data-corporate-action-queue");
        Add([UserPermission.ViewConfig, UserPermission.ViewDiagnostics, UserPermission.ManageProviders, UserPermission.AdminMaintenance],
            "data-provider-capabilities", "data-provider-rate-limits");
        Add([UserPermission.ViewHistoricalData],
            "data-query-results");
        Add([UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster],
            "data-upload-preview", "data-workbook-preview");
        Add([UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill],
            "data-ingestion-operations", "backfill-coverage-evidence");
        Add([UserPermission.ManageCredentials],
            "provider-verification-events");
        return map;
    }
}
