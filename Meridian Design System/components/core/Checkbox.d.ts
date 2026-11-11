import React from "react";
export interface CheckboxProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "onChange" | "size"> { checked?: boolean; indeterminate?: boolean; onChange?: (checked: boolean) => void; label?: string; hint?: string; disabled?: boolean; }
export function Checkbox(props: CheckboxProps): React.ReactElement;
export interface ToggleProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "onChange" | "size"> { checked?: boolean; onChange?: (checked: boolean) => void; label?: string; disabled?: boolean; }
export function Toggle(props: ToggleProps): React.ReactElement;
