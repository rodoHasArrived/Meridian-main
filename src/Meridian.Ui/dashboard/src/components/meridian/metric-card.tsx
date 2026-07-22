import { memo } from "react";
import { MetricCard as ConcreteMetricCard, type MetricCardTone } from "@/components/data/metric-card";
import { buildMetricCardViewModel } from "@/components/meridian/metric-card.view-model";
import type { MetricSnapshot } from "@/types";

/**
 * Displays a `MetricSnapshot` through the canonical Concrete `MetricCard`.
 * Snapshot-specific derivation stays in `buildMetricCardViewModel`; rendering
 * stays with the design-system primitive.
 *
 * @example
 * <MetricSnapshotCard {...metric} />
 */
function MetricSnapshotCardComponent(metric: MetricSnapshot) {
  const vm = buildMetricCardViewModel(metric);

  return (
    <ConcreteMetricCard
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

const metricSnapshotTone: Record<MetricSnapshot["tone"], MetricCardTone> = {
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

export const MetricSnapshotCard = memo(MetricSnapshotCardComponent);
export const MetricCard = MetricSnapshotCard;
