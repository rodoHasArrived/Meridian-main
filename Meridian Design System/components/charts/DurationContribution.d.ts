import * as React from "react";
export type DurationRank = "contribution" | "difference" | "weight" | "label";
export interface DurationContributionItem {
  id: string | number;
  label: string;
  /** Percent of portfolio value. Missing weights are exposed, never coerced to zero. */
  weight?: number;
  /** Instrument or group duration in `durationUnit` — not interchangeable with contribution. */
  duration?: number;
  /** Contribution in `contributionUnit`. Derived from weight × duration when omitted. */
  contribution?: number;
  /** Benchmark or prior-period contribution on compatible methodology. */
  comparisonContribution?: number;
  condition?: string;
  detail?: string;
  [key: string]: unknown;
}
export interface DurationContributionProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect"> {
  items: DurationContributionItem[];
  /** The duration definition in force, e.g. "Effective duration", "Spread duration". Always visible. */
  durationDefinition?: string;
  durationUnit?: string;
  contributionUnit?: string;
  comparisonLabel?: string;
  showComparison?: boolean;
  /** Stated aggregate duration; contributions are tied back to it and any residual is named. */
  total?: number;
  comparisonTotal?: number;
  /** Scenario name — rendered as a scenario, never as a forecast. */
  scenario?: string;
  /** What rows represent, e.g. "Sector", "Issuer", "Security". */
  groupBy?: string;
  /** Denominator for weight, e.g. "market value". */
  unit?: string;
  asOf?: string;
  source?: string;
  rank?: DurationRank;
  defaultRank?: DurationRank;
  onRankChange?: (rank: DurationRank) => void;
  selectedId?: string | number | null;
  onSelect?: (item: DurationContributionItem, index: number) => void;
  /** Double-click / trace affordance — wire to CalculationTrace. */
  onOpenTrace?: (item: DurationContributionItem) => void;
  showTable?: boolean;
  valueFormat?: (value: number) => React.ReactNode;
  description?: string;
}
export declare function DurationContribution(props: DurationContributionProps): React.JSX.Element;

/** Explicit missing/not-applicable/restricted or invalid required amounts render
 * an unavailable result without row labels, numeric bars, or derived table totals.
 * Restore usable inputs to recover the ordinary view. */
