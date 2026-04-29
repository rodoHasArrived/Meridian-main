import { cn } from "@/lib/utils";
import { buildMetricCardViewModel } from "@/components/meridian/metric-card.view-model";
import type { MetricSnapshot } from "@/types";

export function MetricCard(metric: MetricSnapshot) {
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
