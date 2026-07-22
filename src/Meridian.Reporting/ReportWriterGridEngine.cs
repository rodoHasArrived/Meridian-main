using System.Collections.Frozen;
using System.Globalization;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

public static class ReportWriterGridEngine
{
    public static IReadOnlyList<ReportWriterGridRenderDto> RenderGrids(
        IReadOnlyList<ReportWriterGridDefinitionDto>? grids,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? datasetRows)
    {
        if (grids is null || grids.Count == 0)
        {
            return [];
        }

        var rows = NormalizeRows(datasetRows);
        return grids
            .Select(grid => RenderGrid(grid, rows))
            .ToArray();
    }

    private static ReportWriterGridRenderDto RenderGrid(
        ReportWriterGridDefinitionDto grid,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        var warnings = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowDimensions = NormalizeFields(grid.RowFields);
        var columnDimensions = NormalizeFields(grid.ColumnFields);
        var dimensions = rowDimensions
            .Concat(columnDimensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var metrics = NormalizeMetrics(grid.Metrics);
        var formulas = NormalizeFormulas(grid.Formulas);
        var circularFormulas = DetectCircularFormulas(grid, formulas, warnings);
        var filters = NormalizeFilters(grid.Filters);

        if (rows.Count == 0)
        {
            warnings.Add($"Grid '{grid.GridId}' has no dataset rows to render.");
        }

        var filteredRows = ApplyFilters(grid, rows, filters, warnings);
        IReadOnlyList<ReportWriterGridColumnDto> columnList;
        IReadOnlyList<ReportWriterGridRowDto> renderedRows;
        if (grid.Kind == ReportWriterGridKindDto.Pivot && columnDimensions.Length > 0)
        {
            var pivotColumns = BuildPivotColumns(filteredRows, columnDimensions);
            columnList = BuildPivotRenderColumns(rowDimensions, pivotColumns, metrics, formulas);
            renderedRows = RenderPivotRows(grid, filteredRows, rowDimensions, columnDimensions, pivotColumns, metrics, formulas, circularFormulas, warnings);
        }
        else
        {
            columnList = BuildColumns(dimensions, metrics, formulas, includeContribution: grid.Kind == ReportWriterGridKindDto.Contribution);
            renderedRows = grid.Kind == ReportWriterGridKindDto.Detail
                ? RenderDetailRows(grid, filteredRows, dimensions, metrics, formulas, circularFormulas, warnings)
                : RenderAggregateRows(grid, filteredRows, dimensions, metrics, formulas, circularFormulas, warnings);
        }

        renderedRows = ApplyFormatRules(grid, renderedRows, warnings);
        var chart = BuildChart(grid, columnList, renderedRows);

        var lineage = BuildLineage(rows.Count, filteredRows.Count, renderedRows.Count, dimensions, metrics, formulas, filters);

        var dataDictionary = BuildDataDictionary(columnList, renderedRows, lineage);
        var validationChecks = BuildValidationChecks(columnList, renderedRows, warnings, lineage, dataDictionary);

        return new ReportWriterGridRenderDto(
            grid.GridId.Trim(),
            string.IsNullOrWhiteSpace(grid.Title) ? grid.GridId.Trim() : grid.Title.Trim(),
            grid.Kind,
            columnList,
            renderedRows,
            warnings.ToArray(),
            lineage,
            dataDictionary,
            validationChecks,
            chart);
    }

    private static IReadOnlyList<ReportWriterGridRowDto> ApplyFormatRules(
        ReportWriterGridDefinitionDto grid,
        IReadOnlyList<ReportWriterGridRowDto> rows,
        ISet<string> warnings)
    {
        var rules = NormalizeFormatRules(grid.FormatRules);
        if (rules.Length == 0 || rows.Count == 0)
        {
            return rows;
        }

        return rows
            .Select(row =>
            {
                Dictionary<string, ReportWriterCellStyleDto>? hints = null;
                foreach (var rule in rules)
                {
                    var probe = new ReportWriterFilterDefinitionDto(rule.Column, rule.Operator, rule.Value, rule.Label);
                    if (!MatchesFilter(row.Values, probe, warnings))
                    {
                        continue;
                    }

                    hints ??= new Dictionary<string, ReportWriterCellStyleDto>(StringComparer.OrdinalIgnoreCase);
                    // Later rules win for the same column, mirroring spreadsheet rule precedence.
                    hints[rule.Column] = rule.Style;
                }

                return hints is null ? row : row with { StyleHints = hints };
            })
            .ToArray();
    }

    private static ReportWriterChartRenderDto? BuildChart(
        ReportWriterGridDefinitionDto grid,
        IReadOnlyList<ReportWriterGridColumnDto> columns,
        IReadOnlyList<ReportWriterGridRowDto> rows)
    {
        var chart = grid.Chart;
        if (chart is null)
        {
            return null;
        }

        var warnings = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var title = string.IsNullOrWhiteSpace(chart.Title) ? grid.Title?.Trim() ?? grid.GridId.Trim() : chart.Title.Trim();
        var categoryField = chart.CategoryField?.Trim() ?? string.Empty;
        var valueColumns = (chart.ValueColumns ?? [])
            .Where(static column => !string.IsNullOrWhiteSpace(column))
            .Select(static column => column.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (categoryField.Length == 0)
        {
            warnings.Add($"Chart for grid '{grid.GridId}' has no category field; chart was not rendered.");
        }

        if (valueColumns.Length == 0)
        {
            warnings.Add($"Chart for grid '{grid.GridId}' has no value columns; chart was not rendered.");
        }

        if (categoryField.Length == 0 || valueColumns.Length == 0)
        {
            return new ReportWriterChartRenderDto(chart.Type, title, categoryField, [], [], warnings.ToArray());
        }

        var labelByKey = columns
            .GroupBy(static column => column.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Label, StringComparer.OrdinalIgnoreCase);
        var categories = rows
            .Select(row => row.Values.TryGetValue(categoryField, out var value) ? value : string.Empty)
            .ToArray();
        var series = new List<ReportWriterChartSeriesDto>(valueColumns.Length);
        foreach (var column in valueColumns)
        {
            if (!labelByKey.ContainsKey(column))
            {
                warnings.Add($"Chart value column '{column}' is not a rendered grid column; series read raw row values.");
            }

            var values = rows
                .Select(row =>
                    row.Values.TryGetValue(column, out var raw) &&
                    decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                        ? number
                        : 0m)
                .ToArray();
            series.Add(new ReportWriterChartSeriesDto(column, labelByKey.TryGetValue(column, out var label) ? label : column, values));
        }

        return new ReportWriterChartRenderDto(chart.Type, title, categoryField, categories, series, warnings.ToArray());
    }

    private static ReportWriterFormatRuleDto[] NormalizeFormatRules(IReadOnlyList<ReportWriterFormatRuleDto>? rules) =>
        rules?
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.Column))
            .Select(static rule => rule with
            {
                Column = rule.Column.Trim(),
                Value = string.IsNullOrWhiteSpace(rule.Value) ? null : rule.Value.Trim(),
                Label = string.IsNullOrWhiteSpace(rule.Label) ? null : rule.Label.Trim()
            })
            .ToArray() ?? [];

