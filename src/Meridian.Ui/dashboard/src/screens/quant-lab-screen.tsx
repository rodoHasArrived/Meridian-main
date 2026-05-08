import { useCallback, useEffect, useMemo, useState } from "react";
import {
  AlertCircle,
  CheckCircle2,
  FlaskConical,
  Loader2,
  Play,
  Settings2,
  Sparkles
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { QuantPlotChart } from "@/components/meridian/quant-plot";
import { extractQuantParameters, getQuantTemplates, runQuantScript } from "@/lib/api";
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

function mergeParams(existing: QuantParameter[], incoming: QuantParameter[]): QuantParameter[] {
  const map = new Map(existing.map((p) => [p.name, p]));
  for (const p of incoming) map.set(p.name, p);
  return [...map.values()];
}

function initNewParamValues(
  existing: Record<string, string>,
  params: QuantParameter[]
): Record<string, string> {
  const next = { ...existing };
  let changed = false;
  for (const p of params) {
    if (!(p.name in next) && p.defaultValue !== null) {
      next[p.name] = String(p.defaultValue);
      changed = true;
    }
  }
  return changed ? next : existing;
}

function buildParameters(
  params: QuantParameter[],
  values: Record<string, string>
): Record<string, string | number | boolean | null> {
  const result: Record<string, string | number | boolean | null> = {};
  for (const p of params) {
    const raw = values[p.name];
    if (raw === undefined || raw === "") {
      result[p.name] = null;
      continue;
    }
    switch (p.typeName) {
      case "bool":
        result[p.name] = raw === "true";
        break;
      case "int":
      case "long":
      case "double":
      case "float":
      case "decimal": {
        const n = Number(raw);
        result[p.name] = Number.isFinite(n) ? n : null;
        break;
      }
      default:
        result[p.name] = raw;
    }
  }
  return result;
}

export function QuantLabScreen() {
  const [source, setSource] = useState(DEFAULT_SOURCE);
  const [templates, setTemplates] = useState<QuantTemplate[]>([]);
  const [templatesError, setTemplatesError] = useState<string | null>(null);
  const [run, setRun] = useState<RunState>(initialRunState);
  const [detectedParams, setDetectedParams] = useState<QuantParameter[]>([]);
  const [paramValues, setParamValues] = useState<Record<string, string>>({});

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

  // Debounced parameter extraction from source
  useEffect(() => {
    if (!source.trim()) {
      setDetectedParams([]);
      return;
    }
    const timer = setTimeout(() => {
      extractQuantParameters(source)
        .then((r) => {
          setDetectedParams((prev) => mergeParams(prev, r.parameters));
          setParamValues((prev) => initNewParamValues(prev, r.parameters));
        })
        .catch(() => {/* non-critical — QuantLab may be disabled */});
    }, 600);
    return () => clearTimeout(timer);
  }, [source]);

  // Merge runtime parameters discovered during a run
  useEffect(() => {
    const rp = run.result?.runtimeParameters;
    if (!rp || rp.length === 0) return;
    setDetectedParams((prev) => mergeParams(prev, rp));
    setParamValues((prev) => initNewParamValues(prev, rp));
  }, [run.result]);

  const handleRun = useCallback(async () => {
    if (!source.trim()) {
      setRun({ phase: "error", result: null, error: "Enter some script source first." });
      return;
    }
    setRun({ phase: "running", result: null, error: null });
    try {
      const parameters = buildParameters(detectedParams, paramValues);
      const result = await runQuantScript({ source, parameters });
      setRun({ phase: "ready", result, error: null });
    } catch (err) {
      setRun({
        phase: "error",
        result: null,
        error: (err as Error)?.message ?? "Failed to run script."
      });
    }
  }, [source, detectedParams, paramValues]);

  const loadTemplate = useCallback((template: QuantTemplate) => {
    setSource(template.source);
    setRun(initialRunState);
    setDetectedParams([]);
    setParamValues({});
  }, []);

  const handleParamChange = useCallback((name: string, value: string) => {
    setParamValues((prev) => ({ ...prev, [name]: value }));
  }, []);

  const handleParamReset = useCallback((param: QuantParameter) => {
    setParamValues((prev) => ({
      ...prev,
      [param.name]: param.defaultValue !== null ? String(param.defaultValue) : ""
    }));
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
        <div className="space-y-4">
          <ParametersSidePanel
            params={detectedParams}
            values={paramValues}
            onChange={handleParamChange}
            onReset={handleParamReset}
          />
          <TemplatesPanel templates={templates} error={templatesError} onSelect={loadTemplate} />
        </div>
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

function inputTypeFor(typeName: string): "number" | "checkbox" | "text" {
  switch (typeName) {
    case "int":
    case "long":
    case "double":
    case "float":
    case "decimal":
      return "number";
    case "bool":
      return "checkbox";
    default:
      return "text";
  }
}

function stepFor(typeName: string): string {
  return typeName === "int" || typeName === "long" ? "1" : "any";
}

interface ParametersSidePanelProps {
  params: QuantParameter[];
  values: Record<string, string>;
  onChange: (name: string, value: string) => void;
  onReset: (param: QuantParameter) => void;
}

function ParametersSidePanel({ params, values, onChange, onReset }: ParametersSidePanelProps) {
  if (params.length === 0) return null;

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
        <ul className="space-y-3" aria-label="Script parameters">
          {params.map((p) => {
            const inputType = inputTypeFor(p.typeName);
            const currentValue = values[p.name] ?? (p.defaultValue !== null ? String(p.defaultValue) : "");
            const isDefault = p.defaultValue !== null && currentValue === String(p.defaultValue);
            return (
              <li key={p.name} className="space-y-1">
                <div className="flex items-center justify-between gap-1">
                  <label
                    htmlFor={`param-${p.name}`}
                    className="text-xs font-medium text-foreground"
                    title={p.description ?? undefined}
                  >
                    {p.label}
                  </label>
                  <span className="rounded bg-secondary/50 px-1 py-0.5 font-mono text-[10px] text-muted-foreground">
                    {p.typeName}
                  </span>
                </div>
                {inputType === "checkbox" ? (
                  <div className="flex items-center gap-2">
                    <input
                      id={`param-${p.name}`}
                      type="checkbox"
                      checked={currentValue === "true"}
                      onChange={(e) => onChange(p.name, e.target.checked ? "true" : "false")}
                      className="h-4 w-4 rounded border border-border accent-primary"
                      aria-label={`${p.label} parameter`}
                    />
                    <span className="text-xs text-muted-foreground">{currentValue === "true" ? "true" : "false"}</span>
                  </div>
                ) : (
                  <input
                    id={`param-${p.name}`}
                    type={inputType}
                    value={currentValue}
                    min={p.min !== null && p.min > Number.MIN_SAFE_INTEGER ? p.min : undefined}
                    max={p.max !== null && p.max < Number.MAX_SAFE_INTEGER ? p.max : undefined}
                    step={inputType === "number" ? stepFor(p.typeName) : undefined}
                    onChange={(e) => onChange(p.name, e.target.value)}
                    className="w-full rounded-md border border-border/70 bg-background/60 px-2 py-1.5 font-mono text-xs text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    aria-label={`${p.label} parameter`}
                  />
                )}
                {p.description ? (
                  <p className="text-[10px] leading-4 text-muted-foreground">{p.description}</p>
                ) : null}
                {!isDefault && p.defaultValue !== null ? (
                  <button
                    type="button"
                    onClick={() => onReset(p)}
                    className="text-[10px] text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
                    aria-label={`Reset ${p.label} to default`}
                  >
                    Reset to {p.defaultValue}
                  </button>
                ) : null}
              </li>
            );
          })}
        </ul>
      </CardContent>
    </Card>
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
