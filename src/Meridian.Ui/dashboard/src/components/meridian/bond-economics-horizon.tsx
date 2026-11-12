import { type KeyboardEvent, useId, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { bondDecimalText } from "@/lib/bond-decimal-format";
import { formatNumber, formatRatioAsPercent } from "@/lib/format";
import type { BondAnalyticsResult, BondExerciseScenario } from "@/types/bond-analytics.types";

type Scenario = BondExerciseScenario & { id: string; governing: boolean };
type Payment = BondAnalyticsResult["cashFlows"][number];
const paymentColumns: DenseDataTableColumn<Payment>[] = [
  { id: "date", label: "Payment date", render: (row) => row.paymentDate },
  { id: "accrual", label: "Accrual period", render: (row) => `${row.accrualStart} – ${row.accrualEnd}` },
  { id: "interest", label: "Interest / 100", render: (row) => bondDecimalText(row.interest) },
  { id: "principal", label: "Principal / 100", render: (row) => bondDecimalText(row.principal) }
];
const scenarioColumns: DenseDataTableColumn<Scenario>[] = [
  { id: "kind", label: "Scenario", render: (row) => `${row.kind}${row.governing ? " · governs yield to worst" : ""}` },
  { id: "date", label: "Exercise / maturity date", render: (row) => row.exerciseDate },
  { id: "yield", label: "Annual yield fraction", render: (row) => bondDecimalText(row.yield) },
  { id: "price", label: "Redemption / 100", render: (row) => bondDecimalText(row.redemptionPrice) },
  { id: "notice", label: "Notice window", render: (row) => row.kind === "Maturity" ? "Not applicable" : `${row.earliestNoticeDate ?? "Unspecified start"} – ${row.latestNoticeDate ?? "Unspecified deadline"}` }
];
const instant = (date: string | null | undefined) => date ? Date.parse(date.length === 10 ? `${date}T00:00:00Z` : date) : NaN;
const finiteDecimal = (value: unknown) => value !== null && value !== undefined && value !== "" && Number.isFinite(Number(value));
const sameScenario = (a: BondExerciseScenario, b: BondExerciseScenario) =>
  a.kind === b.kind && a.exerciseDate === b.exerciseDate && String(a.redemptionPrice) === String(b.redemptionPrice) && String(a.yield) === String(b.yield);

/** Draws retained server scenarios; never calculates yield, notice dates or the governing outcome. */
export function BondEconomicsHorizon({ result }: { result: BondAnalyticsResult }) {
  const id = useId();
  const [selectedScenario, setSelectedScenario] = useState("0");
  const [selectedFlow, setSelectedFlow] = useState<string | null>(null);
  if (result.isBlocked) return null;
  const scenarios: Scenario[] = (result.redemptionScenarios?.length ? result.redemptionScenarios : result.exerciseYields).map((row, index) => ({
    ...row, id: String(index), governing: (result.yieldToWorstScenarios ?? []).some((governing) => sameScenario(row, governing))
  }));
  const plotted = scenarios.filter((row) => finiteDecimal(row.yield) && Number.isFinite(instant(row.exerciseDate)));
  const flows = result.cashFlows.filter((row) => Number.isFinite(instant(row.paymentDate)) && finiteDecimal(row.interest) && finiteDecimal(row.principal));
  const dates = [...plotted.flatMap((row) => [instant(row.exerciseDate), instant(row.earliestNoticeDate), instant(row.latestNoticeDate)]), ...flows.map((row) => instant(row.paymentDate)), instant(result.context.settlementDate)].filter(Number.isFinite);
  const start = dates.length ? Math.min(...dates) : Number.isFinite(instant(result.context.asOf)) ? instant(result.context.asOf) : 0;
  const end = dates.length ? Math.max(...dates) : start + 86_400_000;
  const x = (date: string) => 140 + (instant(date) - start) / Math.max(86_400_000, end - start) * 550;
  const yields = plotted.map((row) => Number(row.yield));
  const minimum = Math.min(0, ...yields);
  const maximum = Math.max(.01, ...yields);
  const y = (value: number) => 200 - (value - minimum) / Math.max(.0001, maximum - minimum) * 156;
  const chosen = scenarios.find((row) => row.id === selectedScenario);
  const chosenFlow = result.cashFlows.find((row) => row.flowId === selectedFlow);
  const maxInterest = Math.max(1, ...flows.map((row) => Math.abs(Number(row.interest))));
  const maxPrincipal = Math.max(1, ...flows.map((row) => Math.abs(Number(row.principal))));
  const selectKey = (event: KeyboardEvent<SVGGElement>, select: () => void) => {
    if (event.key === "Enter" || event.key === " ") { event.preventDefault(); select(); }
  };

  return <section aria-labelledby={`${id}-title`} className="grid gap-3 rounded border border-border p-3">
    <h3 id={`${id}-title`} className="text-sm font-semibold">Callable-bond horizon</h3>
    <p className="text-xs text-muted-foreground">Contractual scenarios from {result.modelVersion} · {result.currency} · settlement {result.context.settlementDate ?? "unspecified"}. Yield to worst compares maturity with issuer calls; holder puts are separate alternatives. Analytical scenarios do not predict exercise.</p>
    {!result.redemptionScenarios?.length || !result.yieldToWorstScenarios?.length ? <p role="status" className="text-xs">This response does not identify the maturity scenario or the scenario governing yield to worst. No governing outcome has been inferred in the browser.</p> : null}
    {!plotted.length ? <p role="status" className="text-xs">No dated scenario yields are available for plotting.</p> : null}
    <div className="overflow-x-auto" role="region" aria-label="Bond scenario chart, scroll horizontally when needed" tabIndex={0}>
    <svg viewBox="0 0 740 300" className="w-full" style={{ minWidth: 740, fontFamily: "var(--font-data)" }} role="group" aria-label="Bond exercise yields and notice deadlines over time">
      {[minimum, (minimum + maximum) / 2, maximum].map((value, index) => <g key={index}>
        <line x1="140" x2="690" y1={y(value)} y2={y(value)} stroke="currentColor" opacity="0.2" />
        <text x="129" y={y(value) + 4} textAnchor="end" fill="currentColor" fontSize="12">{formatRatioAsPercent(value, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</text>
      </g>)}
      <text x="18" y="130" transform="rotate(-90 18 130)" textAnchor="middle" fill="currentColor" fontSize="12">Annual yield</text>
      <text x="140" y="284" fill="currentColor" fontSize="12">{new Date(start).toISOString().slice(0, 10)}</text>
      <text x="690" y="284" textAnchor="end" fill="currentColor" fontSize="12">{new Date(end).toISOString().slice(0, 10)}</text>
      <text x="140" y="249" fill="currentColor" fontSize="12">Notice dates</text>
      {plotted.map((row) => <g key={row.id} role="button" tabIndex={0} aria-pressed={selectedScenario === row.id}
        aria-label={`Inspect ${row.kind} scenario on ${row.exerciseDate}${row.governing ? "; governs yield to worst" : ""}`}
        className="cursor-pointer focus:outline focus:outline-2 focus:outline-offset-2" style={{ outlineColor: "var(--interaction-focus)" }} onClick={() => setSelectedScenario(row.id)} onKeyDown={(event) => selectKey(event, () => setSelectedScenario(row.id))}>
        <title>{`${row.kind} ${row.exerciseDate} · annual fraction ${bondDecimalText(row.yield)}${row.governing ? " · Governs YTW" : ""}`}</title>
        <line x1={x(row.exerciseDate)} x2={x(row.exerciseDate)} y1={y(Number(row.yield))} y2="226" stroke="currentColor" strokeDasharray="3 4" opacity="0.3" />
        <circle cx={x(row.exerciseDate)} cy={y(Number(row.yield))} r={selectedScenario === row.id ? 10 : row.kind === "Put" ? 9 : 7} fill={row.governing ? "currentColor" : "var(--chart-plot)"} stroke="currentColor" strokeWidth={row.kind === "Put" ? 1 : 2.5} />
        {row.kind === "Put" ? <text x={x(row.exerciseDate)} y={y(Number(row.yield)) + 4} textAnchor="middle" fill="currentColor" fontSize="12">P</text> : null}
        {[row.earliestNoticeDate, row.latestNoticeDate].filter((date): date is string => !!date && Number.isFinite(instant(date))).map((date, index) => <rect key={index} x={x(date) - 4} y="255" width="8" height="8" fill={index === 0 && row.earliestNoticeDate ? "none" : "currentColor"} stroke="currentColor" />)}
      </g>)}
    </svg>
    </div>
    <p className="text-xs">Filled yield mark: server-identified yield-to-worst scenario. P: holder put. Squares: supplied notice dates. Larger ring: selected. Notice deadlines do not prove that exercise was announced or missed.</p>
    <DenseDataTable columns={scenarioColumns} rows={scenarios} getRowId={(row) => row.id} selectedRowId={selectedScenario}
      onRowSelect={(row) => setSelectedScenario(row.id)} ariaLabel="Bond redemption scenarios" caption="Server-calculated contractual yield scenarios" emptyText="No supported exercise scenarios are available." />
    {chosen ? <p role="status" className="text-xs">Selected {chosen.kind} · {chosen.exerciseDate} · yield fraction {bondDecimalText(chosen.yield)} · redemption {bondDecimalText(chosen.redemptionPrice)} / 100 · earliest notice {chosen.earliestNoticeDate ?? "unspecified"} · latest notice {chosen.latestNoticeDate ?? "unspecified"}{chosen.governing ? " · Governs yield to worst" : ""}.</p> : null}
    <h4 className="text-sm font-semibold">Projected payment strip</h4>
    <p className="text-xs text-muted-foreground">Interest and principal per 100 face, shown in separate lanes with independent scales. Amounts above zero are positive; amounts below zero are negative. This is the contractual maturity payment schedule, not the selected exercise cash flow or observed payments.</p>
    <div className="overflow-x-auto" role="region" aria-label="Bond payment chart, scroll horizontally when needed" tabIndex={0}>
    <svg viewBox="0 0 740 210" className="w-full" style={{ minWidth: 740, fontFamily: "var(--font-data)" }} role="group" aria-label="Projected bond interest and principal payments">
      {[{ name: "Interest", bottom: 62, top: 24, max: maxInterest }, { name: "Principal", bottom: 148, top: 110, max: maxPrincipal }].map((lane) => <g key={lane.name}>
        <text x="4" y={lane.top} fill="currentColor" fontSize="12">{lane.name}</text>
        <text x="4" y={lane.top + 15} fill="currentColor" fontSize="12">±{formatNumber(lane.max, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} / 100</text>
        <text x="129" y={lane.bottom + 4} textAnchor="end" fill="currentColor" fontSize="12">0</text>
        <line x1="140" x2="690" y1={lane.bottom} y2={lane.bottom} stroke="currentColor" opacity="0.2" />
      </g>)}
      {flows.map((flow) => <g key={flow.flowId} role="button" tabIndex={0} aria-pressed={selectedFlow === flow.flowId} className="cursor-pointer focus:outline focus:outline-2 focus:outline-offset-2" style={{ outlineColor: "var(--interaction-focus)" }}
        aria-label={`Inspect projected payment on ${flow.paymentDate}; interest ${bondDecimalText(flow.interest)}; principal ${bondDecimalText(flow.principal)} per 100`}
        onClick={() => setSelectedFlow(flow.flowId)} onKeyDown={(event) => selectKey(event, () => setSelectedFlow(flow.flowId))}>
        <title>{flow.paymentDate} · Interest {bondDecimalText(flow.interest)} · Principal {bondDecimalText(flow.principal)} / 100</title>
        <rect x={x(flow.paymentDate) - 5} y="24" width="10" height="150" fill="transparent" />
        <line x1={x(flow.paymentDate)} x2={x(flow.paymentDate)} y1="62" y2={62 - Number(flow.interest) / maxInterest * 34} stroke="currentColor" strokeWidth={selectedFlow === flow.flowId ? 7 : 4} />
        <line x1={x(flow.paymentDate)} x2={x(flow.paymentDate)} y1="148" y2={148 - Number(flow.principal) / maxPrincipal * 34} stroke="currentColor" strokeWidth={selectedFlow === flow.flowId ? 7 : 4} strokeDasharray="4 2" />
      </g>)}
      <text x="140" y="200" fill="currentColor" fontSize="12">{new Date(start).toISOString().slice(0, 10)}</text>
      <text x="690" y="200" textAnchor="end" fill="currentColor" fontSize="12">{new Date(end).toISOString().slice(0, 10)}</text>
    </svg>
    </div>
    {chosenFlow ? <p role="status" className="text-xs">Projected payment {chosenFlow.paymentDate}: interest {bondDecimalText(chosenFlow.interest)} / 100; principal {bondDecimalText(chosenFlow.principal)} / 100. Accrual {chosenFlow.accrualStart} – {chosenFlow.accrualEnd}.</p> : <p className="text-xs">Select a payment mark for exact retained amounts; the payment table remains available below.</p>}
    <details>
      <summary className="cursor-pointer text-sm font-semibold">Projected contractual payments ({result.cashFlows.length})</summary>
      <p className="my-2 text-xs text-muted-foreground">Projected cash per 100 face; these rows are not observed payments or posted journals.</p>
      <DenseDataTable columns={paymentColumns} rows={result.cashFlows} getRowId={(row) => row.flowId}
        selectedRowId={selectedFlow} onRowSelect={(row) => setSelectedFlow(row.flowId)}
        ariaLabel="Projected bond payments" caption="Projected contractual payments per 100 face"
        emptyText="No calculated payment schedule is available." />
    </details>
    {result.cashFlows.length !== flows.length || scenarios.length !== plotted.length ? <p className="text-xs">Some rows have unavailable chart coordinates; retain the exact table values for inspection.</p> : null}
  </section>;
}
