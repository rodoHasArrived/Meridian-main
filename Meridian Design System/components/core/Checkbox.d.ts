import React from "react";
export interface CheckboxProps { checked?: boolean; onChange?: (checked: boolean) => void; label?: string; hint?: string; disabled?: boolean; /** Accessible name for a checkbox rendered without a visible `label`. */ "aria-label"?: string; }
export function Checkbox(props: CheckboxProps): React.ReactElement;
export interface ToggleProps { checked?: boolean; onChange?: (checked: boolean) => void; label?: string; disabled?: boolean; }
export function Toggle(props: ToggleProps): React.ReactElement;
