import { cn } from "@/lib/utils";
import type { MetricSnapshot } from "@/types";

const toneClass: Record<MetricSnapshot["tone"], string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

export function MetricCard({ label, value, delta, tone }: MetricSnapshot) {
  return (
    <div className="metric-tile">
      <div className="flex items-center justify-between gap-3">
        <p className="font-mono text-[10px] font-medium uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
        <span className={cn("font-mono text-[10px]", toneClass[tone])}>{delta}</span>
      </div>
      <p className={cn("mt-2 font-mono text-[1.3125rem] font-medium leading-none", toneClass[tone])}>{value}</p>
    </div>
  );
}
