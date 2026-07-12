/**
 * CoverageMatrix — symbol × session data-availability heat grid: which instrument has which
 * day, at what completeness. Statuses render as flat alpha washes on a hairline grid —
 * `full` (green) · `partial` (amber) · `gap` (red) · `pending` (neutral fill) · `none`
 * (faded — no session, e.g. holiday). Hovering or focusing a cell writes a mono readout line
 * under the grid; with `onCellClick` the cells become real buttons (drill into the scan).
 *
 * Feed it either a `data` map (`data[rowId][colId]` → status string or `{ status, detail }`)
 * or a `cell(row, col)` resolver. `detail` lands in the readout and tooltip — put the
 * evidence there ("412/780 bars").
 *
 * @example
 * <CoverageMatrix
 *   rows={["AAPL", "MSFT", "NVDA"]}
 *   cols={days.map((d) => ({ id: d, label: d.slice(5) }))}
 *   data={{ AAPL: { "2026-06-19": { status: "partial", detail: "210/390 bars" } } }}
 *   onCellClick={(row, col, cell) => openScan(row.id, col.id)}
 * />
 */
export type CoverageStatus = "full" | "partial" | "gap" | "pending" | "none";
export interface CoverageCell {
  status: CoverageStatus | string;
  /** Evidence string for the readout/tooltip, e.g. "412/780 bars". */
  detail?: string;
}
export interface CoverageAxisItem { id: string; label: string; }
export interface CoverageMatrixProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Row axis (instruments) — strings or { id, label }. */
  rows: (string | CoverageAxisItem)[];
  /** Column axis (sessions/dates) — strings or { id, label }. */
  cols: (string | CoverageAxisItem)[];
  /** Status lookup by id: data[rowId][colId] → status string or CoverageCell. */
  data?: Record<string, Record<string, CoverageCell | string>>;
  /** Resolver alternative to `data`. */
  cell?: (row: CoverageAxisItem, col: CoverageAxisItem) => CoverageCell | string | undefined;
  /** Cell edge in px. @default 16 */
  cellSize?: number;
  /** Vertical column labels above the grid. @default true */
  colLabels?: boolean;
  /** @default true */
  legend?: boolean;
  /** Mono hover-readout line under the grid. @default true */
  readout?: boolean;
  /** Makes cells buttons. */
  onCellClick?: (row: CoverageAxisItem, col: CoverageAxisItem, cell: CoverageCell) => void;
}
export declare function CoverageMatrix(props: CoverageMatrixProps): JSX.Element;
