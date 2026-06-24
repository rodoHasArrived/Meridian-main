/**
 * Client-side period-over-period diff of two rendered report-writer grids.
 *
 * Mirrors the server-side ReportSnapshotDiffEngine (Meridian.Reporting) so the
 * browser can show "prior / current / Δ" comparisons without a round trip: rows
 * are matched by row key, numeric cells carry a signed delta and direction, and
 * rows are classified Added / Removed / Changed / Unchanged.
 */
import type {
  ReportWriterDiffDirection,
  ReportWriterDiffRowState,
  ReportWriterGridColumn,
  ReportWriterGridDiff,
  ReportWriterGridDiffCell,
  ReportWriterGridDiffRow,
  ReportWriterGridRender,
  ReportWriterGridRow
} from "@/types";

const EMPTY_VALUES: Record<string, string> = {};

function tryParseNumber(value: string | null | undefined): number | null {
  if (value == null) {
    return null;
  }

  const trimmed = value.trim();
  if (trimmed === "") {
    return null;
  }

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

function formatDelta(value: number): string {
  // Match the server formatter's 6-decimal precision without trailing zeros.
  return String(Math.round(value * 1e6) / 1e6);
}

function buildRowLookup(
  rows: ReportWriterGridRow[],
  side: string,
  warnings: Set<string>
): Map<string, ReportWriterGridRow> {
  const lookup = new Map<string, ReportWriterGridRow>();
  for (const row of rows) {
    if (lookup.has(row.rowKey)) {
      warnings.add(`Duplicate row key '${row.rowKey}' in the ${side} snapshot; the first occurrence was used for comparison.`);
      continue;
    }

    lookup.set(row.rowKey, row);
  }

  return lookup;
}

function buildCells(
  columnKeys: string[],
  priorValues: Record<string, string> | null,
  currentValues: Record<string, string> | null
): ReportWriterGridDiffCell[] {
  return columnKeys.map((column) => {
    const priorValue = priorValues && column in priorValues ? priorValues[column] : null;
    const currentValue = currentValues && column in currentValues ? currentValues[column] : null;

    let delta: string | null = null;
    let direction: ReportWriterDiffDirection = "Flat";
    const priorNumber = tryParseNumber(priorValue);
    const currentNumber = tryParseNumber(currentValue);
    if (priorNumber !== null && currentNumber !== null) {
      const change = currentNumber - priorNumber;
      delta = formatDelta(change);
      direction = change > 0 ? "Up" : change < 0 ? "Down" : "Flat";
    }

    return { column, priorValue, currentValue, delta, direction };
  });
}

function rowsDiffer(cells: ReportWriterGridDiffCell[]): boolean {
  return cells.some((cell) => (cell.priorValue ?? "") !== (cell.currentValue ?? ""));
}

export function buildReportWriterGridDiff(
  prior: ReportWriterGridRender,
  current: ReportWriterGridRender
): ReportWriterGridDiff {
  const warnings = new Set<string>();
  const gridId = current.gridId.trim().length > 0 ? current.gridId : prior.gridId;
  const title = current.title.trim().length > 0 ? current.title : prior.title;

  if (current.gridId.toLowerCase() !== prior.gridId.toLowerCase()) {
    warnings.add(`Compared grids have different identifiers ('${prior.gridId}' vs '${current.gridId}'); rows are matched by row key only.`);
  }

  const columns: ReportWriterGridColumn[] = current.columns.length > 0 ? current.columns : prior.columns;
  const priorColumnKeys = new Set(prior.columns.map((column) => column.key.toLowerCase()));
  const currentColumnKeys = new Set(current.columns.map((column) => column.key.toLowerCase()));
  if (priorColumnKeys.size !== currentColumnKeys.size || [...priorColumnKeys].some((key) => !currentColumnKeys.has(key))) {
    warnings.add("Compared grids have different column sets; comparison is limited to shared columns where values are present.");
  }

  const priorByKey = buildRowLookup(prior.rows, "prior", warnings);
  const currentByKey = buildRowLookup(current.rows, "current", warnings);
  const columnKeys = columns.map((column) => column.key);

  const rows: ReportWriterGridDiffRow[] = [];
  let added = 0;
  let removed = 0;
  let changed = 0;
  let unchanged = 0;

  // Walk current rows in rendered order so the diff preserves the live layout.
  for (const currentRow of current.rows) {
    const priorRow = priorByKey.get(currentRow.rowKey) ?? null;
    const cells = buildCells(columnKeys, priorRow?.values ?? null, currentRow.values);
    let state: ReportWriterDiffRowState;
    if (!priorRow) {
      state = "Added";
      added += 1;
    } else if (rowsDiffer(cells)) {
      state = "Changed";
      changed += 1;
    } else {
      state = "Unchanged";
      unchanged += 1;
    }

    rows.push({
      rowKey: currentRow.rowKey,
      state,
      priorValues: priorRow?.values ?? EMPTY_VALUES,
      currentValues: currentRow.values,
      cells
    });
  }

  // Rows only present in the prior snapshot are appended as removals.
  for (const priorRow of prior.rows) {
    if (currentByKey.has(priorRow.rowKey)) {
      continue;
    }

    removed += 1;
    rows.push({
      rowKey: priorRow.rowKey,
      state: "Removed",
      priorValues: priorRow.values,
      currentValues: EMPTY_VALUES,
      cells: buildCells(columnKeys, priorRow.values, null)
    });
  }

  return {
    gridId,
    title,
    columns,
    rows,
    warnings: [...warnings],
    addedRowCount: added,
    removedRowCount: removed,
    changedRowCount: changed,
    unchangedRowCount: unchanged
  };
}
