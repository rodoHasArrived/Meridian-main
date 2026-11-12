import React from "react";
export interface CheckboxProps { id?: string; "aria-labelledby"?: string; "aria-describedby"?: string; "aria-invalid"?: React.AriaAttributes["aria-invalid"]; "aria-label"?: string; checked?: boolean; onChange?: (checked: boolean) => void; label?: string; hint?: string; disabled?: boolean; }
export function Checkbox(props: CheckboxProps): React.ReactElement;
export interface ToggleProps { id?: string; "aria-labelledby"?: string; "aria-describedby"?: string; "aria-invalid"?: React.AriaAttributes["aria-invalid"]; "aria-label"?: string; checked?: boolean; onChange?: (checked: boolean) => void; label?: string; disabled?: boolean; }
export function Toggle(props: ToggleProps): React.ReactElement;
