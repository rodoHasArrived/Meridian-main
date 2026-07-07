import React from "react";

export interface ComboboxOption {
  value: string | number;
  label: string;
}

export interface ComboboxProps {
  /** Small-caps label above the field */
  label?: string;
  /** Options — strings or {value, label} objects */
  options: (string | ComboboxOption)[];
  /** Selected value */
  value?: string | number;
  /** Called with the chosen value */
  onChange?: (value: string | number) => void;
  /** Placeholder text. @default "Search…" */
  placeholder?: string;
  /** Disable the field */
  disabled?: boolean;
  /** Text shown when no options match. @default "No matches" */
  emptyText?: string;
}

export declare function Combobox(props: ComboboxProps): JSX.Element;
