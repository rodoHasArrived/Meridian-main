import { cn } from "@/lib/utils";

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
        "inline-flex items-center gap-1.5 rounded-sm border px-2.5 py-1 font-mono text-[10px] font-medium uppercase tracking-[0.14em]",
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
