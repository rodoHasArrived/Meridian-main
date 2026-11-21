using System.Text;
using System.Text.Json;
using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Documents;
using Meridian.Reporting;
using Meridian.Storage.Export;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportWriterGridArtifactContent(
    string FileName,
    string ContentType,
    byte[] Content);

/// <summary>
/// Renders report-writer grids and produces their downloadable artifact content.
/// </summary>
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

        var manifest = GetManifest(runId, accessContext);
        EnsureRunAccess(manifest, accessContext);

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

        var manifest = GetManifest(runId, accessContext);
        EnsureRunAccess(manifest, accessContext);
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

    private ReportingOutputManifest GetManifest(
        string runId,
        ReportAccessQueryContext? accessContext)
    {
        var normalizedRunId = runId.Trim();
        var tenantId = string.IsNullOrWhiteSpace(accessContext?.TenantId)
            ? null
            : accessContext.TenantId.Trim();
        if (accessContext?.RequireBoundScope == true && tenantId is null)
        {
            throw new UnauthorizedAccessException(
                "Report access requires authenticated actor, tenant, and company scope.");
        }

        return (tenantId is null
                ? _orchestrationService.GetManifest(normalizedRunId)
                : _orchestrationService.GetManifest(tenantId, normalizedRunId))
            ?? throw new KeyNotFoundException($"Reporting run '{runId}' was not found.");
    }

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

    private void EnsureRunAccess(ReportingOutputManifest manifest, ReportAccessQueryContext? accessContext)
    {
        var evaluation = ReportAccessPolicyEvaluator.Evaluate(manifest, accessContext);
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

    private const int PdfPreviewRowLimit = 28;
    private const string DisplayNumberFormat = "#,##0.############################";

    private sealed record GridExportColumn(string Key, string Label, bool IsNumeric);

    private static GridExportColumn[] BuildExportColumns(ReportWriterGridRenderDto grid)
    {
        var columns = grid.Columns.Select(column => new GridExportColumn(
            column.Key, ResolveExportLabel(grid, column), column.Role is "metric" or "formula")).ToArray();
        var duplicateLabels = columns.GroupBy(static column => column.Label, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return columns.Select(column => duplicateLabels.Contains(column.Label)
            ? column with { Label = $"{column.Label} ({column.Key})" }
            : column).ToArray();
    }

    private static string ResolveExportLabel(ReportWriterGridRenderDto grid, ReportWriterGridColumnDto column)
    {
        var label = string.IsNullOrWhiteSpace(column.Label) ? column.Key : column.Label;
        if (column.Role == "metric")
        {
            var metric = grid.Lineage?.Metrics
                .Where(metric => string.Equals(column.Key, metric.Name, StringComparison.OrdinalIgnoreCase)
                    || grid.Kind == ReportWriterGridKindDto.Pivot && column.Key.EndsWith($":{metric.Name}", StringComparison.OrdinalIgnoreCase))
                .MaxBy(static metric => metric.Name.Length);
            if (metric is not null)
            {
                var sourceLabel = ReportWriterDatasetSourceService.GetFieldLabel(metric.SourceField == "*" ? metric.Name : metric.SourceField);
                var metricLabel = metric.Function switch
                {
                    "Count" when metric.SourceField == "*" => "Row count",
                    "Count" => $"Count of {sourceLabel}",
                    "Average" => $"Average {sourceLabel}",
                    "Min" => $"Minimum {sourceLabel}",
                    "Max" => $"Maximum {sourceLabel}",
                    _ => sourceLabel
                };
                if (string.Equals(label, metric.Name, StringComparison.OrdinalIgnoreCase))
                    return metricLabel;
                if (column.Key.Length > metric.Name.Length + 1 && grid.Kind == ReportWriterGridKindDto.Pivot)
                {
                    var pivotKey = column.Key[..^(metric.Name.Length + 1)];
                    var pivotLabel = string.Join(" / ", pivotKey.Split('|')
                        .Select(static part => string.IsNullOrWhiteSpace(part) ? "(blank)" : part));
                    if (string.Equals(label, $"{pivotLabel} {metric.Name}", StringComparison.Ordinal)
                        || string.Equals(label, column.Key, StringComparison.Ordinal))
                        return $"{pivotLabel} {metricLabel}";
                }
            }
        }
        return string.Equals(label, column.Key, StringComparison.OrdinalIgnoreCase)
            ? ReportWriterDatasetSourceService.GetFieldLabel(column.Key)
            : label;
    }

    private static decimal? ReadNumber(GridExportColumn column, string value) =>
        column.IsNumeric && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static string FormatNumber(decimal value) =>
        value.ToString(DisplayNumberFormat, CultureInfo.InvariantCulture);

    private static byte[] BuildXlsx(ReportWriterGridRenderDto grid, ReportBrandingThemeDto? brandingTheme)
    {
        var columns = BuildExportColumns(grid);
        var headers = columns.Select(static column => column.Label).ToArray();
        var widths = headers.Select(static label => label.Length).ToArray();
        var rows = new IReadOnlyList<object?>[grid.Rows.Count];
        var precisionTextCount = 0;
        for (var rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
        {
            var values = grid.Rows[rowIndex].Values;
            var cells = new object?[columns.Length];
            for (var index = 0; index < columns.Length; index++)
            {
                var column = columns[index];
                if (!values.TryGetValue(column.Key, out var text) || string.IsNullOrWhiteSpace(text))
                    continue;

                if (ReadNumber(column, text) is { } value)
                {
                    text = FormatNumber(value);
                    // Excel keeps 15 significant digits. Text preserves values beyond that limit.
                    if (decimal.TryParse(value.ToString("G15", CultureInfo.InvariantCulture),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out var rounded) && rounded == value)
                    {
                        cells[index] = value;
                    }
                    else
                    {
                        cells[index] = text;
                        precisionTextCount++;
                    }
                }
                else
                {
                    cells[index] = text;
                }
                widths[index] = Math.Max(widths[index], text.Length);
            }
            rows[rowIndex] = cells;
        }

        var labels = columns.GroupBy(static column => column.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().Label, StringComparer.Ordinal);
        var validationRows = BuildValidationRows(grid);
        if (precisionTextCount > 0)
        {
            validationRows =
            [
                .. validationRows,
                new object?[]
                {
                    "xlsx-numeric-precision", "Info",
                    $"{precisionTextCount.ToString(CultureInfo.InvariantCulture)} numeric cell(s) use exact text to preserve values beyond Excel's 15 significant digits."
                }
            ];
        }

        return XlsxWorkbookWriter.CreateWorkbook(
        [
            new XlsxWorksheet("ReportWriterGrid", headers, rows)
            {
                Columns = columns.Select((column, index) =>
                    new XlsxColumnLayout(Math.Clamp(widths[index] + 2, 12, 60), column.IsNumeric)).ToArray()
            },
            new XlsxWorksheet(
                "Lineage",
                ["Field", "Value"],
                BuildLineageRows(grid)),
            new XlsxWorksheet(
                "DataDictionary",
                ["Key", "Label", "Role", "Source field", "Data type", "Generated", "Description"],
                BuildDataDictionaryRows(grid, labels)),
            new XlsxWorksheet(
                "Validation",
                ["Check", "Status", "Detail"],
                validationRows),
            new XlsxWorksheet(
                "Branding",
                ["Field", "Value"],
                BuildBrandingRows(brandingTheme))
        ]);
    }

    private static byte[] BuildPdf(ReportWriterGridRenderDto grid, ReportBrandingThemeDto? brandingTheme)
    {
        var columns = BuildExportColumns(grid);
        var headers = columns.Select(static column => column.Label).ToArray();
        var rows = grid.Rows.Take(PdfPreviewRowLimit).Select(row => (IReadOnlyList<string>)columns.Select(column =>
        {
            var value = row.Values.TryGetValue(column.Key, out var cell) ? cell : string.Empty;
            return ReadNumber(column, value) is { } number ? FormatNumber(number) : value;
        }).ToArray()).ToArray();
        var tables = new List<ReportDocumentTable> { new(grid.Title, headers, rows) };
        if (grid.Warnings.Count > 0)
        {
            tables.Add(new ReportDocumentTable("Warnings", ["Detail"],
                grid.Warnings.Select(static warning => (IReadOnlyList<string>)[warning]).ToArray()));
        }

        var header = new List<ReportDocumentField>
        {
            new("Grid", $"{grid.GridId} · {grid.Kind}"),
            new("Rows", $"{rows.Length.ToString(CultureInfo.InvariantCulture)} shown of {grid.Rows.Count.ToString(CultureInfo.InvariantCulture)}")
        };
        if (grid.Lineage is { } lineage)
        {
            header.Add(new("Lineage", FormattableString.Invariant(
                $"{lineage.InputRowCount} input · {lineage.FilteredInputRowCount ?? lineage.InputRowCount} filtered · {lineage.OutputRowCount} output")));
        }
        if (brandingTheme is not null)
        {
            header.Add(new("Branding theme", $"{brandingTheme.Name} ({brandingTheme.ThemeId})"));
            if (!string.IsNullOrWhiteSpace(brandingTheme.LogoUri))
                header.Add(new("Logo", brandingTheme.LogoUri.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(brandingTheme?.Disclaimer))
        {
            tables.Add(new ReportDocumentTable("Disclaimer", ["Detail"], [[brandingTheme.Disclaimer.Trim()]]));
        }

        var moreRows = grid.Rows.Count > PdfPreviewRowLimit
            ? FormattableString.Invariant($"{grid.Rows.Count - PdfPreviewRowLimit} additional row(s) available in XLSX.")
            : null;
        var footer = string.Join(" · ", new[] { moreRows, brandingTheme?.FooterText?.Trim() }
            .Where(static note => !string.IsNullOrWhiteSpace(note)));
        var model = new ReportDocumentModel(grid.Title, header, tables, FooterNote: footer);
        return new ClientGradeReportRenderer().RenderGridPdf(model,
            columns.Select(static column => column.IsNumeric).ToArray(),
            brandingTheme?.FirmName, NormalizeHexColor(brandingTheme?.PrimaryColor, "#1F4E79"));
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

    private static IReadOnlyList<IReadOnlyList<object?>> BuildDataDictionaryRows(
        ReportWriterGridRenderDto grid, IReadOnlyDictionary<string, string> labels) =>
        (grid.DataDictionary ?? BuildDataDictionary(grid))
            .Select(field => (IReadOnlyList<object?>)
            [
                field.Key,
                labels.GetValueOrDefault(field.Key, field.Label),
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
