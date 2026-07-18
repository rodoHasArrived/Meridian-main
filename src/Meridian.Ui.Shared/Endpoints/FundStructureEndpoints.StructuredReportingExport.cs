using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Export;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class FundStructureEndpoints
{
    private const string StructuredXlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static bool IsStructuredCsvRequest(string? format) =>
        string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

    private static bool IsStructuredJsonRequest(string? format) =>
        string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

    private static bool IsStructuredXlsxRequest(string? format) =>
        string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "xls", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase);

    private static byte[] BuildStructuredExportJson(
        StructuredReportingExportPayloadDto payload,
        JsonSerializerOptions jsonOptions) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, jsonOptions);

    private static byte[] BuildStructuredExportCsv(StructuredReportingExportPayloadDto payload)
    {
        var builder = new StringBuilder();
        AppendStructuredExportCsvRow(builder, payload.Columns.Select(static column => column.Name));

        foreach (var row in payload.Rows)
        {
            AppendStructuredExportCsvRow(
                builder,
                payload.Columns.Select(column =>
                    row.TryGetValue(column.Name, out var value) ? value : null));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildStructuredExportXlsx(StructuredReportingExportPayloadDto payload)
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
                BuildStructuredExportMetadataRows(payload)),
            new XlsxWorksheet(
                "DataDictionary",
                ["Ordinal", "Name", "Data type", "Required", "Description"],
                BuildStructuredExportDataDictionaryRows(payload)),
            new XlsxWorksheet(
                "Validation",
                ["Check", "Status", "Detail"],
                BuildStructuredExportValidationRows(payload)),
            new XlsxWorksheet(
                "RowLineage",
                ["Row", "Row key", "SHA-256"],
                BuildStructuredExportRowLineageRows(payload))
        ]);
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildStructuredExportMetadataRows(
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

    private static IReadOnlyList<IReadOnlyList<object?>> BuildStructuredExportDataDictionaryRows(
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

    private static IReadOnlyList<IReadOnlyList<object?>> BuildStructuredExportValidationRows(
        StructuredReportingExportPayloadDto payload) =>
        (payload.ValidationChecks ?? [])
            .Select(static check => (IReadOnlyList<object?>)
            [
                check.CheckId,
                check.Status,
                check.Detail
            ])
            .ToArray();

    private static IReadOnlyList<IReadOnlyList<object?>> BuildStructuredExportRowLineageRows(
        StructuredReportingExportPayloadDto payload) =>
        (payload.RowLineage ?? [])
            .Select(static row => (IReadOnlyList<object?>)
            [
                row.RowNumber,
                row.RowKey,
                row.RowHashSha256
            ])
            .ToArray();

    private static void AppendStructuredExportCsvRow(StringBuilder builder, IEnumerable<string?> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append(EscapeStructuredExportCsvValue(value));
            first = false;
        }

        builder.AppendLine();
    }

    private static string EscapeStructuredExportCsvValue(string? value)
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
