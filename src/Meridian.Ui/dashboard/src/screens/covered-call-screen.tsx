import { AlertCircle, ArrowLeft, ArrowRight, BarChart3, CircleX, Info, Layers, Play, RotateCw } from "lucide-react";
import { useEffect } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DenseDataTable, EntitySummary, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import {
  useCoveredCallScreenViewModel,
  type CoveredCallChainPreviewRowViewModel,
  type CoveredCallFormState,
  type CoveredCallScreenViewModel,
  type CoveredCallStage
} from "@/screens/covered-call-screen.view-model";
import { buildShortCallPayoffCurve, shortCallBreakEven } from "@/lib/covered-call/payoff";

const STAGE_LABEL: Record<CoveredCallStage, string> = {
  configure: "Configure",
  run: "Run",
  results: "Results"
};

const chainPreviewColumns: DenseDataTableColumn<CoveredCallChainPreviewRowViewModel>[] = [
  {
    id: "strike",
    label: "Strike",
    align: "right",
    render: (row) => <span className="font-mono">{row.strikeLabel}</span>
  },
  {
    id: "expiry",
    label: "Expiry",
    render: (row) => <span className="font-mono text-muted-foreground">{row.expirationLabel}</span>
  },
  {
    id: "dte",
    label: "DTE",
    align: "right",
    render: (row) => <span className="font-mono">{row.daysToExpirationLabel}</span>
  },
  {
    id: "bid",
    label: "Bid",
    align: "right",
    render: (row) => <span className="font-mono">{row.bidLabel}</span>
  },
  {
    id: "delta",
    label: "Delta",
    align: "right",
    render: (row) => <span className="font-mono">{row.deltaLabel}</span>
  },
  {
    id: "open-interest",
    label: "OI",
    align: "right",
    render: (row) => <span className="font-mono">{row.openInterestLabel}</span>
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <Badge variant={row.statusBadgeVariant} dot aria-label={row.statusAriaLabel}>
        {row.statusLabel}
      </Badge>
    )
  }
];

export function CoveredCallScreen() {
  const vm = useCoveredCallScreenViewModel();

  useEffect(() => {
    void vm.loadHistory();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="eyebrow-label">Strategy Lane</div>
          <CardTitle className="flex items-center gap-2">
            <Layers className="h-5 w-5 text-primary" aria-hidden="true" />
            Covered Call Backtest
          </CardTitle>
          <CardDescription>
            Run the existing CoveredCallOverwriteStrategy against historical bars. Pick an underlying, configure
            filters, preview the live option chain, then evaluate equity curve, position timeline, and payoff.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <StageStepper currentStage={vm.stage} onSelect={vm.goToStage} />
        </CardContent>
      </Card>

      {vm.errorBanner ? (
        <Card>
          <CardContent className="flex items-start gap-3 py-4 text-sm">
            <AlertCircle className="h-5 w-5 flex-shrink-0 text-danger" aria-hidden="true" />
            <div className="flex-1">
              <div className="font-semibold text-danger">Backtest issue</div>
              <p className="mt-1 text-foreground">{vm.errorBanner}</p>
            </div>
            <Button type="button" variant="ghost" onClick={vm.dismissError} aria-label="Dismiss error">
              <CircleX className="h-4 w-4" aria-hidden="true" />
            </Button>
          </CardContent>
        </Card>
      ) : null}

      <ChainDataAdvisory />

      {vm.stage === "configure" ? <ConfigureStage vm={vm} /> : null}
      {vm.stage === "run" ? <RunStage vm={vm} /> : null}
      {vm.stage === "results" ? <ResultsStage vm={vm} /> : null}

      <HistoryPanel vm={vm} />
    </div>
  );
}

function ChainDataAdvisory() {
  return (
    <Card>
      <CardContent className="flex items-start gap-3 py-3 text-xs">
        <Info className="h-4 w-4 flex-shrink-0 text-warning" aria-hidden="true" />
        <p className="text-foreground/80">
          <span className="font-semibold">Chain data is not point-in-time.</span>{" "}
          Slice 1 uses the configured <code>IOptionsChainProvider</code>'s live snapshot replicated across each scan date with DTE recomputed.
          Strike, IV, OI, and volume reflect today's market — not the historical date being backtested. A historical chain store is tracked as a slice 1.5 follow-up.
        </p>
      </CardContent>
    </Card>
  );
}

function StageStepper({
  currentStage,
  onSelect
}: {
  currentStage: CoveredCallStage;
  onSelect: (s: CoveredCallStage) => void;
}) {
  const stages: CoveredCallStage[] = ["configure", "run", "results"];
  return (
    <ol className="flex items-center gap-2 text-sm" aria-label="Covered call wizard stages">
      {stages.map((stage, idx) => {
        const active = stage === currentStage;
        return (
          <li key={stage} className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => onSelect(stage)}
              className={`rounded-md border px-3 py-1.5 text-xs font-medium uppercase tracking-wide transition-colors ${
                active
                  ? "border-primary bg-primary/10 text-primary"
                  : "border-border/60 bg-background/60 text-muted-foreground hover:text-foreground"
              }`}
              aria-current={active ? "step" : undefined}
            >
              {idx + 1}. {STAGE_LABEL[stage]}
            </button>
            {idx < stages.length - 1 ? (
              <ArrowRight className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
            ) : null}
          </li>
        );
      })}
    </ol>
  );
}

