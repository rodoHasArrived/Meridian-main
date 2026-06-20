import { forwardRef } from "react";
import { semanticPanelToneClass, type SemanticTone } from "@/components/ui/semantic-tone";
import { cn } from "@/lib/utils";

export interface PanelSurfaceProps extends React.HTMLAttributes<HTMLDivElement> {
  elevated?: boolean;
  flat?: boolean;
  raised?: boolean;
  tone?: PanelSurfaceTone;
}

export type PanelSurfaceTone = SemanticTone;

export const PanelSurface = forwardRef<HTMLDivElement, PanelSurfaceProps>(
  ({ className, elevated = false, flat = false, raised = false, tone = "default", ...props }, ref) => (
    <div
      ref={ref}
      className={cn(
        "rounded-[var(--radius-card,0.5rem)] border text-card-foreground shadow-[var(--shadow-panel)]",
        semanticPanelToneClass[tone],
        raised && tone === "default" && "bg-muted/35",
        elevated && "shadow-[var(--shadow-float)]",
        flat && "shadow-none",
        className
      )}
      {...props}
    />
  )
);

PanelSurface.displayName = "PanelSurface";
