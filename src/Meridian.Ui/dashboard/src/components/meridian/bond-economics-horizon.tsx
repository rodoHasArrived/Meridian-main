import { useState } from "react";
import { Button } from "@/components/ui/button";
import { bondDecimalText } from "@/lib/bond-decimal-format";
import { formatRatioAsPercent } from "@/lib/format";
import type { BondAnalyticsResult, BondDecimal } from "@/types/bond-analytics.types";

function day(value: string | null | undefined): number | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const parsed = Date.parse(`${value}T00:00:00Z`);
  return Number.isFinite(parsed) && new Date(parsed).toISOString().slice(0, 10) === value ? parsed : null;
}
function canonical(value: BondDecimal): string {
  return String(value).replace(/(\.\d*?)0+$/, "$1").replace(/\.$/, "");
}

export function BondEconomicsHorizon({ result }: { result: BondAnalyticsResult }) {
  const [selected, setSelected] = useState<string | null>(null);
  const [selectedFlowId, setSelectedFlowId] = useState<string | null>(null);
  if (result.isBlocked) return null;
  const retainedMaturity = result.redemptionScenarios?.find((scenario) => scenario.kind === "Maturity");
  const maturity = result.maturityDate ?? retainedMaturity?.exerciseDate;
  const maturityYield = result.yieldToMaturity ?? retainedMaturity?.yield;
  if (!maturity || day(maturity) == null || maturityYield == null) {
    return <section aria-label="Callable bond economics horizon" className="space-y-2"><h3 className="text-sm font-semibold">Callable-bond horizon</h3><p className="text-xs text-muted-foreground">The host did not supply contractual maturity and its yield together for the horizon. Retained analytics and payment tables remain available.</p></section>;
  }
  const points = (result.redemptionScenarios?.length ? result.redemptionScenarios : [
    { kind: "Maturity", exerciseDate: maturity, yield: maturityYield, redemptionPrice: null, earliestNoticeDate: null, latestNoticeDate: null },
    ...result.exerciseYields
  ]).map((point, index) => ({ ...point, id: `${point.kind}:${point.exerciseDate}:${index}`,
    governing: result.yieldToWorstScenarios != null ? result.yieldToWorstScenarios.some((reference) => reference.kind === point.kind
      && reference.exerciseDate === point.exerciseDate && canonical(reference.yield) === canonical(point.yield)
      && point.redemptionPrice != null && canonical(reference.redemptionPrice) === canonical(point.redemptionPrice))
      : result.governingYieldScenarios?.some((reference) => reference.kind === point.kind
        && reference.date === point.exerciseDate && canonical(reference.yield) === canonical(point.yield)) === true
  }));
  const plottable = points.filter((point) => day(point.exerciseDate) != null && Number.isFinite(Number(point.yield)));
  const dates = [...plottable.flatMap((point) => [day(point.exerciseDate), day(point.earliestNoticeDate), day(point.latestNoticeDate)]), ...result.cashFlows.map((flow) => day(flow.paymentDate))].filter((value): value is number => value != null);
  const start = Math.min(...dates), end = Math.max(...dates);
  const low = Math.min(0, ...plottable.map((point) => Number(point.yield)));
  const high = Math.max(0.01, ...plottable.map((point) => Number(point.yield)));
  const x = (date: string) => 100 + ((day(date)! - start) / (end - start || 1)) * 740;
  const y = (value: BondDecimal) => 155 - ((Number(value) - low) / (high - low || 1)) * 125;
  const selectedPoint = points.find((point) => point.id === selected);
  const selectedFlow = result.cashFlows.find((flow) => flow.flowId === selectedFlowId);
  const maxInterest = Math.max(1, ...result.cashFlows.map((flow) => Number(flow.interest) || 0));
  const maxPrincipal = Math.max(1, ...result.cashFlows.map((flow) => Number(flow.principal) || 0));
  const analysisDate = result.context.asOf.slice(0, 10);
  return <section aria-label="Callable bond economics horizon" className="space-y-3 border-y border-border py-4">
    <h3 className="text-sm font-semibold">Callable-bond horizon</h3>
    <p className="text-xs text-muted-foreground">Discrete contractual scenarios; the yield axis is in percent and exact yields are annual fractions. Yield to worst compares maturity and issuer calls. Holder puts remain separate elections. The payment strip uses the contractual maturity basis.</p>
    {result.governingYieldScenarios == null && result.yieldToWorstScenarios == null ? <p className="text-xs text-warning">The host did not identify governing scenarios; no governing point has been inferred.</p> : null}
    <div className="overflow-x-auto">
      <svg viewBox="0 0 920 345" className="min-w-[640px] w-full" role="group" aria-label="Exercise yield scenarios and projected payments">
        <title>Exercise yield scenarios and projected contractual payments per 100 face</title>
        {[0, 0.5, 1].map((fraction) => {
          const rate = low + fraction * (high - low);
          return <g key={fraction}><line x1="100" x2="840" y1={y(rate)} y2={y(rate)} stroke="var(--chart-grid, #E4E3DE)" /><text x="90" y={y(rate) + 4} textAnchor="end" fill="currentColor" fontSize="12">{formatRatioAsPercent(rate)}</text></g>;
        })}
        {plottable.map((point) => <g key={point.id} role="button" tabIndex={0} aria-pressed={selected === point.id}
          aria-label={`${point.kind} ${point.exerciseDate}, annual yield ${bondDecimalText(point.yield)}${point.governing ? ", governs yield to worst" : ""}`}
          className="cursor-pointer focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary" onClick={() => setSelected(point.id)}
          onKeyDown={(event) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); setSelected(point.id); } }}>
          <rect x={x(point.exerciseDate) - 14} y={y(point.yield) - 14} width="28" height="28" fill="transparent" />
          {point.kind === "Put" ? <path d={`M${x(point.exerciseDate)},${y(point.yield) - 6}l6,12h-12z`} fill="var(--chart-drawdown, #A8443C)" />
            : point.kind === "Maturity" ? <rect x={x(point.exerciseDate) - 5} y={y(point.yield) - 5} width="10" height="10" fill="var(--chart-axis, #5E666F)" />
              : <circle cx={x(point.exerciseDate)} cy={y(point.yield)} r="5" fill="hsl(var(--primary, 208 17% 19%))" />}
          {point.governing ? <circle cx={x(point.exerciseDate)} cy={y(point.yield)} r="10" fill="none" stroke="currentColor" strokeWidth="2" /> : null}
        </g>)}
        {points.flatMap((point) => [point.earliestNoticeDate, point.latestNoticeDate].filter((date): date is string => !!date && day(date) != null).map((date, index) =>
          <g key={`${point.id}:notice:${index}`}><path d={`M${x(date)},180l-4,7h8z`} fill="var(--chart-axis, #5E666F)" /><title>{point.kind} {point.exerciseDate}: {index === 0 && point.earliestNoticeDate ? "earliest" : "latest"} notice {date}</title></g>))}
        <text x="90" y="187" textAnchor="end" fontSize="12" fill="currentColor">Notice dates</text>
        <line x1="100" x2="840" y1="225" y2="225" stroke="var(--chart-grid, #E4E3DE)" />
        <line x1="100" x2="840" y1="280" y2="280" stroke="var(--chart-grid, #E4E3DE)" />
        <text x="90" y="225" textAnchor="end" fontSize="12" fill="currentColor">Interest / 100</text>
        <text x="90" y="280" textAnchor="end" fontSize="12" fill="currentColor">Principal / 100</text>
        {result.cashFlows.filter((flow) => day(flow.paymentDate) != null).map((flow) => <g key={flow.flowId} role="button" tabIndex={0}
          aria-label={`Inspect projected payment ${flow.paymentDate}`} aria-pressed={flow.flowId === selectedFlowId}
          onClick={() => setSelectedFlowId(flow.flowId)} onKeyDown={(event) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); setSelectedFlowId(flow.flowId); } }}
          className="cursor-pointer focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">
          <rect x={x(flow.paymentDate) - 12} y="195" width="24" height="90" fill="transparent" />
          <rect x={x(flow.paymentDate) - 4} y={225 - Number(flow.interest) / maxInterest * 28} width="8" height={Math.max(0, Number(flow.interest) / maxInterest * 28)} fill="var(--chart-equity, #3A7A56)" />
          <rect x={x(flow.paymentDate) - 4} y={280 - Number(flow.principal) / maxPrincipal * 28} width="8" height={Math.max(0, Number(flow.principal) / maxPrincipal * 28)} fill="var(--chart-axis, #5E666F)" />
          <title>{flow.paymentDate}: projected interest {bondDecimalText(flow.interest)}; principal {bondDecimalText(flow.principal)} per 100 face</title>
        </g>)}
        <text x="100" y="310" fontSize="12" fill="currentColor">{new Date(start).toISOString().slice(0, 10)}</text>
        <text x="840" y="310" textAnchor="end" fontSize="12" fill="currentColor">{new Date(end).toISOString().slice(0, 10)}</text>
        <text x="100" y="334" fontSize="12" fill="currentColor">● Issuer call · ▲ Holder put · ■ Maturity · ring = server-identified yield to worst</text>
      </svg>
    </div>
    <p className="text-xs text-muted-foreground">Projected contractual payments per 100 face. Interest and principal have independent vertical scales; exact amounts remain in the payment table.</p>
    {selectedFlow ? <p role="status" className="text-xs">Projected payment {selectedFlow.paymentDate}: interest {bondDecimalText(selectedFlow.interest)} / 100 · principal {bondDecimalText(selectedFlow.principal)} / 100.</p> : null}
    <div className="flex flex-wrap gap-2" aria-label="Select exercise scenario">
      {points.map((point) => <Button key={point.id} size="sm" variant="outline" aria-pressed={selected === point.id} onClick={() => setSelected(point.id)}>{point.kind} {point.exerciseDate}{point.governing ? " · YTW" : ""}</Button>)}
    </div>
    {selectedPoint ? <div className="space-y-1 text-xs" role="status">
      <p><strong>{selectedPoint.kind} · {selectedPoint.exerciseDate}</strong> · Annual yield: {bondDecimalText(selectedPoint.yield)}{selectedPoint.governing ? " · Governs yield to worst" : ""}</p>
      {selectedPoint.redemptionPrice != null ? <p>Redemption price per 100: {bondDecimalText(selectedPoint.redemptionPrice)}</p> : null}
      {selectedPoint.kind !== "Maturity" ? <p>Earliest notice: {selectedPoint.earliestNoticeDate ?? "Not supplied"} · Latest notice: {selectedPoint.latestNoticeDate ?? "Not supplied"}</p> : null}
      {selectedPoint.latestNoticeDate && selectedPoint.latestNoticeDate < analysisDate ? <p className="text-warning">Notice deadline precedes the analysis date. Actual exercise status requires retained notice evidence.</p> : null}
    </div> : <p className="text-xs text-muted-foreground">Select a scenario to inspect exact yield, redemption and notice dates.</p>}
  </section>;
}
