// "Export as shown" for TableWorkbench (UX Phase E): the visible columns, in screen order, with the
// values formatted as the table shows them and the totals row last. The raw source export
// (`exportValue`, exact decimals) is kept beside it for reconciliation and re-import.
import type { CsvCell } from "@/lib/csv";

export interface ShownExportColumn<T> {
  id: string;
  label: string;
  /** Raw source value. */
  exportValue?: (row: T) => CsvCell;
  /** The value as the table shows it, e.g. "120,500.00" or "Posted". Falls back to the raw value. */
  exportDisplay?: (row: T) => string | null | undefined;
  exportType?: "decimal" | "text";
  /** Spreadsheet number format for numeric cells, e.g. "#,##0.00;(#,##0.00)". */
  exportNumberFormat?: string;
}

/** One exported cell: the text as shown, and the number behind it when it is safely numeric. */
export interface ShownCell {
  text: string;
  /** Set only when the source value is a number that survives as a double without losing digits. */
  number: number | null;
  /** Spreadsheet number format for `number`. */
  format: string | null;
}

export interface ShownExport {
  headers: string[];
  rows: ShownCell[][];
  /** The totals row, labelled in its first cell when the table supplies no label there. */
  footer: ShownCell[] | null;
}

const DEFAULT_DECIMAL_FORMAT = "#,##0.00";
/** Doubles hold 15 significant digits exactly; longer decimals stay text so nothing is rounded. */
const MAX_SAFE_SIGNIFICANT_DIGITS = 15;

function significantDigits(value: string): number {
  return value.replace(/^-/, "").replace(".", "").replace(/^0+/, "").length;
}

function toNumber(raw: CsvCell, decimal: boolean): number | null {
  if (typeof raw === "number") return Number.isFinite(raw) ? raw : null;
  if (!decimal || typeof raw !== "string") return null;
  const trimmed = raw.trim();
  if (!/^-?\d+(?:\.\d+)?$/.test(trimmed) || significantDigits(trimmed) > MAX_SAFE_SIGNIFICANT_DIGITS) return null;
  return Number(trimmed);
}

function shownCell<T>(column: ShownExportColumn<T>, raw: CsvCell, display: string | null | undefined): ShownCell {
  const decimal = column.exportType === "decimal";
  const number = toNumber(raw, decimal);
  const text = display ?? (raw === null || raw === undefined ? "" : String(raw));
  return { text, number, format: number === null ? null : column.exportNumberFormat ?? (decimal ? DEFAULT_DECIMAL_FORMAT : null) };
}

/** A totals cell: a raw value, or a value with the text the table shows for it. */
export type ShownFooterCell = CsvCell | { value: CsvCell; display: string };

function isDisplayedFooterCell(cell: ShownFooterCell): cell is { value: CsvCell; display: string } {
  return cell !== null && typeof cell === "object";
}

export function buildShownExport<T>(
  columns: readonly ShownExportColumn<T>[],
  rows: readonly T[],
  footer?: { cells: Partial<Record<string, ShownFooterCell>>; label: string } | null
): ShownExport {
  const exported = columns.filter((column) => column.exportValue || column.exportDisplay);
  const footerRow = footer && Object.keys(footer.cells).length > 0
    ? exported.map((column, index) => {
      const cell: ShownFooterCell = footer.cells[column.id] ?? (index === 0 ? footer.label : null);
      return isDisplayedFooterCell(cell) ? shownCell(column, cell.value, cell.display) : shownCell(column, cell, undefined);
    })
    : null;
  return {
    headers: exported.map((column) => column.label),
    rows: rows.map((row) => exported.map((column) => shownCell(column, column.exportValue?.(row) ?? null, column.exportDisplay?.(row)))),
    footer: footerRow
  };
}

/** The shown export as CSV rows: text as shown; the footer last. */
export function shownExportCsvRows(model: ShownExport): string[][] {
  return [...model.rows, ...(model.footer ? [model.footer] : [])].map((row) => row.map((cell) => cell.text));
}

export interface XlsxCell { value?: string | number; type?: StringConstructor | NumberConstructor; format?: string; fontWeight?: "bold" }

/** The shown export as spreadsheet sheets: typed number cells, a bold header and totals, and an About sheet. */
export function shownExportXlsxSheets(model: ShownExport, context: Record<string, string>, sheetName = "Table") {
  const cell = (value: ShownCell, bold = false): XlsxCell => value.number !== null
    ? { value: value.number, type: Number, ...(value.format ? { format: value.format } : {}), ...(bold ? { fontWeight: "bold" as const } : {}) }
    : { value: value.text, type: String, ...(bold ? { fontWeight: "bold" as const } : {}) };
  const data: XlsxCell[][] = [
    model.headers.map((header) => ({ value: header, type: String, fontWeight: "bold" as const })),
    ...model.rows.map((row) => row.map((value) => cell(value))),
    ...(model.footer ? [model.footer.map((value) => cell(value, true))] : [])
  ];
  const about: XlsxCell[][] = Object.entries(context).map(([key, value]) => [{ value: key, type: String, fontWeight: "bold" as const }, { value, type: String }]);
  return [
    { sheet: sheetName.slice(0, 31), data, stickyRowsCount: 1, columns: model.headers.map((header) => ({ width: Math.min(48, Math.max(10, header.length + 2)) })) },
    { sheet: "About", data: about, columns: [{ width: 22 }, { width: 80 }] }
  ];
}

/** Writes and downloads the workbook. The spreadsheet library loads only when an operator exports. */
export async function downloadShownXlsx(fileName: string, model: ShownExport, context: Record<string, string>, sheetName?: string): Promise<void> {
  const { default: writeXlsxFile } = await import("write-excel-file/browser");
  const sheets = shownExportXlsxSheets(model, context, sheetName);
  // The library's sheet type is wider than the cells built here; the cells are its documented shape.
  await (writeXlsxFile as unknown as (sheets: unknown) => { toFile: (name: string) => Promise<void> })(sheets).toFile(fileName);
}
