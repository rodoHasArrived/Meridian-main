import { memo } from "react";
import { MetricTile, type MetricTileTone } from "@/components/data/metric-tile";
import { buildMetricCardViewModel } from "@/components/meridian/metric-card.view-model";
import type { MetricSnapshot } from "@/types";

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
      value={vm.value}
      delta={vm.delta ?? undefined}
      tone={metricSnapshotTone[metric.tone]}
      trend={deriveTrend(vm.deltaAriaLabel)}
      ariaLabel={vm.ariaLabel}
      deltaAriaLabel={vm.deltaAriaLabel ?? undefined}
    />
  );
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
