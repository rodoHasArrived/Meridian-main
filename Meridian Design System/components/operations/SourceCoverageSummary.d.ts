import * as React from "react";
export type CoverageCategory = "confirmed" | "provisional" | "stale" | "missing" | "na";
export interface SourceCoverageItem { id?: string; label: string; condition?: string; state?: string; age?: string; [key: string]: unknown }
export interface SourceCoverageSummaryProps extends Omit<React.HTMLAttributes<HTMLElement>, "onSelect" | "title"> {
  /** The population, one entry per real item — the Field is never decorative. */
  sources?: SourceCoverageItem[];
  /** Counts instead of items, for populations too large to enumerate. Requires `total`. */
  counts?: Partial<Record<CoverageCategory, number>>;
  /** Population size. Defaults to the sum of the categories. Required denominator when using `counts`. */
  total?: number;
  compact?: boolean;
  title?: React.ReactNode;
  onSelect?: (source: SourceCoverageItem, index: number) => void;
  /** Filter the underlying population to a category — "show the 3 stale documents". */
  onFilter?: (category: CoverageCategory, info: { count: number; required: number; population: number }) => void;
  selectedCategory?: CoverageCategory | null;
  /** Stated on the face of the component, e.g. "24h for pricing, 2 business days for statements". */
  freshnessThreshold?: React.ReactNode;
  /** Whether coverage counts records or weights by value. */
  weighting?: "record" | "value";
  weightLabel?: React.ReactNode;
  unit?: string;
  asOf?: React.ReactNode;
  showField?: boolean;
  showNote?: boolean;
}
export declare function SourceCoverageSummary(props: SourceCoverageSummaryProps): React.JSX.Element;
