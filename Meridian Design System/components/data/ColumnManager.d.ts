/**
 * ColumnManager — keyboard- and screen-reader-accessible column controls: the counterpart to
 * DenseDataTable's mouse-only header drag/resize/pin. A popover listing every column in order
 * with move up/down, pin, show/hide, and width steppers as real buttons. Binds directly to a
 * `useTableColumns()` return value (from TableHooks) — no separate wiring.
 *
 * @example
 * const cols = useTableColumns(BASE, { persistKey: "blotter.cols" });
 * <ColumnManager cols={cols} />
 * <DenseDataTable columns={cols.visibleColumns} onColumnResize={cols.resize}
 *   onColumnReorder={cols.reorder} onColumnPin={cols.togglePin} … />
 */
export interface ColumnManagerProps {
  /** The object returned by useTableColumns(). */
  cols: {
    allColumns: { key: string; label: React.ReactNode }[];
    order: string[];
    widths: Record<string, number>;
    hidden: Record<string, boolean>;
    pinnedKeys: string[];
    resize: (key: string, width: number) => void;
    move: (key: string, dir: -1 | 1) => void;
    togglePin: (key: string) => void;
    toggleVisible: (key: string) => void;
    reset: () => void;
  };
  /** Trigger button label. @default "Columns" */
  label?: string;
  /** Px change per width step. @default 20 */
  widthStep?: number;
}
export declare function ColumnManager(props: ColumnManagerProps): JSX.Element;
