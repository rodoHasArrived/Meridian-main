import React from "react";

export interface MultiSelectOption {
  value: string | number;
  label: string;
  /** Right-aligned secondary text (count, region, AUM) */
  meta?: React.ReactNode;
  /** Options sharing a group string render under one heading (keep them adjacent) */
  group?: string;
  disabled?: boolean;
}

export interface MultiSelectProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onChange" | "defaultValue"> {
  /** Small-caps label above the field */
  label?: string;
  /** Options — strings or option objects */
  options: (string | MultiSelectOption)[];
  /** Selected values, in the order they were picked */
  value?: (string | number)[];
  /** Fires with the full next selection */
  onChange?: (values: (string | number)[]) => void;
  placeholder?: string;
  disabled?: boolean;
  required?: boolean;
  error?: React.ReactNode;
  emptyText?: string;
  /** Chips shown before collapsing to "+N". @default 4 */
  maxChips?: number;
  /** Hard cap on selections — further options go unselectable */
  maxSelected?: number;
  /** Show the select-all / clear row. @default true */
  showBulk?: boolean;
  /** Custom match predicate; replaces the default case-insensitive substring test */
  filter?: (option: MultiSelectOption, query: string) => boolean;
  onQueryChange?: (query: string) => void;
  id?: string;
  name?: string;
}

/**
 * Pick **several** values from a list — books, desks, strategies, counterparties, tags. Chips in the
 * field, a checkbox listbox below, type to filter. ARIA 1.2 multi-select combobox with
 * `aria-activedescendant`; Backspace removes the last chip, Enter toggles the active option.
 *
 * Bulk actions operate on the **currently filtered** options, so typing "EUR" then "Select matches"
 * adds exactly those.
 *
 * For a single value use `Select` (short lists) or `Combobox` (long ones). For acting on selected
 * *table rows* use `BulkActionBar` — that is a different job and a different component.
 *
 * @example
 * <MultiSelect
 *   label="Books"
 *   options={books}
 *   value={selected}
 *   onChange={setSelected}
 *   maxSelected={8}
 * />
 */
export declare function MultiSelect(props: MultiSelectProps): React.JSX.Element;
