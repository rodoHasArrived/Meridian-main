import { memo } from "react";
import { cn } from "@/lib/utils";
import { buildMetricCardViewModel } from "@/components/meridian/metric-card.view-model";
import type { MetricSnapshot } from "@/types";

/**
 * Displays a single `MetricSnapshot` as a compact key-value tile using the `.metric-tile`
 * CSS utility class (Track B surface, consistent with the workstation masthead palette).
 *
 * **Tones:** derived from `MetricSnapshot.tone` by `buildMetricCardViewModel`:
 * `"default"` (foreground), `"success"` (green), `"warning"` (amber), `"danger"` (red).
 *
 * Props are spread directly from `MetricSnapshot` — do not add wrapper props.
 * If a delta value is present, it is displayed alongside the primary value using the same
 * tone, and both receive `id` attributes for `aria-describedby` on the tile group.
 *
 * For an icon-bearing variant or a smaller tile size, extend `MetricSnapshot` with an
 * `icon` field or add a `size` variant to this component rather than creating a new one.
 *
 * @example
 * <MetricCard {...metric} />
 * // or with explicit spread:
 * <MetricCard id="pnl" label="Daily P&L" value="$4,210" tone="success" delta="+3.2%" />
 */
function MetricCardComponent(metric: MetricSnapshot) {
  const vm = buildMetricCardViewModel(metric);

  return (
    <div
      className="metric-tile"
      role="group"
      aria-label={vm.ariaLabel}
      aria-describedby={vm.deltaId ?? vm.valueId}
    >
      <div className="flex items-center justify-between gap-3">
        <p id={vm.labelId} className="font-mono text-[10px] font-medium uppercase tracking-[0.14em] text-muted-foreground">{vm.label}</p>
        {vm.delta && vm.deltaId && (
          <span id={vm.deltaId} className={cn("font-mono text-[10px]", vm.toneClass)} aria-label={vm.deltaAriaLabel ?? undefined}>{vm.delta}</span>
        )}
      </div>
      <p id={vm.valueId} className={cn("mt-2 font-mono text-[1.3125rem] font-medium leading-none", vm.toneClass)}>{vm.value}</p>
    </div>
  );
}

export const MetricCard = memo(MetricCardComponent);
