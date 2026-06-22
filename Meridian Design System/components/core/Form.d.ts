import React from "react";
export interface FormRowProps { label?: string; hint?: string; error?: string; horizontal?: boolean; span?: 1|2|3; children: React.ReactNode; }
export function FormRow(props: FormRowProps): React.ReactElement;
export interface FormGridProps { cols?: 1|2|3; children: React.ReactNode; }
export function FormGrid(props: FormGridProps): React.ReactElement;
export function FormDivider(): React.ReactElement;
export function FormSectionLabel(props: { children: React.ReactNode }): React.ReactElement;
