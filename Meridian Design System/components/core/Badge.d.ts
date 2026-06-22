/**
 * Compact tag badge — mirrors `NeutralBadgeStyle` + the semantic badge styles. Small-caps,
 * 4px radius, alpha-10 fill with a solid semantic border and dim text. Used for status and
 * the always-visible environment mode (LIVE / PAPER / FIXTURE).
 */
export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  /**
   * `neutral` · `info` (blue) · `success` · `warning` · `danger` ·
   * mode tags `live` (red), `paper` (blue), `fixture` (amber).
   * @default "neutral"
   */
  variant?: "neutral" | "info" | "success" | "warning" | "danger" | "live" | "paper" | "fixture";
  /** Prepend a filled status dot. @default false */
  dot?: boolean;
  children?: React.ReactNode;
}
export declare function Badge(props: BadgeProps): JSX.Element;
