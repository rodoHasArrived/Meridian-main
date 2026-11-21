import React from "react";
export interface TabDef { label: string; count?: number; disabled?: boolean; }
export interface TabsProps { tabs?: (TabDef | string)[]; /** controlled active index */ value?: number; defaultTab?: number; onChange?: (index: number, tab: TabDef | string) => void; children?: React.ReactNode; }
export function Tabs(props: TabsProps): React.ReactElement;
export function TabPanel(props: { children: React.ReactNode }): React.ReactElement;

/** 3.5 · aria-controls is emitted only for panels that exist — a tabs array used as a bare
 *  switcher (no panel children) no longer references missing ids. */
