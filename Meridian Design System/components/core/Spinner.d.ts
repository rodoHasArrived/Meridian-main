/**
 * Minimal async loading spinner — a thin ring with a semantic top stroke. Motion is its
 * function, so it rotates (and slows under `prefers-reduced-motion`). Pass `label` to render
 * an inline "spinner + text" row.
 */
export interface SpinnerProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Diameter. @default "md" */
  size?: "sm" | "md" | "lg";
  /** Spinner accent. `onDark` for use over the near-black masthead. @default "accent" */
  variant?: "accent" | "muted" | "onDark";
  /** Optional text rendered beside the ring; also becomes the aria-label. */
  label?: string;
}
export declare function Spinner(props: SpinnerProps): JSX.Element;
