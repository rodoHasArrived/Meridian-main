import type React from "react";
/**
 * Thin determinate / indeterminate progress bar — the system's ONE bar: batch operations,
 * reconciliation runs, sync status, and any bounded ratio (utilisation, coverage, funded
 * share). Squared track with a semantic fill. Pass `value` (with `max` if not 0–100) for
 * determinate; omit (or `null`) for an indeterminate sweep (respects
 * `prefers-reduced-motion`). `target` draws a dotted datum at the reference ratio.
 */
export interface ProgressBarProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Progress value. Omit or `null` for indeterminate. @default null */
  value?: number | null;
  /** Value that reads as 100%. @default 100 */
  max?: number;
  /** Reference/threshold value, drawn as a dotted datum in the track. @default null */
  target?: number | null;
  /** Optional caption shown above the track. */
  label?: string;
  /** Show the numeric percent (determinate only). @default false */
  showValue?: boolean;
  /** Fill color. @default "accent" */
  variant?: "accent" | "success" | "warning" | "danger";
  /** Track thickness. @default "md" */
  size?: "sm" | "md" | "lg";
}
export declare function ProgressBar(props: ProgressBarProps): React.JSX.Element;
