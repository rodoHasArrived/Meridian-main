import { useCallback, useEffect, useMemo, useState } from "react";
import * as api from "@/lib/api";
import type {
  QuantDiagnostic,
  QuantParameter,
  QuantRunResponse,
  QuantTemplate
} from "@/types";

export const DEFAULT_QUANT_SOURCE = `// Welcome to the Meridian Quant Lab.
// Press Run to compile and execute this C# script in-process.
Print("Hello from the Quant Lab.");
PrintMetric("answer", 42);
`;

export interface QuantRunState {
  phase: "idle" | "running" | "ready" | "error";
  result: QuantRunResponse | null;
  error: string | null;
}

export type QuantTemplatePhase = "loading" | "ready" | "empty" | "error";
export type QuantParameterPhase = "idle" | "extracting" | "ready" | "unavailable";

export interface QuantTemplatePanelState {
  title: string;
  description: string;
  listLabel: string;
  phase: QuantTemplatePhase;
  message: string;
  role: "status" | "alert";
  ariaLive: "polite" | "assertive";
}

export interface QuantSourceEditorState {
  id: string;
  label: string;
  ariaLabel: string;
  describedBy: string;
  helpId: string;
  helpText: string;
}

export interface QuantTemplateRow {
  id: string;
  title: string;
  description: string;
  source: string;
  ariaLabel: string;
}

export interface QuantCommandState {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
}

export interface QuantParameterRow {
  name: string;
  inputId: string;
  descriptionId: string | null;
  label: string;
  typeName: string;
  description: string | null;
  inputType: "number" | "checkbox" | "text";
  value: string;
  checked: boolean;
  min?: number;
  max?: number;
  step?: string;
  isDefault: boolean;
  resetLabel: string | null;
  resetText: string | null;
  ariaLabel: string;
}

export interface QuantParameterPanelState {
  title: string;
  description: string;
  listLabel: string;
  statusMessage: string | null;
  statusRole: "status" | "alert";
  ariaLive: "polite" | "assertive";
  tone: "default" | "pending" | "warning";
  showRows: boolean;
}

export interface QuantLabToolbarItem {
  id: string;
  label: string;
  value: string;
  active?: boolean;
}

export interface QuantDiagnosticSectionState {
  id: string;
  label: string;
  entries: QuantDiagnostic[];
  tone: "danger" | "warning";
}

export interface QuantRunResultPanelState {
  phase: QuantRunState["phase"];
  tone: "success" | "danger" | "default";
  role: "status" | "alert" | "region";
  ariaLive: "polite" | "assertive";
  title: string;
  description: string;
  statusLabel: string;
  statusBadgeLabel: string;
  runtimeSummary: string;
  metricsLabel: string;
  consoleLabel: string;
  plotsLabel: string;
  plotsDescription: string;
  hasResult: boolean;
  hasMetrics: boolean;
  hasConsoleOutput: boolean;
  hasPlots: boolean;
  hasEvidence: boolean;
  evidenceEmptyTitle: string;
  evidenceEmptyDetail: string;
  evidenceEmptyRole: "status" | "alert";
  evidenceEmptyTone: "warning" | "danger";
  diagnosticSections: QuantDiagnosticSectionState[];
}

export interface QuantLabScreenViewModel {
  source: string;
  setSource: (source: string) => void;
  run: QuantRunState;
  resultPanel: QuantRunResultPanelState;
  consoleLines: string[];
  summaryTone: "success" | "danger" | "default";
  templates: QuantTemplate[];
  templateRows: QuantTemplateRow[];
  templatesPanel: QuantTemplatePanelState;
  sourceEditor: QuantSourceEditorState;
  parameterRows: QuantParameterRow[];
  parameterPanel: QuantParameterPanelState;
  parameterPhase: QuantParameterPhase;
  toolbarItems: QuantLabToolbarItem[];
  runCommand: QuantCommandState;
  runStatusAnnouncement: string;
  runScript: () => Promise<void>;
  loadTemplate: (template: QuantTemplate) => void;
  updateParameter: (name: string, value: string) => void;
  resetParameter: (name: string) => void;
}

