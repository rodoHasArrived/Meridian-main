import { forwardRef } from "react";
import { cn } from "@/lib/utils";

export const Eyebrow = forwardRef<HTMLSpanElement, React.HTMLAttributes<HTMLSpanElement>>(
  ({ className, ...props }, ref) => (
    <span
      ref={ref}
      className={cn("font-mono text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground", className)}
      {...props}
    />
  )
);

Eyebrow.displayName = "Eyebrow";