    private static IReadOnlyList<ReportWriterGridRowDto> RenderDetailRows(
        ReportWriterGridDefinitionDto grid,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<ReportWriterMetricDefinitionDto> metrics,
        IReadOnlyList<ReportWriterFormulaDefinitionDto> formulas,
        IReadOnlySet<string> circularFormulas,
        ISet<string> warnings)
    {
        var fieldTotals = BuildSourceTotals(rows, metrics.Select(metric => metric.SourceField), warnings);
        var output = new List<WorkingRow>(rows.Count);

        foreach (var row in rows)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var numericValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var dimension in dimensions)
            {
                values[dimension] = GetValue(row, dimension);
            }

            foreach (var metric in metrics)
            {
                var value = TryGetDecimal(row, metric.SourceField, warnings);
                values[metric.Name] = value is null ? string.Empty : FormatDecimal(value.Value);
                if (value is not null)
                {
                    numericValues[metric.Name] = value.Value;
                }
            }

            output.Add(new WorkingRow(values, numericValues));
        }

        ApplyFormulas(output, formulas, circularFormulas, fieldTotals, warnings);
        return output
            .Select((row, index) => new ReportWriterGridRowDto(BuildRowKey(row.Values, dimensions, index), row.Values))
            .ToArray();
    }

    private static IReadOnlyList<ReportWriterGridRowDto> RenderPivotRows(
        ReportWriterGridDefinitionDto grid,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyList<string> rowDimensions,
        IReadOnlyList<string> columnDimensions,
        IReadOnlyList<PivotColumn> pivotColumns,
        IReadOnlyList<ReportWriterMetricDefinitionDto> metrics,
        IReadOnlyList<ReportWriterFormulaDefinitionDto> formulas,
        IReadOnlySet<string> circularFormulas,
        ISet<string> warnings)
    {
        var groups = new Dictionary<string, PivotAggregateGroup>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var rowValues = rowDimensions
                .ToDictionary(dimension => dimension, dimension => GetValue(row, dimension), StringComparer.OrdinalIgnoreCase);
            var groupKey = BuildGroupKey(rowValues, rowDimensions);
            if (!groups.TryGetValue(groupKey, out var group))
            {
                group = new PivotAggregateGroup(rowValues, metrics);
                groups[groupKey] = group;
            }

            var pivotValues = columnDimensions
                .ToDictionary(dimension => dimension, dimension => GetValue(row, dimension), StringComparer.OrdinalIgnoreCase);
            var pivotKey = BuildGroupKey(pivotValues, columnDimensions);
            foreach (var metric in metrics)
            {
                group.Add(metric, pivotKey, row, warnings);
            }
        }

        var workingRows = groups.Values
            .Select(group => group.ToWorkingRow(metrics, pivotColumns))
            .ToList();
        var metricTotals = BuildMetricTotals(workingRows);
        ApplyFormulas(workingRows, formulas, circularFormulas, metricTotals, warnings);
        var sorted = SortRows(workingRows, grid, metrics);

        return sorted
            .Select((row, index) => new ReportWriterGridRowDto(BuildRowKey(row.Values, rowDimensions, index), row.Values))
            .ToArray();
    }

    private static IReadOnlyList<ReportWriterGridRowDto> RenderAggregateRows(
        ReportWriterGridDefinitionDto grid,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<ReportWriterMetricDefinitionDto> metrics,
        IReadOnlyList<ReportWriterFormulaDefinitionDto> formulas,
        IReadOnlySet<string> circularFormulas,
        ISet<string> warnings)
    {
        var groups = new Dictionary<string, AggregateGroup>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var groupValues = dimensions
                .ToDictionary(dimension => dimension, dimension => GetValue(row, dimension), StringComparer.OrdinalIgnoreCase);
            var groupKey = BuildGroupKey(groupValues, dimensions);
            if (!groups.TryGetValue(groupKey, out var group))
            {
                group = new AggregateGroup(groupValues, metrics);
                groups[groupKey] = group;
            }

            foreach (var metric in metrics)
            {
                group.Add(metric, row, warnings);
            }
        }

        var workingRows = groups.Values
            .Select(group => group.ToWorkingRow(metrics))
            .ToList();

        if (grid.Kind == ReportWriterGridKindDto.Contribution && metrics.Count > 0)
        {
            ApplyContribution(workingRows, metrics[0].Name);
        }

        var metricTotals = BuildMetricTotals(workingRows);
        ApplyFormulas(workingRows, formulas, circularFormulas, metricTotals, warnings);
        var sorted = SortRows(workingRows, grid, metrics);
        var limited = grid.Kind == ReportWriterGridKindDto.TopN && grid.TopN is > 0
            ? sorted.Take(grid.TopN.Value)
            : sorted;

        return limited
            .Select((row, index) => new ReportWriterGridRowDto(BuildRowKey(row.Values, dimensions, index), row.Values))
            .ToArray();
    }

    private static IReadOnlyList<ReportWriterGridColumnDto> BuildColumns(
        IReadOnlyList<string> dimensions,
        IReadOnlyList<ReportWriterMetricDefinitionDto> metrics,
        IReadOnlyList<ReportWriterFormulaDefinitionDto> formulas,
        bool includeContribution)
    {
        var columns = new List<ReportWriterGridColumnDto>(dimensions.Count + metrics.Count + formulas.Count + 1);
        columns.AddRange(dimensions.Select(field => new ReportWriterGridColumnDto(field, field, "dimension")));
        columns.AddRange(metrics.Select(metric => new ReportWriterGridColumnDto(metric.Name, metric.Label ?? metric.Name, "metric")));
        if (includeContribution)
        {
            columns.Add(new ReportWriterGridColumnDto("contributionPercent", "Contribution %", "formula"));
            columns.Add(new ReportWriterGridColumnDto("contributionAbsPercent", "Abs Contribution %", "formula"));
        }

        columns.AddRange(formulas.Select(formula => new ReportWriterGridColumnDto(formula.Name, formula.Label ?? formula.Name, "formula")));
        return columns;
    }

    private static IReadOnlyList<ReportWriterGridColumnDto> BuildPivotRenderColumns(
        IReadOnlyList<string> rowDimensions,
        IReadOnlyList<PivotColumn> pivotColumns,
        IReadOnlyList<ReportWriterMetricDefinitionDto> metrics,
        IReadOnlyList<ReportWriterFormulaDefinitionDto> formulas)
    {
        var columns = new List<ReportWriterGridColumnDto>(rowDimensions.Count + (pivotColumns.Count * metrics.Count) + formulas.Count);
        columns.AddRange(rowDimensions.Select(field => new ReportWriterGridColumnDto(field, field, "dimension")));
        foreach (var pivotColumn in pivotColumns)
        {
            columns.AddRange(metrics.Select(metric =>
            {
                var key = BuildPivotMetricKey(pivotColumn.Key, metric.Name);
                var metricLabel = metric.Label ?? metric.Name;
                return new ReportWriterGridColumnDto(key, $"{pivotColumn.Label} {metricLabel}", "metric");
            }));
        }

        columns.AddRange(formulas.Select(formula => new ReportWriterGridColumnDto(formula.Name, formula.Label ?? formula.Name, "formula")));
        return columns;
    }

    private static IReadOnlyList<PivotColumn> BuildPivotColumns(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyList<string> columnDimensions)
    {
        var columns = new List<PivotColumn>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var values = columnDimensions
                .ToDictionary(dimension => dimension, dimension => GetValue(row, dimension), StringComparer.OrdinalIgnoreCase);
            var key = BuildGroupKey(values, columnDimensions);
            if (!seen.Add(key))
            {
                continue;
            }

            columns.Add(new PivotColumn(key, BuildPivotLabel(values, columnDimensions)));
        }

        return columns;
    }

    private static string BuildPivotMetricKey(string pivotKey, string metricName) =>
        $"{NormalizePivotKeySegment(pivotKey)}:{metricName}";

    private static string BuildPivotLabel(
        IReadOnlyDictionary<string, string> values,
        IEnumerable<string> columnDimensions)
    {
        var label = string.Join(" / ", columnDimensions.Select(dimension =>
        {
            var value = values.TryGetValue(dimension, out var fieldValue) ? fieldValue : string.Empty;
            return string.IsNullOrWhiteSpace(value) ? "(blank)" : value;
        }));
        return string.IsNullOrWhiteSpace(label) ? "(blank)" : label;
    }

    private static string NormalizePivotKeySegment(string pivotKey) =>
        string.IsNullOrWhiteSpace(pivotKey) ? "(blank)" : pivotKey;

    private static ReportWriterGridLineageDto BuildLineage(
        int inputRowCount,
        int filteredInputRowCount,
        int outputRowCount,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<ReportWriterMetricDefinitionDto> metrics,
        IReadOnlyList<ReportWriterFormulaDefinitionDto> formulas,
        IReadOnlyList<ReportWriterFilterDefinitionDto> filters)
    {
        var formulaLineage = formulas
            .Select(formula => new ReportWriterFormulaLineageDto(
                formula.Name,
                formula.Expression,
                ExtractFormulaSourceFields(formula.Expression)))
            .ToArray();
        var filterLineage = filters
            .Select(static filter => new ReportWriterFilterLineageDto(
                filter.Field,
                filter.Operator.ToString(),
                filter.Value,
                filter.Label))
            .ToArray();
        var sourceFields = dimensions
            .Concat(metrics.Select(static metric => metric.SourceField))
            .Concat(formulaLineage.SelectMany(static formula => formula.SourceFields))
            .Concat(filterLineage.Select(static filter => filter.Field))
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Select(static field => field.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static field => field, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var metricLineage = metrics
            .Select(static metric => new ReportWriterMetricLineageDto(
                metric.Name,
                metric.SourceField,
                metric.Function.ToString()))
            .ToArray();

        return new ReportWriterGridLineageDto(
            inputRowCount,
            outputRowCount,
            sourceFields,
            metricLineage,
            formulaLineage,
            filteredInputRowCount,
            filterLineage);
    }

    private static IReadOnlyList<ReportWriterGridDataDictionaryFieldDto> BuildDataDictionary(
        IReadOnlyList<ReportWriterGridColumnDto> columns,
        IReadOnlyList<ReportWriterGridRowDto> rows,
        ReportWriterGridLineageDto lineage)
    {
        var sourceFields = lineage.SourceFields;
        return columns
            .Select(column =>
            {
                var sourceField = ResolveSourceField(column, lineage);
                var formulaColumn = string.Equals(column.Role, "formula", StringComparison.OrdinalIgnoreCase);
                var formulaHasLineage = IsFormulaLineageColumn(column, lineage);
                var generated = formulaColumn
                    || string.IsNullOrWhiteSpace(sourceField)
                    || !sourceFields.Contains(sourceField, StringComparer.OrdinalIgnoreCase);
                var dataType = InferColumnDataType(column.Key, rows);
                return new ReportWriterGridDataDictionaryFieldDto(
                    column.Key,
                    column.Label,
                    column.Role,
                    generated && !formulaHasLineage ? string.Empty : sourceField,
                    dataType,
                    generated,
                    BuildFieldDescription(column, dataType, generated, sourceField));
            })
            .ToArray();
    }

    private static IReadOnlyList<ReportWriterGridValidationCheckDto> BuildValidationChecks(
        IReadOnlyList<ReportWriterGridColumnDto> columns,
        IReadOnlyList<ReportWriterGridRowDto> rows,
        IReadOnlyCollection<string> warnings,
        ReportWriterGridLineageDto lineage,
        IReadOnlyList<ReportWriterGridDataDictionaryFieldDto> dataDictionary) =>
        [
            new(
                "row-count",
                lineage.OutputRowCount == rows.Count ? "Passed" : "Failed",
                $"Rendered {rows.Count.ToString(CultureInfo.InvariantCulture)} row(s); lineage expected {lineage.OutputRowCount.ToString(CultureInfo.InvariantCulture)}."),
            new(
                "column-dictionary",
                columns.Count == dataDictionary.Count ? "Passed" : "Failed",
                $"Dictionary covers {dataDictionary.Count.ToString(CultureInfo.InvariantCulture)} of {columns.Count.ToString(CultureInfo.InvariantCulture)} rendered column(s)."),
            new(
                "source-field-lineage",
                lineage.SourceFields.Count > 0 ? "Passed" : "Warning",
                lineage.SourceFields.Count > 0
                    ? $"Lineage retains {lineage.SourceFields.Count.ToString(CultureInfo.InvariantCulture)} source field(s)."
                    : "No source field lineage was retained with this grid."),
            new(
                "warnings",
                warnings.Count == 0 ? "Passed" : "Warning",
                warnings.Count == 0
                    ? "No render warnings were retained."
                    : $"{warnings.Count.ToString(CultureInfo.InvariantCulture)} render warning(s) retained: {string.Join("; ", warnings)}")
        ];

    private static bool IsFormulaLineageColumn(
        ReportWriterGridColumnDto column,
        ReportWriterGridLineageDto lineage) =>
        string.Equals(column.Role, "formula", StringComparison.OrdinalIgnoreCase)
        && lineage.Formulas.Any(formula => string.Equals(formula.Name, column.Key, StringComparison.OrdinalIgnoreCase));

    private static string ResolveSourceField(
        ReportWriterGridColumnDto column,
        ReportWriterGridLineageDto lineage)
    {
        if (lineage.Metrics.FirstOrDefault(metric =>
                string.Equals(metric.Name, column.Key, StringComparison.OrdinalIgnoreCase)) is { } metric)
        {
            return metric.SourceField;
        }

        if (lineage.Formulas.FirstOrDefault(formula =>
                string.Equals(formula.Name, column.Key, StringComparison.OrdinalIgnoreCase)) is { } formula)
        {
            return string.Join(";", formula.SourceFields);
        }

        return lineage.SourceFields.FirstOrDefault(field =>
            string.Equals(field, column.Key, StringComparison.OrdinalIgnoreCase)) ?? column.Key;
    }

    private static string InferColumnDataType(
        string columnKey,
        IReadOnlyList<ReportWriterGridRowDto> rows)
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
        string sourceField) =>
        generated
            ? $"{column.Label} is generated by the retained report-writer grid as a {column.Role} column."
            : $"{column.Label} maps to source field {sourceField} and exports as {dataType}.";

    private static IReadOnlyList<string> ExtractFormulaSourceFields(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return [];
        }

        var fields = new List<string>();
        var position = 0;
        while (position < expression.Length)
        {
            if (IsIdentifierStart(expression[position]))
            {
                var identifierStart = position;
                var identifier = ReadIdentifier(expression, ref position);
                var nextToken = SkipWhitespace(expression, position);
                if (string.Equals(identifier, "total", StringComparison.OrdinalIgnoreCase)
                    && nextToken < expression.Length
                    && expression[nextToken] == '('
                    && TryReadTotalArgument(expression, nextToken + 1, out var totalReference, out var afterTotal))
                {
                    fields.Add(totalReference);
                    position = afterTotal;
                    continue;
                }

                if (IsFormulaFunctionIdentifier(identifier)
                    && nextToken < expression.Length
                    && expression[nextToken] == '(')
                {
                    position = nextToken + 1;
                    continue;
                }

                fields.Add(identifier);
                position = identifierStart + Math.Max(identifier.Length, 1);
                continue;
            }

            if (expression[position] != '{')
            {
                position++;
                continue;
            }

            var end = expression.IndexOf('}', position + 1);
            if (end < 0)
            {
                break;
            }

            var field = expression[(position + 1)..end].Trim();
            if (field.Length > 0)
            {
                fields.Add(field);
            }

            position = end + 1;
        }

        return fields
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static field => field, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryReadTotalArgument(
        string expression,
        int argumentStart,
        out string reference,
        out int nextPosition)
    {
        reference = string.Empty;
        nextPosition = argumentStart;
        var start = SkipWhitespace(expression, argumentStart);
        if (start >= expression.Length)
        {
            return false;
        }

        if (expression[start] == '{')
        {
            var closeBrace = expression.IndexOf('}', start + 1);
            if (closeBrace < 0)
            {
                return false;
            }

            reference = expression[(start + 1)..closeBrace].Trim();
            nextPosition = SkipWhitespace(expression, closeBrace + 1);
            if (nextPosition < expression.Length && expression[nextPosition] == ')')
            {
                nextPosition++;
                return reference.Length > 0;
            }

            return false;
        }

        var close = expression.IndexOf(')', start);
        if (close < 0)
        {
            return false;
        }

        reference = expression[start..close].Trim();
        nextPosition = close + 1;
        return reference.Length > 0;
    }

    private static string ReadIdentifier(string expression, ref int position)
    {
        var start = position;
        while (position < expression.Length && IsIdentifierPart(expression[position]))
        {
            position++;
        }

        return expression[start..position];
    }

    private static int SkipWhitespace(string expression, int position)
    {
        while (position < expression.Length && char.IsWhiteSpace(expression[position]))
        {
            position++;
        }

        return position;
    }

    private static bool IsIdentifierStart(char value) =>
        char.IsLetter(value) || value == '_';

    private static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-' or '.';

    private static bool IsFormulaFunctionIdentifier(string identifier) =>
        FormulaEvaluator.IsFunctionName(identifier);

    private static void ApplyContribution(IReadOnlyList<WorkingRow> rows, string metricName)
    {
        var total = rows.Sum(row => row.NumericValues.TryGetValue(metricName, out var value) ? Math.Abs(value) : 0m);
        foreach (var row in rows)
        {
            var value = row.NumericValues.TryGetValue(metricName, out var metricValue) ? metricValue : 0m;
            var contribution = total == 0m ? 0m : value / total * 100m;
            var absoluteContribution = Math.Abs(contribution);
            row.NumericValues["contributionPercent"] = contribution;
            row.NumericValues["contributionAbsPercent"] = absoluteContribution;
            row.Values["contributionPercent"] = FormatDecimal(contribution);
            row.Values["contributionAbsPercent"] = FormatDecimal(absoluteContribution);
        }
    }

    /// <summary>
    /// Determines which formulas participate in (or transitively depend on) a circular reference
    /// among the grid's own formulas, so those formulas are skipped instead of resolving to
    /// misleading empty/partial values or masking the cycle as a generic "field not found" warning.
    /// </summary>
    private static IReadOnlySet<string> DetectCircularFormulas(
        ReportWriterGridDefinitionDto grid,
        IReadOnlyList<ReportWriterFormulaDefinitionDto> formulas,
        ISet<string> warnings)
    {
        if (formulas.Count == 0)
        {
            return FrozenSet<string>.Empty;
        }

        var formulaNames = formulas
            .Select(static formula => formula.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Edges point from a formula to the sibling formulas it references (self-references included).
        // Duplicate formula names are tolerated the same way the rest of the engine tolerates them
        // (last definition wins) rather than throwing.
        var dependencies = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var formula in formulas)
        {
            dependencies[formula.Name] = ExtractFormulaSourceFields(formula.Expression)
                .Where(formulaNames.Contains)
                .ToArray();
        }

        // Kahn-style resolution: a formula is resolvable once every sibling formula it depends on is
        // resolvable. Whatever never resolves is inside a cycle or downstream of one.
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool changed;
        do
        {
            changed = false;
            foreach (var name in formulaNames)
            {
                if (resolved.Contains(name))
                {
                    continue;
                }

                if (dependencies[name].All(resolved.Contains))
                {
                    resolved.Add(name);
                    changed = true;
                }
            }
        }
        while (changed);

        var circular = formulaNames
            .Where(name => !resolved.Contains(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in circular.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add($"Formula '{name}' in grid '{grid.GridId}' is part of a circular reference and was not evaluated.");
        }

        return circular;
    }

    private static void ApplyFormulas(
        IReadOnlyList<WorkingRow> rows,
        IReadOnlyList<ReportWriterFormulaDefinitionDto> formulas,
        IReadOnlySet<string> circularFormulas,
        IReadOnlyDictionary<string, decimal> totals,
        ISet<string> warnings)
    {
        foreach (var row in rows)
        {
            foreach (var formula in formulas)
            {
                if (circularFormulas.Contains(formula.Name))
                {
                    row.Values[formula.Name] = string.Empty;
                    continue;
                }

                try
                {
                    var evaluator = new FormulaEvaluator(
                        formula.Expression,
                        field => ResolveRowNumber(row, field),
                        field => totals.TryGetValue(field, out var value) ? value : null);
                    var value = evaluator.Evaluate();
                    row.NumericValues[formula.Name] = value;
                    row.Values[formula.Name] = FormatDecimal(value);
                }
                catch (Exception ex) when (ex is InvalidOperationException or DivideByZeroException)
                {
                    warnings.Add($"Formula '{formula.Name}' could not be evaluated: {ex.Message}");
                    row.Values[formula.Name] = string.Empty;
                }
            }
        }
    }

    private static IEnumerable<WorkingRow> SortRows(
        IReadOnlyList<WorkingRow> rows,
        ReportWriterGridDefinitionDto grid,
        IReadOnlyList<ReportWriterMetricDefinitionDto> metrics)
    {
        var sortBy = string.IsNullOrWhiteSpace(grid.SortBy)
            ? grid.Kind == ReportWriterGridKindDto.Contribution
                ? "contributionAbsPercent"
                : metrics.FirstOrDefault()?.Name
            : grid.SortBy.Trim();
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return rows;
        }

        return grid.SortDescending
            ? rows.OrderByDescending(row => SortValue(row, sortBy), NumericSortComparer.Instance)
                .ThenBy(row => BuildGroupKey(row.Values, row.Values.Keys), StringComparer.Ordinal)
            : rows.OrderBy(row => SortValue(row, sortBy), NumericSortComparer.Instance)
                .ThenBy(row => BuildGroupKey(row.Values, row.Values.Keys), StringComparer.Ordinal);
    }

    private static decimal? SortValue(WorkingRow row, string sortBy) =>
        row.NumericValues.TryGetValue(sortBy, out var value) ? value : null;

    private static decimal? ResolveRowNumber(WorkingRow row, string field)
    {
        if (row.NumericValues.TryGetValue(field, out var numeric))
        {
            return numeric;
        }

        return row.Values.TryGetValue(field, out var value)
               && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyDictionary<string, decimal> BuildSourceTotals(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IEnumerable<string> fields,
        ISet<string> warnings)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields.Where(static field => !string.IsNullOrWhiteSpace(field)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            totals[field] = rows.Sum(row => TryGetDecimal(row, field, warnings) ?? 0m);
        }

        return totals;
    }

    private static IReadOnlyDictionary<string, decimal> BuildMetricTotals(IEnumerable<WorkingRow> rows)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var value in row.NumericValues)
            {
                totals[value.Key] = totals.TryGetValue(value.Key, out var current)
                    ? current + value.Value
                    : value.Value;
            }
        }

        return totals;
    }

    private static IReadOnlyDictionary<string, string>[] NormalizeRows(
        IReadOnlyList<IReadOnlyDictionary<string, string>>? rows) =>
        rows?
            .Select(row => row
                .Where(static kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                .ToDictionary(kvp => kvp.Key.Trim(), kvp => kvp.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .Cast<IReadOnlyDictionary<string, string>>()
            .ToArray() ?? [];

    private static string[] NormalizeFields(IReadOnlyList<string>? fields) =>
        fields?
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Select(static field => field.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static ReportWriterMetricDefinitionDto[] NormalizeMetrics(IReadOnlyList<ReportWriterMetricDefinitionDto>? metrics) =>
        metrics?
            .Where(static metric => !string.IsNullOrWhiteSpace(metric.Name) && !string.IsNullOrWhiteSpace(metric.SourceField))
            .Select(static metric => metric with
            {
                Name = metric.Name.Trim(),
                SourceField = metric.SourceField.Trim(),
                Label = string.IsNullOrWhiteSpace(metric.Label) ? null : metric.Label.Trim()
            })
            .ToArray() ?? [];

    private static ReportWriterFormulaDefinitionDto[] NormalizeFormulas(IReadOnlyList<ReportWriterFormulaDefinitionDto>? formulas) =>
        formulas?
            .Where(static formula => !string.IsNullOrWhiteSpace(formula.Name) && !string.IsNullOrWhiteSpace(formula.Expression))
            .Select(static formula => formula with
            {
                Name = formula.Name.Trim(),
                Expression = formula.Expression.Trim(),
                Label = string.IsNullOrWhiteSpace(formula.Label) ? null : formula.Label.Trim()
            })
            .ToArray() ?? [];

    private static ReportWriterFilterDefinitionDto[] NormalizeFilters(IReadOnlyList<ReportWriterFilterDefinitionDto>? filters) =>
        filters?
            .Where(static filter => !string.IsNullOrWhiteSpace(filter.Field))
            .Select(static filter => filter with
            {
                Field = filter.Field.Trim(),
                Value = string.IsNullOrWhiteSpace(filter.Value) ? null : filter.Value.Trim(),
                Label = string.IsNullOrWhiteSpace(filter.Label) ? null : filter.Label.Trim()
            })
            .ToArray() ?? [];

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ApplyFilters(
        ReportWriterGridDefinitionDto grid,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyList<ReportWriterFilterDefinitionDto> filters,
        ISet<string> warnings)
    {
        if (filters.Count == 0 || rows.Count == 0)
        {
            return rows;
        }

        var filtered = rows
            .Where(row => filters.All(filter => MatchesFilter(row, filter, warnings)))
            .ToArray();
        if (filtered.Length == 0)
        {
            warnings.Add($"Grid '{grid.GridId}' filters removed all {rows.Count} dataset rows.");
        }

        return filtered;
    }

    private static bool MatchesFilter(
        IReadOnlyDictionary<string, string> row,
        ReportWriterFilterDefinitionDto filter,
        ISet<string> warnings)
    {
        var actual = GetValue(row, filter.Field);
        var expected = filter.Value ?? string.Empty;
        return filter.Operator switch
        {
            ReportWriterFilterOperatorDto.NotEquals => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            ReportWriterFilterOperatorDto.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            ReportWriterFilterOperatorDto.StartsWith => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            ReportWriterFilterOperatorDto.EndsWith => actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
            ReportWriterFilterOperatorDto.GreaterThan => CompareFilterNumbers(actual, expected, filter, warnings) is { } comparison && comparison > 0,
            ReportWriterFilterOperatorDto.GreaterThanOrEqual => CompareFilterNumbers(actual, expected, filter, warnings) is { } comparison && comparison >= 0,
            ReportWriterFilterOperatorDto.LessThan => CompareFilterNumbers(actual, expected, filter, warnings) is { } comparison && comparison < 0,
            ReportWriterFilterOperatorDto.LessThanOrEqual => CompareFilterNumbers(actual, expected, filter, warnings) is { } comparison && comparison <= 0,
            ReportWriterFilterOperatorDto.IsBlank => string.IsNullOrWhiteSpace(actual),
            ReportWriterFilterOperatorDto.IsNotBlank => !string.IsNullOrWhiteSpace(actual),
            _ => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static int? CompareFilterNumbers(
        string actual,
        string expected,
        ReportWriterFilterDefinitionDto filter,
        ISet<string> warnings)
    {
        if (decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var actualNumber) &&
            decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var expectedNumber))
        {
            return actualNumber.CompareTo(expectedNumber);
        }

        warnings.Add($"Filter '{filter.Field} {filter.Operator}' requires numeric values; non-numeric rows were excluded.");
        return null;
    }

    private static decimal? TryGetDecimal(
        IReadOnlyDictionary<string, string> row,
        string field,
        ISet<string> warnings)
    {
        if (string.Equals(field, "*", StringComparison.Ordinal))
        {
            return 1m;
        }

        if (!row.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        warnings.Add($"Field '{field}' contained non-numeric values; affected cells were skipped.");
        return null;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> row, string field) =>
        row.TryGetValue(field, out var value) ? value : string.Empty;

    private static string BuildRowKey(
        IReadOnlyDictionary<string, string> values,
        IEnumerable<string> dimensions,
        int index)
    {
        var key = BuildGroupKey(values, dimensions);
        return string.IsNullOrWhiteSpace(key) ? $"row-{index + 1}" : key;
    }

    private static string BuildGroupKey(
        IReadOnlyDictionary<string, string> values,
        IEnumerable<string> dimensions) =>
        string.Join("|", dimensions.Select(dimension => values.TryGetValue(dimension, out var value) ? value : string.Empty));

    private static string FormatDecimal(decimal value) =>
        ReportingNumberFormat.FormatDecimal(value);

    private sealed class AggregateGroup
    {
        private readonly Dictionary<string, MetricAccumulator> _metrics;

        public AggregateGroup(
            IReadOnlyDictionary<string, string> dimensionValues,
            IEnumerable<ReportWriterMetricDefinitionDto> metrics)
        {
            DimensionValues = new Dictionary<string, string>(dimensionValues, StringComparer.OrdinalIgnoreCase);
            _metrics = metrics.ToDictionary(
                metric => metric.Name,
                metric => new MetricAccumulator(metric.Function),
                StringComparer.OrdinalIgnoreCase);
        }

        private IReadOnlyDictionary<string, string> DimensionValues { get; }

        public void Add(
            ReportWriterMetricDefinitionDto metric,
            IReadOnlyDictionary<string, string> row,
            ISet<string> warnings)
        {
            if (_metrics.TryGetValue(metric.Name, out var accumulator))
            {
                accumulator.Add(TryGetDecimal(row, metric.SourceField, warnings), metric.SourceField);
            }
        }

        public WorkingRow ToWorkingRow(IReadOnlyList<ReportWriterMetricDefinitionDto> metrics)
        {
            var values = new Dictionary<string, string>(DimensionValues, StringComparer.OrdinalIgnoreCase);
            var numericValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var metric in metrics)
            {
                var value = _metrics[metric.Name].Result;
                values[metric.Name] = FormatDecimal(value);
                numericValues[metric.Name] = value;
            }

            return new WorkingRow(values, numericValues);
        }
    }

    private sealed class PivotAggregateGroup
    {
        private readonly Dictionary<string, MetricAccumulator> _rowMetrics;
        private readonly Dictionary<string, Dictionary<string, MetricAccumulator>> _pivotMetrics = new(StringComparer.Ordinal);

        public PivotAggregateGroup(
            IReadOnlyDictionary<string, string> dimensionValues,
            IEnumerable<ReportWriterMetricDefinitionDto> metrics)
        {
            DimensionValues = new Dictionary<string, string>(dimensionValues, StringComparer.OrdinalIgnoreCase);
            _rowMetrics = metrics.ToDictionary(
                metric => metric.Name,
                metric => new MetricAccumulator(metric.Function),
                StringComparer.OrdinalIgnoreCase);
        }

        private IReadOnlyDictionary<string, string> DimensionValues { get; }

        public void Add(
            ReportWriterMetricDefinitionDto metric,
            string pivotKey,
            IReadOnlyDictionary<string, string> row,
            ISet<string> warnings)
        {
            var value = TryGetDecimal(row, metric.SourceField, warnings);
            if (_rowMetrics.TryGetValue(metric.Name, out var rowAccumulator))
            {
                rowAccumulator.Add(value, metric.SourceField);
            }

            if (!_pivotMetrics.TryGetValue(pivotKey, out var pivotMetricAccumulators))
            {
                pivotMetricAccumulators = new Dictionary<string, MetricAccumulator>(StringComparer.OrdinalIgnoreCase);
                _pivotMetrics[pivotKey] = pivotMetricAccumulators;
            }

            if (!pivotMetricAccumulators.TryGetValue(metric.Name, out var pivotAccumulator))
            {
                pivotAccumulator = new MetricAccumulator(metric.Function);
                pivotMetricAccumulators[metric.Name] = pivotAccumulator;
            }

            pivotAccumulator.Add(value, metric.SourceField);
        }

        public WorkingRow ToWorkingRow(
            IReadOnlyList<ReportWriterMetricDefinitionDto> metrics,
            IReadOnlyList<PivotColumn> pivotColumns)
        {
            var values = new Dictionary<string, string>(DimensionValues, StringComparer.OrdinalIgnoreCase);
            var numericValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var metric in metrics)
            {
                var value = _rowMetrics[metric.Name].Result;
                values[metric.Name] = FormatDecimal(value);
                numericValues[metric.Name] = value;
            }

            foreach (var pivotColumn in pivotColumns)
            {
                _pivotMetrics.TryGetValue(pivotColumn.Key, out var pivotMetricAccumulators);
                foreach (var metric in metrics)
                {
                    var key = BuildPivotMetricKey(pivotColumn.Key, metric.Name);
                    var value = pivotMetricAccumulators is not null &&
                                pivotMetricAccumulators.TryGetValue(metric.Name, out var accumulator)
                        ? accumulator.Result
                        : 0m;
                    values[key] = FormatDecimal(value);
                    numericValues[key] = value;
                }
            }

            return new WorkingRow(values, numericValues);
        }
    }

    private sealed record PivotColumn(
        string Key,
        string Label);

    private sealed class MetricAccumulator
    {
        private readonly ReportWriterAggregateFunctionDto _function;
        private decimal _sum;
        private decimal? _min;
        private decimal? _max;
        private int _numericCount;
        private int _rowCount;

        public MetricAccumulator(ReportWriterAggregateFunctionDto function)
        {
            _function = function;
        }

        public decimal Result => _function switch
        {
            ReportWriterAggregateFunctionDto.Count => _rowCount,
            ReportWriterAggregateFunctionDto.Average => _numericCount == 0 ? 0m : _sum / _numericCount,
            ReportWriterAggregateFunctionDto.Min => _min ?? 0m,
            ReportWriterAggregateFunctionDto.Max => _max ?? 0m,
            _ => _sum
        };

        public void Add(decimal? value, string sourceField)
        {
            if (string.Equals(sourceField, "*", StringComparison.Ordinal))
            {
                _rowCount++;
                _numericCount++;
                _sum += 1m;
                _min = _min is null || 1m < _min.Value ? 1m : _min;
                _max = _max is null || 1m > _max.Value ? 1m : _max;
                return;
            }

            if (value is null)
            {
                return;
            }

            _rowCount++;
            _numericCount++;
            _sum += value.Value;
            _min = _min is null || value.Value < _min.Value ? value.Value : _min;
            _max = _max is null || value.Value > _max.Value ? value.Value : _max;
        }
    }

    private sealed class NumericSortComparer : IComparer<decimal?>
    {
        public static NumericSortComparer Instance { get; } = new();

        public int Compare(decimal? x, decimal? y)
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            return x.Value.CompareTo(y.Value);
        }
    }

    private sealed record WorkingRow(
        Dictionary<string, string> Values,
        Dictionary<string, decimal> NumericValues);

    private sealed class FormulaEvaluator
    {
        private readonly string _expression;
        private readonly Func<string, decimal?> _valueResolver;
        private readonly Func<string, decimal?> _totalResolver;
        private int _position;

        public FormulaEvaluator(
            string expression,
            Func<string, decimal?> valueResolver,
            Func<string, decimal?> totalResolver)
        {
            _expression = expression;
            _valueResolver = valueResolver;
            _totalResolver = totalResolver;
        }

        public decimal Evaluate()
        {
            var value = ParseExpression();
            SkipWhitespace();
            if (_position != _expression.Length)
            {
                throw new InvalidOperationException($"unexpected token at position {_position}.");
            }

            return value;
        }

        private decimal ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('+'))
                {
                    value += ParseTerm();
                }
                else if (TryConsume('-'))
                {
                    value -= ParseTerm();
                }
                else
                {
                    return value;
                }
            }
        }

        private decimal ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('*'))
                {
                    value *= ParseFactor();
                }
                else if (TryConsume('/'))
                {
                    var divisor = ParseFactor();
                    if (divisor == 0m)
                    {
                        // Raw '/' surfaces division by zero as a warning (see ApplyFormulas). Callers
                        // who want a silent fallback must use safeDivide/percent/basisPoints, whose
                        // zero handling is centralized in DivideOrFallback.
                        throw new DivideByZeroException("formula attempted division by zero.");
                    }

                    value /= divisor;
                }
                else
                {
                    return value;
                }
            }
        }

        private decimal ParseFactor()
        {
            SkipWhitespace();
            if (TryConsume('+'))
            {
                return ParseFactor();
            }

            if (TryConsume('-'))
            {
                return -ParseFactor();
            }

            if (TryConsume('('))
            {
                var value = ParseExpression();
                Expect(')');
                return value;
            }

            if (Peek() == '{')
            {
                return ResolveValue(ParseBraceReference());
            }

            if (char.IsDigit(Peek()))
            {
                return ParseNumber();
            }

            var identifier = ParseIdentifier();
            if (identifier.Length == 0)
            {
                throw new InvalidOperationException($"expected number or field reference at position {_position}.");
            }

            SkipWhitespace();
            if (TryConsume('('))
            {
                return EvaluateFunction(identifier);
            }

            return ResolveValue(identifier);
        }

        // Single source of truth for formula functions. Adding a function means adding one entry
        // here — recognition (lineage extraction), dispatch, and evaluation all read from this map.
        private static readonly FrozenDictionary<string, Func<FormulaEvaluator, decimal>> Functions =
            new Dictionary<string, Func<FormulaEvaluator, decimal>>(StringComparer.OrdinalIgnoreCase)
            {
                ["total"] = static self => self.EvaluateTotal(),
                ["abs"] = static self => self.EvaluateAbs(),
                ["min"] = static self => self.EvaluateMin(),
                ["max"] = static self => self.EvaluateMax(),
                ["safeDivide"] = static self => self.EvaluateSafeDivide(),
                ["percent"] = static self => self.EvaluateRatio(100m),
                ["basisPoints"] = static self => self.EvaluateRatio(10000m),
                ["round"] = static self => self.EvaluateRound(),
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        public static bool IsFunctionName(string identifier) => Functions.ContainsKey(identifier);

        private decimal EvaluateFunction(string identifier) =>
            Functions.TryGetValue(identifier, out var evaluator)
                ? evaluator(this)
                : throw new InvalidOperationException($"function '{identifier}' is not supported.");

        private decimal EvaluateTotal()
        {
            var field = ParseFieldArgument();
            Expect(')');
            return _totalResolver(field)
                   ?? throw new InvalidOperationException($"total field '{field}' was not found.");
        }

        private decimal EvaluateAbs()
        {
            var value = ParseExpression();
            Expect(')');
            return Math.Abs(value);
        }

        private decimal EvaluateMin() => ParseExpressionArgumentList(minimumCount: 1).Min();

        private decimal EvaluateMax() => ParseExpressionArgumentList(minimumCount: 1).Max();

        private decimal EvaluateSafeDivide()
        {
            var values = ParseExpressionArgumentList(minimumCount: 2, maximumCount: 3);
            return DivideOrFallback(values[0], values[1], values.Count == 3 ? values[2] : 0m);
        }

        private decimal EvaluateRatio(decimal scale)
        {
            var values = ParseExpressionArgumentList(minimumCount: 2, maximumCount: 3);
            var ratio = DivideOrFallback(values[0], values[1], values.Count == 3 ? values[2] : 0m);
            return ratio * scale;
        }

        private decimal EvaluateRound()
        {
            var values = ParseExpressionArgumentList(minimumCount: 1, maximumCount: 2);
            var decimals = values.Count == 2 ? ToScale(values[1]) : 2;
            return Math.Round(values[0], decimals, MidpointRounding.AwayFromZero);
        }

        // Centralized zero-denominator policy for the named "safe" division functions
        // (safeDivide/percent/basisPoints): a zero denominator yields the caller-supplied fallback
        // (defaulting to 0) rather than throwing. This is the deliberate counterpart to the raw
        // '/' operator, which surfaces division by zero as a DivideByZeroException so the grid records
        // a formula warning. The two paths are intentionally different and both defined in one place.
        private static decimal DivideOrFallback(decimal numerator, decimal denominator, decimal fallbackWhenZeroDenominator) =>
            denominator == 0m ? fallbackWhenZeroDenominator : numerator / denominator;

        private static int ToScale(decimal value)
        {
            if (value < 0m || value > 8m || value != decimal.Truncate(value))
            {
                throw new InvalidOperationException("round scale must be a whole number between 0 and 8.");
            }

            return decimal.ToInt32(value);
        }

        private List<decimal> ParseExpressionArgumentList(int minimumCount, int? maximumCount = null)
        {
            var values = new List<decimal>();
            while (true)
            {
                values.Add(ParseExpression());
                SkipWhitespace();
                if (!TryConsume(','))
                {
                    break;
                }
            }

            Expect(')');
            if (values.Count < minimumCount)
            {
                throw new InvalidOperationException($"function expected at least {minimumCount.ToString(CultureInfo.InvariantCulture)} argument(s).");
            }

            if (maximumCount is { } max && values.Count > max)
            {
                throw new InvalidOperationException($"function expected no more than {max.ToString(CultureInfo.InvariantCulture)} argument(s).");
            }

            return values;
        }

        private decimal ResolveValue(string field) =>
            _valueResolver(field)
            ?? throw new InvalidOperationException($"field '{field}' was not found or was not numeric.");

        private decimal ParseNumber()
        {
            var start = _position;
            while (_position < _expression.Length &&
                   (char.IsDigit(_expression[_position]) || _expression[_position] == '.'))
            {
                _position++;
            }

            var token = _expression[start.._position];
            return decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new InvalidOperationException($"number '{token}' is invalid.");
        }

        private string ParseBraceReference()
        {
            Expect('{');
            var start = _position;
            while (_position < _expression.Length && _expression[_position] != '}')
            {
                _position++;
            }

            if (_position >= _expression.Length)
            {
                throw new InvalidOperationException("field reference is missing a closing brace.");
            }

            var field = _expression[start.._position].Trim();
            Expect('}');
            if (field.Length == 0)
            {
                throw new InvalidOperationException("field reference cannot be empty.");
            }

            return field;
        }

        private string ParseFieldArgument()
        {
            SkipWhitespace();
            if (Peek() == '{')
            {
                return ParseBraceReference();
            }

            var start = _position;
            while (_position < _expression.Length && _expression[_position] != ')')
            {
                _position++;
            }

            var field = _expression[start.._position].Trim();
            if (field.Length == 0)
            {
                throw new InvalidOperationException("total field argument cannot be empty.");
            }

            return field;
        }

        private string ParseIdentifier()
        {
            var start = _position;
            while (_position < _expression.Length)
            {
                var c = _expression[_position];
                if (!char.IsLetterOrDigit(c) && c is not '_' and not '-' and not '.')
                {
                    break;
                }

                _position++;
            }

            return _expression[start.._position];
        }

        private void Expect(char expected)
        {
            SkipWhitespace();
            if (!TryConsume(expected))
            {
                throw new InvalidOperationException($"expected '{expected}' at position {_position}.");
            }
        }

        private bool TryConsume(char expected)
        {
            if (Peek() != expected)
            {
                return false;
            }

            _position++;
            return true;
        }

        private char Peek() => _position < _expression.Length ? _expression[_position] : '\0';

        private void SkipWhitespace()
        {
            while (_position < _expression.Length && char.IsWhiteSpace(_expression[_position]))
            {
                _position++;
            }
        }
    }
}
