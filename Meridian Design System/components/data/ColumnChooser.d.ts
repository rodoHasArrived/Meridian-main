import React from "react";
export interface ColumnDef { key: string; label: string; }
export interface ColumnChooserProps { columns?: ColumnDef[]; visible?: string[]; onChange?: (visibleKeys: string[]) => void; }
export function ColumnChooser(props: ColumnChooserProps): React.ReactElement;
