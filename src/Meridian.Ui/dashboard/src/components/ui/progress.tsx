import { forwardRef } from "react";
import { cn } from "@/lib/utils";

export interface ProgressProps extends React.HTMLAttributes<HTMLDivElement> {
  value?: number | null;
  max?: number;
  /** Overrides the fill color (e.g. a status tone) while keeping the track styling. */
  indicatorClassName?: string;
}

export const Progress = forwardRef<HTMLDivElement, ProgressProps>(
  ({ className, value = 0, max = 100, indicatorClassName, ...props }, ref) => {
    const safeMax = Number.isFinite(max) && max > 0 ? max : 100;
    const safeValue = Number.isFinite(value ?? 0) ? value ?? 0 : 0;
    const percent = Math.min(100, Math.max(0, (safeValue / safeMax) * 100));

    return (
      <div
        ref={ref}
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={safeMax}
        aria-valuenow={safeValue}
        className={cn("h-2 overflow-hidden rounded-[2px] border border-border bg-[#F3F6F9]", className)}
        {...props}
      >
        <div
          className={cn("h-full rounded-[2px] bg-primary transition-all duration-300", indicatorClassName)}
          style={{ width: `${percent}%` }}
        />
      </div>
    );
  },
);

Progress.displayName = "Progress";
