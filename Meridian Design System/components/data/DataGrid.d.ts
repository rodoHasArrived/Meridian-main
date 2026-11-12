import type React from "react";
/**
 * DataGrid — the one tabular primitive. Capabilities are opt-in props, not
 * separate components: `select`, `groupBy`, `expand`, `edit`, `totals`,
 * `density`. Columns are a **schema**, and `proof` is a first-class contract, so
 * a table states its own arithmetic ("Σ debit = Σ credit ✓" / "Out by 12.40")
 * instead of each domain table re-implementing it.
 *
 * Domain tables are schemas, not modules: `GRID_SCHEMAS.ledger`,
 * `.trialBalance`, `.taxLots`, `.blotter`, `.aging` — pass `schema="ledger"` and
 * override `columns`/`proof` when a screen needs a variant. A cell can carry a
 * full value envelope with `column.envelope`.
 *
 * Law: `guidelines/TABULAR_CONTRACT.md`.
 *
 * @example
 * <DataGrid schema="trialBalance" rows={rows} density="dense" />
 * <DataGrid columns={[
 *   { key: "account", label: "Account" },
 *   { key: "balance", label: "Balance", align: "right", format: "money", total: "sum",
 *     cell: (r) => <ValueEnvelope value={r.balance} condition={r.condition} authority={r.authority} align="right" /> },
 * ]} rows={rows} select expand={(r) => <Detail row={r} />} proof={{ type: "sum", column: "balance", expected: 0 }} />
 */
export type GridFormat = "text" | "num" | "money" | "signed" | "price" | "pct";

export interface GridColumn<Row = any> {
  key: string;
  label: string;
  format?: GridFormat;
  align?: "left" | "right";
  width?: number;
  /** Decimal places for numeric formats (prices default 4). */
  digits?: number;
  /** Column footer aggregate. */
  total?: "sum";
  /** Double-click to edit when the grid has `edit`. */
  editable?: boolean;
  /** Custom cell renderer — use it to render a `ValueEnvelope` for the cell. */
  cell?: (row: Row) => React.ReactNode;
  /** Opt this column out of header sorting. */
  sortable?: boolean;
  /** Depth tier. `"full"` withdraws the column at summary depth; untiered columns —
   *  identity and the figures the table is about — never withdraw. */
  tier?: "summary" | "full";
}

export type GridProof =
  | { type: "balance"; debit: string; credit: string; label?: string }
  | { type: "sum"; column: string; expected?: number; label?: string }
  | { type: "custom"; check: (rows: any[]) => { ok: boolean; label: string; text: string } };

export interface DataGridProps<Row = any> extends React.HTMLAttributes<HTMLDivElement> {
  columns?: GridColumn<Row>[];
  rows: Row[];
  /** Named column-schema recipe; `columns`/`proof` override its parts. */
  schema?: keyof typeof GRID_SCHEMAS;
  /** Field name, `"index"` for positional keys (static seed tables), or a fn. */
  rowKey?: string | ((row: Row) => string);
  /** Omit to track the theme density token (`--theme-row-height`), like every Meridian table. */
  density?: "terminal" | "dense" | "standard" | "review";
  select?: boolean;
  /** One key/fn, or an array for nested grouping. Group headers are collapsible
   *  and carry each group's own subtotals for every column declaring `total`. */
  groupBy?: string | ((row: Row) => string) | (string | ((row: Row) => string))[];
  /** Reading depth. At `"summary"` columns marked `tier: "full"` withdraw and the grid
   *  states how many it withheld. Pass `useDisclosure().detailFor(region)`; the grid never
   *  reads the context itself. @default "full" */
  detail?: "summary" | "full";
  /** Wire the withheld-columns strip's reveal — usually `setDetailFor(region, level)`. */
  onDetailChange?: (level: "summary" | "full") => void;
  /** Tier-1 depth: detail renderer, inline under the row — enables the expander column. */
  expand?: (row: Row) => React.ReactNode;
  /** Tier-2 depth: adds a trailing drill affordance per row (click, Enter, Space). Open a
   *  drawer over the plane; reserve a new screen for the audit tier. */
  onDrill?: (row: Row, key: string | number, index: number) => void;
  /** @default "Open detail" */
  drillLabel?: string;
  edit?: boolean;
  totals?: boolean;
  /** Header-click sorting managed by the grid. When the screen owns order, use
   *  `sortKey`/`sortDir`/`onSort` instead and pass `rows` already ordered — the
   *  grid then only renders the header indicators. */
  sortable?: boolean;
  sortKey?: string;
  sortDir?: "asc" | "desc";
  onSort?: (key: string) => void;
  /** Row activation (the operator's committed row) — independent of checkbox `select`. */
  onRowClick?: (row: Row, key: string | number, index: number) => void;
  activeKey?: string | number;
  /** Column management — grip / drag / double-click-pin on headers, with Alt+←/→,
   *  Ctrl+←/→, and P on the focused header. Columns may carry `pinned`/`pinnedOffset`.
   *  Pairs with `TableHooks.useTableColumns` + `ColumnManager`. */
  onColumnResize?: (key: string, px: number) => void;
  onColumnReorder?: (dragKey: string, dropKey: string) => void;
  onColumnPin?: (key: string) => void;
  /** Windowed rendering — automatic past `virtualizeThreshold` (default 500) unless the
   *  grid groups or expands (non-uniform row heights). Omit to auto-decide. */
  virtualize?: boolean;
  virtualizeThreshold?: number;
  /** Names the grid region (role="region") and, with `caption`, the hidden <caption>. */
  label?: string;
  caption?: string;
  proof?: GridProof | null;
  selected?: (string | number)[];
  onSelectedChange?: (keys: (string | number)[]) => void;
  onEdit?: (change: { row: Row; column: string; value: string }) => void;
  emptyText?: string;
  maxHeight?: number | string;
}
export declare function DataGrid<Row = any>(props: DataGridProps<Row>): React.JSX.Element;

export declare const GRID_SCHEMAS: Record<"ledger" | "trialBalance" | "taxLots" | "blotter" | "aging",
  { columns: GridColumn[]; proof?: GridProof }>;
export declare const CELL_FORMATS: Record<GridFormat, (value: any, col: GridColumn) => string>;
/** Also reachable as `DataGrid.proof`; schemas as `DataGrid.schemas`. */
export declare function gridProof(proof: GridProof | null, rows: any[]): { ok: boolean; label: string; text: string } | null;
