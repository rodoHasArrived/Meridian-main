import React from "react";

/**
 * A worksheet cell. Addressed by `"<column key><row number>"` (e.g. `"E2"`), row numbers 1-based.
 * `type` drives presentation: `label`/`text` render in the body font (left-aligned by default);
 * everything else renders mono + right-aligned. `formula` cells get an accent corner mark and
 * surface their `formula` in the formula bar; `error` cells tint with the red wash.
 */
export interface WorksheetCell {
  /** Displayed value. */
  value?: React.ReactNode;
  /** Formula string shown in the formula bar when the cell is active. */
  formula?: string;
  /** Presentation type. `num`/`pct` are accepted aliases of `number`/`percent`. */
  type?: "label" | "text" | "number" | "num" | "percent" | "pct" | "formula" | "error";
}

export interface WorksheetColumn {
  /** Column key — the letter used in cell refs (e.g. `"A"`). */
  key: string;
  /** Header label (defaults to `key`). */
  label?: React.ReactNode;
  /** Column width in px. @default 92 */
  width?: number;
  /** Override alignment (defaults from cell type). */
  align?: "left" | "right";
}

export interface WorksheetGridProps {
  /** Column definitions — objects or bare key strings. */
  columns: (WorksheetColumn | string)[];
  /** Number of data rows (numbered 1…rows). */
  rows: number;
  /** Cell map keyed by ref (e.g. `{ E2: { value: "4.61%", formula: "=YTM(B2)", type: "formula" } }`). */
  cells: Record<string, WorksheetCell>;
  /** Active cell ref (controlled). Omit to run uncontrolled. */
  activeCell?: string | null;
  /** Initial active ref when uncontrolled. */
  defaultActiveCell?: string | null;
  /** Fired with the newly selected ref (click or arrow-key). */
  onActiveCellChange?: (ref: string) => void;
  /** Show the Excel-style formula bar above the grid. @default true */
  showFormulaBar?: boolean;
  /** Data row height in px. @default 32 */
  rowHeight?: number;
  /** Header row height in px. @default 28 */
  headerHeight?: number;
  /** Row-number gutter width in px. @default 38 */
  rowHeaderWidth?: number;
  /** Placeholder shown in the formula bar for an empty/blank active cell. @default "Empty cell" */
  emptyFormulaText?: React.ReactNode;
  /** Allow inline editing — click selects, double-click or type to edit, Enter/Tab commits & advances, Escape cancels. @default false */
  editable?: boolean;
  /** Fired on commit with the cell ref and the raw draft string (e.g. `("E2", "=YTM(B2)")`). */
  onCellCommit?: (ref: string, value: string) => void;
  style?: React.CSSProperties;
}

export declare function WorksheetGrid(props: WorksheetGridProps): JSX.Element;
