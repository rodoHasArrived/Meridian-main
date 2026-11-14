import { memo } from "react";
import { MetricTile, type MetricTileTone } from "@/components/data/metric-tile";
import { buildMetricCardViewModel } from "@/components/meridian/metric-card.view-model";
import { useKpi } from "@/lib/kpi-context";
import { kpiAttributes, type KpiId, type KpiScope } from "@/lib/kpi-registry";
import type { MetricSnapshot } from "@/types";
import { MetricPassport } from "@/components/meridian/metric-passport";
import { hasDeclaredMetricBasis, metricBasisLabel } from "@/lib/metric-context";

export type MetricCardTone = MetricTileTone;

/** Shared KPI cards resolve their value, label, units and scope from the registry. */
export interface MetricCardProps {
  id: KpiId;
  scope?: KpiScope;
}

const metricTone: Record<MetricSnapshot["tone"], MetricTileTone> = {
  default: "neutral",
  success: "success",
  warning: "warning",
  danger: "danger"
};

function MetricCardComponent({ id, scope }: MetricCardProps) {
  const metric = useKpi(id, scope);
  const view = buildMetricCardViewModel(metric);
  const trend = view.deltaAriaLabel?.includes("Change up")
    ? "up"
    : view.deltaAriaLabel?.includes("Change down") ? "down" : "flat";

  return (
    <div {...kpiAttributes(metric)}>
      <MetricTile
        label={view.label}
        value={<span className="inline-flex items-center gap-1"><span data-kpi-rendered-value>{view.value}</span><MetricPassport metric={metric} /></span>}
        delta={view.delta ?? undefined}
        tone={metricTone[metric.tone]}
        trend={trend}
        ariaLabel={view.ariaLabel}
        deltaAriaLabel={view.deltaAriaLabel ?? undefined}
        freshnessLabel={view.freshnessLabel ?? undefined}
        provenance={view.provenanceText ?? undefined}
        basis={hasDeclaredMetricBasis(metric) ? metricBasisLabel(metric) : metric.asOf?.trim() ? `As of ${metric.asOf}` : undefined}
      />
    </div>
  );
}

export const MetricCard = memo(MetricCardComponent);
