using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.Maintenance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static async Task<IResult?> TryExportWorkstationDataTableAsync(
        string tableId, WorkstationTableExportRequest request, HttpContext context, JsonSerializerOptions jsonOptions)
    {
        var retainedQuery = await TryExportRetainedDataQueryAsync(tableId, request, context, jsonOptions).ConfigureAwait(false);
        if (retainedQuery is not null)
            return retainedQuery;
        if (tableId == "data-corporate-action-queue")
            return await ExportCorporateActionInboxAsync(request, context, jsonOptions).ConfigureAwait(false);
        if (tableId is "data-backfill-queue" or "data-export-packages")
            return await ExportDataWorkspaceSnapshotAsync(tableId, request, context, jsonOptions).ConfigureAwait(false);
        if (tableId == "data-remediation-queue")
            return ExportRetainedBackfillRemediation(request, context, jsonOptions);
        return BuildWorkstationDataTableExport(tableId, request, context, jsonOptions);
    }

    internal static IResult? BuildWorkstationDataTableExport(
        string tableId, WorkstationTableExportRequest request, HttpContext context, JsonSerializerOptions jsonOptions)
    {
        if (tableId != "data-maintenance-executions")
            return null;
        if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
            return EndpointHelpers.Forbidden();
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["task"] = "Task",
            ["status"] = "Status",
            ["trigger"] = "Manual trigger",
            ["started"] = "Started UTC",
            ["duration"] = "Duration",
            ["files"] = "Files processed",
            ["issues"] = "Issues resolved",
            ["reclaimed"] = "Bytes reclaimed"
        };
        if (request.ColumnIds is null || request.ColumnIds.Count is < 1 or > 8
            || request.ColumnIds.Distinct(StringComparer.Ordinal).Count() != request.ColumnIds.Count
            || request.ColumnIds.Any(column => !headers.ContainsKey(column))
            || request.SearchText?.Length > 1000 || request.SortDirection is not (null or "asc" or "desc")
            || request.SortColumnId is not null && !headers.ContainsKey(request.SortColumnId)
            || request.Filters?.Keys.Any(key => key != "failedOnly") == true
            || request.Filters?.TryGetValue("failedOnly", out var rawFailed) == true && rawFailed is not ("true" or "false"))
            return Results.BadRequest(new { error = "Unknown maintenance export columns or filters." });
        var service = context.RequestServices.GetService<ArchiveMaintenanceScheduleManager>();
        if (service is null)
            return Results.Problem("Maintenance execution history is unavailable.", statusCode: 503);
        var failedOnly = request.Filters?.GetValueOrDefault("failedOnly") == "true";
        // The source retains at most 1,000 executions. int.MaxValue selects that entire retained
        // population, bypassing only the browser's 25-row view window, not source retention.
        var source = service.ExecutionHistory.GetRecentExecutions(int.MaxValue);
        var query = request.SearchText?.Trim() ?? string.Empty;
        var selected = source.Where(row => (!failedOnly || row.Status is MaintenanceExecutionStatus.Failed or MaintenanceExecutionStatus.TimedOut)
            && (query.Length == 0 || $"{row.TaskType} {MaintenanceStatusLabel(row.Status)} {(row.ManualTrigger ? "Manual" : "Scheduled")} {row.ErrorMessage}".Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        object? Cell(MaintenanceExecution row, string column) => column switch
        {
            "task" => row.TaskType.ToString(),
            "status" => row.Status.ToString(),
            "trigger" => row.ManualTrigger,
            "started" => row.StartedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            "duration" => row.Duration?.ToString("c", CultureInfo.InvariantCulture),
            "files" => row.FilesProcessed,
            "issues" => row.IssuesResolved,
            "reclaimed" => row.BytesSaved,
            _ => null
        };
        IEnumerable<MaintenanceExecution> ordered = selected;
        if (request.SortColumnId is not null)
            ordered = request.SortDirection == "desc"
                ? selected.OrderByDescending(row => Cell(row, request.SortColumnId)).ThenBy(row => row.ExecutionId, StringComparer.Ordinal)
                : selected.OrderBy(row => Cell(row, request.SortColumnId)).ThenBy(row => row.ExecutionId, StringComparer.Ordinal);
        var asOf = DateTimeOffset.UtcNow;
        var lines = new List<string> { string.Join(',', request.ColumnIds.Select(column => CsvCell(headers[column])).Concat(
            new[] { "Context · Execution ID", "Context · Source", "Context · Exported at UTC" }.Select(CsvCell))) };
        foreach (var row in ordered)
            lines.Add(string.Join(',', request.ColumnIds.Select(column => CsvCell(Cell(row, column))).Concat(
                new[] { row.ExecutionId, "All retained maintenance executions; host retention applies", asOf.ToString("O", CultureInfo.InvariantCulture) }.Select(CsvCell))));
        return Results.Json(new WorkstationTableExportDto($"maintenance-executions-{asOf:yyyyMMddTHHmmssZ}.csv", string.Join("\r\n", lines), selected.Length, asOf), jsonOptions);
    }

    private static string MaintenanceStatusLabel(MaintenanceExecutionStatus status) => status switch
    {
        MaintenanceExecutionStatus.CompletedWithWarnings => "Completed with warnings",
        MaintenanceExecutionStatus.TimedOut => "Timed out",
        _ => status.ToString()
    };
}
