import React from "react";
export interface SelectOption { value: string | number; label: string; disabled?: boolean; }
export interface SelectProps { id?: string; "aria-label"?: string; "aria-labelledby"?: string; "aria-describedby"?: string; "aria-invalid"?: boolean | "true" | "false" | "grammar" | "spelling"; label?: string; options?: (SelectOption | string | "---")[]; value?: string | number; onChange?: (value: string | number) => void; placeholder?: string; disabled?: boolean; }
export function Select(props: SelectProps): React.ReactElement;
