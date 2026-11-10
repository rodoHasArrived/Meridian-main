import { Clock3 } from "lucide-react";
import { cn } from "@/lib/utils";
import { normalizeFinancialLabel } from "@/lib/format";
import { buildMetricTrust, describeMetricProvenance, sharedMetricProvenance, withMetricContext, worstMetricFreshness } from "@/lib/metric-trust";
import { useWorkspaceFigureContext } from "@/lib/workspace-figure-context";
import type { MetricFreshness, MetricSnapshot } from "@/types";
import { usesTrackedCaps } from "@/design-system/label-case";

const statToneClass: Record<MetricSnapshot["tone"], string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

const degradedStripLabel: Record<Exclude<MetricFreshness, "fresh">, string> = {
  stale: "Stale figures",
  unverified: "Unverified figures"
};

/**
 * Compact single-row alternative to the four-card metric band. Workspace
 * routes lead with their working surface, so headline metrics compress into
 * one strip; each stat stays scannable without spending a viewport band.
 *
 * The strip applies the headline-figure trust rules from `@/lib/metric-trust`: a change is shown
 * only with what it is measured against, a figure fed by stale or unverified data is muted and
 * carries a clock, and the source, as-of time and scope the figures share are stated once under
 * the row (each figure's own provenance is on hover when they differ).
 *
 * Pass `source` (and `scope` when the page has one) for a strip fed by the workstation load: the
 * as-of time and freshness then come from the shell. A screen with its own freshness evidence
 * stamps its metrics with `withMetricContext` instead and leaves `source` off.
 */
export function StatStrip({
  metrics: suppliedMetrics,
  label,
  className,
  compact = false,
  source,
  scope
}: {
  metrics: MetricSnapshot[];
  label: string;
  className?: string;
  compact?: boolean;
  /** The read model behind the strip, e.g. "Strategy workspace". */
  source?: string;
  /** The accounts, fund or run the strip covers. */
  scope?: string | null;
}) {
  const workspace = useWorkspaceFigureContext();
  if (suppliedMetrics.length === 0) {
    return null;
  }

  const stamped = source
    ? withMetricContext(suppliedMetrics, {
        provenance: { source, asOf: workspace?.asOf ?? null, scope: scope ?? null },
        freshness: workspace?.freshness ?? null
      })
    : suppliedMetrics;
  // A session that is not on real data says so in every figure's source.
  const dataLabel = workspace?.dataLabel;
  const metrics = dataLabel
    ? stamped.map((metric) => metric.provenance
        ? { ...metric, provenance: { ...metric.provenance, source: `${metric.provenance.source} (${dataLabel})` } }
        : metric)
    : stamped;
  const sharedProvenance = describeMetricProvenance(sharedMetricProvenance(metrics));
  const stripFreshness = worstMetricFreshness(metrics);
  const footnote = [
    stripFreshness === "fresh" ? null : degradedStripLabel[stripFreshness],
    sharedProvenance
  ].filter(Boolean).join(" · ");

  return (
    <div className={cn("panel-surface px-4 py-2.5", className)}>
      <dl
        aria-label={label}
        className={compact ? "grid grid-cols-[repeat(auto-fit,minmax(min(100%,10rem),1fr))] gap-3" : "flex flex-wrap items-baseline gap-x-6 gap-y-1.5"}
      >
        {metrics.map((metric) => {
          const trust = buildMetricTrust(metric);
          const metricLabel = normalizeFinancialLabel(metric.label);

          return (
            <div
              key={metric.id}
              className={cn("flex min-w-0 gap-2", compact ? "flex-col gap-0.5" : "items-baseline")}
              title={trust.title ?? undefined}
              data-freshness={trust.freshness}
            >
              <dt className={cn("whitespace-nowrap text-[11px] font-medium text-muted-foreground", usesTrackedCaps(metricLabel) && "uppercase tracking-[0.08em]")}>
                {metricLabel}
              </dt>
              <dd className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
                <span
                  className={cn(
                    "inline-flex items-baseline gap-1 whitespace-nowrap font-mono text-sm font-semibold tabular-nums",
                    trust.degraded ? "text-muted-foreground" : statToneClass[metric.tone]
                  )}
                >
                  {trust.degraded ? <Clock3 className="size-3 self-center" aria-hidden="true" /> : null}
                  {metric.value}
                </span>
                {trust.freshnessDetail ? <span className="sr-only">{trust.freshnessDetail}</span> : null}
                {trust.delta ? (
                  <span className="whitespace-nowrap text-xs text-muted-foreground">{trust.delta.text}</span>
                ) : null}
                {!sharedProvenance && trust.provenanceText ? <span className="sr-only">{trust.provenanceText}</span> : null}
              </dd>
            </div>
          );
        })}
      </dl>
      {footnote ? (
        <p className="mt-1.5 text-xs leading-4 text-muted-foreground" data-testid="stat-strip-provenance">
          {footnote}
        </p>
      ) : null}
    </div>
  );
}
