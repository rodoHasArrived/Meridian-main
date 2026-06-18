import React from "react";
export interface SelectOption { value: string | number; label: string; }
export interface SelectProps { label?: string; options?: (SelectOption | string | "---")[]; value?: string | number; onChange?: (value: string | number) => void; placeholder?: string; disabled?: boolean; }
export function Select(props: SelectProps): React.ReactElement;
