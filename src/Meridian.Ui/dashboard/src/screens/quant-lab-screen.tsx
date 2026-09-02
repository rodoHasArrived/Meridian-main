import {
  AlertCircle,
  CheckCircle2,
  FlaskConical,
  Play,
  Settings2,
  Sparkles
} from "lucide-react";
import { useEffect } from "react";
import { useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ScreenLayout } from "@/components/ui/screen-layout";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { SeverityBadge } from "@/components/operations";
import { QuantPlotChart } from "@/components/meridian/quant-plot";
import {
  DenseDataTable,
  EntitySummary,
  ToolbarStrip,
  type DenseDataTableColumn
} from "@/components/meridian/ui-kit-primitives";
import {
  useQuantLabScreenViewModel,
  type QuantMetricRowViewModel,
  type QuantParameterPanelState,
  type QuantParameterRow,
  type QuantTradeLedgerState,
  type QuantTradeRowViewModel,
  type QuantRunResultPanelState,
  type QuantRunState,
  type QuantTemplatePanelState,
  type QuantTemplateRow
} from "@/screens/quant-lab-screen.view-model";
import type { QuantDiagnostic } from "@/types";

const quantTradeColumns: DenseDataTableColumn<QuantTradeRowViewModel>[] = [
  { id: "timestamp", label: "Timestamp", render: (row) => <span className="font-mono">{row.timestamp}</span> },
  { id: "symbol", label: "Symbol", render: (row) => <span className="font-mono text-foreground">{row.symbol}</span> },
  { id: "side", label: "Side", render: (row) => <span className="font-mono">{row.side}</span> },
  { id: "quantity", label: "Qty", align: "right", render: (row) => <span className="font-mono">{row.quantity}</span> },
  { id: "price", label: "Price", align: "right", render: (row) => <span className="font-mono">{row.price}</span> },
  { id: "notional", label: "Notional", align: "right", render: (row) => <span className="font-mono">{row.notional}</span> },
  { id: "commission", label: "Comm.", align: "right", render: (row) => <span className="font-mono">{row.commission}</span> }
];

const quantMetricColumns: DenseDataTableColumn<QuantMetricRowViewModel>[] = [
  {
    id: "metric",
    label: "Metric",
    render: (row) => <span className="font-mono text-muted-foreground">{row.label}</span>
  },
  {
    id: "value",
    label: "Value",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.value}</span>
  }
];

// The Formulas tab is withheld while `/strategy/quant-lab?view=formulas` is registered in
// UNWIRED_WORKSTATION_ROUTES. Filtering it only out of the command palette left the dead end fully
// navigable from inside the screen, which is the more likely way an operator would find it. Restore
// this entry in the same change that mounts the built strategy-formula-workbench component against
// a real formula-catalog endpoint.
const QUANT_LAB_TABS = [{ id: "lab", label: "Script lab" }];

