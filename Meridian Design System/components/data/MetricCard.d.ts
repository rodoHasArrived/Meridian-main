/**
 * KPI tile — mirrors `MetricCardStyle` + its semantic variants. Raised paper surface with a
 * **3px left-accent border** in the tone color, small-caps label, 24px mono value, signed delta.
 */
export interface MetricCardProps {
  /** Small-caps label, e.g. "Net liquidation". */
  label: string;
  /** Mono tabular value, e.g. "$1,284,002.18". */
  value: string;
  /** Signed delta/context line, e.g. "+1.84% today". */
  delta?: string;
  /** Left-accent color. @default "neutral" */
  tone?: "neutral" | "info" | "success" | "warning" | "danger";
  /** Force delta color; otherwise inferred from a leading +/− sign. */
  trend?: "up" | "down" | "flat";
}
export declare function MetricCard(props: MetricCardProps): JSX.Element;
