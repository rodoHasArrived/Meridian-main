import { useState } from "react";
import { Button } from "@/components/ui/button";
import { PeriodComparisonPanel } from "@/components/meridian/period-comparison-panel";
import { getCashLadderComparison, type PeriodComparison } from "@/lib/api/period-comparison.api";
import type { ApiRequestOptions } from "@/lib/api";
import { defaultMaterialityPolicyForClass } from "@/lib/reporting-materiality";
import { usePeriodComparison } from "@/lib/use-period-comparison";

type CashLadderComparisonService = (query: Parameters<typeof getCashLadderComparison>[0], options?: ApiRequestOptions) => Promise<PeriodComparison>;

const CASH_MATERIALITY = defaultMaterialityPolicyForClass("Portfolio");

/**
 * Today's ladder against the ladder rebuilt as of the end of last month (UX Phase E). Rebuilding a
 * second ladder is not free, so it loads when the operator asks for it.
 */
export function CashLadderPeriodComparison({ scenario, minimumCash, fundAccountId, service = getCashLadderComparison }: {
  scenario: string;
  minimumCash?: number;
  fundAccountId?: string;
  service?: CashLadderComparisonService;
}) {
  const [open, setOpen] = useState(false);
  const query = { scenario, minimumCash: minimumCash ?? null, fundAccountId: fundAccountId ?? null };
  const comparison = usePeriodComparison(open ? JSON.stringify(query) : null, (options) => service(query, options));
  return (
    <section aria-label="Cash ladder vs prior period" className="space-y-2">
      <Button type="button" size="sm" variant="outline" aria-pressed={open} onClick={() => setOpen((value) => !value)}>Vs end of last month</Button>
      {open ? (
        <PeriodComparisonPanel title="Cash ladder vs end of last month" workbenchId="portfolio-cash-ladder-comparison"
          state={comparison} onRetry={comparison.retry} policy={CASH_MATERIALITY} currentLabel="Today" countKeys={["breaches"]} />
      ) : null}
    </section>
  );
}