export function QuantLabScreen() {
  const [searchParams, setSearchParams] = useSearchParams();
  // A stale `?view=formulas` deep link or bookmark falls back to the script lab rather than
  // rendering a permanent "not connected" card.
  const view = "lab" as const;
  const staleViewParam = searchParams.get("view");

  // Canonicalize the address when an obsolete view arrives, so the URL matches what is on screen.
  // Without this the shell's Copy Link action would share a link claiming to be the Formula
  // Workbench while the operator is looking at the Script Lab.
  useEffect(() => {
    if (staleViewParam === null) return;
    const nextParams = new URLSearchParams(searchParams);
    nextParams.delete("view");
    setSearchParams(nextParams, { replace: true });
    // `searchParams` is intentionally omitted: it is a fresh object each render, and the effect
    // only needs to re-run when the offending parameter changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [staleViewParam, setSearchParams]);

  return (
    <ScreenLayout
      title={
        <span className="flex items-center gap-2">
          <FlaskConical className="h-5 w-5 text-primary" />
          Quant Lab
        </span>
      }
      scope="Strategy Lane"
      description="Compile and execute C# / .csx scripts against Meridian's price-series, statistics, and backtesting APIs. Plots, metrics, and diagnostics returned inline."
    >
      <Tabs
        tabs={QUANT_LAB_TABS}
        value={view}
        onValueChange={() => {
          const nextParams = new URLSearchParams(searchParams);
          nextParams.delete("view");
          setSearchParams(nextParams, { replace: true });
        }}
      >
        <TabPanel>
          {view === "lab" ? <ScriptLabPanel /> : null}
        </TabPanel>
      </Tabs>
    </ScreenLayout>
  );
}

function ScriptLabPanel() {
  const vm = useQuantLabScreenViewModel();

  return (
    <div className="grid gap-4">
      <span className="sr-only" aria-live="polite">{vm.runStatusAnnouncement}</span>
      <Card>
        <CardContent className="space-y-3">
          <ToolbarStrip
            ariaLabel="Quant Lab status"
            items={vm.toolbarItems}
            right={
              <Button
                type="button"
                variant="default"
                onClick={() => void vm.runScript()}
                disabled={vm.runCommand.disabled}
                disabledReason={vm.runCommand.disabledReason}
                busy={vm.runCommand.busy}
                busyLabel={vm.runCommand.label}
                aria-label={vm.runCommand.ariaLabel}
              >
                <Play className="h-4 w-4" aria-hidden="true" />
                <span className="ml-1.5">{vm.runCommand.label}</span>
              </Button>
            }
          />
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs text-muted-foreground">{vm.resultPanel.runtimeSummary}</span>
          </div>
          <label htmlFor={vm.sourceEditor.id} className="sr-only">{vm.sourceEditor.label}</label>
          <textarea
            id={vm.sourceEditor.id}
            spellCheck={false}
            value={vm.source}
            onChange={(event) => vm.setSource(event.target.value)}
            className="w-full min-h-[16rem] resize-y rounded-md border border-border/70 bg-background/60 p-3 font-mono text-xs leading-5 text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
            aria-label={vm.sourceEditor.ariaLabel}
            aria-describedby={vm.sourceEditor.describedBy}
          />
          <p id={vm.sourceEditor.helpId} className="text-xs text-muted-foreground">
            {vm.sourceEditor.helpText}
          </p>
        </CardContent>
      </Card>

      <div className="grid gap-4 lg:grid-cols-[1fr_280px]">
        <RunResultPanel
          run={vm.run}
          panel={vm.resultPanel}
          consoleLines={vm.consoleLines}
          tradeLedger={vm.tradeLedger}
          onTradeSelect={vm.selectTradeRow}
        />
        <div className="space-y-4">
          <ParametersSidePanel
            rows={vm.parameterRows}
            panel={vm.parameterPanel}
            onChange={vm.updateParameter}
            onReset={vm.resetParameter}
          />
          <TemplatesPanel templates={vm.templateRows} state={vm.templatesPanel} onSelect={vm.loadTemplate} />
        </div>
      </div>
    </div>
  );
}

interface RunResultPanelProps {
  run: QuantRunState;
  panel: QuantRunResultPanelState;
  consoleLines: string[];
  tradeLedger: QuantTradeLedgerState;
  onTradeSelect: (rowId: string) => void;
}

function RunResultPanel({ run, panel, consoleLines, tradeLedger, onTradeSelect }: RunResultPanelProps) {
  if (panel.phase === "idle") {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground" role={panel.role} aria-live={panel.ariaLive}>
          {panel.description}
        </CardContent>
      </Card>
    );
  }

  if (panel.phase === "running") {
    return (
      <Card>
        <CardContent className="space-y-3 py-10 text-sm text-muted-foreground" role={panel.role} aria-live={panel.ariaLive}>
          <div className="flex items-center gap-2">
            <span className="h-4 w-4 animate-spin rounded-full border border-primary/30 border-t-primary" aria-hidden="true" />
            {panel.description}
          </div>
          <SourceDriftNotice panel={panel} />
        </CardContent>
      </Card>
    );
  }

  if (!panel.hasResult || !run.result) {
    return (
      <Card role={panel.role} aria-live={panel.ariaLive}>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base text-danger">
            <AlertCircle className="h-4 w-4" aria-hidden="true" />
            {panel.title}
          </CardTitle>
          <CardDescription className="text-danger/80">{panel.description}</CardDescription>
        </CardHeader>
        {panel.sourceDrifted ? (
          <CardContent>
            <SourceDriftNotice panel={panel} />
          </CardContent>
        ) : null}
      </Card>
    );
  }

  const result = run.result;
  return (
    <div className="space-y-4">
      <Card role={panel.role} aria-live={panel.ariaLive}>
        <CardHeader>
          <div className="flex items-center justify-between gap-2">
            <CardTitle className="flex items-center gap-2 text-base">
              {result.success ? (
                <CheckCircle2 className="h-4 w-4 text-success" aria-hidden="true" />
              ) : (
                <AlertCircle className="h-4 w-4 text-danger" aria-hidden="true" />
              )}
              {panel.title}
            </CardTitle>
            <SeverityBadge status={panel.tone} label={panel.statusBadgeLabel} />
          </div>
          {result.runtimeError ? (
            <CardDescription className="text-danger/80">{panel.description}</CardDescription>
          ) : null}
        </CardHeader>
        <CardContent className="space-y-3">
          <SourceDriftNotice panel={panel} />
          {!panel.hasEvidence ? (
            <div
              role={panel.evidenceEmptyRole}
              aria-live={panel.ariaLive}
              className={`rounded-md border px-3 py-2 text-sm leading-6 ${emptyEvidenceToneClass[panel.evidenceEmptyTone]}`}
            >
              <div className="font-semibold">{panel.evidenceEmptyTitle}</div>
              <p className="mt-1">{panel.evidenceEmptyDetail}</p>
            </div>
          ) : null}
          {panel.hasMetrics ? (
            <section aria-label={panel.metricsLabel}>
              <div className="eyebrow-label mb-1">{panel.metricsLabel}</div>
              <DenseDataTable
                columns={quantMetricColumns}
                rows={panel.metricRows}
                getRowId={(row) => row.id}
                getRowAriaLabel={(row) => row.ariaLabel}
                emptyText={panel.metricsEmptyText}
                ariaLabel={panel.metricsTableLabel}
                caption={panel.metricsTableCaption}
              />
            </section>
          ) : null}
          {panel.hasConsoleOutput ? (
            <div>
              <div className="eyebrow-label mb-1">{panel.consoleLabel}</div>
              <pre className="max-h-48 overflow-auto rounded-md border border-border/60 bg-secondary/15 p-3 font-mono text-xs leading-5 text-foreground">
                {consoleLines.map((line, idx) => `${line}${idx < consoleLines.length - 1 ? "\n" : ""}`).join("")}
              </pre>
            </div>
          ) : null}
          {result.success || tradeLedger.hasTrades ? (
            <TradeLedgerPanel ledger={tradeLedger} onSelect={onTradeSelect} />
          ) : null}
          {panel.diagnosticSections.map((section) => (
            <DiagnosticsBlock key={section.id} label={section.label} entries={section.entries} tone={section.tone} />
          ))}
        </CardContent>
      </Card>

      {panel.hasPlots ? (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">{panel.plotsLabel}</CardTitle>
            <CardDescription>{panel.plotsDescription}</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="grid gap-3 lg:grid-cols-2">
              {result.plots.map((plot, idx) => (
                <QuantPlotChart key={`${plot.title}-${idx}`} plot={plot} />
              ))}
            </div>
          </CardContent>
        </Card>
      ) : null}
    </div>
  );
}

function TradeLedgerPanel({
  ledger,
  onSelect
}: {
  ledger: QuantTradeLedgerState;
  onSelect: (rowId: string) => void;
}) {
  return (
    <section className="space-y-3" aria-label={ledger.title}>
      <div>
        <div className="eyebrow-label mb-1">{ledger.title}</div>
        <p className="text-xs text-muted-foreground">{ledger.description}</p>
      </div>
      <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(18rem,0.42fr)]">
        <DenseDataTable
          columns={quantTradeColumns}
          rows={ledger.rows}
          getRowId={(row) => row.id}
          getRowSelectAriaLabel={(row) => row.ariaLabel}
          getRowAriaControls={() => ledger.detailPanelId}
          getRowAriaExpanded={(row) => row.id === ledger.selectedRowId}
          onRowSelect={(row) => onSelect(row.id)}
          selectedRowId={ledger.selectedRowId}
          emptyText={ledger.emptyText}
          ariaLabel={ledger.tableLabel}
          caption={ledger.tableCaption}
        />
        <div id={ledger.detailPanelId} aria-live="polite">
          {ledger.selectedDetail ? (
            <EntitySummary
              eyebrow={ledger.selectedDetail.eyebrow}
              title={ledger.selectedDetail.title}
              subtitle={ledger.selectedDetail.subtitle}
              description={ledger.selectedDetail.description}
              ariaLabel={ledger.selectedDetail.ariaLabel}
              fields={ledger.selectedDetail.fields}
              status={
                <SeverityBadge
                  status={ledger.selectedDetail.statusTone}
                  label={ledger.selectedDetail.statusLabel}
                />
              }
            />
          ) : (
            <aside
              className="row-detail-panel h-fit min-w-0 border-dashed text-sm text-muted-foreground"
              role="region"
              aria-label={ledger.detailEmptyTitle}
            >
              <div className="eyebrow-label">{ledger.detailEmptyTitle}</div>
              <p className="mt-2">{ledger.detailEmptyText}</p>
            </aside>
          )}
        </div>
      </div>
    </section>
  );
}