function ConfigureStage({ vm }: { vm: CoveredCallScreenViewModel }) {
  return (
    <div className="grid gap-4 lg:grid-cols-[1fr_360px]">
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Parameters</CardTitle>
          <CardDescription>Conservative defaults follow the strategy's documented values.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <NumberField vm={vm} field="underlyingSymbol" label="Underlying" type="text" />
          <div className="grid gap-3 sm:grid-cols-2">
            <NumberField vm={vm} field="from" label="From" type="date" />
            <NumberField vm={vm} field="to" label="To" type="date" />
          </div>
          <NumberField vm={vm} field="minStrike" label="Min strike" type="number" step="0.01" required />
          <div className="grid gap-3 sm:grid-cols-2">
            <NumberField vm={vm} field="overwriteRatio" label="Overwrite ratio" type="number" step="0.01" />
            <NumberField vm={vm} field="maxDelta" label="Max delta" type="number" step="0.01" />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <NumberField vm={vm} field="minDte" label="Min DTE" type="number" />
            <NumberField vm={vm} field="maxDte" label="Max DTE" type="number" />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <NumberField vm={vm} field="minIvPercentile" label="Min IV percentile" type="number" />
            <NumberField vm={vm} field="maxSpreadPct" label="Max spread %" type="number" step="0.01" />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <NumberField vm={vm} field="minOpenInterest" label="Min open interest" type="number" />
            <NumberField vm={vm} field="minVolume" label="Min volume" type="number" />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <NumberField vm={vm} field="takeProfitCapture" label="Take-profit capture" type="number" step="0.01" />
            <NumberField vm={vm} field="rollDelta" label="Roll delta" type="number" step="0.01" />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <NumberField vm={vm} field="exDivWindowDays" label="Ex-div window (days)" type="number" />
            <NumberField vm={vm} field="riskFreeRate" label="Risk-free rate" type="number" step="0.001" />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <NumberField vm={vm} field="initialCash" label="Initial cash" type="number" />
            <NumberField vm={vm} field="initialUnderlyingShares" label="Underlying shares" type="number" />
          </div>
          <NumberField vm={vm} field="label" label="Run label (optional)" type="text" />

          <div className="flex items-center gap-2 pt-2">
            <Button type="button" variant="default" onClick={() => void vm.startRun()}>
              <Play className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">Run backtest</span>
            </Button>
            <Button type="button" variant="ghost" onClick={vm.resetForm}>
              Reset
            </Button>
            <Button type="button" variant="ghost" onClick={() => void vm.refreshChainPreview()}>
              <RotateCw className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">Refresh chain</span>
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Chain preview</CardTitle>
          <CardDescription>{vm.chainPreviewPanel.description}</CardDescription>
        </CardHeader>
        <CardContent>
          <ChainPreviewTable vm={vm} />
        </CardContent>
      </Card>
    </div>
  );
}

interface NumberFieldProps {
  vm: CoveredCallScreenViewModel;
  field: keyof CoveredCallFormState;
  label: string;
  type: "text" | "number" | "date";
  step?: string;
  required?: boolean;
}

function NumberField({ vm, field, label, type, step, required }: NumberFieldProps) {
  const error = vm.formErrors[field];
  const inputId = `cc-${String(field)}`;
  const errorId = `${inputId}-error`;
  return (
    <div className="space-y-1">
      <label htmlFor={inputId} className="text-xs font-medium text-foreground">
        {label}
        {required ? <span className="ml-0.5 text-danger" aria-hidden="true">*</span> : null}
      </label>
      <input
        id={inputId}
        type={type}
        step={step}
        value={vm.form[field]}
        onChange={(e) => vm.setField(field, e.target.value as CoveredCallFormState[typeof field])}
        className={`w-full rounded-md border bg-background/60 px-2 py-1.5 font-mono text-xs text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 ${
          error ? "border-danger" : "border-border/70"
        }`}
        aria-invalid={error ? "true" : undefined}
        aria-describedby={error ? errorId : undefined}
      />
      {error ? (
        <p id={errorId} className="text-xs text-danger">
          {error}
        </p>
      ) : null}
    </div>
  );
}

