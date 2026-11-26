import { useId } from "react";
import { buildIncomeWaterfall, formatIncomeAmount } from "@/lib/income-movement-waterfall";
import type { IncomeComparison } from "@/types/reporting-income-comparison";

const colors = {
  total: "var(--chart-primary)",
  increase: "var(--chart-equity)",
  decrease: "var(--chart-drawdown)",
  zero: "var(--chart-axis)"
};

export function IncomeMovementWaterfall({ comparison, onSupport }: { comparison: IncomeComparison; onSupport: (id: string) => void }) {
  const titleId = useId();
  const model = buildIncomeWaterfall(comparison);
  const currency = comparison.current.currency;
  const y = (pct: number) => 210 - pct * 1.8;
  return <section aria-labelledby={titleId} className="space-y-2 rounded-sm border border-border p-3">
    <h3 id={titleId} className="text-sm font-semibold">Income movement waterfall</h3>
    {model.reason ? <p role="status" className="text-sm text-[var(--orange-dim)]">Waterfall unavailable. {model.reason}</p> : <>
      <p className="text-xs text-muted-foreground">{currency} · {comparison.current.accountingBasis} · {comparison.current.periodId} · {comparison.current.population}. Baseline and current are totals; floating bars show signed changes.</p>
      <div className="overflow-x-auto" tabIndex={0} role="region" aria-label="Income waterfall chart and exact amounts">
        <div className="min-w-[720px]">
          <svg viewBox="0 0 900 238" role="img" aria-label="Income bridge from baseline through journal, population, methodology, and unexplained changes to current. Exact amounts and supporting records follow." className="block w-full">
            <line x1={35} x2={890} y1={y(model.zeroPct)} y2={y(model.zeroPct)} stroke="var(--chart-axis)" strokeWidth={1} />
            <text x={8} y={y(model.zeroPct) + 4} fill="currentColor" fontSize={12}>0</text>
            {model.stages.map((stage, index) => {
              const x = 26 + index * 150;
              const top = Math.min(y(stage.startPct), y(stage.endPct));
              const height = Math.abs(y(stage.startPct) - y(stage.endPct));
              return <g key={stage.label}>
                <title>{stage.label}: {formatIncomeAmount(stage.amount, currency, stage.direction !== "total")}</title>
                {index < model.stages.length - 1 ? <line x1={x + 98} x2={x + 150} y1={y(stage.endPct)} y2={y(stage.endPct)} stroke="var(--chart-axis)" strokeDasharray="3 3" /> : null}
                {height > 0 ? <rect x={x} y={top} width={98} height={height} fill={stage.label === "Unexplained difference" ? "var(--chart-axis)" : colors[stage.direction]} /> : <line x1={x} x2={x + 98} y1={top} y2={top} stroke={colors.zero} strokeWidth={2} />}
                <text x={x + 49} y={228} textAnchor="middle" fill="currentColor" fontSize={12}>{stage.direction === "total" ? "Total" : stage.direction === "increase" ? "+ Increase" : stage.direction === "decrease" ? "− Decrease" : "No net change"}</text>
              </g>;
            })}
          </svg>
          <div className="grid grid-cols-6 gap-2 border-t border-border pt-2">
            {model.stages.map((stage) => <div key={stage.label} className="min-w-0 space-y-1">
              <p className="text-xs font-semibold">{stage.label}</p>
              <p className="break-all font-mono text-xs">{formatIncomeAmount(stage.amount, currency, stage.direction !== "total")}</p>
              {stage.contributions.map((item, index) => <button key={item.contributionId} type="button" onClick={() => onSupport(item.contributionId)} aria-label={`Inspect waterfall contribution: ${item.label}`} title={item.label} className="block max-w-full break-words text-left text-xs text-primary underline underline-offset-2 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">
                <span className="block">{index + 1}. {item.label}</span>
                <span className="block break-all font-mono">{formatIncomeAmount(item.amount, currency, true)}</span>
              </button>)}
            </div>)}
          </div>
        </div>
      </div>
      <p className="text-xs text-muted-foreground">Exact amounts are shown beneath each bar. Select a record to inspect retained support. Unexplained records describe the residual; they are not additional contributions. Positive and negative records remain separate even when their net is zero.</p>
    </>}
  </section>;
}
