import { AlertCircle, ArrowLeft, ArrowRight, CircleX, FileText, Info, Layers, LineChart, Play, RotateCw, SlidersHorizontal } from "lucide-react";
import { useEffect } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ChartCard, EquityCurve as EquityCurveChart } from "@/components/charts";
import { SeverityBadge } from "@/components/operations";
import { DenseDataTable, EntitySummary, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import {
  useCoveredCallScreenViewModel,
  type CoveredCallChainPreviewRowViewModel,
  type CoveredCallFormFieldViewModel,
  type CoveredCallHistoryRowViewModel,
  type CoveredCallScreenViewModel,
  type CoveredCallStage,
  type CoveredCallTradeTimelineRowViewModel
} from "@/screens/covered-call-screen.view-model";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";

/** Map the view-model's `Badge` variant onto a Concrete operator-severity status string so
 * covered-call run/trade/chain statuses render through the shared `SeverityBadge`. Presentational
 * only — the view-model keeps emitting `statusBadgeVariant` for its own tests. */
function coveredCallSeverityStatus(variant: string): string {
  switch (variant) {
    case "success": return "ready";
    case "danger": return "blocked";
    case "warning": return "action";
    case "paper":
    case "research": return "review";
    default: return "info";
  }
}

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
      <SeverityBadge
        status={coveredCallSeverityStatus(row.statusBadgeVariant)}
        label={row.statusLabel}
        aria-label={row.statusAriaLabel}
      />
    )
  }
];

const historyColumns: DenseDataTableColumn<CoveredCallHistoryRowViewModel>[] = [
  {
    id: "started",
    label: "Started",
    render: (row) => <span className="font-mono text-muted-foreground">{row.startedAtLabel}</span>
  },
  {
    id: "underlying",
    label: "Underlying",
    render: (row) => <span className="font-mono font-semibold text-foreground">{row.underlyingSymbol}</span>
  },
  {
    id: "range",
    label: "Range",
    render: (row) => <span className="font-mono text-foreground">{row.rangeLabel}</span>
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <SeverityBadge status={coveredCallSeverityStatus(row.statusBadgeVariant)} label={row.statusLabel} />
    )
  },
  {
    id: "cagr",
    label: "CAGR",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.cagrLabel}</span>
  },
  {
    id: "sharpe",
    label: "Sharpe",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.sharpeRatioLabel}</span>
  },
  {
    id: "label",
    label: "Label",
    render: (row) => <span className="text-muted-foreground">{row.labelText}</span>
  }
];

