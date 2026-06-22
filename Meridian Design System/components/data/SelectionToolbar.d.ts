/**
 * Selection toolbar — a floating action bar that appears when rows are selected in a table.
 * Shows the count of selected items, shortcuts to "Select all" / "Clear", and a primary action
 * plus additional ghost-variant actions. Slides in from the bottom with a subtle animation.
 * Typically placed above a DenseDataTable or ReconciliationPanel.
 */
export interface SelectionAction {
  /** Button label. */
  label: string;
  /** Fired when the button is clicked. */
  onClick: () => void;
  /** Button variant: "primary" | "ghost" | "danger". @default "ghost" */
  variant?: "primary" | "ghost" | "danger";
  /** Optional tooltip. */
  tooltip?: string;
}

export interface SelectionToolbarProps {
  /** Number of currently selected rows. */
  count?: number;
  /** Total number of rows in the table. */
  total?: number;
  /** Callback to select all rows. If provided, "Select all" link appears when count < total. */
  onSelectAll?: () => void;
  /** Callback to clear selection. If provided, "Clear" link appears when count > 0. */
  onClear?: () => void;
  /**
   * Primary action — rendered as a solid teal-blue button. Typically the main bulk operation
   * (e.g. "Export" or "Match selected").
   */
  primaryAction?: SelectionAction;
  /**
   * Secondary actions — rendered as ghost buttons to the right. Use for less common
   * operations (e.g. "Reclassify", "Delete").
   */
  actions?: SelectionAction[];
  /** CSS class to apply to the root element. */
  className?: string;
}
export declare function SelectionToolbar(props: SelectionToolbarProps): JSX.Element;
