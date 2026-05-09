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
  useQuantLabScreenViewModel,
  type QuantParameterRow,
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
            {vm.run.result ? (
              <span className="text-xs text-muted-foreground">
                Compiled in {vm.run.result.compileTimeMs.toFixed(0)} ms · executed in {vm.run.result.elapsedMs.toFixed(0)} ms · peak {(vm.run.result.peakMemoryBytes / 1024).toFixed(0)} KB
              </span>
            ) : (
              <span className="text-xs text-muted-foreground">
                Run state, parameters, and template availability are tracked before execution.
              </span>
            )}
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
        <RunResultPanel run={vm.run} consoleLines={vm.consoleLines} tone={vm.summaryTone} />
        <div className="space-y-4">
          <ParametersSidePanel
            rows={vm.parameterRows}
            phase={vm.parameterPhase}
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
  consoleLines: string[];
  tone: "success" | "danger" | "default";
}

function RunResultPanel({ run, consoleLines, tone }: RunResultPanelProps) {
  if (run.phase === "idle") {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Run a script to see console output, metrics, plots, and diagnostics here.
        </CardContent>
      </Card>
    );
  }

  if (run.phase === "running") {
    return (
      <Card>
        <CardContent className="flex items-center gap-2 py-10 text-sm text-muted-foreground" role="status" aria-live="polite">
          <span className="h-4 w-4 animate-spin rounded-full border border-primary/30 border-t-primary" aria-hidden="true" />
          Compiling and running script…
        </CardContent>
      </Card>
    );
  }

  if (run.phase === "error" || !run.result) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base text-danger">
            <AlertCircle className="h-4 w-4" aria-hidden="true" />
            Run failed
          </CardTitle>
          <CardDescription className="text-danger/80">{run.error ?? "Unknown error."}</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const result = run.result;
  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between gap-2">
            <CardTitle className="flex items-center gap-2 text-base">
              {result.success ? (
                <CheckCircle2 className="h-4 w-4 text-positive" aria-hidden="true" />
              ) : (
                <AlertCircle className="h-4 w-4 text-danger" aria-hidden="true" />
              )}
              {result.success ? "Run succeeded" : "Run finished with errors"}
            </CardTitle>
            <Badge variant={tone === "success" ? "success" : tone === "danger" ? "danger" : "outline"} dot>
              {result.success ? "OK" : "ERR"}
            </Badge>
          </div>
          {result.runtimeError ? (
            <CardDescription className="text-danger/80">{result.runtimeError}</CardDescription>
          ) : null}
        </CardHeader>
        <CardContent className="space-y-3">
          {result.metrics.length > 0 ? (
            <div>
              <div className="eyebrow-label mb-1">Metrics</div>
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
          {consoleLines.some((line) => line.length > 0) ? (
            <div>
              <div className="eyebrow-label mb-1">Console</div>
              <pre className="max-h-48 overflow-auto rounded-md border border-border/60 bg-secondary/15 p-3 font-mono text-xs leading-5 text-foreground">
                {consoleLines.map((line, idx) => `${line}${idx < consoleLines.length - 1 ? "\n" : ""}`).join("")}
              </pre>
            </div>
          ) : null}
          <DiagnosticsBlock label="Compilation errors" entries={result.compilationErrors} tone="danger" />
          <DiagnosticsBlock label="Runtime diagnostics" entries={result.runtimeDiagnostics} tone="warning" />
        </CardContent>
      </Card>

      {result.plots.length > 0 ? (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Plots</CardTitle>
            <CardDescription>{result.plots.length} chart{result.plots.length === 1 ? "" : "s"} returned by this run.</CardDescription>
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
  phase: "idle" | "extracting" | "ready" | "unavailable";
  onChange: (name: string, value: string) => void;
  onReset: (name: string) => void;
}

function ParametersSidePanel({ rows, phase, onChange, onReset }: ParametersSidePanelProps) {
  if (rows.length === 0 && phase !== "extracting" && phase !== "unavailable") return null;

  return (
    <Card className="self-start">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Settings2 className="h-4 w-4 text-primary" aria-hidden="true" />
          Parameters
        </CardTitle>
        <CardDescription>Override script parameters before running.</CardDescription>
      </CardHeader>
      <CardContent>
        {phase === "extracting" && rows.length === 0 ? (
          <p role="status" className="text-sm text-muted-foreground">Scanning source for parameters...</p>
        ) : phase === "unavailable" && rows.length === 0 ? (
          <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
            Parameter extraction is unavailable. The script can still run with inline defaults.
          </p>
        ) : (
          <ul className="space-y-3" aria-label="Script parameters">
            {rows.map((row) => (
              <li key={row.name} className="space-y-1">
                <div className="flex items-center justify-between gap-1">
                  <label
                    htmlFor={`param-${row.name}`}
                    className="text-xs font-medium text-foreground"
                    title={row.description ?? undefined}
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
                      id={`param-${row.name}`}
                      type="checkbox"
                      checked={row.checked}
                      onChange={(e) => onChange(row.name, e.target.checked ? "true" : "false")}
                      className="h-4 w-4 rounded border border-border accent-primary"
                      aria-label={row.ariaLabel}
                    />
                    <span className="text-xs text-muted-foreground">{row.checked ? "true" : "false"}</span>
                  </div>
                ) : (
                  <input
                    id={`param-${row.name}`}
                    type={row.inputType}
                    value={row.value}
                    min={row.min}
                    max={row.max}
                    step={row.step}
                    onChange={(e) => onChange(row.name, e.target.value)}
                    className="w-full rounded-md border border-border/70 bg-background/60 px-2 py-1.5 font-mono text-xs text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    aria-label={row.ariaLabel}
                  />
                )}
                {row.description ? (
                  <p className="text-[10px] leading-4 text-muted-foreground">{row.description}</p>
                ) : null}
                {row.resetLabel ? (
                  <button
                    type="button"
                    onClick={() => onReset(row.name)}
                    className="text-[10px] text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
                    aria-label={row.resetLabel}
                  >
                    Reset to default
                  </button>
                ) : null}
              </li>
            ))}
          </ul>
        )}
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
          Starter templates
        </CardTitle>
        <CardDescription>Load a working snippet to verify the lab end-to-end.</CardDescription>
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
          <ul className="space-y-2">
            {templates.map((template) => (
              <li key={template.id}>
                <button
                  type="button"
                  onClick={() => onSelect(template)}
                  className="w-full rounded-md border border-border/60 bg-background/40 px-3 py-2 text-left text-sm transition-colors hover:bg-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  aria-label={`Load ${template.title} template`}
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
