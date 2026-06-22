/**
 * Editable cell — inline editing in tables without modals. Click to enter edit mode,
 * Tab/Enter to commit, Escape to cancel. On blur, commits automatically. Validator
 * function receives the draft value and returns true (valid) or an error message string.
 * Error state is shown as a red border; a hint line appears below the input.
 */
export interface EditableCellProps {
  /** The current value (will be stringified for display). */
  value: string | number | null | undefined;
  /** Fired on every keystroke during editing (does not validate). */
  onChange?: (val: string) => void;
  /** Fired when the value is committed (via Enter, Tab, or blur). */
  onCommit: (val: string) => void;
  /**
   * Optional validation function. Receives the draft value (string) and returns:
   * - `true` if valid
   * - an error message (string) if invalid
   */
  validator?: (val: string) => true | string;
  /** Input type. @default "text" */
  mode?: "text" | "number";
  /** Placeholder text for the input. */
  placeholder?: string;
  /** CSS class to apply to the root element. */
  className?: string;
  /** Render as a custom display (instead of the stringified value). */
  children?: React.ReactNode;
  /** Disable editing. @default false */
  disabled?: boolean;
  /** Hint text shown below the input during edit (or error message if validation fails). */
  hint?: string;
}
export declare function EditableCell(props: EditableCellProps): JSX.Element;
