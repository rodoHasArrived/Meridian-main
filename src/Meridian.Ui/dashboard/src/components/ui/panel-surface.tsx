import { forwardRef } from "react";
import { cn } from "@/lib/utils";

export interface PanelSurfaceProps extends React.HTMLAttributes<HTMLDivElement> {
  elevated?: boolean;
  flat?: boolean;
  raised?: boolean;
  tone?: PanelSurfaceTone;
}

export type PanelSurfaceTone = "default" | "success" | "warning" | "danger" | "muted";

const toneClasses: Record<PanelSurfaceTone, string> = {
  default: "border-border bg-card",
  success: "border-success/35 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10",
  muted: "border-border/70 bg-secondary/25"
};

export const PanelSurface = forwardRef<HTMLDivElement, PanelSurfaceProps>(
  ({ className, elevated = false, flat = false, raised = false, tone = "default", ...props }, ref) => (
    <div
      ref={ref}
      className={cn(
        "rounded-[var(--radius-card,0.5rem)] border text-card-foreground shadow-[var(--shadow-panel)]",
        toneClasses[tone],
        raised && "bg-muted/35",
        elevated && "shadow-[var(--shadow-float)]",
        flat && "shadow-none",
        className
      )}
      {...props}
    />
  )
);

PanelSurface.displayName = "PanelSurface";
