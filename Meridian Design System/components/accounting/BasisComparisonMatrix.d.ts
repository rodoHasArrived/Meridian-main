import * as React from "react";
export type BasisDifferenceMode = "none" | "absolute" | "percent" | "bp";
export interface BasisColumn {
  id: string;
  label: string;
  /** Secondary line under the column head, e.g. "SSAP 26" or "ASC 320". */
  meta?: string;
}
export interface BasisCell {
  /** Numbers render in the matrix notation; strings render verbatim (classifications, methods). */
  value: React.ReactNode;
  /** Measurement method, e.g. "Amortized cost", "Fair value". */
  method?: string;
  source?: string;
  asOf?: string;
  /** One of the eleven data conditions. "na" renders "n/a"; absent renders "—". */
  condition?: string;
  /** Override description, surfaced under the value. */
  override?: string;
  [key: string]: unknown;
}
export interface BasisComparisonRow {
  id?: string;
  label: string;
  /** Unit for the row, e.g. "USD", "%" — rows never mix units across bases. */
  unit?: string;
  note?: string;
  /** Word used for non-numeric differences: "Classification", "Method", "Timing". */
  differenceKind?: string;
  /** Marks the row as method-valued so differences read as method differences. */
  method?: boolean;
  values: Record<string, BasisCell | React.ReactNode>;
}
export interface BasisComparisonSelection {
  row: BasisComparisonRow;
  basis: BasisColumn;
  cell: BasisCell;
  key: string;
  reference: BasisCell;
}
export interface BasisComparisonMatrixProps extends React.HTMLAttributes<HTMLElement> {
  bases: BasisColumn[];
  rows: BasisComparisonRow[];
  attributeLabel?: string;
  /** Basis every difference is measured against. Defaults to the first column; always named on the face. */
  referenceBasis?: string;
  title?: string;
  /** The economic object being compared, e.g. "CUSIP 912828ZK5". */
  object?: string;
  asOf?: string;
  period?: string;
  /** "accounting" renders negatives in parentheses. */
  notation?: "accounting" | "plain";
  differenceMode?: BasisDifferenceMode;
  defaultDifferenceMode?: BasisDifferenceMode;
  onDifferenceModeChange?: (mode: BasisDifferenceMode) => void;
  /** Selected cell key, "<rowId>::<basisId>". */
  selectedCell?: string | null;
  onSelectCell?: (selection: BasisComparisonSelection) => void;
  /** Wire to CalculationTrace. */
  onOpenTrace?: (selection: Omit<BasisComparisonSelection, "key" | "reference">) => void;
  onOpenEvidence?: (selection: Omit<BasisComparisonSelection, "key" | "reference">) => void;
  /** Table caption — the exportable textual description. */
  description?: string;
}
export declare function BasisComparisonMatrix(props: BasisComparisonMatrixProps): React.JSX.Element;
