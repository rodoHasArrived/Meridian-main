/**
 * Thin determinate / indeterminate progress bar — for batch operations, reconciliation runs,
 * sync status. Squared track with a semantic fill. Pass `value` 0–100 for determinate; omit
 * (or `null`) for an indeterminate sweep (respects `prefers-reduced-motion`).
 */
export interface ProgressBarProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Percent complete 0–100. Omit or `null` for indeterminate. @default null */
  value?: number | null;
  /** Optional caption shown above the track. */
  label?: string;
  /** Show the numeric percent (determinate only). @default false */
  showValue?: boolean;
  /** Fill color. @default "accent" */
  variant?: "accent" | "success" | "warning" | "danger";
  /** Track thickness. @default "md" */
  size?: "sm" | "md" | "lg";
}
export declare function ProgressBar(props: ProgressBarProps): JSX.Element;