function ChainPreviewTable({ vm }: { vm: CoveredCallScreenViewModel }) {
  const panel = vm.chainPreviewPanel;

  return (
    <div className="grid gap-3">
      <DenseDataTable
        columns={chainPreviewColumns}
        rows={panel.rows}
        getRowId={(row) => row.id}
        getRowAriaLabel={(row) => row.rowAriaLabel}
        getRowAriaControls={(row) => row.detailPanelId}
        getRowAriaExpanded={(row) => row.ariaExpanded}
        getRowSelectAriaLabel={(row) => row.rowSelectAriaLabel}
        onRowSelect={(row) => vm.selectChainRow(row.index)}
        selectedRowId={panel.selectedRowId}
        emptyText={panel.emptyText}
        ariaLabel={panel.tableLabel}
        caption={panel.tableCaption}
      />
      {panel.selectedDetail ? (
        <div id={panel.selectedDetail.panelId}>
          <EntitySummary
            eyebrow={panel.selectedDetail.eyebrow}
            title={panel.selectedDetail.title}
            subtitle={panel.selectedDetail.subtitle}
            description={panel.selectedDetail.description}
            status={<Badge variant={panel.selectedDetail.statusBadgeVariant} dot>{panel.selectedDetail.statusLabel}</Badge>}
            fields={panel.selectedDetail.fields}
            ariaLabel={panel.selectedDetail.ariaLabel}
          />
        </div>
      ) : (
        <section
          id={panel.detailPanelId}
          className="row-detail-panel"
          aria-label={panel.detailEmptyAriaLabel}
        >
          <div className="head">{panel.detailEmptyTitle}</div>
          <div className="body">{panel.detailEmptyText}</div>
        </section>
      )}
    </div>
  );
}

function RunStage({ vm }: { vm: CoveredCallScreenViewModel }) {
  const status = vm.run.status;
  const percent = status ? Math.round(status.percentComplete * 100) : 0;
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Running backtest</CardTitle>
        <CardDescription>
          {status
            ? `Phase: ${status.phase}${status.currentBacktestDate ? ` · ${status.currentBacktestDate}` : ""}`
            : "Queued — waiting for the engine."}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="h-2 w-full overflow-hidden rounded-full bg-secondary/40" role="progressbar" aria-valuenow={percent} aria-valuemin={0} aria-valuemax={100}>
          <div className="h-full bg-primary transition-[width]" style={{ width: `${percent}%` }} />
        </div>
        <div className="flex items-center gap-2">
          <Button type="button" variant="ghost" onClick={() => vm.goToStage("configure")}>
            <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">Back</span>
          </Button>
          <Button type="button" variant="ghost" onClick={() => void vm.cancelRun()}>
            <CircleX className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">Cancel run</span>
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

function ResultsStage({ vm }: { vm: CoveredCallScreenViewModel }) {
  const result = vm.run.result;
  if (!result) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          No completed run loaded yet.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[1fr_360px]">
      <div className="space-y-4">
        <MetricsTable result={result} />
        <EquityCurve result={result} />
        <PositionTimeline vm={vm} />
      </div>
      <PayoffDiagramPanel vm={vm} />
    </div>
  );
}

