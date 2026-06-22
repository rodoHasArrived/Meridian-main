import { cn } from "@/lib/utils";

/**
 * Compact status label in font-mono uppercase, used for environment state, data posture,
 * and event classification throughout the operator workstation.
 *
 * **Variants:**
 * - `"default"` — primary/cyan, general status
 * - `"outline"` — muted, secondary metadata
 * - `"success"` / `"warning"` / `"danger"` — semantic state tones
 * - `"paper"` — paper (simulated) trading mode (blue)
 * - `"live"` — **LIVE environment alarm** using `--live-env` (alarm red). NOT the same as
 *   data-posture "live" (cyan). Workspace status `live` maps to `"success"` in view-models.
 * - `"research"` — research/backtest mode
 *
 * **Dot:** set `dot` to prepend a filled circle indicator matched to the variant color.
 *
 * @example
 * <Badge variant="live" dot>LIVE</Badge>
 * <Badge variant="paper">Paper</Badge>
 * <Badge variant="success" dot>Healthy</Badge>
 */
interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  variant?: "default" | "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research";
  dot?: boolean;
}

const variantClasses: Record<NonNullable<BadgeProps["variant"]>, string> = {
  default:  "border-primary/40 bg-primary/15 text-primary",
  outline:  "border-border bg-secondary/35 text-muted-foreground",
  success:  "border-success/35 bg-success/12 text-success",
  warning:  "border-warning/35 bg-warning/12 text-warning",
  danger:   "border-danger/35 bg-danger/12 text-danger",
  paper:    "border-paper/35 bg-paper/12 text-paper",
  // "live" variant = LIVE environment (real-money alarm). Uses --live-env (alarm red),
  // not --live (cyan data-posture). Workspace status "live" maps to "success" in view-models.
  live:     "border-live-env/40 bg-live-env/12 text-live-env",
  research: "border-primary/35 bg-primary/12 text-primary"
};

export function Badge({ children, className, dot = false, variant = "default", ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex min-h-6 items-center gap-1.5 rounded-[var(--radius-chip,0.25rem)] border px-2.5 py-1 font-mono text-[10px] font-semibold uppercase tracking-[0.14em] shadow-[var(--shadow-panel)]",
        variantClasses[variant],
        className
      )}
      {...props}
    >
      {dot ? <span aria-hidden="true" className="h-1.5 w-1.5 rounded-full bg-current" /> : null}
      {children}
    </span>
  );
}
