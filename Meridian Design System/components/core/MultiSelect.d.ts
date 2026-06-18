export interface MultiSelectAction {
  id: string;
  label: string;
  icon?: string;
  dangerous?: boolean;
  onClick: (selectedIds: Set<string | number>) => void;
}

/**
 * MultiSelect — batch selection toolbar for tables.
 *
 * Manages a Set of selected row IDs. Shows a toolbar with select-all checkbox,
 * selection count, and contextual action buttons (delete, export, reassign, etc.).
 * Wire SelectCheckbox into each table row.
 *
 * @example
 * const [selected, setSelected] = useState(new Set());
 * <MultiSelect
 *   selectedIds={selected}
 *   onSelectionChange={setSelected}
 *   totalCount={rows.length}
 *   actions={[
 *     { id: 'delete', label: 'Delete', icon: '🗑️', dangerous: true,
 *       onClick: (ids) => deleteRows(Array.from(ids)) }
 *   ]}
 * />
 */
export function MultiSelect(props: {
  selectedIds?: Set<string | number>;
  onSelectionChange?: (newSelection: Set<string | number>) => void;
  totalCount?: number;
  actions?: MultiSelectAction[];
  onClearSelection?: () => void;
}): JSX.Element;

/**
 * SelectCheckbox — individual row checkbox.
 * Wire into each table row; call onChange with (rowId, isChecked).
 */
export function SelectCheckbox(props: {
  rowId: string | number;
  isSelected?: boolean;
  onChange?: (rowId: string | number, isChecked: boolean) => void;
}): JSX.Element;
