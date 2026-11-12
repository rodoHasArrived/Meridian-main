import { cn } from "@/lib/utils";

export interface SpinnerProps {
  size?: "sm" | "md" | "lg";
  variant?: "accent" | "muted" | "onDark";
  label?: string;
  className?: string;
}

const sizeClasses: Record<NonNullable<SpinnerProps["size"]>, string> = {
  sm: "h-3.5 w-3.5 border-2",
  md: "h-5 w-5 border-2",
  lg: "h-8 w-8 border-[3px]"
};

const variantClasses: Record<NonNullable<SpinnerProps["variant"]>, string> = {
  accent: "border-border border-t-primary",
  muted: "border-border border-t-muted-foreground",
  onDark: "border-white/25 border-t-white"
};

/**
 * Async loading indicator — a minimal ring with a semantic stroke. Motion is the whole
 * function here, so it animates (respecting `prefers-reduced-motion` via `motion-reduce`).
 */
export function Spinner({ size = "md", variant = "accent", label, className }: SpinnerProps) {
  const ring = (
    <span
      role="status"
      aria-label={label ?? "Loading"}
      className={cn(
        "inline-block shrink-0 animate-spin rounded-full motion-reduce:[animation-duration:1.6s]",
        sizeClasses[size],
        variantClasses[variant],
        className
      )}
    />
  );

  if (!label) {
    return ring;
  }

  return (
    <span className="inline-flex items-center gap-2 text-sm text-muted-foreground">
      {ring}
      <span>{label}</span>
    </span>
  );
}
