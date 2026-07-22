using System.Globalization;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

/// <summary>
/// Produces a deterministic period-over-period comparison between two rendered report-writer grids.
/// Rows are matched by <see cref="ReportWriterGridRowDto.RowKey"/>; numeric cells carry a signed delta and
/// direction so the UI can present "prior / current / Δ" columns without re-querying the source data.
/// </summary>
public static class ReportSnapshotDiffEngine
{
    public static ReportWriterGridDiffDto Diff(
        ReportWriterGridRenderDto prior,
        ReportWriterGridRenderDto current)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(current);

        var warnings = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var gridId = string.IsNullOrWhiteSpace(current.GridId) ? prior.GridId : current.GridId;
        var title = string.IsNullOrWhiteSpace(current.Title) ? prior.Title : current.Title;

        if (!string.Equals(prior.GridId, current.GridId, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Compared grids have different identifiers ('{prior.GridId}' vs '{current.GridId}'); rows are matched by row key only.");
        }

        var columns = current.Columns is { Count: > 0 } ? current.Columns : prior.Columns;
        var priorColumnKeys = prior.Columns.Select(static column => column.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentColumnKeys = current.Columns.Select(static column => column.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!priorColumnKeys.SetEquals(currentColumnKeys))
        {
            warnings.Add("Compared grids have different column sets; comparison is limited to shared columns where values are present.");
        }

        var priorByKey = BuildRowLookup(prior.Rows, "prior", warnings);
        var currentByKey = BuildRowLookup(current.Rows, "current", warnings);
        var columnKeys = columns.Select(static column => column.Key).ToArray();

        var diffRows = new List<ReportWriterGridDiffRowDto>(current.Rows.Count + prior.Rows.Count);
        var added = 0;
        var removed = 0;
        var changed = 0;
        var unchanged = 0;

        // Walk current rows in their rendered order so the comparison preserves the live report layout.
        foreach (var currentRow in current.Rows)
        {
            priorByKey.TryGetValue(currentRow.RowKey, out var priorRow);
            var cells = BuildCells(columnKeys, priorRow?.Values, currentRow.Values);
            var state = priorRow is null
                ? ReportWriterDiffRowStateDto.Added
                : RowsDiffer(cells)
                    ? ReportWriterDiffRowStateDto.Changed
                    : ReportWriterDiffRowStateDto.Unchanged;

            switch (state)
            {
                case ReportWriterDiffRowStateDto.Added:
                    added++;
                    break;
                case ReportWriterDiffRowStateDto.Changed:
                    changed++;
                    break;
                default:
                    unchanged++;
                    break;
            }

            diffRows.Add(new ReportWriterGridDiffRowDto(
                currentRow.RowKey,
                state,
                priorRow?.Values ?? EmptyValues,
                currentRow.Values,
                cells));
        }

        // Rows present only in the prior snapshot are appended in their original order as removals.
        foreach (var priorRow in prior.Rows)
        {
            if (currentByKey.ContainsKey(priorRow.RowKey))
            {
                continue;
            }

            removed++;
            diffRows.Add(new ReportWriterGridDiffRowDto(
                priorRow.RowKey,
                ReportWriterDiffRowStateDto.Removed,
                priorRow.Values,
                EmptyValues,
                BuildCells(columnKeys, priorRow.Values, currentValues: null)));
        }

        return new ReportWriterGridDiffDto(
            gridId,
            title,
            columns,
            diffRows,
            warnings.ToArray(),
            added,
            removed,
            changed,
            unchanged);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, ReportWriterGridRowDto> BuildRowLookup(
        IReadOnlyList<ReportWriterGridRowDto> rows,
        string side,
        ISet<string> warnings)
    {
        var lookup = new Dictionary<string, ReportWriterGridRowDto>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (lookup.ContainsKey(row.RowKey))
            {
                warnings.Add($"Duplicate row key '{row.RowKey}' in the {side} snapshot; the first occurrence was used for comparison.");
                continue;
            }

            lookup[row.RowKey] = row;
        }

        return lookup;
    }

    private static IReadOnlyList<ReportWriterGridDiffCellDto> BuildCells(
        IReadOnlyList<string> columnKeys,
        IReadOnlyDictionary<string, string>? priorValues,
        IReadOnlyDictionary<string, string>? currentValues)
    {
        var cells = new List<ReportWriterGridDiffCellDto>(columnKeys.Count);
        foreach (var column in columnKeys)
        {
            var priorValue = priorValues is not null && priorValues.TryGetValue(column, out var priorRaw) ? priorRaw : null;
            var currentValue = currentValues is not null && currentValues.TryGetValue(column, out var currentRaw) ? currentRaw : null;

            string? delta = null;
            var direction = ReportWriterDiffDirectionDto.Flat;
            if (TryParse(priorValue, out var priorNumber) && TryParse(currentValue, out var currentNumber))
            {
                var change = currentNumber - priorNumber;
                delta = FormatDecimal(change);
                direction = change switch
                {
                    > 0m => ReportWriterDiffDirectionDto.Up,
                    < 0m => ReportWriterDiffDirectionDto.Down,
                    _ => ReportWriterDiffDirectionDto.Flat
                };
            }

            cells.Add(new ReportWriterGridDiffCellDto(column, priorValue, currentValue, delta, direction));
        }

        return cells;
    }

    private static bool RowsDiffer(IReadOnlyList<ReportWriterGridDiffCellDto> cells) =>
        cells.Any(static cell => !string.Equals(cell.PriorValue ?? string.Empty, cell.CurrentValue ?? string.Empty, StringComparison.Ordinal));

    private static bool TryParse(string? value, out decimal number)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
        {
            return true;
        }

        number = 0m;
        return false;
    }

    private static string FormatDecimal(decimal value) =>
        ReportingNumberFormat.FormatDecimal(value);
}
