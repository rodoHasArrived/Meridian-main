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
        var dimensions = NormalizeFields(grid.RowFields)
            .Concat(NormalizeFields(grid.ColumnFields))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var metrics = NormalizeMetrics(grid.Metrics);
        var formulas = NormalizeFormulas(grid.Formulas);
        var filters = NormalizeFilters(grid.Filters);

        if (rows.Count == 0)
        {
            warnings.Add($"Grid '{grid.GridId}' has no dataset rows to render.");
        }

        var filteredRows = ApplyFilters(grid, rows, filters, warnings);
        var columnList = BuildColumns(dimensions, metrics, formulas, includeContribution: grid.Kind == ReportWriterGridKindDto.Contribution);
        var renderedRows = grid.Kind == ReportWriterGridKindDto.Detail
            ? RenderDetailRows(grid, filteredRows, dimensions, metrics, formulas, warnings)
            : RenderAggregateRows(grid, filteredRows, dimensions, metrics, formulas, warnings);
        var lineage = BuildLineage(rows.Count, filteredRows.Count, renderedRows.Count, dimensions, metrics, formulas, filters);

        return new ReportWriterGridRenderDto(
            grid.GridId.Trim(),
            string.IsNullOrWhiteSpace(grid.Title) ? grid.GridId.Trim() : grid.Title.Trim(),
            grid.Kind,
            columnList,
            renderedRows,
            warnings.ToArray(),
            lineage);
    }

    private static IReadOnlyList<ReportWriterGridRowDto> RenderDetailRows(
        ReportWriterGridDefinitionDto grid,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<ReportWriterMetricDefinitionDto> metrics,
        IReadOnlyList<ReportWriterFormulaDefinitionDto> formulas,
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

        ApplyFormulas(output, formulas, fieldTotals, warnings);
        return output
            .Select((row, index) => new ReportWriterGridRowDto(BuildRowKey(row.Values, dimensions, index), row.Values))
            .ToArray();
    }

    private static IReadOnlyList<ReportWriterGridRowDto> RenderAggregateRows(
        ReportWriterGridDefinitionDto grid,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<ReportWriterMetricDefinitionDto> metrics,
        IReadOnlyList<ReportWriterFormulaDefinitionDto> formulas,
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
        ApplyFormulas(workingRows, formulas, metricTotals, warnings);
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
        }

        columns.AddRange(formulas.Select(formula => new ReportWriterGridColumnDto(formula.Name, formula.Label ?? formula.Name, "formula")));
        return columns;
    }

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

    private static void ApplyContribution(IReadOnlyList<WorkingRow> rows, string metricName)
    {
        var total = rows.Sum(row => row.NumericValues.TryGetValue(metricName, out var value) ? value : 0m);
        foreach (var row in rows)
        {
            var value = row.NumericValues.TryGetValue(metricName, out var metricValue) ? metricValue : 0m;
            var contribution = total == 0m ? 0m : value / total * 100m;
            row.NumericValues["contributionPercent"] = contribution;
            row.Values["contributionPercent"] = FormatDecimal(contribution);
        }
    }

    private static void ApplyFormulas(
        IReadOnlyList<WorkingRow> rows,
        IReadOnlyList<ReportWriterFormulaDefinitionDto> formulas,
        IReadOnlyDictionary<string, decimal> totals,
        ISet<string> warnings)
    {
        foreach (var row in rows)
        {
            foreach (var formula in formulas)
            {
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
                ? "contributionPercent"
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
        value.ToString("0.######", CultureInfo.InvariantCulture);

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
            if (string.Equals(identifier, "total", StringComparison.OrdinalIgnoreCase) && TryConsume('('))
            {
                var field = ParseFieldArgument();
                Expect(')');
                return _totalResolver(field)
                       ?? throw new InvalidOperationException($"total field '{field}' was not found.");
            }

            return ResolveValue(identifier);
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
