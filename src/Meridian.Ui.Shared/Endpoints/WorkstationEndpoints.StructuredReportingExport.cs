using System.Text;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Export;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Structured reporting export helpers for the workstation API surface: CSV/XLSX
/// rendering, metadata/data-dictionary/validation/row-lineage worksheet rows, and
/// export audit headers. Split out of the WorkstationEndpoints core partial as a
/// capability group; the inline export endpoint lambda remains in the core file.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static void ApplyWorkstationStructuredExportAuditHeaders(
        HttpContext context,
        StructuredReportingExportPayloadDto payload)
    {
        context.Response.Headers["X-Meridian-Export-Id"] = payload.Export.ExportId;
        context.Response.Headers["X-Meridian-Export-Generated-At"] = payload.GeneratedAtUtc.ToString("O");
        if (!string.IsNullOrWhiteSpace(payload.GeneratedByPrincipalId))
        {
            context.Response.Headers["X-Meridian-Export-Generated-By"] = payload.GeneratedByPrincipalId;
        }

        if (!string.IsNullOrWhiteSpace(payload.GeneratedForCompanyId))
        {
            context.Response.Headers["X-Meridian-Export-Company"] = payload.GeneratedForCompanyId;
        }

        if (payload.GeneratedForGroupPrincipalIds is { Count: > 0 })
        {
            context.Response.Headers["X-Meridian-Export-Groups"] = string.Join(",", payload.GeneratedForGroupPrincipalIds);
        }

        if (!string.IsNullOrWhiteSpace(payload.Export.VersionStamp))
        {
            context.Response.Headers["X-Meridian-Export-Version"] = payload.Export.VersionStamp;
        }

        if (!string.IsNullOrWhiteSpace(payload.Export.IntegrityHashSha256))
        {
            context.Response.Headers["X-Meridian-Export-Sha256"] = payload.Export.IntegrityHashSha256;
        }
    }

    private static bool IsWorkstationStructuredCsvRequest(string? format) =>
        string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

    private static bool IsWorkstationStructuredJsonRequest(string? format) =>
        string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

    private static bool IsWorkstationStructuredXlsxRequest(string? format) =>
        string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "xls", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase);

    private static byte[] BuildWorkstationStructuredExportCsv(StructuredReportingExportPayloadDto payload)
    {
        var builder = new StringBuilder();
        AppendWorkstationStructuredExportCsvRow(builder, payload.Columns.Select(static column => column.Name));

        foreach (var row in payload.Rows)
        {
            AppendWorkstationStructuredExportCsvRow(
                builder,
                payload.Columns.Select(column =>
                    row.TryGetValue(column.Name, out var value) ? value : null));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildWorkstationStructuredExportXlsx(StructuredReportingExportPayloadDto payload)
    {
        var headers = payload.Columns.Select(static column => column.Name).ToArray();
        var rows = payload.Rows
            .Select(row => headers
                .Select(header => row.TryGetValue(header, out var value) ? value : null)
                .Cast<object?>()
                .ToArray())
            .Cast<IReadOnlyList<object?>>()
            .ToArray();

        return XlsxWorkbookWriter.CreateWorkbook(
        [
            new XlsxWorksheet(payload.Export.Dataset, headers, rows),
            new XlsxWorksheet(
                "Metadata",
                ["Field", "Value"],
                BuildWorkstationStructuredExportMetadataRows(payload)),
            new XlsxWorksheet(
                "DataDictionary",
                ["Ordinal", "Name", "Data type", "Required", "Description"],
                BuildWorkstationStructuredExportDataDictionaryRows(payload)),
            new XlsxWorksheet(
                "Validation",
                ["Check", "Status", "Detail"],
                BuildWorkstationStructuredExportValidationRows(payload)),
            new XlsxWorksheet(
                "RowLineage",
                ["Row", "Row key", "SHA-256"],
                BuildWorkstationStructuredExportRowLineageRows(payload))
        ]);
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildWorkstationStructuredExportMetadataRows(
        StructuredReportingExportPayloadDto payload) =>
    [
        ["exportId", payload.Export.ExportId],
        ["label", payload.Export.Label],
        ["purpose", payload.Export.Purpose.ToString()],
        ["dataset", payload.Export.Dataset],
        ["consumer", payload.Export.Consumer],
        ["schemaVersion", payload.Export.SchemaVersion],
        ["rowCount", payload.Export.RowCount],
        ["fieldCount", payload.Export.FieldCount],
        ["sourceCount", payload.Export.SourceCount],
        ["currency", payload.Export.Currency],
        ["asOf", payload.Export.AsOf],
        ["isReady", payload.Export.IsReady],
        ["retainedPath", payload.Export.RetainedPath],
        ["route", payload.Export.Route],
        ["versionStamp", payload.Export.VersionStamp],
        ["generatedAtUtc", payload.GeneratedAtUtc],
        ["generatedByPrincipalId", payload.GeneratedByPrincipalId],
        ["generatedForCompanyId", payload.GeneratedForCompanyId],
        ["generatedForGroups", string.Join(";", payload.GeneratedForGroupPrincipalIds ?? [])],
        ["rowLineageCount", payload.RowLineage?.Count ?? 0]
    ];

    private static IReadOnlyList<IReadOnlyList<object?>> BuildWorkstationStructuredExportDataDictionaryRows(
        StructuredReportingExportPayloadDto payload)
    {
        var fields = payload.DataDictionary is { Count: > 0 }
            ? payload.DataDictionary
            : payload.Columns.Select((column, index) => new StructuredReportingExportDataDictionaryFieldDto(
                column.Name,
                column.DataType,
                column.Description,
                index + 1)).ToArray();

        return fields
            .Select(static field => (IReadOnlyList<object?>)
            [
                field.Ordinal,
                field.Name,
                field.DataType,
                field.Required ? "true" : "false",
                field.Description
            ])
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildWorkstationStructuredExportValidationRows(
        StructuredReportingExportPayloadDto payload) =>
        (payload.ValidationChecks ?? [])
            .Select(static check => (IReadOnlyList<object?>)
            [
                check.CheckId,
                check.Status,
                check.Detail
            ])
            .ToArray();

    private static IReadOnlyList<IReadOnlyList<object?>> BuildWorkstationStructuredExportRowLineageRows(
        StructuredReportingExportPayloadDto payload) =>
        (payload.RowLineage ?? [])
            .Select(static row => (IReadOnlyList<object?>)
            [
                row.RowNumber,
                row.RowKey,
                row.RowHashSha256
            ])
            .ToArray();

    private static void AppendWorkstationStructuredExportCsvRow(StringBuilder builder, IEnumerable<string?> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append(EscapeWorkstationStructuredExportCsvValue(value));
            first = false;
        }

        builder.AppendLine();
    }

    private static string EscapeWorkstationStructuredExportCsvValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }
}
