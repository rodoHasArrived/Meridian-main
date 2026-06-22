import { forwardRef } from "react";
import { cn } from "@/lib/utils";

export interface PanelSurfaceProps extends React.HTMLAttributes<HTMLDivElement> {
  elevated?: boolean;
  flat?: boolean;
  raised?: boolean;
}

export const PanelSurface = forwardRef<HTMLDivElement, PanelSurfaceProps>(
  ({ className, elevated = false, flat = false, raised = false, ...props }, ref) => (
    <div
      ref={ref}
      className={cn(
        "rounded-[var(--radius-card,0.5rem)] border border-border bg-card text-card-foreground shadow-[var(--shadow-panel)]",
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
