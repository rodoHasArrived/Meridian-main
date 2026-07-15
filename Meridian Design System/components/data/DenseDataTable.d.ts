/**
 * Dense mono data grid — mirrors `.dense-data-table`. Zebra striping, sticky mono headers,
 * 3px cyan inset rail on the hovered/selected row, optional sort affordance, and optional
 * checkbox column for multi-select workflows. The workhorse of every Meridian screen.
 *
 * Rows past `virtualizeThreshold` (default 500) render as a scrolling window — only the
 * visible slice mounts as real `<tr>`s — instead of every row hitting the DOM at once. This
 * is automatic; you only need `virtualize`/`rowHeight`/`maxHeight` to override the default.
 */
export interface DenseDataTableColumn {
  key: string;
  label: string;
  align?: "left" | "right";
  /** Show a sort affordance for this column. @default true when onSort is set */
  sortable?: boolean;
  /** Custom cell renderer; receives the row object. */
  render?: (row: Record<string, any>) => React.ReactNode;
}
export interface DenseDataTableProps {
  columns: DenseDataTableColumn[];
  rows: Record<string, any>[];
  /** Row index highlighted with the cyan rail. @default -1 */
  selectedIndex?: number;
  /** Active sort column key. */
  sortKey?: string;
  /** @default "asc" */
  sortDir?: "asc" | "desc";
  onRowClick?: (row: Record<string, any>, index: number) => void;
  onSort?: (key: string) => void;
  /** Enable checkbox column for multi-select. @default false */
  selectable?: boolean;
  /** Array of row indices that are currently selected. */
  selectedRows?: number[];
  /** Fired when a row checkbox is toggled. Receives (row, index, isSelected). */
  onSelectRow?: (row: Record<string, any>, index: number, isSelected: boolean) => void;
  /** Fired when the "select all" checkbox is toggled. Receives isSelected. */
  onSelectAll?: (isSelected: boolean) => void;
  /** Force windowed rendering on/off. Omit to auto-decide from `rows.length` vs `virtualizeThreshold`. */
  virtualize?: boolean;
  /** Row count above which rendering auto-windows. @default 500 */
  virtualizeThreshold?: number;
  /** Must match the table's actual row height for correct scroll math when virtualized. @default 40 */
  rowHeight?: number;
  /** Scrolling viewport height once virtualized. @default 560 */
  maxHeight?: number;
}
export declare function DenseDataTable(props: DenseDataTableProps): JSX.Element;
