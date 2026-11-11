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
  /** Muted context line after the delta. */
  context?: string;
  /** Larger value type for the lead tile. @default false */
  hero?: boolean;
  /** Inline hairline trend sparkline values. */
  sparkline?: number[];
  /** @default "muted" */
  sparklineTone?: "muted" | "success" | "danger" | "warning" | "info";
  /** 4.0 · Value-envelope facets for the tile's figure. When present, the value renders as a
   *  `ValueEnvelope` (label and value come from the tile) — condition, authority, evidence,
   *  clocks and materiality gain the standard marks and hover envelope. Reserve for stated
   *  figures (P&L, totals, tie-out differences); counts don't need one. */
  envelope?: {
    condition?: string; detail?: string; authority?: string;
    evidence?: { label: string; ref?: string; href?: string }[];
    clocks?: Record<string, string>;
    materiality?: { material: boolean; threshold?: string };
  };
  /** 3.5 · Text reading of the sparkline (e.g. "trend over 30 sessions, rising") — appended to
   *  the group's accessible name, which already carries value, trend + delta, context, and tone. */
  sparklineLabel?: string;
}
export declare function MetricCard(props: MetricCardProps): JSX.Element;
