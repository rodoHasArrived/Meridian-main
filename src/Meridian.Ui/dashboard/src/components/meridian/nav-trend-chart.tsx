import { useEffect, useMemo, useState } from "react";
import { ChartCard } from "@/components/charts/ChartCard";
import { EquityCurve } from "@/components/charts/EquityCurve";
import { Button } from "@/components/ui/button";
import { getNavTrend, type FundNavTrend } from "@/lib/api/period-comparison.api";
import type { ApiRequestOptions } from "@/lib/api";
import { formatCurrency } from "@/lib/format";
import { accountingAmount } from "@/components/meridian/period-comparison-panel";

type NavTrendService = (query: { fundProfileId: string; months?: number }, options?: ApiRequestOptions) => Promise<FundNavTrend>;

/**
 * Ledger NAV at each of the last month-ends, ending with NAV now (UX Phase E follow-up). Every
 * point is the posted ledger as it stood on that date. The plotted values are listed in a table.
 */
export function NavTrendChart({ fundProfileId, months = 12, service = getNavTrend }: { fundProfileId: string; months?: number; service?: NavTrendService }) {
  const key = `${fundProfileId}:${months}`;
  const [result, setResult] = useState<{ key: string; trend?: FundNavTrend; error?: string } | null>(null);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setResult(null);
    service({ fundProfileId, months }, { signal: controller.signal }).then(
      (trend) => { if (!controller.signal.aborted) setResult({ key, trend }); },
      (failure: unknown) => { if (!controller.signal.aborted) setResult({ key, error: failure instanceof Error ? failure.message : "The NAV trend could not be loaded." }); }
    );
    return () => controller.abort();
    // `service` is a stable import or test seam; `key` covers the request.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, retry]);
  const active = result?.key === key ? result : null;
  const points = useMemo(() => active?.trend?.points ?? [], [active]);
  const accessibleData = useMemo(() => ({
    caption: `Ledger NAV by month-end${active?.trend ? `, ${active.trend.currency}` : ""}`,
    columns: ["As of", "NAV"],
    rows: points.map((point) => [point.asOf, accountingAmount(point.totalNav)])
  }), [points, active?.trend]);

  return (
    <section aria-label="NAV trend" className="space-y-2" data-chart-table="nav-trend">
      <h3 className="text-sm font-semibold">NAV, last {months} month-ends</h3>
      {!active ? <p role="status" className="text-sm text-muted-foreground">Loading the NAV trend…</p>
        : active.error ? <div role="status" className="space-y-2 text-sm"><p>{active.error}</p><Button size="sm" variant="outline" onClick={() => setRetry((value) => value + 1)}>Retry NAV trend</Button></div>
          : points.length < 2 ? <p role="status" className="text-sm text-muted-foreground">At least two dates with ledger NAV are needed for a trend.</p>
            : <ChartCard title={`Ledger NAV · ${active.trend!.currency}`} subtitle={`${points[0]!.asOf} to ${points.at(-1)!.asOf}`} height={240}>
              <EquityCurve labels={points.map((point) => point.asOf)} valueFmt={(value) => formatCurrency(value, { currency: active.trend!.currency, notation: "compact", maximumFractionDigits: 2 })}
                series={[{ label: "NAV", color: "var(--chart-equity, #2E7655)", points: points.map((point) => Number(point.totalNav)) }]}
                ariaLabel={`Ledger NAV from ${points[0]!.asOf} to ${points.at(-1)!.asOf}`} accessibleData={accessibleData} />
            </ChartCard>}
      {active?.trend?.notes.length ? <ul className="list-disc space-y-0.5 pl-5 text-xs text-muted-foreground">{active.trend.notes.map((note) => <li key={note}>{note}</li>)}</ul> : null}
    </section>
  );
}
