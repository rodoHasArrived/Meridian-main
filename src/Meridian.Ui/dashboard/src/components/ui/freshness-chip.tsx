import { useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import {
  buildFreshnessChipViewModel,
  type FreshnessChipInput
} from "@/components/ui/freshness-chip.view-model";

/**
 * Compact "how current is this number" indicator built on Badge. Feed it the last
 * successful update timestamp (lifecycle `lastSucceededAt` or a server as-of field)
 * plus a per-surface staleness budget; it re-evaluates its age on an internal tick
 * so a surface whose polling stops visibly ages. Intentionally not `aria-live` —
 * high-frequency surfaces update every couple of seconds.
 */
export interface FreshnessChipProps extends Omit<FreshnessChipInput, "now"> {
  className?: string;
}

const MAX_TICK_MS = 30_000;

export function FreshnessChip({ className, ...input }: FreshnessChipProps) {
  const now = useNowTick(Math.max(1000, Math.min(input.staleBudgetMs, MAX_TICK_MS)), input.timestamp);
  const vm = buildFreshnessChipViewModel({ ...input, now });

  return (
    <Badge
      aria-label={vm.ariaLabel}
      className={className}
      data-freshness-state={vm.state}
      dot={vm.dot}
      title={vm.title ?? undefined}
      variant={vm.badgeVariant}
    >
      {vm.text}
    </Badge>
  );
}

function useNowTick(intervalMs: number, resetKey: string | null): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    setNow(Date.now());
    const id = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(id);
  }, [intervalMs, resetKey]);

  return now;
}
