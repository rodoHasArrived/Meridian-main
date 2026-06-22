import React from "react";
export interface CheckboxProps { checked?: boolean; onChange?: (checked: boolean) => void; label?: string; hint?: string; disabled?: boolean; }
export function Checkbox(props: CheckboxProps): React.ReactElement;
export interface ToggleProps { checked?: boolean; onChange?: (checked: boolean) => void; label?: string; disabled?: boolean; }
export function Toggle(props: ToggleProps): React.ReactElement;
