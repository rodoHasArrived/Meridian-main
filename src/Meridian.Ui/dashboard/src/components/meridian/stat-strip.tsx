import { cn } from "@/lib/utils";
import type { MetricSnapshot } from "@/types";
import { metricBasisLabel, presentMetricChange } from "@/lib/metric-context";
import { useId } from "react";
import { MetricPassport } from "@/components/meridian/metric-passport";

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
  className,
  compact = false
}: {
  metrics: MetricSnapshot[];
  label: string;
  className?: string;
  compact?: boolean;
}) {
  const basisId = useId();
  if (metrics.length === 0) {
    return null;
  }
  const firstBasis = metricBasisLabel(metrics[0]!);
  const sharedBasis = metrics.every(metric => metricBasisLabel(metric) === firstBasis) ? firstBasis : null;

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
        <div key={metric.id} className={cn("flex min-w-0 gap-2", compact ? "flex-col gap-0.5" : "items-baseline")}>
          <dt className="whitespace-nowrap text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground">
            {metric.label}
          </dt>
          <dd className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5" aria-describedby={sharedBasis ? basisId : undefined}>
            <span className={cn("whitespace-nowrap tabular-nums text-sm font-semibold", statToneClass[metric.tone])}>
              {metric.value}
            </span>
            <MetricPassport metric={metric} />
            {presentMetricChange(metric) ? (
              <span className="whitespace-nowrap text-xs text-muted-foreground">{presentMetricChange(metric)}</span>
            ) : null}
            {!sharedBasis ? <span className="text-[11px] text-muted-foreground" title={metric.source ? `Source: ${metric.source}` : "Source evidence has not been supplied for this metric."}>
              {metricBasisLabel(metric)}
            </span> : null}
          </dd>
        </div>
      ))}
      {sharedBasis ? <div className="flex min-w-0 items-baseline gap-2 text-[11px] text-muted-foreground">
        <dt>Basis</dt><dd id={basisId}>{sharedBasis}</dd>
      </div> : null}
    </dl>
  );
}