function MetricsTable({ result }: { result: NonNullable<CoveredCallScreenViewModel["run"]["result"]> }) {
  const rows: [string, string][] = [
    ["CAGR", fmtPct(result.metrics.cagr)],
    ["Sharpe", result.metrics.sharpeRatio.toFixed(2)],
    ["Sortino", result.metrics.sortinoRatio.toFixed(2)],
    ["Max DD", fmtPct(result.metrics.maxDrawdownPct)],
    ["Win rate", fmtPct(result.metrics.winRate)],
    ["Assignment rate", fmtPct(result.metrics.assignmentRate)],
    ["Total trades", String(result.metrics.totalOptionTrades)],
    ["Total premium", fmtMoney(result.metrics.totalPremiumCollected)],
    ["Option PnL", fmtMoney(result.metrics.totalOptionPnl)],
    ["Up capture", fmtPct(result.metrics.upCapture)],
    ["Down capture", fmtPct(result.metrics.downCapture)]
  ];
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Metrics</CardTitle>
        <CardDescription>{result.underlyingSymbol} · {result.from} → {result.to}</CardDescription>
      </CardHeader>
      <CardContent>
        <table className="w-full text-sm">
          <tbody>
            {rows.map(([label, value]) => (
              <tr key={label} className="border-b border-border/30">
                <td className="py-1 pr-2 font-mono text-muted-foreground">{label}</td>
                <td className="py-1 text-right font-mono">{value}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </CardContent>
    </Card>
  );
}

function EquityCurve({ result }: { result: NonNullable<CoveredCallScreenViewModel["run"]["result"]> }) {
  const points = result.equityCurve;
  if (points.length < 2) {
    return (
      <Card>
        <CardContent className="py-6 text-center text-sm text-muted-foreground">
          Not enough equity-curve points to plot.
        </CardContent>
      </Card>
    );
  }
  const min = Math.min(...points.map((p) => Math.min(p.strategyEquity, p.underlyingEquity)));
  const max = Math.max(...points.map((p) => Math.max(p.strategyEquity, p.underlyingEquity)));
  const width = 600;
  const height = 180;
  const xScale = (i: number) => (i / (points.length - 1)) * width;
  const yScale = (v: number) => height - ((v - min) / Math.max(max - min, 1e-6)) * height;
  const stratPath = points.map((p, i) => `${i === 0 ? "M" : "L"}${xScale(i).toFixed(1)},${yScale(p.strategyEquity).toFixed(1)}`).join(" ");
  const underPath = points.map((p, i) => `${i === 0 ? "M" : "L"}${xScale(i).toFixed(1)},${yScale(p.underlyingEquity).toFixed(1)}`).join(" ");
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <BarChart3 className="h-4 w-4 text-primary" aria-hidden="true" />
          Equity curve
        </CardTitle>
        <CardDescription>Strategy vs underlying-only buy-and-hold.</CardDescription>
      </CardHeader>
      <CardContent>
        <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Equity curve" className="h-44 w-full">
          <path d={underPath} fill="none" stroke="currentColor" strokeOpacity={0.35} strokeWidth={1.2} />
          <path d={stratPath} fill="none" stroke="hsl(var(--primary))" strokeWidth={1.6} />
        </svg>
      </CardContent>
    </Card>
  );
}

function PositionTimeline({ vm }: { vm: CoveredCallScreenViewModel }) {
  const trades = vm.run.result?.trades ?? [];
  if (trades.length === 0) {
    return (
      <Card>
        <CardContent className="py-6 text-center text-sm text-muted-foreground">No trades recorded.</CardContent>
      </Card>
    );
  }
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Trades ({trades.length})</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="max-h-64 overflow-auto">
          <table className="w-full text-xs">
            <thead>
              <tr className="text-left text-muted-foreground">
                <th className="pb-1 pr-2">Entry</th>
                <th className="pb-1 pr-2">Exit</th>
                <th className="pb-1 pr-2">Strike</th>
                <th className="pb-1 pr-2">PnL</th>
                <th className="pb-1 pl-2">Reason</th>
              </tr>
            </thead>
            <tbody>
              {trades.map((t, idx) => (
                <tr key={idx} className="border-t border-border/30">
                  <td className="py-1 pr-2 font-mono">{t.entryDate}</td>
                  <td className="py-1 pr-2 font-mono">{t.exitDate}</td>
                  <td className="py-1 pr-2 font-mono">{t.strike.toFixed(2)}</td>
                  <td className={`py-1 pr-2 font-mono ${t.totalNetPnl >= 0 ? "text-success" : "text-danger"}`}>
                    {fmtMoney(t.totalNetPnl)}
                  </td>
                  <td className="py-1 pl-2">
                    <Badge variant={t.wasAssigned ? "outline" : "success"} dot>
                      {t.exitReason}
                    </Badge>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  );
}

function PayoffDiagramPanel({ vm }: { vm: CoveredCallScreenViewModel }) {
  const openPositions = vm.run.result?.openPositionsAtEnd ?? [];
  const pos = openPositions[vm.run.selectedPositionIndex];

  if (!pos) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Payoff diagram</CardTitle>
          <CardDescription>Short-call payoff diagram for any open position at end of run.</CardDescription>
        </CardHeader>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          No open positions at end of run.
        </CardContent>
      </Card>
    );
  }

  // Slice 1 renders the short-call leg only; the backend does not yet thread the underlying
  // cost basis through CoveredCallOpenPositionDto, so plotting a combined covered curve would
  // require a fabricated basis. We surface that explicitly in the description.
  const inputs = {
    strike: pos.strike,
    entryCredit: pos.entryCredit,
    contracts: pos.contracts,
    multiplier: pos.multiplier
  };
  const spotMin = pos.strike * 0.75;
  const spotMax = pos.strike * 1.25;
  const samples = buildShortCallPayoffCurve(inputs, spotMin, spotMax, 80);
  const breakEven = shortCallBreakEven(inputs);
  const width = 320;
  const height = 180;
  const allPayoffs = samples.map((p) => p.payoff);
  const yMin = Math.min(...allPayoffs);
  const yMax = Math.max(...allPayoffs);
  const xScale = (s: number) => ((s - spotMin) / Math.max(spotMax - spotMin, 1e-6)) * width;
  const yScale = (v: number) => height - ((v - yMin) / Math.max(yMax - yMin, 1e-6)) * height;
  const path = samples
    .map((p, i) => `${i === 0 ? "M" : "L"}${xScale(p.spot).toFixed(1)},${yScale(p.payoff).toFixed(1)}`)
    .join(" ");

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Payoff diagram (short call leg)</CardTitle>
        <CardDescription>
          {pos.contracts} × {pos.strike.toFixed(2)} call expiring {pos.expiration} · short-call break-even ≈ ${breakEven.toFixed(2)}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Short call payoff diagram" className="h-44 w-full">
          <line x1={0} y1={yScale(0)} x2={width} y2={yScale(0)} stroke="currentColor" strokeOpacity={0.25} />
          <line x1={xScale(pos.strike)} y1={0} x2={xScale(pos.strike)} y2={height} stroke="currentColor" strokeOpacity={0.25} strokeDasharray="3 3" />
          <path d={path} fill="none" stroke="hsl(var(--primary))" strokeWidth={1.8} />
        </svg>
        <p className="mt-2 text-xs text-muted-foreground">
          Covered-call net curve requires the underlying cost basis which is not yet threaded through the API — see slice 1.5.
        </p>
      </CardContent>
    </Card>
  );
}

