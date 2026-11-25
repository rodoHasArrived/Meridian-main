import { memo } from "react";
import { MetricTile, type MetricTileTone } from "@/components/data/metric-tile";
import { buildMetricCardViewModel } from "@/components/meridian/metric-card.view-model";
import type { MetricSnapshot } from "@/types";
import { hasDeclaredMetricBasis, metricBasisLabel } from "@/lib/metric-context";
import { MetricPassport } from "@/components/meridian/metric-passport";

/**
 * Displays a `MetricSnapshot` through the presentational `MetricTile`.
 * Snapshot-specific derivation stays in `buildMetricCardViewModel`; rendering
 * stays with the design-system primitive.
 *
 * @example
 * <SnapshotMetricTile {...metric} />
 */
function SnapshotMetricTileComponent(metric: MetricSnapshot) {
  const vm = buildMetricCardViewModel(metric);

  return (
    <MetricTile
      label={vm.label}
      value={<span className="inline-flex items-center gap-1">{vm.value}<MetricPassport metric={metric} /></span>}
      delta={vm.delta ?? undefined}
      tone={metricSnapshotTone[metric.tone]}
      trend={deriveTrend(vm.deltaAriaLabel)}
      ariaLabel={vm.ariaLabel}
      deltaAriaLabel={vm.deltaAriaLabel ?? undefined}
      freshnessLabel={vm.freshnessLabel ?? undefined}
      provenance={vm.provenanceText ?? undefined}
      // An undeclared basis is not repeated on every card; the figure passport states it. A
      // top-level as-of time stays visible on its own, since the provenance line does not carry it.
      basis={hasDeclaredMetricBasis(metric) ? metricBasisLabel(metric) : metricAsOfLine(metric)}
    />
  );
}

function metricAsOfLine(metric: MetricSnapshot): string | undefined {
  const asOf = metric.asOf?.trim();
  return asOf ? `As of ${asOf}` : undefined;
}

const metricSnapshotTone: Record<MetricSnapshot["tone"], MetricTileTone> = {
  default: "neutral",
  success: "success",
  warning: "warning",
  danger: "danger"
};

function deriveTrend(deltaAriaLabel: string | null): "up" | "down" | "flat" {
  if (deltaAriaLabel?.includes("Change up")) {
    return "up";
  }
  if (deltaAriaLabel?.includes("Change down")) {
    return "down";
  }
  return "flat";
}

export const SnapshotMetricTile = memo(SnapshotMetricTileComponent);