function SourceDriftNotice({ panel }: { panel: QuantRunResultPanelState }) {
  if (!panel.sourceDrifted || !panel.sourceDriftTitle || !panel.sourceDriftDetail) {
    return null;
  }

  return (
    <div
      role="status"
      aria-live="polite"
      className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning"
    >
      <div className="font-semibold">{panel.sourceDriftTitle}</div>
      <p className="mt-1">{panel.sourceDriftDetail}</p>
    </div>
  );
}

const emptyEvidenceToneClass: Record<QuantRunResultPanelState["evidenceEmptyTone"], string> = {
  warning: "border-warning/30 bg-warning/10 text-warning",
  danger: "border-danger/30 bg-danger/10 text-danger"
};

function DiagnosticsBlock({
  label,
  entries,
  tone
}: {
  label: string;
  entries: QuantDiagnostic[];
  tone: "danger" | "warning";
}) {
  if (entries.length === 0) return null;
  const toneClass = tone === "danger" ? "text-danger" : "text-warning";
  return (
    <div>
      <div className="eyebrow-label mb-1">{label}</div>
      <ul className="space-y-1 text-xs">
        {entries.map((entry, idx) => (
          <li key={idx} className={`font-mono ${toneClass}`}>
            <span className="mr-1.5 uppercase tracking-wide opacity-70">{entry.severity}</span>
            <span className="opacity-70">[{entry.line}:{entry.column}]</span>
            <span className="ml-1.5 text-foreground">{entry.message}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

interface ParametersSidePanelProps {
  rows: QuantParameterRow[];
  panel: QuantParameterPanelState;
  onChange: (name: string, value: string) => void;
  onReset: (name: string) => void;
}

function ParametersSidePanel({ rows, panel, onChange, onReset }: ParametersSidePanelProps) {
  const statusToneClass = {
    default: "border-border/70 bg-secondary/20 text-muted-foreground",
    pending: "border-primary/30 bg-primary/10 text-primary",
    warning: "border-warning/30 bg-warning/10 text-warning"
  } satisfies Record<QuantParameterPanelState["tone"], string>;

  return (
    <Card className="self-start">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Settings2 className="h-4 w-4 text-primary" aria-hidden="true" />
          {panel.title}
        </CardTitle>
        <CardDescription>{panel.description}</CardDescription>
      </CardHeader>
      <CardContent>
        {panel.statusMessage ? (
          <p
            role={panel.statusRole}
            aria-live={panel.ariaLive}
            className={`mb-3 rounded-md border px-3 py-2 text-sm ${statusToneClass[panel.tone]}`}
          >
            {panel.statusMessage}
          </p>
        ) : null}
        {panel.showRows ? (
          <ul className="space-y-3" aria-label={panel.listLabel}>
            {rows.map((row) => (
              <li key={row.name} className="space-y-1">
                <div className="flex items-center justify-between gap-1">
                  <label
                    htmlFor={row.inputId}
                    className="text-xs font-medium text-foreground"
                  >
                    {row.label}
                  </label>
                  <span className="rounded bg-secondary/50 px-1 py-0.5 font-mono text-[10px] text-muted-foreground">
                    {row.typeName}
                  </span>
                </div>
                {row.inputType === "checkbox" ? (
                  <div className="flex items-center gap-2">
                    <input
                      id={row.inputId}
                      type="checkbox"
                      checked={row.checked}
                      disabled={panel.controlsDisabled}
                      onChange={(e) => onChange(row.name, e.target.checked ? "true" : "false")}
                      className="h-4 w-4 rounded border border-border accent-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
                      aria-label={row.ariaLabel}
                      aria-describedby={row.descriptionId ?? undefined}
                    />
                    <span className="text-xs text-muted-foreground">{row.checked ? "true" : "false"}</span>
                  </div>
                ) : (
                  <input
                    id={row.inputId}
                    type={row.inputType}
                    value={row.value}
                    min={row.min}
                    max={row.max}
                    step={row.step}
                    disabled={panel.controlsDisabled}
                    onChange={(e) => onChange(row.name, e.target.value)}
                    className="w-full rounded-md border border-border/70 bg-background/60 px-2 py-1.5 font-mono text-xs text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
                    aria-label={row.ariaLabel}
                    aria-describedby={row.descriptionId ?? undefined}
                  />
                )}
                {row.description ? (
                  <p id={row.descriptionId ?? undefined} className="text-[10px] leading-4 text-muted-foreground">{row.description}</p>
                ) : null}
                {row.resetLabel ? (
                  <button
                    type="button"
                    disabled={panel.controlsDisabled}
                    onClick={() => onReset(row.name)}
                    className="text-[10px] text-muted-foreground underline-offset-2 hover:text-foreground hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
                    aria-label={row.resetLabel}
                  >
                    {row.resetText}
                  </button>
                ) : null}
              </li>
            ))}
          </ul>
        ) : null}
      </CardContent>
    </Card>
  );
}

interface TemplatesPanelProps {
  templates: QuantTemplateRow[];
  state: QuantTemplatePanelState;
  onSelect: (template: QuantTemplateRow) => void;
}

function TemplatesPanel({ templates, state, onSelect }: TemplatesPanelProps) {
  return (
    <Card className="self-start">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Sparkles className="h-4 w-4 text-primary" aria-hidden="true" />
          {state.title}
        </CardTitle>
        <CardDescription>{state.description}</CardDescription>
      </CardHeader>
      <CardContent>
        {state.phase !== "ready" ? (
          <p
            role={state.role}
            aria-live={state.ariaLive}
            className={state.phase === "error" ? "text-sm text-danger" : "text-sm text-muted-foreground"}
          >
            {state.message}
          </p>
        ) : (
          <ul className="space-y-2" aria-label={state.listLabel}>
            {templates.map((template) => (
              <li key={template.id}>
                <button
                  type="button"
                  onClick={() => onSelect(template)}
                  className="w-full rounded-md border border-border/60 bg-background/40 px-3 py-2 text-left text-sm transition-colors hover:bg-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  aria-label={template.ariaLabel}
                >
                  <div className="font-semibold text-foreground">{template.title}</div>
                  <div className="mt-1 text-xs text-muted-foreground">{template.description}</div>
                </button>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
