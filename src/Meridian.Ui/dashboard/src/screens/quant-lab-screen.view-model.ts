import { useCallback, useEffect, useMemo, useState } from "react";
import * as api from "@/lib/api";
import type {
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
  phase: QuantTemplatePhase;
  message: string;
  role: "status" | "alert";
  ariaLive: "polite" | "assertive";
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
  ariaLabel: string;
}

export interface QuantLabToolbarItem {
  id: string;
  label: string;
  value: string;
  active?: boolean;
}

export interface QuantLabScreenViewModel {
  source: string;
  setSource: (source: string) => void;
  run: QuantRunState;
  consoleLines: string[];
  summaryTone: "success" | "danger" | "default";
  templates: QuantTemplate[];
  templatesPanel: QuantTemplatePanelState;
  parameterRows: QuantParameterRow[];
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

    setParameterPhase("extracting");
    const timer = window.setTimeout(() => {
      services.extractParameters(source)
        .then((response) => {
          setDetectedParams((prev) => mergeQuantParameters(prev, response.parameters));
          setParamValues((prev) => initializeNewParameterValues(prev, response.parameters));
          setParameterPhase(response.parameters.length > 0 ? "ready" : "idle");
        })
        .catch(() => {
          setParameterPhase("unavailable");
        });
    }, 600);

    return () => window.clearTimeout(timer);
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

  return {
    source,
    setSource,
    run,
    consoleLines,
    summaryTone: buildSummaryTone(run),
    templates,
    templatesPanel,
    parameterRows,
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
  switch (phase) {
    case "loading":
      return {
        phase,
        message: "Loading starter templates...",
        role: "status",
        ariaLive: "polite"
      };
    case "empty":
      return {
        phase,
        message: "No starter templates are available from the Quant Lab API.",
        role: "status",
        ariaLive: "polite"
      };
    case "error":
      return {
        phase,
        message: error ?? "Failed to load templates.",
        role: "alert",
        ariaLive: "assertive"
      };
    default:
      return {
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
  return {
    name: parameter.name,
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
    ariaLabel: `${parameter.label} parameter`
  };
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
