import * as React from "react";
/**
 * Analytical figure frame — the titled, sourced, condition-stamped container every Meridian
 * chart lives inside: question, unit, as-of, source, coverage, selected readout, method footnote.
 */
export interface AnalyticalFigureProps extends Omit<React.HTMLAttributes<HTMLElement>, "title">{title:React.ReactNode;question?:React.ReactNode;subtitle?:React.ReactNode;unit?:React.ReactNode;asOf?:React.ReactNode;source?:React.ReactNode;condition?:string;continuityState?:string;age?:React.ReactNode;coverage?:React.ReactNode;actions?:React.ReactNode;selectedLabel?:React.ReactNode;selectedValue?:React.ReactNode;annotation?:React.ReactNode;note?:React.ReactNode;method?:React.ReactNode;fallback?:React.ReactNode;
  /** `"standard"` (default) is the full figure. `"small-multiple"` compacts head,
   *  readout, and foot for tiling in an `AnalyticalFigureGrid` — the question and
   *  the exact-values disclosure are dropped because the grid declares them once.
   *  `"print"` flattens to paper: white plot, expanded exact-values table, and a
   *  break-inside guard. */
  variant?: "standard" | "small-multiple" | "print";
}
export declare function AnalyticalFigure(props:AnalyticalFigureProps):React.JSX.Element;

export interface AnalyticalFigureGridProps extends Omit<React.HTMLAttributes<HTMLElement>, "title">{
  title?:React.ReactNode;
  /** State the shared scale explicitly, e.g. "0–6% yield, common y-axis". Omitted,
   *  the grid prints the standing warning that scales differ. */
  sharedScale?:React.ReactNode;
  columns?:number;
  source?:React.ReactNode;
  note?:React.ReactNode;
  actions?:React.ReactNode;
}
/** Tiles `variant="small-multiple"` figures under one declared scale, source, and note. */
export declare function AnalyticalFigureGrid(props:AnalyticalFigureGridProps):React.JSX.Element;
