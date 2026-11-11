using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    internal static async Task<IResult?> TryExportExecutionAuditAsync(string tableId, WorkstationTableExportRequest request,
        HttpContext context, JsonSerializerOptions jsonOptions)
    {
        if (tableId != "trading-execution-audit") return null;
        if (!await AuditTrailHttpAuthority.CanReadExecutionFeedAsync(context).ConfigureAwait(false) ||
            !TryResolveRequiredTenantId(context, out _) || !TryResolveCurrentUser(context, out _)) return EndpointHelpers.Forbidden();
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["column-1"] = "Occurred UTC", ["column-2"] = "Object", ["column-3"] = "Action", ["column-4"] = "Outcome",
            ["column-5"] = "Actor", ["column-6"] = "Context", ["column-7"] = "Action ledger"
        };
        var knownFilters = new HashSet<string>(["runId", "category", "action", "outcome", "actor", "symbol", "correlationId", "objectKind", "objectId", "relatedObjectId", "fromUtc", "toUtc"], StringComparer.Ordinal);
        if (request.ColumnIds is null || request.ColumnIds.Count is < 1 or > 7 || request.ColumnIds.Any(column => !headers.ContainsKey(column)) ||
            request.ColumnIds.Distinct(StringComparer.Ordinal).Count() != request.ColumnIds.Count || request.SearchText?.Length > 1000 ||
            request.SortColumnId is not null && !headers.ContainsKey(request.SortColumnId) || request.SortDirection is not (null or "asc" or "desc") ||
            request.Filters?.Any(filter => !knownFilters.Contains(filter.Key) || filter.Value?.Length > 1000) == true)
            return Results.BadRequest(new { error = "Unknown audit export columns, ordering or source filters." });
        string? Filter(string key) => request.Filters?.GetValueOrDefault(key);
        bool ParseDate(string key, out DateTimeOffset? value)
        {
            value = null;
            var text = Filter(key);
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!Regex.IsMatch(text, @"T.*(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
            value = parsed;
            return true;
        }
        if (!ParseDate("fromUtc", out var from) || !ParseDate("toUtc", out var to) || from > to)
            return Results.BadRequest(new { error = "Audit date bounds require ordered ISO timestamps with explicit offsets." });
        var service = context.RequestServices.GetService<AuditTrailExplorerService>() ?? new AuditTrailExplorerService(
            context.RequestServices.GetService<ExecutionAuditTrailService>(), context.RequestServices.GetService<IOperationsContinuityWorkflowService>());
        if (!service.HasSource) return Results.Problem("The retained audit source is unavailable; no export was produced.", statusCode: 503);
        try
        {
            var query = new AuditTrailExplorerQueryDto(request.SearchText, Filter("runId"), Filter("category"), Filter("action"),
                Filter("outcome"), Filter("actor"), Filter("symbol"), Filter("correlationId"), Filter("objectKind"), Filter("objectId"), Filter("relatedObjectId"), from, to);
            var source = await service.ExportAuthorizedAsync(query,
                (workflow, token) => AuditTrailHttpAuthority.CanReadWorkflowAsync(context, workflow, token), context.RequestAborted).ConfigureAwait(false);
            object? Cell(AuditTrailTimelineEntryDto row, string column) => column switch
            {
                "column-1" => row.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                "column-2" => $"{row.ObjectKind} {row.ObjectId}", "column-3" => $"{row.Category} · {row.Action}",
                "column-4" => row.Outcome, "column-5" => row.Actor ?? "System", "column-6" => AuditExportContext(row),
                "column-7" => string.IsNullOrWhiteSpace(row.CurrentActionHash) ? "Unchained" : $"{row.ActionLedgerStatus ?? "Recorded"} {(row.ActionLedgerSequence is { } sequence ? $"#{sequence}" : "no sequence")}", _ => null
            };
            IEnumerable<AuditTrailTimelineEntryDto> ordered = source.Entries;
            if (request.SortColumnId is not null)
            {
                object? SortCell(AuditTrailTimelineEntryDto row) => request.SortColumnId == "column-1" ? row.OccurredAt : Cell(row, request.SortColumnId);
                ordered = request.SortDirection == "desc"
                    ? source.Entries.OrderByDescending(SortCell).ThenBy(row => row.AuditId, StringComparer.Ordinal).ThenBy(row => row.ActionLedgerSource, StringComparer.Ordinal)
                    : source.Entries.OrderBy(SortCell).ThenBy(row => row.AuditId, StringComparer.Ordinal).ThenBy(row => row.ActionLedgerSource, StringComparer.Ordinal);
            }
            var metadata = new[] { "Context · Audit ID", "Context · Source namespace", "Context · Source population", "Context · Workflow ID", "Context · Fund account ID", "Context · Current action hash", "Context · Previous action hash", "Context · Evidence route", "Context · Source query", "Context · Read completed UTC" };
            var lines = new List<string> { string.Join(',', request.ColumnIds.Select(column => CsvCell(headers[column])).Concat(metadata.Select(CsvCell))) };
            var bytes = (long)Encoding.UTF8.GetByteCount(lines[0]) + 2;
            var sourceQuery = JsonSerializer.Serialize(new { SearchText = request.SearchText ?? string.Empty,
                Filters = request.Filters ?? new Dictionary<string, string>(), Population = "All authorized retained matches; browse limits do not apply" }, jsonOptions);
            foreach (var row in ordered)
            {
                context.RequestAborted.ThrowIfCancellationRequested();
                var isExecution = row.ActionLedgerSource == "ExecutionAuditTrail";
                var line = string.Join(',', request.ColumnIds.Select(column => CsvCell(Cell(row, column))).Concat(new[]
                {
                    row.AuditId,
                    isExecution ? "host-global execution feed; not tenant partitioned" : "authorized tenant/company operations timeline",
                    isExecution ? "All retained host execution entries matching this query; feed retention limits historical coverage" : "All retained matching entries from authorized operations workflows",
                    row.Metadata?.GetValueOrDefault("workflowId"), row.Metadata?.GetValueOrDefault("fundAccountId"),
                    row.CurrentActionHash, row.PreviousActionHash, row.EvidenceRoute, sourceQuery, source.AsOf.ToString("O", CultureInfo.InvariantCulture)
                }.Select(CsvCell)));
                bytes += Encoding.UTF8.GetByteCount(line) + 2;
                if (bytes > 64L * 1024 * 1024) throw new AuditTrailExportLimitException("The complete audit CSV exceeds the response quota. Narrow source filters or select fewer columns; no partial export was produced.");
                lines.Add(line);
            }
            return Results.Json(new WorkstationTableExportDto($"execution-audit-{source.AsOf:yyyyMMddTHHmmssZ}.csv", string.Join("\r\n", lines), source.TotalMatched, source.AsOf), jsonOptions);
        }
        catch (AuditTrailExportLimitException exception) { return Results.Json(new { error = exception.Message }, statusCode: 413); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { return Results.Problem("The complete authorized audit source could not be read or verified; no export was produced.", statusCode: 503); }
    }

    private static string AuditExportContext(AuditTrailTimelineEntryDto row)
    {
        var context = string.Join(" · ", new[] { row.Symbol, row.RunId, row.CorrelationId, row.Reason }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return context.Length > 0 ? context : row.Message ?? "—";
    }
}
