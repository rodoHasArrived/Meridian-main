/**
 * Dense mono data grid — mirrors `.dense-data-table`. Zebra striping, sticky mono headers,
 * 3px cyan inset rail on the hovered/selected row, optional sort affordance, and optional
 * checkbox column for multi-select workflows. The workhorse of every Meridian screen.
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
}
export declare function DenseDataTable(props: DenseDataTableProps): JSX.Element;
