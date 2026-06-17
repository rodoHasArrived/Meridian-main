using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Export;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportWriterGridArtifactContent(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed class ReportWriterGridArtifactService
{
    private const string JsonContentType = "application/json";
    private const string CsvContentType = "text/csv";
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PdfContentType = "application/pdf";

    private readonly IReportingOrchestrationService _orchestrationService;
    private readonly GovernedReportingTemplateCatalog? _governedTemplateCatalog;

    public ReportWriterGridArtifactService(
        IReportingOrchestrationService orchestrationService,
        GovernedReportingTemplateCatalog? governedTemplateCatalog = null)
    {
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        _governedTemplateCatalog = governedTemplateCatalog;
    }

    public ReportWriterGridRenderDto GetGrid(
        string runId,
        string gridId,
        ReportAccessQueryContext? accessContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gridId);

        var manifest = GetManifest(runId);
        EnsureTemplateAccess(manifest.TemplateId, accessContext);

        return GetGrid(manifest, runId, gridId);
    }

    public ReportWriterGridArtifactContent GetArtifact(
        string runId,
        string gridId,
        string? format,
        JsonSerializerOptions jsonOptions,
        ReportAccessQueryContext? accessContext = null)
    {
        ArgumentNullException.ThrowIfNull(jsonOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gridId);

        var manifest = GetManifest(runId);
        EnsureTemplateAccess(manifest.TemplateId, accessContext);
        var grid = GetGrid(manifest, runId, gridId);
        var normalizedFormat = NormalizeFormat(format);
        var safeRunId = SanitizeFileName(runId);
        var safeGridId = SanitizeFileName(grid.GridId);
        return normalizedFormat switch
        {
            "csv" => new ReportWriterGridArtifactContent(
                $"{safeRunId}-{safeGridId}.csv",
                CsvContentType,
                BuildCsv(grid)),
            "xlsx" => new ReportWriterGridArtifactContent(
                $"{safeRunId}-{safeGridId}.xlsx",
                XlsxContentType,
                BuildXlsx(grid, manifest.BrandingTheme)),
            "pdf" => new ReportWriterGridArtifactContent(
                $"{safeRunId}-{safeGridId}.pdf",
                PdfContentType,
                BuildPdf(grid, manifest.BrandingTheme)),
            _ => new ReportWriterGridArtifactContent(
                $"{safeRunId}-{safeGridId}.json",
                JsonContentType,
                JsonSerializer.SerializeToUtf8Bytes(grid, jsonOptions))
        };
    }

    private ReportingOutputManifest GetManifest(string runId) =>
        _orchestrationService.GetManifest(runId.Trim())
        ?? throw new KeyNotFoundException($"Reporting run '{runId}' was not found.");

    private static ReportWriterGridRenderDto GetGrid(
        ReportingOutputManifest manifest,
        string runId,
        string gridId)
    {
        if (manifest.RenderedReportWriterGrids.IsDefaultOrEmpty)
        {
            throw new KeyNotFoundException($"Reporting run '{runId}' has no rendered report-writer grids.");
        }

        var grid = manifest.RenderedReportWriterGrids
            .FirstOrDefault(grid => string.Equals(grid.GridId, gridId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Report-writer grid '{gridId}' was not found for reporting run '{runId}'.");
        return EnrichGridArtifact(grid);
    }

    private void EnsureTemplateAccess(string templateId, ReportAccessQueryContext? accessContext)
    {
        if (_governedTemplateCatalog is null)
        {
            return;
        }

        var evaluation = _governedTemplateCatalog.EvaluateAccess(templateId, accessContext);
        if (!evaluation.IsAccessible)
        {
            throw new UnauthorizedAccessException(evaluation.Reason);
        }
    }

    private static byte[] BuildCsv(ReportWriterGridRenderDto grid)
    {
        var headers = grid.Columns.Select(static column => column.Key).ToArray();
        var builder = new StringBuilder();
        AppendCsvRow(builder, headers);
        foreach (var row in grid.Rows)
        {
            AppendCsvRow(builder, headers.Select(header => row.Values.TryGetValue(header, out var value) ? value : null));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildXlsx(ReportWriterGridRenderDto grid, ReportBrandingThemeDto? brandingTheme)
    {
        var headers = grid.Columns.Select(static column => column.Key).ToArray();
        var rows = grid.Rows
            .Select(row => headers
                .Select(header => row.Values.TryGetValue(header, out var value) ? value : null)
                .Cast<object?>()
                .ToArray())
            .Cast<IReadOnlyList<object?>>()
            .ToArray();

        return XlsxWorkbookWriter.CreateWorkbook(
        [
            new XlsxWorksheet("ReportWriterGrid", headers, rows),
            new XlsxWorksheet(
                "Lineage",
                ["Field", "Value"],
                BuildLineageRows(grid)),
            new XlsxWorksheet(
                "DataDictionary",
                ["Key", "Label", "Role", "Source field", "Data type", "Generated", "Description"],
                BuildDataDictionaryRows(grid)),
            new XlsxWorksheet(
                "Validation",
                ["Check", "Status", "Detail"],
                BuildValidationRows(grid)),
            new XlsxWorksheet(
                "Branding",
                ["Field", "Value"],
                BuildBrandingRows(brandingTheme))
        ]);
    }

    private static byte[] BuildPdf(ReportWriterGridRenderDto grid, ReportBrandingThemeDto? brandingTheme)
    {
        var headers = grid.Columns.Select(static column => column.Key).ToArray();
        var content = new StringBuilder();
        var (red, green, blue) = NormalizePdfColor(brandingTheme?.PrimaryColor);
        content.AppendLine($"{red} {green} {blue} rg");
        content.AppendLine("72 742 468 34 re f");
        content.AppendLine("BT");
        content.AppendLine("/F1 12 Tf");
        content.AppendLine("1 1 1 rg");
        content.AppendLine("1 0 0 1 84 764 Tm");
        content.AppendLine($"({EscapePdfText(BuildPdfHeader(grid, brandingTheme))}) Tj");
        content.AppendLine("0 0 0 rg");
        content.AppendLine("/F1 9 Tf");
        var y = 724;
        if (brandingTheme is not null)
        {
            AppendPdfLine(content, $"Branding theme {brandingTheme.ThemeId} | {brandingTheme.Name} | {brandingTheme.FirmName}", ref y);
            if (!string.IsNullOrWhiteSpace(brandingTheme.LogoUri))
            {
                AppendPdfLine(content, $"Logo {brandingTheme.LogoUri.Trim()}", ref y);
            }
        }

        AppendPdfLine(content, $"Grid {grid.GridId} | {grid.Kind} | rows {grid.Rows.Count} | columns {grid.Columns.Count}", ref y);
        if (grid.Lineage is not null)
        {
            AppendPdfLine(
                content,
                $"Lineage input {grid.Lineage.InputRowCount} | filtered {grid.Lineage.FilteredInputRowCount} | output {grid.Lineage.OutputRowCount}",
                ref y);
        }

        if (grid.Warnings.Count > 0)
        {
            AppendPdfLine(content, $"Warnings {grid.Warnings.Count}: {string.Join("; ", grid.Warnings.Take(2))}", ref y);
        }

        AppendPdfLine(content, string.Join(" | ", headers), ref y);
        foreach (var row in grid.Rows.Take(28))
        {
            var values = headers.Select(header => row.Values.TryGetValue(header, out var value) ? value : "");
            AppendPdfLine(content, string.Join(" | ", values), ref y);
        }

        if (grid.Rows.Count > 28)
        {
            AppendPdfLine(content, $"... {grid.Rows.Count - 28} additional row(s) retained in JSON/CSV/XLSX exports.", ref y);
        }

        if (!string.IsNullOrWhiteSpace(brandingTheme?.FooterText))
        {
            AppendPdfLine(content, brandingTheme.FooterText.Trim(), ref y);
        }

        if (!string.IsNullOrWhiteSpace(brandingTheme?.Disclaimer))
        {
            AppendPdfLine(content, brandingTheme.Disclaimer.Trim(), ref y);
        }

        content.AppendLine("ET");
        return BuildSinglePagePdf(content.ToString());
    }

    private static void AppendPdfLine(StringBuilder content, string value, ref int y)
    {
        content.AppendLine($"1 0 0 1 72 {y} Tm");
        content.AppendLine($"({EscapePdfText(TruncatePdfLine(value))}) Tj");
        y -= 14;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildLineageRows(ReportWriterGridRenderDto grid)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "gridId", grid.GridId },
            new object?[] { "title", grid.Title },
            new object?[] { "kind", grid.Kind.ToString() },
            new object?[] { "columnCount", grid.Columns.Count },
            new object?[] { "rowCount", grid.Rows.Count },
            new object?[] { "warningCount", grid.Warnings.Count }
        };

        if (grid.Lineage is not null)
        {
            rows.Add(new object?[] { "inputRowCount", grid.Lineage.InputRowCount });
            rows.Add(new object?[] { "filteredInputRowCount", grid.Lineage.FilteredInputRowCount });
            rows.Add(new object?[] { "outputRowCount", grid.Lineage.OutputRowCount });
            rows.Add(new object?[] { "sourceFields", string.Join(";", grid.Lineage.SourceFields) });
            rows.Add(new object?[] { "metricCount", grid.Lineage.Metrics.Count });
            rows.Add(new object?[] { "formulaCount", grid.Lineage.Formulas.Count });
        }

        for (var index = 0; index < grid.Warnings.Count; index++)
        {
            rows.Add(new object?[] { $"warnings[{index}]", grid.Warnings[index] });
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildDataDictionaryRows(ReportWriterGridRenderDto grid) =>
        (grid.DataDictionary ?? BuildDataDictionary(grid))
            .Select(static field => (IReadOnlyList<object?>)
            [
                field.Key,
                field.Label,
                field.Role,
                field.SourceField,
                field.DataType,
                field.IsGenerated ? "true" : "false",
                field.Description
            ])
            .ToArray();

    private static IReadOnlyList<IReadOnlyList<object?>> BuildValidationRows(ReportWriterGridRenderDto grid) =>
        (grid.ValidationChecks ?? BuildValidationChecks(grid))
            .Select(static check => (IReadOnlyList<object?>)
            [
                check.CheckId,
                check.Status,
                check.Detail
            ])
            .ToArray();

    private static IReadOnlyList<IReadOnlyList<object?>> BuildBrandingRows(ReportBrandingThemeDto? brandingTheme)
    {
        if (brandingTheme is null)
        {
            return
            [
                new object?[] { "themeId", "" },
                new object?[] { "status", "No branding theme retained on the reporting run manifest." }
            ];
        }

        return
        [
            new object?[] { "themeId", brandingTheme.ThemeId },
            new object?[] { "name", brandingTheme.Name },
            new object?[] { "firmName", brandingTheme.FirmName },
            new object?[] { "primaryColor", brandingTheme.PrimaryColor },
            new object?[] { "accentColor", brandingTheme.AccentColor },
            new object?[] { "textColor", brandingTheme.TextColor },
            new object?[] { "backgroundColor", brandingTheme.BackgroundColor },
            new object?[] { "logoUri", brandingTheme.LogoUri },
            new object?[] { "footerText", brandingTheme.FooterText },
            new object?[] { "disclaimer", brandingTheme.Disclaimer },
            new object?[] { "isBuiltIn", brandingTheme.IsBuiltIn ? "true" : "false" }
        ];
    }

    private static ReportWriterGridRenderDto EnrichGridArtifact(ReportWriterGridRenderDto grid)
    {
        var dataDictionary = grid.DataDictionary is { Count: > 0 }
            ? grid.DataDictionary
            : BuildDataDictionary(grid);
        var validationChecks = grid.ValidationChecks is { Count: > 0 }
            ? grid.ValidationChecks
            : BuildValidationChecks(grid);

        return grid with
        {
            DataDictionary = dataDictionary,
            ValidationChecks = validationChecks
        };
    }

    private static IReadOnlyList<ReportWriterGridDataDictionaryFieldDto> BuildDataDictionary(ReportWriterGridRenderDto grid)
    {
        var sourceFields = grid.Lineage?.SourceFields ?? [];
        return grid.Columns
            .Select(column =>
            {
                var sourceField = ResolveSourceField(column, grid);
                var formulaColumn = string.Equals(column.Role, "formula", StringComparison.OrdinalIgnoreCase);
                var generated = formulaColumn
                    || string.IsNullOrWhiteSpace(sourceField)
                    || !sourceFields.Contains(sourceField, StringComparer.OrdinalIgnoreCase);
                var dataType = InferColumnDataType(column.Key, grid.Rows);
                return new ReportWriterGridDataDictionaryFieldDto(
                    column.Key,
                    column.Label,
                    column.Role,
                    generated ? "" : sourceField,
                    dataType,
                    generated,
                    BuildFieldDescription(column, dataType, generated, sourceField));
            })
            .ToArray();
    }

    private static IReadOnlyList<ReportWriterGridValidationCheckDto> BuildValidationChecks(ReportWriterGridRenderDto grid)
    {
        var checks = new List<ReportWriterGridValidationCheckDto>
        {
            new(
                "row-count",
                grid.Lineage is null || grid.Lineage.OutputRowCount == grid.Rows.Count ? "Passed" : "Failed",
                grid.Lineage is null
                    ? $"Rendered {grid.Rows.Count.ToString(CultureInfo.InvariantCulture)} row(s); no lineage row count was retained."
                    : $"Rendered {grid.Rows.Count.ToString(CultureInfo.InvariantCulture)} row(s); lineage expected {grid.Lineage.OutputRowCount.ToString(CultureInfo.InvariantCulture)}."),
            new(
                "column-dictionary",
                grid.Columns.Count == BuildDataDictionary(grid).Count ? "Passed" : "Failed",
                $"Dictionary covers {BuildDataDictionary(grid).Count.ToString(CultureInfo.InvariantCulture)} of {grid.Columns.Count.ToString(CultureInfo.InvariantCulture)} rendered column(s)."),
            new(
                "source-field-lineage",
                grid.Lineage is { SourceFields.Count: > 0 } ? "Passed" : "Warning",
                grid.Lineage is { SourceFields.Count: > 0 }
                    ? $"Lineage retains {grid.Lineage.SourceFields.Count.ToString(CultureInfo.InvariantCulture)} source field(s)."
                    : "No source field lineage was retained with this grid."),
            new(
                "warnings",
                grid.Warnings.Count == 0 ? "Passed" : "Warning",
                grid.Warnings.Count == 0
                    ? "No render warnings were retained."
                    : $"{grid.Warnings.Count.ToString(CultureInfo.InvariantCulture)} render warning(s) retained: {string.Join("; ", grid.Warnings)}")
        };

        return checks;
    }

    private static string ResolveSourceField(ReportWriterGridColumnDto column, ReportWriterGridRenderDto grid)
    {
        if (grid.Lineage?.Metrics.FirstOrDefault(metric =>
                string.Equals(metric.Name, column.Key, StringComparison.OrdinalIgnoreCase)) is { } metric)
        {
            return metric.SourceField;
        }

        if (grid.Lineage?.Formulas.FirstOrDefault(formula =>
                string.Equals(formula.Name, column.Key, StringComparison.OrdinalIgnoreCase)) is { } formula)
        {
            return string.Join(";", formula.SourceFields);
        }

        return grid.Lineage?.SourceFields.FirstOrDefault(field =>
            string.Equals(field, column.Key, StringComparison.OrdinalIgnoreCase)) ?? column.Key;
    }

    private static string InferColumnDataType(string columnKey, IReadOnlyList<ReportWriterGridRowDto> rows)
    {
        var values = rows
            .Select(row => row.Values.TryGetValue(columnKey, out var value) ? value : null)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(25)
            .ToArray();
        if (values.Length == 0)
        {
            return "string";
        }

        return values.All(static value => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            ? "decimal"
            : "string";
    }

    private static string BuildFieldDescription(
        ReportWriterGridColumnDto column,
        string dataType,
        bool generated,
        string sourceField)
    {
        if (generated)
        {
            return $"{column.Label} is generated by the retained report-writer grid as a {column.Role} column.";
        }

        return $"{column.Label} maps to source field {sourceField} and exports as {dataType}.";
    }

    private static void AppendCsvRow(StringBuilder builder, IEnumerable<string?> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append(EscapeCsvValue(value));
            first = false;
        }

        builder.AppendLine();
    }

    private static string EscapeCsvValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static string TruncatePdfLine(string value) =>
        value.Length <= 110 ? value : string.Concat(value.AsSpan(0, 107), "...");

    private static string BuildPdfHeader(ReportWriterGridRenderDto grid, ReportBrandingThemeDto? brandingTheme) =>
        brandingTheme is null
            ? grid.Title
            : $"{brandingTheme.FirmName} - {grid.Title}";

    private static (string Red, string Green, string Blue) NormalizePdfColor(string? value)
    {
        var normalized = NormalizeHexColor(value, "#1F4E79");
        var red = Convert.ToInt32(normalized.Substring(1, 2), 16) / 255m;
        var green = Convert.ToInt32(normalized.Substring(3, 2), 16) / 255m;
        var blue = Convert.ToInt32(normalized.Substring(5, 2), 16) / 255m;
        return (
            red.ToString("0.###", CultureInfo.InvariantCulture),
            green.ToString("0.###", CultureInfo.InvariantCulture),
            blue.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static string NormalizeHexColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        return normalized.Length == 7
               && normalized[0] == '#'
               && normalized.Skip(1).All(Uri.IsHexDigit)
            ? normalized
            : fallback;
    }

    private static string EscapePdfText(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static byte[] BuildSinglePagePdf(string contentStream)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(contentStream)} >>\nstream\n{contentStream}\nendstream"
        };

        var builder = new StringBuilder();
        var offsets = new List<int>(objects.Length + 1) { 0 };
        builder.AppendLine("%PDF-1.4");
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.AppendLine($"{index + 1} 0 obj");
            builder.AppendLine(objects[index]);
            builder.AppendLine("endobj");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.AppendLine("xref");
        builder.AppendLine($"0 {objects.Length + 1}");
        builder.AppendLine("0000000000 65535 f ");
        foreach (var offset in offsets.Skip(1))
        {
            builder.AppendLine($"{offset:0000000000} 00000 n ");
        }

        builder.AppendLine("trailer");
        builder.AppendLine($"<< /Size {objects.Length + 1} /Root 1 0 R >>");
        builder.AppendLine("startxref");
        builder.AppendLine(xrefOffset.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string NormalizeFormat(string? format) =>
        format?.Trim().ToLowerInvariant() switch
        {
            "csv" => "csv",
            "xlsx" or "xls" or "excel" => "xlsx",
            "pdf" => "pdf",
            _ => "json"
        };

    private static string SanitizeFileName(string value)
    {
        var chars = value
            .Trim()
            .Select(static c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray();
        var sanitized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "report-writer-grid" : sanitized;
    }
}
