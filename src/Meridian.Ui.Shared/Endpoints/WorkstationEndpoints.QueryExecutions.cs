using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.Query;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static bool TryResolveQueryOwner(HttpContext context, out DataQueryOwner owner)
    {
        owner = null!;
        if (!TryResolveRequiredTenantId(context, out var tenant) || !TryResolveCurrentUser(context, out var actor))
            return false;
        var company = EndpointAuthorization.ResolveCompanyId(context);
        if (string.IsNullOrWhiteSpace(company))
            return false;
        owner = new(tenant, company, actor);
        return true;
    }

    internal static async Task<DataQueryResult> ExecuteRetainedWorkstationQueryAsync(DuckDbQueryService service,
        string? sql, DataQueryOptions options, HttpContext context)
    {
        if (!TryResolveQueryOwner(context, out var owner))
            return DataQueryResult.Failed("An authenticated tenant, company and operator are required to retain a query execution.");
        try
        {
            var store = new RetainedDataQueryStore(options, service.QueryableRoot);
            await using var sink = await store.CreateAsync(owner, context.RequestAborted).ConfigureAwait(false);
            return await service.ExecuteRetainedAsync(sql, options, sink, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RetainedDataQueryException or IOException or UnauthorizedAccessException or JsonException)
        {
            return DataQueryResult.Failed(exception is RetainedDataQueryException
                ? $"The complete query execution could not be retained. {exception.Message}"
                : "The complete query execution could not be retained. The private result store is unavailable.");
        }
    }

    internal static async Task<IResult?> TryExportRetainedDataQueryAsync(string tableId, WorkstationTableExportRequest request,
        HttpContext context, JsonSerializerOptions jsonOptions)
    {
        if (tableId != "data-query-results")
            return null;
        if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewHistoricalData) || !TryResolveQueryOwner(context, out var owner))
            return EndpointHelpers.Forbidden();
        if (request.Filters is null || request.Filters.Count != 1 || !request.Filters.TryGetValue("executionId", out var executionId) ||
            string.IsNullOrWhiteSpace(executionId) || request.ColumnIds is null || request.ColumnIds.Count is < 1 or > 1000 ||
            request.ColumnIds.Distinct(StringComparer.Ordinal).Count() != request.ColumnIds.Count || request.SearchText?.Length > 1000 ||
            request.SortDirection is not (null or "asc" or "desc"))
            return Results.BadRequest(new { error = "Export requires an exact retained execution and valid column/query settings. SQL is never accepted or rerun by this operation." });
        var service = context.RequestServices.GetService<DuckDbQueryService>();
        var options = context.RequestServices.GetService<IOptionsMonitor<DataQueryOptions>>()?.CurrentValue ?? new DataQueryOptions();
        if (service is null || !options.Enabled)
            return Results.Problem("Retained SQL exports are unavailable.", statusCode: 503);
        try
        {
            var store = new RetainedDataQueryStore(options, service.QueryableRoot);
            var snapshot = await store.ReadAsync(owner, executionId, context.RequestAborted).ConfigureAwait(false);
            var manifest = snapshot.Manifest;
            var ids = manifest.Columns.Select((column, index) => $"{index}:{column}").ToArray();
            if (request.ColumnIds.Any(column => !ids.Contains(column, StringComparer.Ordinal)) ||
                request.SortColumnId is not null && !ids.Contains(request.SortColumnId, StringComparer.Ordinal))
                return Results.BadRequest(new { error = "The selected columns do not belong to this exact execution." });
            var indices = request.ColumnIds.Select(column => Array.IndexOf(ids, column)).ToArray();
            var query = request.SearchText?.Trim() ?? string.Empty;
            var rows = snapshot.Rows.Select((values, ordinal) => (Values: values, Ordinal: ordinal))
                .Where(row => query.Length == 0 || string.Join(' ', row.Values.Select(value => value ?? string.Empty))
                    .Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            IEnumerable<(string?[] Values, int Ordinal)> ordered = rows;
            if (request.SortColumnId is not null)
            {
                var index = Array.IndexOf(ids, request.SortColumnId);
                var comparer = Comparer<string?>.Create((left, right) => CompareRetainedSqlCells(left, right, manifest.ColumnTypes[index]));
                ordered = request.SortDirection == "desc"
                    ? rows.OrderByDescending(row => row.Values[index], comparer).ThenBy(row => row.Ordinal)
                    : rows.OrderBy(row => row.Values[index], comparer).ThenBy(row => row.Ordinal);
            }
            var exportedAt = DateTimeOffset.UtcNow;
            var metadataHeaders = new[] { "Context · Execution ID", "Context · Source row ordinal", "Context · Executed SQL SHA256", "Context · Source column types", "Context · Execution read began UTC", "Context · Exported at UTC" };
            var sourceTypes = string.Join("; ", indices.Select(index => $"{ids[index]}={manifest.ColumnTypes[index]}"));
            var lines = new List<string> { string.Join(',', indices.Select(index => CsvCell(manifest.Columns[index])).Concat(metadataHeaders.Select(CsvCell))) };
            var csvBytes = (long)Encoding.UTF8.GetByteCount(lines[0]) + 2;
            var maxCsvBytes = Math.Min(options.MaxRetainedTotalBytes, 64L * 1024 * 1024);
            foreach (var row in ordered)
            {
                context.RequestAborted.ThrowIfCancellationRequested();
                var line = string.Join(',', indices.Select(index => RetainedSqlCsvCell(row.Values[index], manifest.ColumnTypes[index])).Concat(
                    new[] { manifest.ExecutionId, (row.Ordinal + 1).ToString(CultureInfo.InvariantCulture), manifest.SqlSha256, sourceTypes,
                        manifest.ExecutedAt.ToString("O", CultureInfo.InvariantCulture), exportedAt.ToString("O", CultureInfo.InvariantCulture) }.Select(CsvCell)));
                csvBytes += Encoding.UTF8.GetByteCount(line) + 2;
                if (csvBytes > maxCsvBytes)
                    throw new RetainedDataQueryException("Quota", "The complete CSV exceeds the response quota. Narrow the active search or choose fewer columns; no partial export was produced.");
                lines.Add(line);
            }
            return Results.Json(new WorkstationTableExportDto($"sql-execution-{manifest.ExecutionId[..12]}-{exportedAt:yyyyMMddTHHmmssZ}.csv",
                string.Join("\r\n", lines), rows.Length, manifest.ExecutedAt), jsonOptions);
        }
        catch (RetainedDataQueryException exception)
        {
            return Results.Json(new { error = exception.Message }, statusCode: exception.Code switch
            { "NotFound" => 404, "Expired" => 410, "Quota" => 413, "Invalid" => 409, _ => 503 });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Results.Problem("The complete retained execution could not be verified. No export was produced.", statusCode: 503);
        }
    }

    private static bool IsRetainedSqlNumericType(string type)
        => Regex.IsMatch(type, @"^(?:U?(?:TINY|SMALL|BIG|HUGE)?INT(?:EGER)?|DECIMAL(?:\(\d+,\d+\))?|NUMERIC(?:\(\d+,\d+\))?|REAL|FLOAT|DOUBLE)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string RetainedSqlCsvCell(string? value, string type)
        => value is not null && IsRetainedSqlNumericType(type) && Regex.IsMatch(value, @"^-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?$", RegexOptions.CultureInvariant)
            ? value : CsvCell(value);

    internal static int CompareRetainedSqlCells(string? left, string? right, string type)
    {
        if (left is null || right is null)
            return left is null ? right is null ? 0 : -1 : 1;
        if (IsRetainedSqlNumericType(type) && TryScaleRetainedDecimal(left, out var leftValue, out var leftScale) &&
            TryScaleRetainedDecimal(right, out var rightValue, out var rightScale))
        {
            var scale = Math.Max(leftScale, rightScale);
            return (leftValue * BigInteger.Pow(10, scale - leftScale)).CompareTo(rightValue * BigInteger.Pow(10, scale - rightScale));
        }
        return string.Compare(left, right, StringComparison.Ordinal);
    }

    private static bool TryScaleRetainedDecimal(string text, out BigInteger value, out int scale)
    {
        value = default;
        scale = 0;
        if (text.Length > 128 || !Regex.IsMatch(text, @"^-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?$", RegexOptions.CultureInvariant))
            return false;
        var exponentIndex = text.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? text : text[..exponentIndex];
        var exponent = 0;
        if (exponentIndex >= 0 && (!int.TryParse(text[(exponentIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out exponent) || Math.Abs((long)exponent) > 1024))
            return false;
        var point = mantissa.IndexOf('.');
        scale = (point < 0 ? 0 : mantissa.Length - point - 1) - exponent;
        return BigInteger.TryParse(mantissa.Replace(".", string.Empty, StringComparison.Ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
