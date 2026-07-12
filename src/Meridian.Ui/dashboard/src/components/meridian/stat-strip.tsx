import { cn } from "@/lib/utils";
import type { MetricSnapshot } from "@/types";

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
  metrics,
  label,
  className
}: {
  metrics: MetricSnapshot[];
  label: string;
  className?: string;
}) {
  if (metrics.length === 0) {
    return null;
  }

  return (
    <dl
      aria-label={label}
      className={cn(
        "panel-surface flex flex-wrap items-baseline gap-x-6 gap-y-1.5 px-4 py-2.5",
        className
      )}
    >
      {metrics.map((metric) => (
        <div key={metric.id} className="flex min-w-0 items-baseline gap-2">
          <dt className="whitespace-nowrap text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground">
            {metric.label}
          </dt>
          <dd className="flex items-baseline gap-2">
            <span className={cn("whitespace-nowrap font-mono text-sm font-semibold", statToneClass[metric.tone])}>
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
