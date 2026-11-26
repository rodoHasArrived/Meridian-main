import { PeriodComparisonPanel } from "@/components/meridian/period-comparison-panel";
import { getRunAttributionPeriodComparison, type PeriodComparison } from "@/lib/api/period-comparison.api";
import type { ApiRequestOptions } from "@/lib/api";
import { defaultMaterialityPolicyForClass } from "@/lib/reporting-materiality";
import { usePeriodComparison } from "@/lib/use-period-comparison";

type AttributionComparisonService = (runId: string, periodEnd?: string | null, options?: ApiRequestOptions) => Promise<PeriodComparison>;

const ATTRIBUTION_MATERIALITY = defaultMaterialityPolicyForClass("Portfolio");

/**
 * The selected run's per-symbol P&L for its latest month against the month before (UX Phase E),
 * from the run's daily snapshots. Split out of portfolio-screen.tsx as that screen was touched.
 */
export function RunAttributionPeriodComparison({ runId, service = getRunAttributionPeriodComparison }: {
  runId: string | null | undefined;
  service?: AttributionComparisonService;
}) {
  const comparison = usePeriodComparison(runId ?? null, (options) => service(runId!, null, options));
  if (!runId) return null;
  return (
    <div className="mt-3">
      <PeriodComparisonPanel title="P&L by symbol vs prior month" workbenchId="portfolio-attribution-period-comparison"
        state={comparison} onRetry={comparison.retry} policy={ATTRIBUTION_MATERIALITY} currentLabel="This month" />
    </div>
  );
}
