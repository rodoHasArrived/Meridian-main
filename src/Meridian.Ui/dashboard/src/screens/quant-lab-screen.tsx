import {
  AlertCircle,
  CheckCircle2,
  FlaskConical,
  Play,
  Settings2,
  Sparkles
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { QuantPlotChart } from "@/components/meridian/quant-plot";
import { ToolbarStrip } from "@/components/meridian/ui-kit-primitives";
import {
  buildTemplateLoadAriaLabel,
  useQuantLabScreenViewModel,
  type QuantParameterPanelState,
  type QuantParameterRow,
  type QuantRunResultPanelState,
  type QuantRunState,
  type QuantTemplatePanelState
} from "@/screens/quant-lab-screen.view-model";
import type { QuantDiagnostic, QuantTemplate } from "@/types";

export function QuantLabScreen() {
  const vm = useQuantLabScreenViewModel();

  return (
    <div className="space-y-6">
      <span className="sr-only" aria-live="polite">{vm.runStatusAnnouncement}</span>
      <Card>
        <CardHeader>
          <div className="eyebrow-label">Strategy Lane</div>
          <CardTitle className="flex items-center gap-2">
            <FlaskConical className="h-5 w-5 text-primary" />
            Quant Lab
          </CardTitle>
          <CardDescription>
            Compile and execute C# / .csx scripts against Meridian's price-series, statistics, and backtesting APIs. Plots, metrics, and diagnostics returned inline.
          </CardDescription>
        </CardHeader>
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
          <label htmlFor="quant-lab-source" className="sr-only">Script source</label>
          <textarea
            id="quant-lab-source"
            spellCheck={false}
            value={vm.source}
            onChange={(event) => vm.setSource(event.target.value)}
            className="w-full min-h-[16rem] resize-y rounded-md border border-border/70 bg-background/60 p-3 font-mono text-xs leading-5 text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
            aria-label="Script source"
            aria-describedby="quant-lab-source-help"
          />
          <p id="quant-lab-source-help" className="text-xs text-muted-foreground">
            Source is scanned for runtime parameters after edits settle.
          </p>
        </CardContent>
      </Card>

      <div className="grid gap-4 lg:grid-cols-[1fr_280px]">
        <RunResultPanel run={vm.run} panel={vm.resultPanel} consoleLines={vm.consoleLines} />
        <div className="space-y-4">
          <ParametersSidePanel
            rows={vm.parameterRows}
            panel={vm.parameterPanel}
            onChange={vm.updateParameter}
            onReset={vm.resetParameter}
          />
          <TemplatesPanel templates={vm.templates} state={vm.templatesPanel} onSelect={vm.loadTemplate} />
        </div>
      </div>
    </div>
  );
}

interface RunResultPanelProps {
  run: QuantRunState;
  panel: QuantRunResultPanelState;
  consoleLines: string[];
}

function RunResultPanel({ run, panel, consoleLines }: RunResultPanelProps) {
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
        <CardContent className="flex items-center gap-2 py-10 text-sm text-muted-foreground" role={panel.role} aria-live={panel.ariaLive}>
          <span className="h-4 w-4 animate-spin rounded-full border border-primary/30 border-t-primary" aria-hidden="true" />
          {panel.description}
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
                <CheckCircle2 className="h-4 w-4 text-positive" aria-hidden="true" />
              ) : (
                <AlertCircle className="h-4 w-4 text-danger" aria-hidden="true" />
              )}
              {panel.title}
            </CardTitle>
            <Badge variant={panel.tone === "success" ? "success" : panel.tone === "danger" ? "danger" : "outline"} dot>
              {panel.statusBadgeLabel}
            </Badge>
          </div>
          {result.runtimeError ? (
            <CardDescription className="text-danger/80">{panel.description}</CardDescription>
          ) : null}
        </CardHeader>
        <CardContent className="space-y-3">
          {panel.hasMetrics ? (
            <div>
              <div className="eyebrow-label mb-1">{panel.metricsLabel}</div>
              <table className="w-full text-sm">
                <tbody>
                  {result.metrics.map((m) => (
                    <tr key={m.label} className="border-b border-border/30">
                      <td className="py-1 pr-2 font-mono text-muted-foreground">{m.label}</td>
                      <td className="py-1 text-right font-mono text-foreground">{m.value}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
          {panel.hasConsoleOutput ? (
            <div>
              <div className="eyebrow-label mb-1">{panel.consoleLabel}</div>
              <pre className="max-h-48 overflow-auto rounded-md border border-border/60 bg-secondary/15 p-3 font-mono text-xs leading-5 text-foreground">
                {consoleLines.map((line, idx) => `${line}${idx < consoleLines.length - 1 ? "\n" : ""}`).join("")}
              </pre>
            </div>
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
                      onChange={(e) => onChange(row.name, e.target.checked ? "true" : "false")}
                      className="h-4 w-4 rounded border border-border accent-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
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
                    onChange={(e) => onChange(row.name, e.target.value)}
                    className="w-full rounded-md border border-border/70 bg-background/60 px-2 py-1.5 font-mono text-xs text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
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
                    onClick={() => onReset(row.name)}
                    className="text-[10px] text-muted-foreground underline-offset-2 hover:text-foreground hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
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
  templates: QuantTemplate[];
  state: QuantTemplatePanelState;
  onSelect: (template: QuantTemplate) => void;
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
                  aria-label={buildTemplateLoadAriaLabel(template)}
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
