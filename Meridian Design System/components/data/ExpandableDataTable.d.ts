/**
 * ExpandableDataTable — DenseDataTable with collapsible detail rows.
 *
 * Each row can expand to show a full-width detail panel (audit trail, allocations,
 * related records, etc.). Detail content is a render function (slot). Smooth slide
 * animation on expand/collapse. Supports sorting, selection, and custom cell renderers.
 *
 * @example
 * <ExpandableDataTable
 *   columns={[
 *     { key: 'date', label: 'Date' },
 *     { key: 'amount', label: 'Amount', align: 'right' }
 *   ]}
 *   rows={transactions}
 *   expandable={(row, index) => (
 *     <div>
 *       <h4>{row.description}</h4>
 *       <p>Account: {row.account}</p>
 *     </div>
 *   )}
 *   onSort={(key) => handleSort(key)}
 * />
 */

export interface ExpandableDataTableColumn {
  key: string;
  label: string;
  align?: "left" | "right";
  /** Show a sort affordance for this column. @default true when onSort is set */
  sortable?: boolean;
  /** Custom cell renderer; receives the row object. */
  render?: (row: Record<string, any>) => React.ReactNode;
}

export interface ExpandableDataTableProps {
  /** Column definitions */
  columns: ExpandableDataTableColumn[];

  /** Data rows */
  rows: Record<string, any>[];

  /**
   * Render function for expanded detail content.
   * Receives (row, index). Return JSX/React node.
   * If falsy, no expand column is shown.
   */
  expandable?: (row: Record<string, any>, index: number) => React.ReactNode;

  /** Row index highlighted with the accent rail. @default -1 */
  selectedIndex?: number;

  /** Active sort column key. */
  sortKey?: string;

  /** @default "asc" */
  sortDir?: "asc" | "desc";

  /** Fired when a row is clicked (main row only, not expand chevron). */
  onRowClick?: (row: Record<string, any>, index: number) => void;

  /** Fired when a sortable column header is clicked. */
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

export declare function ExpandableDataTable(props: ExpandableDataTableProps): JSX.Element;
