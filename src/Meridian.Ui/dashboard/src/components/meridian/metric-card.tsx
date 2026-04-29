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
    <div className="rounded-lg border border-border/70 bg-secondary/30 p-4">
      <div className="flex items-center justify-between gap-3">
        <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
        <span className={cn("font-mono text-xs", toneClass[tone])}>{delta}</span>
      </div>
      <p className={cn("mt-3 font-mono text-2xl font-semibold", toneClass[tone])}>{value}</p>
    </div>
  );
}
