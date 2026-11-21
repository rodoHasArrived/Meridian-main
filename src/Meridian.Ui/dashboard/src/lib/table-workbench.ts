import { getJson, postJson } from "@/lib/api";
import { downloadCsv, serializeCsv, type CsvCell } from "@/lib/csv";

export interface TableView {
  viewId: string;
  label: string;
  searchText: string;
  columnIds: string[];
  density: "compact" | "comfortable";
  sortColumnId?: string | null;
  sortDirection?: "asc" | "desc" | null;
  filters?: Record<string, string> | null;
  isReadOnly?: boolean;
  origin?: string | null;
}

export type TableViewRequest = Omit<TableView, "viewId" | "isReadOnly" | "origin">;

function decodeTableView(value: unknown): TableView | null {
  if (!value || typeof value !== "object") return null;
  const view = value as Partial<TableView>;
  if (typeof view.viewId !== "string" || !view.viewId || typeof view.label !== "string" || !view.label.trim() ||
      typeof view.searchText !== "string" || view.searchText.length > 1000 || !Array.isArray(view.columnIds) ||
      view.columnIds.length > 100 || !view.columnIds.every(column => typeof column === "string" && column.length > 0 && column.length <= 100) ||
      new Set(view.columnIds).size !== view.columnIds.length ||
      (view.density !== "compact" && view.density !== "comfortable") ||
      (view.sortColumnId != null && typeof view.sortColumnId !== "string") ||
      (view.sortDirection != null && view.sortDirection !== "asc" && view.sortDirection !== "desc") ||
      (view.filters != null && (typeof view.filters !== "object" || Array.isArray(view.filters) ||
        !Object.values(view.filters).every(filter => typeof filter === "string"))) ||
      (view.isReadOnly != null && typeof view.isReadOnly !== "boolean")) return null;
  return view as TableView;
}

export async function loadTableViews(tableId: string, signal?: AbortSignal): Promise<TableView[]> {
  const payload = await getJson<unknown>(`/api/workstation/tables/${encodeURIComponent(tableId)}/views`, { signal, allowDevelopmentFallback: false });
  if (!Array.isArray(payload)) throw new Error("Saved views response is invalid.");
  const views = payload.map(decodeTableView);
  if (views.some(view => view === null)) throw new Error("Saved views response contains invalid presentation settings.");
  return views as TableView[];
}

export async function saveTableView(tableId: string, view: TableViewRequest, signal?: AbortSignal): Promise<TableView> {
  const payload = await postJson<unknown>(`/api/workstation/tables/${encodeURIComponent(tableId)}/views`, view, { signal, allowDevelopmentFallback: false });
  const decoded = decodeTableView(payload);
  if (!decoded || decoded.isReadOnly) throw new Error("The server did not confirm a valid personal saved view. Refresh before retrying.");
  return decoded;
}

export async function exportTableQuery(tableId: string, columnIds: string[], searchText: string,
  sortColumnId?: string, sortDirection?: "asc" | "desc", signal?: AbortSignal, filters?: Record<string, string>): Promise<void> {
  const result = await postJson<{ filename: string; csv: string; rowCount: number; asOf: string }>(
    `/api/workstation/tables/${encodeURIComponent(tableId)}/export`, { columnIds, searchText, sortColumnId, sortDirection, filters },
    { signal, allowDevelopmentFallback: false });
  if (signal?.aborted) return;
  if (typeof result.csv !== "string" || typeof result.filename !== "string" || !result.filename.endsWith(".csv") ||
      !Number.isInteger(result.rowCount) || result.rowCount < 0 || typeof result.asOf !== "string" || !Number.isFinite(Date.parse(result.asOf)))
    throw new Error("The server did not return a complete valid export.");
  if (!downloadCsv(result.filename, result.csv)) throw new Error("CSV downloads are unavailable in this browser.");
}

/** Only textual cells are neutralized. A negative numeric amount must remain numeric. */
export interface ExactDecimalCsvCell { kind: "decimal"; value: string; }
export function safeTableCsv(headers: string[], rows: (CsvCell | ExactDecimalCsvCell)[][]): string {
  const safeCell = (cell: CsvCell | ExactDecimalCsvCell): CsvCell => {
    if (cell == null) return null;
    if (typeof cell === "object") {
      if (cell.kind !== "decimal" || !/^-?\d+(?:\.\d+)?$/.test(cell.value)) throw new Error("A declared decimal column contains an invalid source value.");
      return cell.value;
    }
    return typeof cell === "string" && /^[\s]*[=+\-@\t\r]/.test(cell) ? `'${cell}` : cell;
  };
  return serializeCsv(headers.map(header => String(safeCell(header))), rows.map(row => row.map(safeCell)));
}
