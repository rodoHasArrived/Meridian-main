import { cn } from "@/lib/utils";
import type { MetricSnapshot } from "@/types";
import { useKpiModels } from "@/lib/kpi-context";
import { kpiAttributes, resolveKpi, type KpiId } from "@/lib/kpi-registry";

const statToneClass: Record<MetricSnapshot["tone"], string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

/**
 * Compact single-row alternative to the four-card metric band. Workspace
 * routes lead with their working surface, so headline metrics compress into
 * one strip; each stat stays scannable without spending a viewport band.
 */
export function StatStrip({
  ids,
  label,
  className,
  compact = false
}: {
  ids: readonly KpiId[];
  label: string;
  className?: string;
  compact?: boolean;
}) {
  const models = useKpiModels();
  const metrics = ids.map((id) => resolveKpi(id, models));
  if (metrics.length === 0) {
    return null;
  }

  return (
    <dl
      aria-label={label}
      className={cn(
        "panel-surface px-4 py-2.5",
        compact ? "grid grid-cols-[repeat(auto-fit,minmax(min(100%,10rem),1fr))] gap-3" : "flex flex-wrap items-baseline gap-x-6 gap-y-1.5",
        className
      )}
    >
      {metrics.map((metric) => (
        <div key={metric.id} {...kpiAttributes(metric)} title={metric.delta || undefined} className={cn("flex min-w-0 gap-2", compact ? "flex-col gap-0.5" : "items-baseline")}>
          <dt className="whitespace-nowrap text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground">
            {metric.label}
          </dt>
          <dd className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
            <span data-kpi-rendered-value className={cn("whitespace-nowrap font-mono text-sm font-semibold", statToneClass[metric.tone])}>
              {metric.value}
            </span>
            {metric.delta ? (
              <span className="whitespace-nowrap text-xs text-muted-foreground">{metric.delta}</span>
            ) : null}
          </dd>
        </div>
      ))}
    </dl>
  );
}
