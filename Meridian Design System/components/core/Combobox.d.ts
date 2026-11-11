import React from "react";

export interface ComboboxOption {
  value: string | number;
  label: string;
  /** Right-aligned secondary text (count, exchange, last price) */
  meta?: React.ReactNode;
  /** Options sharing a group string render under one small-caps heading (keep them adjacent) */
  group?: string;
  /** Not selectable; skipped by arrow navigation */
  disabled?: boolean;
}

export interface ComboboxProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onChange"> {
  /** Small-caps label above the field; associated with the input */
  label?: string;
  /** Options — strings or option objects */
  options: (string | ComboboxOption)[];
  /** Selected value */
  value?: string | number;
  /** Called with the chosen value, or `undefined` when cleared */
  onChange?: (value: string | number | undefined) => void;
  /** Placeholder text. @default "Search…" */
  placeholder?: string;
  disabled?: boolean;
  /** Text shown when no options match. @default "No matches" */
  emptyText?: string;
  /** Error message — renders below, sets `aria-invalid` and red border */
  error?: React.ReactNode;
  /** Marks the field required (asterisk + `aria-required`) */
  required?: boolean;
  /** Show an ✕ that clears the selection. @default false */
  clearable?: boolean;
  /** Open the menu on focus as well as click. @default false */
  openOnFocus?: boolean;
  /** Custom match predicate; replaces the default case-insensitive substring test */
  filter?: (option: ComboboxOption, query: string) => boolean;
  /** Fires as the user types (server-side filtering, analytics) */
  onQueryChange?: (query: string) => void;
  /** Base id — the input gets it; listbox/options/error derive from it. Auto-generated if omitted. */
  id?: string;
  name?: string;
}

/**
 * Searchable single-select. Type to filter (matches are marked), arrows to navigate, Enter to
 * commit, Escape to close. Implements ARIA 1.2 combobox with `aria-activedescendant` — focus never
 * leaves the input, and the menu scrolls to keep the active option visible. Ids are per-instance,
 * so several comboboxes can coexist on one screen.
 *
 * Field geometry matches `Input` (32px) so the two align in a form row.
 *
 * @example
 * <Combobox
 *   label="Counterparty"
 *   options={[{ value: "GS", label: "Goldman Sachs", meta: "12 open" }]}
 *   value={cp}
 *   onChange={setCp}
 *   clearable
 *   required
 *   error={touched && !cp ? "Select a counterparty" : undefined}
 * />
 */
export declare function Combobox(props: ComboboxProps): JSX.Element;
