import { useCallback, useEffect, useMemo, useState } from "react";
import {
  AlertCircle,
  CheckCircle2,
  FlaskConical,
  Loader2,
  Play,
  Sparkles
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { QuantPlotChart } from "@/components/meridian/quant-plot";
import { getQuantTemplates, runQuantScript } from "@/lib/api";
import type {
  QuantDiagnostic,
  QuantParameter,
  QuantRunResponse,
  QuantTemplate
} from "@/types";

const DEFAULT_SOURCE = `// Welcome to the Meridian Quant Lab.
// Press Run to compile and execute this C# script in-process.
Print("Hello from the Quant Lab.");
PrintMetric("answer", 42);
`;

interface RunState {
  phase: "idle" | "running" | "ready" | "error";
  result: QuantRunResponse | null;
  error: string | null;
}

const initialRunState: RunState = { phase: "idle", result: null, error: null };

export function QuantLabScreen() {
  const [source, setSource] = useState(DEFAULT_SOURCE);
  const [templates, setTemplates] = useState<QuantTemplate[]>([]);
  const [templatesError, setTemplatesError] = useState<string | null>(null);
  const [run, setRun] = useState<RunState>(initialRunState);

  useEffect(() => {
    let cancelled = false;
    getQuantTemplates()
      .then((response) => {
        if (cancelled) return;
        setTemplates(response.templates);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setTemplatesError((err as Error)?.message ?? "Failed to load templates.");
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const handleRun = useCallback(async () => {
    if (!source.trim()) {
      setRun({ phase: "error", result: null, error: "Enter some script source first." });
      return;
    }
    setRun({ phase: "running", result: null, error: null });
    try {
      const result = await runQuantScript({ source, parameters: {} });
      setRun({ phase: "ready", result, error: null });
    } catch (err) {
      setRun({
        phase: "error",
        result: null,
        error: (err as Error)?.message ?? "Failed to run script."
      });
    }
  }, [source]);

  const loadTemplate = useCallback((template: QuantTemplate) => {
    setSource(template.source);
    setRun(initialRunState);
  }, []);

  const consoleLines = useMemo(() => {
    if (!run.result) return [] as string[];
    return run.result.consoleOutput.split("\n");
  }, [run.result]);

  const summaryTone = run.result?.success ? "success" : run.phase === "error" || (run.result && !run.result.success) ? "danger" : "default";

  return (
    <div className="space-y-6">
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
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="default"
              onClick={() => void handleRun()}
              disabled={run.phase === "running"}
              aria-label="Run script"
            >
              {run.phase === "running" ? (
                <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
              ) : (
                <Play className="h-4 w-4" aria-hidden="true" />
              )}
              <span className="ml-1.5">{run.phase === "running" ? "Running…" : "Run"}</span>
            </Button>
            {run.result ? (
              <span className="text-xs text-muted-foreground">
                Compiled in {run.result.compileTimeMs.toFixed(0)} ms · executed in {run.result.elapsedMs.toFixed(0)} ms · peak {(run.result.peakMemoryBytes / 1024).toFixed(0)} KB
              </span>
            ) : null}
          </div>
          <label htmlFor="quant-lab-source" className="sr-only">Script source</label>
          <textarea
            id="quant-lab-source"
            spellCheck={false}
            value={source}
            onChange={(event) => setSource(event.target.value)}
            className="w-full min-h-[16rem] resize-y rounded-md border border-border/70 bg-background/60 p-3 font-mono text-xs leading-5 text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
            aria-label="Script source"
          />
        </CardContent>
      </Card>

      <div className="grid gap-4 lg:grid-cols-[1fr_280px]">
        <RunResultPanel run={run} consoleLines={consoleLines} tone={summaryTone} />
        <TemplatesPanel templates={templates} error={templatesError} onSelect={loadTemplate} />
      </div>
    </div>
  );
}

interface RunResultPanelProps {
  run: RunState;
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
        <CardContent className="flex items-center gap-2 py-10 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
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
          <RuntimeParametersBlock parameters={result.runtimeParameters} />
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

function RuntimeParametersBlock({ parameters }: { parameters: QuantParameter[] }) {
  if (parameters.length === 0) return null;
  return (
    <div>
      <div className="eyebrow-label mb-1">Parameters detected</div>
      <ul className="space-y-1 text-xs text-muted-foreground">
        {parameters.map((p) => (
          <li key={p.name} className="font-mono">
            {p.name} <span className="text-foreground/70">({p.typeName})</span>
            {p.defaultValue !== null ? <span> = {p.defaultValue}</span> : null}
            {p.description ? <span className="ml-2 italic opacity-70">{p.description}</span> : null}
          </li>
        ))}
      </ul>
    </div>
  );
}

interface TemplatesPanelProps {
  templates: QuantTemplate[];
  error: string | null;
  onSelect: (template: QuantTemplate) => void;
}

function TemplatesPanel({ templates, error, onSelect }: TemplatesPanelProps) {
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
        {error && templates.length === 0 ? (
          <p className="text-sm text-danger">{error}</p>
        ) : templates.length === 0 ? (
          <p className="text-sm text-muted-foreground">Loading templates…</p>
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