const tradeTimelineColumns: DenseDataTableColumn<CoveredCallTradeTimelineRowViewModel>[] = [
  {
    id: "entry",
    label: "Entry",
    render: (row) => <span className="font-mono">{row.entryDateLabel}</span>
  },
  {
    id: "exit",
    label: "Exit",
    render: (row) => <span className="font-mono">{row.exitDateLabel}</span>
  },
  {
    id: "strike",
    label: "Strike",
    align: "right",
    render: (row) => <span className="font-mono">{row.strikeLabel}</span>
  },
  {
    id: "pnl",
    label: "PnL",
    align: "right",
    render: (row) => <span className={`font-mono ${row.pnlClassName}`}>{row.pnlLabel}</span>
  },
  {
    id: "reason",
    label: "Reason",
    render: (row) => (
      <SeverityBadge status={coveredCallSeverityStatus(row.statusBadgeVariant)} label={row.exitReasonLabel} />
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <SeverityBadge status={coveredCallSeverityStatus(row.statusBadgeVariant)} label={row.statusLabel} />
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
          <StageStepper navigation={vm.stageNavigation} onSelect={vm.goToStage} />
        </CardContent>
      </Card>

      {vm.errorBanner ? (
        <Card>
          <CardContent className="flex items-start gap-3 py-4 text-sm">
            <AlertCircle className="h-5 w-5 flex-shrink-0 text-danger" aria-hidden="true" />
            <div className="flex-1">
              <div className="font-semibold text-danger">Backtest issue</div>
              <p className="mt-1 text-foreground">{vm.errorBanner.summary}</p>
              {vm.errorBanner.details.length > 0 ? (
                <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5 text-danger">
                  {vm.errorBanner.details.map((detail) => (
                    <li key={detail}>{detail}</li>
                  ))}
                </ul>
              ) : null}
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
  navigation,
  onSelect
}: {
  navigation: CoveredCallScreenViewModel["stageNavigation"];
  onSelect: (s: CoveredCallStage) => void;
}) {
  return (
    <div className="space-y-2">
      <ol
        className="flex flex-wrap items-center gap-2 text-sm"
        aria-label="Covered call wizard stages"
        aria-describedby={navigation.feedbackText ? navigation.feedbackId : undefined}
      >
        {navigation.steps.map((step, idx) => {
          return (
            <li key={step.stage} className="flex items-center gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={step.disabled}
                disabledReason={step.disabledReason}
                onClick={() => onSelect(step.stage)}
                className={`uppercase tracking-wide ${
                  step.isCurrent
                    ? "border-primary bg-primary/10 text-primary"
                    : step.disabled
                      ? "border-border/50 bg-background/40 text-muted-foreground/60"
                      : "border-border/60 bg-background/60 text-muted-foreground hover:text-foreground"
                }`}
                aria-label={step.ariaLabel}
                aria-describedby={step.ariaDescribedBy}
                aria-current={step.ariaCurrent}
              >
                {step.buttonLabel}
              </Button>
              {idx < navigation.steps.length - 1 ? (
                <ArrowRight className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
              ) : null}
            </li>
          );
        })}
      </ol>
      <CommandFeedback id={navigation.feedbackId} message={navigation.feedbackText} />
    </div>
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
          {vm.formFieldGroups.map((group) => (
            <div key={group.id} className={group.columns === 2 ? "grid gap-3 sm:grid-cols-2" : "grid gap-3"}>
              {group.fields.map((field) => (
                <CoveredCallFormField key={field.key} vm={vm} field={field} />
              ))}
            </div>
          ))}

          <div className="flex items-center gap-2 pt-2">
            <Button
              type="button"
              variant="default"
              onClick={() => void vm.startRun()}
              disabled={vm.runCommand.disabled}
              disabledReason={vm.runCommand.disabledReason}
              busy={vm.runCommand.busy}
              busyLabel={vm.runCommand.busyLabel}
              aria-label={vm.runCommand.ariaLabel}
              aria-describedby={vm.runCommand.feedbackText ? vm.runCommand.feedbackId : undefined}
            >
              <Play className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">{vm.runCommand.label}</span>
            </Button>
            <Button type="button" variant="ghost" onClick={vm.resetForm}>
              Reset
            </Button>
            <Button type="button" variant="ghost" onClick={() => void vm.refreshChainPreview()}>
              <RotateCw className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">Refresh chain</span>
            </Button>
          </div>
          <CommandFeedback id={vm.runCommand.feedbackId} message={vm.runCommand.feedbackText} />
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

interface CoveredCallFormFieldProps {
  vm: CoveredCallScreenViewModel;
  field: CoveredCallFormFieldViewModel;
}

function CoveredCallFormField({ vm, field }: CoveredCallFormFieldProps) {
  return (
    <div className="space-y-1">
      <label htmlFor={field.id} className="text-xs font-medium text-foreground">
        {field.label}
        {field.required ? <span className="ml-0.5 text-danger" aria-hidden="true">*</span> : null}
      </label>
      {field.type === "select" ? (
        <Select
          id={field.id}
          value={vm.form[field.key]}
          onChange={(e) => vm.setField(field.key, e.target.value)}
          error={field.invalid}
          aria-describedby={field.describedBy}
        >
          {field.options.map((option) => (
            <option key={option.value} value={option.value} title={option.description}>
              {option.label}
            </option>
          ))}
        </Select>
      ) : (
        <Input
          id={field.id}
          type={field.type}
          step={field.step}
          value={vm.form[field.key]}
          onChange={(e) => vm.setField(field.key, e.target.value)}
          error={field.invalid}
          aria-describedby={field.describedBy}
          className="font-mono text-xs"
        />
      )}
      <p id={`${field.id}-help`} className="text-xs text-muted-foreground">
        {field.helperText}
      </p>
      {field.error ? (
        <p id={field.errorId} className="text-xs text-danger">
          {field.error}
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
            status={<SeverityBadge status={coveredCallSeverityStatus(panel.selectedDetail.statusBadgeVariant)} label={panel.selectedDetail.statusLabel} />}
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
          <div className="body">
            <div>{panel.detailEmptyText}</div>
            {panel.errorDetails.length > 0 ? (
              <ul className="mt-2 list-disc pl-5 text-xs text-muted-foreground">
                {panel.errorDetails.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
          </div>
        </section>
      )}
    </div>
  );
}

function RunStage({ vm }: { vm: CoveredCallScreenViewModel }) {
  const progress = vm.runProgressPanel;
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{progress.title}</CardTitle>
        <CardDescription>{progress.description}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <div
          className="h-2 w-full overflow-hidden rounded-full bg-secondary/40"
          role="progressbar"
          aria-valuenow={progress.percentComplete}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuetext={progress.ariaValueText}
          aria-busy={progress.ariaBusy}
        >
          <div className="h-full bg-primary transition-[width]" style={{ width: `${progress.percentComplete}%` }} />
        </div>
        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="ghost"
            onClick={() => vm.goToStage("configure")}
            disabled={vm.stageNavigation.configure.disabled}
            disabledReason={vm.stageNavigation.configure.disabledReason}
          >
            <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">Back</span>
          </Button>
          <Button
            type="button"
            variant="ghost"
            onClick={() => void vm.cancelRun()}
            disabled={vm.cancelRunCommand.disabled}
            disabledReason={vm.cancelRunCommand.disabledReason}
            busy={vm.cancelRunCommand.busy}
            busyLabel={vm.cancelRunCommand.busyLabel}
            aria-label={vm.cancelRunCommand.ariaLabel}
            aria-describedby={vm.cancelRunCommand.feedbackText ? vm.cancelRunCommand.feedbackId : undefined}
          >
            <CircleX className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">{vm.cancelRunCommand.label}</span>
          </Button>
        </div>
        <CommandFeedback id={vm.cancelRunCommand.feedbackId} message={vm.cancelRunCommand.feedbackText} />
      </CardContent>
    </Card>
  );
}

function CommandFeedback({ id, message }: { id: string; message: string | null }) {
  if (!message) return null;
  return (
    <p id={id} className="text-xs text-muted-foreground" aria-live="polite">
      {message}
    </p>
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
      <div className="space-y-4">
        <PayoffDiagramPanel vm={vm} />
        <ResultsActionPanel vm={vm} />
      </div>
    </div>
  );
}

function ResultsActionPanel({ vm }: { vm: CoveredCallScreenViewModel }) {
  const panel = vm.resultsActionPanel;
  const iconByAction: Record<string, typeof LineChart> = {
    "live-quote": LineChart,
    "strategy-designer": SlidersHorizontal,
    "report-pack": FileText
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{panel.title}</CardTitle>
        <CardDescription>{panel.description}</CardDescription>
      </CardHeader>
      <CardContent>
        <nav className="grid gap-2" aria-label="Covered-call results next workflow">
          {panel.actions.map((action) => {
            const Icon = iconByAction[action.id] ?? ArrowRight;
            return (
              <Button key={action.id} asChild variant="outline" className="h-auto justify-start px-3 py-2 text-left">
                <Link to={action.href} aria-label={action.ariaLabel}>
                  <Icon className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                  <span className="min-w-0">
                    <span className="block text-sm font-semibold text-foreground">{action.label}</span>
                    <span className="block text-xs font-normal leading-5 text-muted-foreground">{action.description}</span>
                  </span>
                </Link>
              </Button>
            );
          })}
        </nav>
      </CardContent>
    </Card>
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
  const strategy = points.map((p) => p.strategyEquity);
  const underlying = points.map((p) => p.underlyingEquity);
  const finalStrategy = strategy[strategy.length - 1];
  const finalUnderlying = underlying[underlying.length - 1];
  return (
    <ChartCard
      title="Equity curve"
      subtitle="Strategy vs underlying-only buy-and-hold."
      readout={[
        { label: "Strategy", value: fmtMoney(finalStrategy), color: "var(--chart-equity, #2F6F8F)" },
        { label: "Underlying", value: fmtMoney(finalUnderlying), color: "var(--chart-axis, #59636F)" }
      ]}
      height={220}
      style={{ flexShrink: 0 }}
    >
      <EquityCurveChart
        series={[
          { label: "Strategy", color: "var(--chart-equity, #2F6F8F)", points: strategy },
          { label: "Underlying", color: "var(--chart-axis, #59636F)", points: underlying, dashed: true, area: false }
        ]}
        valueFmt={fmtMoney}
      />
    </ChartCard>
  );
}

function PositionTimeline({ vm }: { vm: CoveredCallScreenViewModel }) {
  const panel = vm.tradeTimelinePanel;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{panel.title}</CardTitle>
      </CardHeader>
      <CardContent className="grid gap-3">
        <DenseDataTable
          columns={tradeTimelineColumns}
          rows={panel.rows}
          getRowId={(row) => row.id}
          getRowAriaLabel={(row) => row.rowAriaLabel}
          getRowSelectAriaLabel={(row) => row.rowSelectAriaLabel}
          getRowAriaControls={(row) => row.detailPanelId}
          getRowAriaExpanded={(row) => row.ariaExpanded}
          onRowSelect={(row) => vm.selectTradeRow(row.index)}
          selectedRowId={panel.selectedRowId}
          emptyText={panel.emptyText}
          ariaLabel={panel.tableLabel}
          caption={panel.tableCaption}
        />
        <div id={panel.detailPanelId} aria-live="polite">
          {panel.selectedDetail ? (
            <EntitySummary
              eyebrow={panel.selectedDetail.eyebrow}
              title={panel.selectedDetail.title}
              subtitle={panel.selectedDetail.subtitle}
              description={panel.selectedDetail.description}
              status={<SeverityBadge status={coveredCallSeverityStatus(panel.selectedDetail.statusBadgeVariant)} label={panel.selectedDetail.statusLabel} />}
              fields={panel.selectedDetail.fields}
              ariaLabel={panel.selectedDetail.ariaLabel}
            />
          ) : (
            <section
              className="row-detail-panel"
              aria-label={panel.detailEmptyAriaLabel}
            >
              <div className="head">{panel.detailEmptyTitle}</div>
              <div className="body">{panel.detailEmptyText}</div>
            </section>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

function PayoffDiagramPanel({ vm }: { vm: CoveredCallScreenViewModel }) {
  const panel = vm.payoffPanel;

  if (!panel.chart) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="text-base">{panel.title}</CardTitle>
          <CardDescription>{panel.description}</CardDescription>
        </CardHeader>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          {panel.emptyText}
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{panel.title}</CardTitle>
        <CardDescription>{panel.description}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {panel.positionOptions.length > 1 ? (
          <div className="flex flex-wrap gap-2" aria-label={panel.selectorAriaLabel}>
            {panel.positionOptions.map((option) => (
              <Button
                key={option.id}
                type="button"
                variant={option.buttonVariant}
                size="sm"
                onClick={() => vm.selectOpenPosition(option.index)}
                aria-pressed={option.selected}
                aria-label={option.ariaLabel}
                className="h-auto flex-col items-start gap-0.5 px-3 py-2 text-left"
              >
                <span className="font-mono">{option.label}</span>
                <span className="text-[11px] font-normal text-muted-foreground">{option.description}</span>
              </Button>
            ))}
          </div>
        ) : null}
        <svg viewBox={panel.chart.viewBox} role="img" aria-label={panel.chart.ariaLabel} className="h-44 w-full">
          <line {...panel.chart.zeroLine} stroke="currentColor" strokeOpacity={0.25} />
          <line {...panel.chart.strikeLine} stroke="currentColor" strokeOpacity={0.25} strokeDasharray="3 3" />
          <path d={panel.chart.path} fill="none" stroke="hsl(var(--primary))" strokeWidth={1.8} />
        </svg>
        <p className="mt-2 text-xs text-muted-foreground">
          {panel.note}
        </p>
      </CardContent>
    </Card>
  );
}

function HistoryPanel({ vm }: { vm: CoveredCallScreenViewModel }) {
  if (!vm.historyLoaded && !vm.historyLoading && vm.historyRows.length === 0 && !vm.historyError) {
    return null;
  }

  const description = vm.historyLoading
    ? "Loading previous covered-call runs from the strategy engine."
    : vm.historyError
      ? `Failed to load history: ${vm.historyError.summary}`
      : "Most recent first. Select a row to reload its results from the cached run store.";

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Previous runs</CardTitle>
        <CardDescription className={vm.historyError ? "text-danger" : undefined}>
          {description}
        </CardDescription>
      </CardHeader>
      <CardContent>
        {vm.historyLoading ? (
          <div
            role="status"
            aria-live="polite"
            className="rounded-lg border border-primary/25 bg-primary/10 px-4 py-3 text-sm text-primary"
          >
            {vm.historyStatusText}
          </div>
        ) : vm.historyError && vm.historyRows.length === 0 ? (
          <div
            role="alert"
            className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger"
          >
            <div>{vm.historyStatusText}</div>
            {vm.historyError.details.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                {vm.historyError.details.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
          </div>
        ) : (
          <DenseDataTable
            columns={historyColumns}
            rows={vm.historyRows}
            getRowId={(row) => row.runId}
            getRowAriaLabel={(row) => row.rowAriaLabel}
            getRowSelectAriaLabel={(row) => row.rowSelectAriaLabel}
            onRowSelect={(row) => void vm.openRun(row.runId)}
            selectedRowId={vm.run.runId}
            emptyText={vm.historyEmptyText}
            ariaLabel={vm.historyTableLabel}
            caption={vm.historyCaption}
          />
        )}
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