function HistoryPanel({ vm }: { vm: CoveredCallScreenViewModel }) {
  if (vm.history.length === 0 && !vm.historyError) {
    return null;
  }
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Previous runs</CardTitle>
        {vm.historyError ? (
          <CardDescription className="text-danger">Failed to load history: {vm.historyError}</CardDescription>
        ) : (
          <CardDescription>Most recent first. Click a row to reload its results (cached for 30 min).</CardDescription>
        )}
      </CardHeader>
      <CardContent>
        <table className="w-full text-xs">
          <thead>
            <tr className="text-left text-muted-foreground">
              <th className="pb-1 pr-2">Started</th>
              <th className="pb-1 pr-2">Underlying</th>
              <th className="pb-1 pr-2">Range</th>
              <th className="pb-1 pr-2">Status</th>
              <th className="pb-1 pr-2">CAGR</th>
              <th className="pb-1 pr-2">Sharpe</th>
              <th className="pb-1 pl-2">Label</th>
            </tr>
          </thead>
          <tbody>
            {vm.history.map((row) => (
              <tr
                key={row.runId}
                className="cursor-pointer border-t border-border/30 hover:bg-secondary/30 focus-within:bg-secondary/30"
                onClick={() => void vm.openRun(row.runId)}
                tabIndex={0}
                role="button"
                aria-label={`Open run ${row.runId}`}
                onKeyDown={(e) => {
                  if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    void vm.openRun(row.runId);
                  }
                }}
              >
                <td className="py-1 pr-2 font-mono">{row.startedAt.replace("T", " ").slice(0, 16)}</td>
                <td className="py-1 pr-2 font-mono">{row.underlyingSymbol}</td>
                <td className="py-1 pr-2 font-mono">{row.from} → {row.to}</td>
                <td className="py-1 pr-2">{row.status}</td>
                <td className="py-1 pr-2 font-mono">{row.cagr !== null ? fmtPct(row.cagr) : "—"}</td>
                <td className="py-1 pr-2 font-mono">{row.sharpeRatio !== null ? row.sharpeRatio.toFixed(2) : "—"}</td>
                <td className="py-1 pl-2">{row.label ?? ""}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </CardContent>
    </Card>
  );
}

function fmtPct(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return `${(value * 100).toFixed(2)}%`;
}

function fmtMoney(value: number): string {
  if (!Number.isFinite(value)) return "—";
  const sign = value < 0 ? "-$" : "$";
  return `${sign}${Math.abs(value).toLocaleString("en-US", { maximumFractionDigits: 2 })}`;
}