export interface QuantLabServices {
  getTemplates: () => Promise<{ templates: QuantTemplate[] }>;
  extractParameters: (source: string) => Promise<{ parameters: QuantParameter[] }>;
  runScript: (request: { source: string; parameters: Record<string, string | number | boolean | null> }) => Promise<QuantRunResponse>;
}

export const initialQuantRunState: QuantRunState = { phase: "idle", result: null, error: null };

const defaultQuantLabServices: QuantLabServices = {
  getTemplates: () => api.getQuantTemplates(),
  extractParameters: (source) => api.extractQuantParameters(source),
  runScript: (request) => api.runQuantScript(request)
};

export function useQuantLabScreenViewModel(
  services: QuantLabServices = defaultQuantLabServices
): QuantLabScreenViewModel {
  const [source, setSource] = useState(DEFAULT_QUANT_SOURCE);
  const [templates, setTemplates] = useState<QuantTemplate[]>([]);
  const [templatesPhase, setTemplatesPhase] = useState<QuantTemplatePhase>("loading");
  const [templatesError, setTemplatesError] = useState<string | null>(null);
  const [run, setRun] = useState<QuantRunState>(initialQuantRunState);
  const [detectedParams, setDetectedParams] = useState<QuantParameter[]>([]);
  const [paramValues, setParamValues] = useState<Record<string, string>>({});
  const [parameterPhase, setParameterPhase] = useState<QuantParameterPhase>("idle");

  useEffect(() => {
    let cancelled = false;
    setTemplatesPhase("loading");
    services.getTemplates()
      .then((response) => {
        if (cancelled) return;
        setTemplates(response.templates);
        setTemplatesError(null);
        setTemplatesPhase(response.templates.length > 0 ? "ready" : "empty");
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setTemplates([]);
        setTemplatesError((err as Error)?.message ?? "Failed to load templates.");
        setTemplatesPhase("error");
      });
    return () => {
      cancelled = true;
    };
  }, [services]);

  useEffect(() => {
    if (!source.trim()) {
      setDetectedParams([]);
      setParameterPhase("idle");
      return;
    }

    let cancelled = false;
    setParameterPhase("extracting");
    const timer = window.setTimeout(() => {
      services.extractParameters(source)
        .then((response) => {
          if (cancelled) return;
          setDetectedParams((prev) => mergeQuantParameters(prev, response.parameters));
          setParamValues((prev) => initializeNewParameterValues(prev, response.parameters));
          setParameterPhase(response.parameters.length > 0 ? "ready" : "idle");
        })
        .catch(() => {
          if (cancelled) return;
          setParameterPhase("unavailable");
        });
    }, 600);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [services, source]);

  useEffect(() => {
    const runtimeParameters = run.result?.runtimeParameters;
    if (!runtimeParameters || runtimeParameters.length === 0) return;
    setDetectedParams((prev) => mergeQuantParameters(prev, runtimeParameters));
    setParamValues((prev) => initializeNewParameterValues(prev, runtimeParameters));
    setParameterPhase("ready");
  }, [run.result]);

  const runScriptCommand = useCallback(async () => {
    const validation = validateQuantSource(source);
    if (validation) {
      setRun({ phase: "error", result: null, error: validation });
      return;
    }

    setRun({ phase: "running", result: null, error: null });
    try {
      const parameters = buildQuantParameters(detectedParams, paramValues);
      const result = await services.runScript({ source, parameters });
      setRun({ phase: "ready", result, error: null });
    } catch (err) {
      setRun({
        phase: "error",
        result: null,
        error: (err as Error)?.message ?? "Failed to run script."
      });
    }
  }, [detectedParams, paramValues, services, source]);

  const loadTemplate = useCallback((template: QuantTemplate) => {
    setSource(template.source);
    setRun(initialQuantRunState);
    setDetectedParams([]);
    setParamValues({});
    setParameterPhase("extracting");
  }, []);

  const updateParameter = useCallback((name: string, value: string) => {
    setParamValues((prev) => ({ ...prev, [name]: value }));
  }, []);

  const resetParameter = useCallback((name: string) => {
    const parameter = detectedParams.find((p) => p.name === name);
    if (!parameter) return;
    setParamValues((prev) => ({
      ...prev,
      [name]: parameter.defaultValue !== null ? String(parameter.defaultValue) : ""
    }));
  }, [detectedParams]);

  const consoleLines = useMemo(() => {
    if (!run.result) return [] as string[];
    return run.result.consoleOutput.split("\n");
  }, [run.result]);

  const parameterRows = useMemo(
    () => detectedParams.map((parameter) => buildParameterRow(parameter, paramValues)),
    [detectedParams, paramValues]
  );

  const runCommand = buildRunCommandState(source, run.phase);
  const templatesPanel = buildTemplatePanelState(templatesPhase, templatesError);
  const parameterPanel = buildParameterPanelState(parameterPhase, parameterRows.length, source.trim().length > 0);

  return {
    source,
    setSource,
    run,
    resultPanel: buildRunResultPanelState(run),
    consoleLines,
    summaryTone: buildSummaryTone(run),
    templates,
    templateRows: buildTemplateRows(templates),
    templatesPanel,
    sourceEditor: buildSourceEditorState(),
    parameterRows,
    parameterPanel,
    parameterPhase,
    toolbarItems: buildToolbarItems(source, templates.length, parameterRows.length, run, parameterPhase),
    runCommand,
    runStatusAnnouncement: buildRunStatusAnnouncement(run),
    runScript: runScriptCommand,
    loadTemplate,
    updateParameter,
    resetParameter
  };
}

export function mergeQuantParameters(existing: QuantParameter[], incoming: QuantParameter[]): QuantParameter[] {
  const map = new Map(existing.map((parameter) => [parameter.name, parameter]));
  for (const parameter of incoming) {
    map.set(parameter.name, parameter);
  }
  return [...map.values()];
}

export function initializeNewParameterValues(
  existing: Record<string, string>,
  params: QuantParameter[]
): Record<string, string> {
  const next = { ...existing };
  let changed = false;
  for (const parameter of params) {
    if (!(parameter.name in next) && parameter.defaultValue !== null) {
      next[parameter.name] = String(parameter.defaultValue);
      changed = true;
    }
  }
  return changed ? next : existing;
}

export function buildQuantParameters(
  params: QuantParameter[],
  values: Record<string, string>
): Record<string, string | number | boolean | null> {
  const result: Record<string, string | number | boolean | null> = {};
  for (const parameter of params) {
    const raw = values[parameter.name];
    if (raw === undefined || raw === "") {
      result[parameter.name] = null;
      continue;
    }
    switch (parameter.typeName) {
      case "bool":
        result[parameter.name] = raw === "true";
        break;
      case "int":
      case "long":
      case "double":
      case "float":
      case "decimal": {
        const numericValue = Number(raw);
        result[parameter.name] = Number.isFinite(numericValue) ? numericValue : null;
        break;
      }
      default:
        result[parameter.name] = raw;
    }
  }
  return result;
}

export function validateQuantSource(source: string): string | null {
  return source.trim() ? null : "Enter some script source first.";
}

export function buildRunCommandState(source: string, phase: QuantRunState["phase"]): QuantCommandState {
  const sourceError = validateQuantSource(source);
  const running = phase === "running";
  return {
    label: running ? "Running..." : "Run",
    ariaLabel: running ? "Quant script is running" : "Run script",
    disabled: running || sourceError !== null,
    disabledReason: sourceError,
    busy: running
  };
}

export function buildTemplatePanelState(
  phase: QuantTemplatePhase,
  error: string | null
): QuantTemplatePanelState {
  const base = {
    title: "Starter templates",
    description: "Load a working snippet to verify the lab end-to-end.",
    listLabel: "Starter templates"
  };

  switch (phase) {
    case "loading":
      return {
        ...base,
        phase,
        message: "Loading starter templates...",
        role: "status",
        ariaLive: "polite"
      };
    case "empty":
      return {
        ...base,
        phase,
        message: "No starter templates are available from the Quant Lab API.",
        role: "status",
        ariaLive: "polite"
      };
    case "error":
      return {
        ...base,
        phase,
        message: error ?? "Failed to load templates.",
        role: "alert",
        ariaLive: "assertive"
      };
    default:
      return {
        ...base,
        phase,
        message: `${phase}`,
        role: "status",
        ariaLive: "polite"
      };
  }
}

export function buildParameterRow(
  parameter: QuantParameter,
  values: Record<string, string>
): QuantParameterRow {
  const inputType = inputTypeForQuantParameter(parameter.typeName);
  const value = values[parameter.name] ?? (parameter.defaultValue !== null ? String(parameter.defaultValue) : "");
  const isDefault = parameter.defaultValue !== null && value === String(parameter.defaultValue);
  const stableId = stableQuantParameterId(parameter.name);
  return {
    name: parameter.name,
    inputId: `quant-param-${stableId}`,
    descriptionId: parameter.description ? `quant-param-${stableId}-description` : null,
    label: parameter.label,
    typeName: parameter.typeName,
    description: parameter.description,
    inputType,
    value,
    checked: value === "true",
    min: parameter.min !== null && parameter.min > Number.MIN_SAFE_INTEGER ? parameter.min : undefined,
    max: parameter.max !== null && parameter.max < Number.MAX_SAFE_INTEGER ? parameter.max : undefined,
    step: inputType === "number" ? stepForQuantParameter(parameter.typeName) : undefined,
    isDefault,
    resetLabel: !isDefault && parameter.defaultValue !== null ? `Reset ${parameter.label} to default` : null,
    resetText: !isDefault && parameter.defaultValue !== null ? "Reset to default" : null,
    ariaLabel: `${parameter.label} parameter`
  };
}

export function buildTemplateLoadAriaLabel(template: QuantTemplate): string {
  return `Load ${template.title} template`;
}

export function buildTemplateRows(templates: QuantTemplate[]): QuantTemplateRow[] {
  return templates.map((template) => ({
    id: template.id,
    title: template.title,
    description: template.description,
    source: template.source,
    ariaLabel: buildTemplateLoadAriaLabel(template)
  }));
}

export function buildSourceEditorState(): QuantSourceEditorState {
  return {
    id: "quant-lab-source",
    label: "Script source",
    ariaLabel: "Script source",
    describedBy: "quant-lab-source-help",
    helpId: "quant-lab-source-help",
    helpText: "Source is scanned for runtime parameters after edits settle."
  };
}

export function buildParameterPanelState(
  phase: QuantParameterPhase,
  rowCount: number,
  hasSource: boolean
): QuantParameterPanelState {
  const base = {
    title: "Parameters",
    description: "Override runtime parameters before running the script.",
    listLabel: "Script parameters",
    statusRole: "status" as const,
    ariaLive: "polite" as const,
    showRows: rowCount > 0
  };

  if (rowCount > 0) {
    if (phase === "extracting") {
      return {
        ...base,
        tone: "pending",
        statusMessage: "Refreshing parameter metadata from the current source."
      };
    }

    if (phase === "unavailable") {
      return {
        ...base,
        tone: "warning",
        statusMessage: "Parameter extraction is unavailable. Existing values can still be edited and submitted."
      };
    }

    return {
      ...base,
      tone: "default",
      statusMessage: null
    };
  }

  if (!hasSource) {
    return {
      ...base,
      tone: "default",
      statusMessage: "Enter script source to scan for runtime parameters."
    };
  }

  if (phase === "extracting") {
    return {
      ...base,
      tone: "pending",
      statusMessage: "Scanning source for runtime parameters."
    };
  }

  if (phase === "unavailable") {
    return {
      ...base,
      tone: "warning",
      statusMessage: "Parameter extraction is unavailable. The script can still run with inline defaults."
    };
  }

  return {
    ...base,
    tone: "default",
    statusMessage: "No runtime parameters detected in the current script."
  };
}

function stableQuantParameterId(name: string): string {
  const normalized = name.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "parameter";
}

export function inputTypeForQuantParameter(typeName: string): "number" | "checkbox" | "text" {
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

export function stepForQuantParameter(typeName: string): string {
  return typeName === "int" || typeName === "long" ? "1" : "any";
}

export function buildSummaryTone(run: QuantRunState): "success" | "danger" | "default" {
  if (run.result?.success) return "success";
  if (run.phase === "error" || (run.result && !run.result.success)) return "danger";
  return "default";
}

export function buildRunStatusAnnouncement(run: QuantRunState): string {
  if (run.phase === "running") return "Quant script is compiling and running.";
  if (run.phase === "error") return run.error ? `Quant script error: ${run.error}` : "Quant script error.";
  if (run.result?.success) return "Quant script completed successfully.";
  if (run.result && !run.result.success) return "Quant script completed with errors.";
  return "Quant Lab is ready.";
}

export function buildRunResultPanelState(run: QuantRunState): QuantRunResultPanelState {
  const tone = buildSummaryTone(run);
  const result = run.result;

  if (run.phase === "idle") {
    return {
      phase: run.phase,
      tone,
      role: "status",
      ariaLive: "polite",
      title: "Run workspace idle",
      description: "Run a script to see console output, metrics, plots, and diagnostics here.",
      statusLabel: "Ready",
      statusBadgeLabel: "IDLE",
      runtimeSummary: "Run state, parameters, and template availability are tracked before execution.",
      metricsLabel: "Metrics",
      consoleLabel: "Console",
      plotsLabel: "Plots",
      plotsDescription: "No plots returned yet.",
      hasResult: false,
      hasMetrics: false,
      hasConsoleOutput: false,
      hasPlots: false,
      hasEvidence: false,
      evidenceEmptyTitle: "No runtime evidence yet",
      evidenceEmptyDetail: "Run a script to see emitted metrics, console output, diagnostics, and plots.",
      evidenceEmptyRole: "status",
      evidenceEmptyTone: "warning",
      diagnosticSections: []
    };
  }

  if (run.phase === "running") {
    return {
      phase: run.phase,
      tone,
      role: "status",
      ariaLive: "polite",
      title: "Running script",
      description: "Compiling and running script...",
      statusLabel: "Running",
      statusBadgeLabel: "RUN",
      runtimeSummary: "Quant Lab is compiling the current script and waiting for runtime evidence.",
      metricsLabel: "Metrics",
      consoleLabel: "Console",
      plotsLabel: "Plots",
      plotsDescription: "Plots will render after the run completes.",
      hasResult: false,
      hasMetrics: false,
      hasConsoleOutput: false,
      hasPlots: false,
      hasEvidence: false,
      evidenceEmptyTitle: "Waiting for runtime evidence",
      evidenceEmptyDetail: "Runtime output will appear after the script finishes compiling and executing.",
      evidenceEmptyRole: "status",
      evidenceEmptyTone: "warning",
      diagnosticSections: []
    };
  }

  if (run.phase === "error" || !result) {
    return {
      phase: run.phase,
      tone: "danger",
      role: "alert",
      ariaLive: "assertive",
      title: "Run failed",
      description: run.error ?? "Unknown error.",
      statusLabel: "Failed",
      statusBadgeLabel: "ERR",
      runtimeSummary: "Quant Lab could not complete the script run.",
      metricsLabel: "Metrics",
      consoleLabel: "Console",
      plotsLabel: "Plots",
      plotsDescription: "No plots returned because the run failed.",
      hasResult: false,
      hasMetrics: false,
      hasConsoleOutput: false,
      hasPlots: false,
      hasEvidence: false,
      evidenceEmptyTitle: "No runtime evidence returned",
      evidenceEmptyDetail: "The run failed before Meridian received metrics, console output, diagnostics, or plots.",
      evidenceEmptyRole: "alert",
      evidenceEmptyTone: "danger",
      diagnosticSections: []
    };
  }

  const hasMetrics = result.metrics.length > 0;
  const consoleLines = result.consoleOutput.split("\n");
  const hasConsoleOutput = consoleLines.some((line) => line.length > 0);
  const plotCount = result.plots.length;
  const diagnosticSections = [
    { id: "compilation", label: "Compilation errors", entries: result.compilationErrors, tone: "danger" as const },
    { id: "runtime", label: "Runtime diagnostics", entries: result.runtimeDiagnostics, tone: "warning" as const }
  ].filter((section) => section.entries.length > 0);
  const hasEvidence = hasMetrics || hasConsoleOutput || plotCount > 0 || diagnosticSections.length > 0;

  return {
    phase: run.phase,
    tone,
    role: result.success ? "region" : "alert",
    ariaLive: result.success ? "polite" : "assertive",
    title: result.success ? "Run succeeded" : "Run finished with errors",
    description: result.runtimeError ?? "Runtime evidence returned by this run.",
    statusLabel: result.success ? "Completed successfully" : "Completed with errors",
    statusBadgeLabel: result.success ? "OK" : "ERR",
    runtimeSummary: `Compiled in ${formatWholeNumber(result.compileTimeMs)} ms · executed in ${formatWholeNumber(result.elapsedMs)} ms · peak ${formatWholeNumber(result.peakMemoryBytes / 1024)} KB`,
    metricsLabel: hasMetrics ? `Metrics · ${result.metrics.length}` : "Metrics",
    consoleLabel: hasConsoleOutput ? "Console output" : "Console",
    plotsLabel: "Plots",
    plotsDescription: `${plotCount} chart${plotCount === 1 ? "" : "s"} returned by this run.`,
    hasResult: true,
    hasMetrics,
    hasConsoleOutput,
    hasPlots: plotCount > 0,
    hasEvidence,
    evidenceEmptyTitle: result.success ? "Run completed without runtime evidence" : "No runtime evidence returned",
    evidenceEmptyDetail: result.success
      ? "The script compiled and executed, but did not emit metrics, console output, diagnostics, or plots. Add Print, PrintMetric, or plot calls to produce inspectable evidence."
      : "The run completed with errors before Meridian received metrics, console output, diagnostics, or plots. Review the script and run it again.",
    evidenceEmptyRole: result.success ? "status" : "alert",
    evidenceEmptyTone: result.success ? "warning" : "danger",
    diagnosticSections
  };
}

export function buildToolbarItems(
  source: string,
  templateCount: number,
  parameterCount: number,
  run: QuantRunState,
  parameterPhase: QuantParameterPhase
): QuantLabToolbarItem[] {
  return [
    {
      id: "source",
      label: "Source",
      value: source.trim() ? "Ready" : "Empty",
      active: source.trim().length > 0
    },
    {
      id: "templates",
      label: "Templates",
      value: String(templateCount)
    },
    {
      id: "params",
      label: "Params",
      value: parameterPhase === "extracting" ? "Scan" : String(parameterCount),
      active: parameterCount > 0 || parameterPhase === "extracting"
    },
    {
      id: "run",
      label: "Run",
      value: run.phase === "ready" && run.result ? (run.result.success ? "OK" : "ERR") : run.phase,
      active: run.phase === "running"
    }
  ];
}

function formatWholeNumber(value: number): string {
  return Number.isFinite(value) ? value.toFixed(0) : "0";
}
