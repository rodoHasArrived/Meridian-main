import { cn } from "@/lib/utils";

interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  variant?: "default" | "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research";
}

const variantClasses: Record<NonNullable<BadgeProps["variant"]>, string> = {
  default: "border-primary/40 bg-primary/15 text-primary",
  outline: "border-border bg-secondary/35 text-muted-foreground",
  success: "border-success/35 bg-success/12 text-success",
  warning: "border-warning/35 bg-warning/12 text-warning",
  danger: "border-danger/35 bg-danger/12 text-danger",
  paper: "border-paper/35 bg-paper/12 text-paper",
  live: "border-live/35 bg-live/12 text-live",
  research: "border-primary/35 bg-primary/12 text-primary"
};

export function Badge({ className, variant = "default", ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full border px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em]",
        variantClasses[variant],
        className
      )}
      {...props}
    />
  );
}
