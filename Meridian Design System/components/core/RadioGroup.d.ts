/**
 * Single-select radio group — the form primitive `Checkbox`/`Toggle` don't cover. A column
 * (or row) of radios with a filled accent center dot, optional per-option hint. Controlled
 * via `value` + `onChange`.
 */
export interface RadioOption {
  value: string;
  label: React.ReactNode;
  /** Secondary line under the label. */
  hint?: string;
  disabled?: boolean;
}
export interface RadioGroupProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onChange"> {
  /** Options — strings or `{value, label, hint?, disabled?}`. */
  options: (string | RadioOption)[];
  /** Currently selected value. */
  value?: string;
  /** Fired with the newly selected value. */
  onChange?: (value: string) => void;
  /** Shared input `name` (auto-generated if omitted). */
  name?: string;
  /** Layout. @default "vertical" */
  orientation?: "vertical" | "horizontal";
  /** Disable the whole group. @default false */
  disabled?: boolean;
}
export declare function RadioGroup(props: RadioGroupProps): JSX.Element;
