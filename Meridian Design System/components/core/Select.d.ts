import React from "react";
export interface SelectOption { value: string | number; label: string; disabled?: boolean; }
export interface SelectProps { label?: string; options?: (SelectOption | string | "---")[]; value?: string | number; onChange?: (value: string | number) => void; placeholder?: string; disabled?: boolean;   /** 3.5 · Accessible name when no visible `label` is rendered — an unlabeled Select is never
   *  anonymous (falls back to `placeholder`). */
  ariaLabel?: string;
}
export function Select(props: SelectProps): React.ReactElement;
