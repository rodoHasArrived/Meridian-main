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
        "rounded-[2px] border border-border bg-card text-card-foreground",
        raised && "bg-[#F3F6F9]",
        elevated && "shadow-[0_2px_6px_rgba(0,0,0,0.18)]",
        flat && "shadow-none",
        className
      )}
      {...props}
    />
  )
);

PanelSurface.displayName = "PanelSurface";
