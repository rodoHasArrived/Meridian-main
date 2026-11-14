import { StatStripPresentation } from "@/components/meridian/stat-strip.presentation";
import { useKpis } from "@/lib/kpi-context";
import type { KpiId } from "@/lib/kpi-registry";
import type { FigureSourceKey } from "@/lib/workspace-figure-context";
import type { MetricFreshness, MetricProvenance, MetricSnapshot } from "@/types";
import { combineMetricFreshness } from "@/lib/metric-trust";

export type KpiMetricContext = Partial<Pick<MetricSnapshot, "freshness" | "provenance" | "delta" | "baseline" | "basis">>;

export interface StatStripProps {
  ids: readonly KpiId[];
  label: string;
  className?: string;
  compact?: boolean;
  /** Optional page-owned evidence; values and scope identity remain registry-owned. */
  freshness?: MetricFreshness | null;
  sourceContext?: MetricProvenance | null;
  contexts?: Partial<Record<KpiId, KpiMetricContext>>;
  source?: string;
  figureSource?: FigureSourceKey;
  scope?: string | null;
}

/** Headline strips accept KPI identities; their read models own every displayed value. */
export function StatStrip({ ids, contexts, ...presentation }: StatStripProps) {
  const metrics = useKpis(ids).map(metric => {
    const context = contexts?.[metric.id];
    if (!context) return metric;
    return {
      ...metric,
      delta: context.delta ?? metric.delta,
      baseline: context.baseline ?? metric.baseline,
      basis: context.basis ?? metric.basis,
      provenance: context.provenance ?? metric.provenance,
      freshness: combineMetricFreshness(metric.freshness, context.freshness)
    };
  });
  return <StatStripPresentation {...presentation} metrics={metrics} registryOwned />;
}
