import React from "react";
export interface TabDef { label: string; count?: number; disabled?: boolean; }
export interface TabsProps { tabs?: (TabDef | string)[]; defaultTab?: number; onChange?: (index: number, tab: TabDef | string) => void; children?: React.ReactNode; }
export function Tabs(props: TabsProps): React.ReactElement;
export function TabPanel(props: { children: React.ReactNode }): React.ReactElement;
