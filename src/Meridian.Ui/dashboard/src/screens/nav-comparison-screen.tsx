import { useId } from "react";
import { useSearchParams } from "react-router-dom";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { FormRow } from "@/components/ui/form";
import { PeriodComparisonPanel } from "@/components/meridian/period-comparison-panel";
import { getNavComparison, getNavTrend, type PeriodComparison } from "@/lib/api/period-comparison.api";
import { NavTrendChart } from "@/components/meridian/nav-trend-chart";
import type { ApiRequestOptions } from "@/lib/api";
import { defaultMaterialityPolicyForClass } from "@/lib/reporting-materiality";
import { usePeriodComparison } from "@/lib/use-period-comparison";

type NavComparisonService = (query: { fundProfileId: string; baselineAsOf?: string | null }, options?: ApiRequestOptions) => Promise<PeriodComparison>;

const NAV_MATERIALITY = defaultMaterialityPolicyForClass("Portfolio");

/**
 * NAV against a prior date (UX Phase E). Both figures are rebuilt from the posted ledger as of their
 * own date, in total and by asset class. The fund comes from the link or the reporting selection;
 * the prior date defaults to the end of the previous month.
 */
export function NavComparisonScreen({ defaultFundProfileId = null, service = getNavComparison, trendService = getNavTrend }: {
  defaultFundProfileId?: string | null;
  service?: NavComparisonService;
  trendService?: typeof getNavTrend;
}) {
  const fundId = useId();
  const dateId = useId();
  const [search, setSearch] = useSearchParams();
  const fundProfileId = search.get("fundProfileId")?.trim() || defaultFundProfileId?.trim() || "";
  const baselineDate = search.get("baselineAsOf") ?? "";
  const update = (key: string, value: string) => {
    const next = new URLSearchParams(search);
    if (value.trim()) next.set(key, value.trim()); else next.delete(key);
    setSearch(next, { replace: true });
  };
  const baselineAsOf = baselineDate ? `${baselineDate}T23:59:59Z` : null;
  const comparison = usePeriodComparison(
    fundProfileId ? JSON.stringify({ fundProfileId, baselineAsOf }) : null,
    (options) => service({ fundProfileId, baselineAsOf }, options)
  );

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle>NAV vs prior period</CardTitle>
          <CardDescription>Net asset value now against a prior date, in total and by asset class, rebuilt from the posted ledger as of each date.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2">
            <FormRow label="Fund profile" labelFor={fundId}>
              <Input id={fundId} defaultValue={fundProfileId} placeholder="Fund profile ID"
                onBlur={(event) => update("fundProfileId", event.target.value)}
                onKeyDown={(event) => { if (event.key === "Enter") update("fundProfileId", event.currentTarget.value); }} />
            </FormRow>
            <FormRow label="Prior date" labelFor={dateId} hint="Empty means the end of the previous month.">
              <Input id={dateId} type="date" value={baselineDate} onChange={(event) => update("baselineAsOf", event.target.value)} />
            </FormRow>
          </div>
          {fundProfileId ? <NavTrendChart fundProfileId={fundProfileId} service={trendService} /> : null}
          {fundProfileId
            ? <PeriodComparisonPanel title="NAV comparison" workbenchId="portfolio-nav-comparison" state={comparison} onRetry={comparison.retry} policy={NAV_MATERIALITY} currentLabel="Now" />
            : <p role="status" className="text-sm text-muted-foreground">Choose a fund profile to compare its NAV with a prior date.</p>}
        </CardContent>
      </Card>
    </div>
  );
}
