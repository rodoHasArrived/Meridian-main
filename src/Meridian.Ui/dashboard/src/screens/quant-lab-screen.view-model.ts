import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as api from "@/lib/api";
import { formatCurrency as formatCurrencyAmount, formatNumber as formatNumberAmount } from "@/lib/format";
import type {
  QuantDiagnostic,
  QuantParameter,
  QuantRunResponse,
  QuantTrade,
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
  submittedSource?: string | null;
  sourceChangedSinceRun?: boolean;
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

export interface QuantMetricRowViewModel {
  id: string;
  label: string;
  value: string;
  ariaLabel: string;
}

export interface QuantTradeRowViewModel {
  id: string;
  timestamp: string;
  symbol: string;
  side: string;
  quantity: string;
  price: string;
  notional: string;
  commission: string;
  ariaLabel: string;
}

export interface QuantTradeDetailField {
  label: string;
  value: string;
}

export interface QuantTradeDetailViewModel {
  ariaLabel: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "default";
  fields: QuantTradeDetailField[];
}

export interface QuantTradeLedgerState {
  title: string;
  description: string;
  tableLabel: string;
  tableCaption: string;
  emptyText: string;
  detailPanelId: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  rows: QuantTradeRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: QuantTradeDetailViewModel | null;
  hasTrades: boolean;
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
  metricsTableLabel: string;
  metricsTableCaption: string;
  metricsEmptyText: string;
  metricRows: QuantMetricRowViewModel[];
  consoleLabel: string;
  plotsLabel: string;
  plotsDescription: string;
  sourceDrifted: boolean;
  sourceDriftTitle: string | null;
  sourceDriftDetail: string | null;
  hasResult: boolean;
  hasMetrics: boolean;
  hasConsoleOutput: boolean;
  hasPlots: boolean;
  hasTrades: boolean;
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
  tradeLedger: QuantTradeLedgerState;
  toolbarItems: QuantLabToolbarItem[];
  runCommand: QuantCommandState;
  runStatusAnnouncement: string;
  runScript: () => Promise<void>;
  loadTemplate: (template: QuantTemplate) => void;
  updateParameter: (name: string, value: string) => void;
  resetParameter: (name: string) => void;
  selectTradeRow: (rowId: string) => void;
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

export const QUANT_TRADE_DETAIL_PANEL_ID = "quant-lab-selected-trade-detail";
const EMPTY_QUANT_TRADES: QuantTrade[] = [];

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
  const [selectedTradeRowId, setSelectedTradeRowId] = useState<string | null>(null);
  const sourceRef = useRef(DEFAULT_QUANT_SOURCE);
  const initialParameterScanScheduledRef = useRef(false);

  const updateSource = useCallback((nextSource: string) => {
    sourceRef.current = nextSource;
    setSource(nextSource);
    setRun((current) => markQuantRunSourceDrift(current, nextSource));
  }, []);

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
      setParamValues({});
      setParameterPhase("idle");
      return;
    }

    let cancelled = false;
    let requestStarted = false;
    const isInitialScan = !initialParameterScanScheduledRef.current;
    initialParameterScanScheduledRef.current = true;
    setDetectedParams([]);
    setParameterPhase("extracting");
    const timer = window.setTimeout(() => {
      requestStarted = true;
      services.extractParameters(source)
        .then((response) => {
          if (cancelled) return;
          setDetectedParams(response.parameters);
          setParamValues((prev) => reconcileQuantParameterValues(prev, response.parameters));
          setParameterPhase(response.parameters.length > 0 ? "ready" : "idle");
        })
        .catch(() => {
          if (cancelled) return;
          setParameterPhase("unavailable");
        });
    }, isInitialScan ? 0 : 600);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
      if (isInitialScan && !requestStarted) {
        initialParameterScanScheduledRef.current = false;
      }
    };
  }, [services, source]);

  useEffect(() => {
    const runtimeParameters = run.result?.runtimeParameters;
    if (!runtimeParameters || runtimeParameters.length === 0 ||
        run.sourceChangedSinceRun === true || run.submittedSource !== sourceRef.current) return;
    setDetectedParams((prev) => mergeQuantParameters(prev, runtimeParameters));
    setParamValues((values) => initializeNewParameterValues(values, runtimeParameters));
    setParameterPhase("ready");
  }, [run.result, run.sourceChangedSinceRun, run.submittedSource]);

  const runScriptCommand = useCallback(async () => {
    const validation = validateQuantSource(source);
    if (validation) {
      setRun({ phase: "error", result: null, error: validation, submittedSource: null, sourceChangedSinceRun: false });
      return;
    }

    const submittedSource = sourceRef.current;
    setRun({
      phase: "running",
      result: null,
      error: null,
      submittedSource,
      sourceChangedSinceRun: false
    });
    setSelectedTradeRowId(null);
    try {
      const parameters = buildQuantParameters(detectedParams, paramValues);
      const result = await services.runScript({ source: submittedSource, parameters });
      const tradeRows = buildQuantTradeRows(result.trades);
      setRun({
        phase: "ready",
        result,
        error: null,
        submittedSource,
        sourceChangedSinceRun: sourceRef.current !== submittedSource
      });
      setSelectedTradeRowId(tradeRows[0]?.id ?? null);
    } catch (err) {
      setRun({
        phase: "error",
        result: null,
        error: (err as Error)?.message ?? "Failed to run script.",
        submittedSource,
        sourceChangedSinceRun: sourceRef.current !== submittedSource
      });
      setSelectedTradeRowId(null);
    }
  }, [detectedParams, paramValues, services, source]);

  const loadTemplate = useCallback((template: QuantTemplate) => {
    updateSource(template.source);
    setRun(initialQuantRunState);
    setDetectedParams([]);
    setParamValues({});
    setParameterPhase("extracting");
    setSelectedTradeRowId(null);
  }, [updateSource]);

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
  const runTrades = run.result?.trades ?? EMPTY_QUANT_TRADES;
  const tradeRows = useMemo(() => buildQuantTradeRows(runTrades), [runTrades]);

  useEffect(() => {
    setSelectedTradeRowId((current) => {
      if (current && tradeRows.some((row) => row.id === current)) {
        return current;
      }

      return tradeRows[0]?.id ?? null;
    });
  }, [tradeRows]);

  const runCommand = buildRunCommandState(
    source,
    run.phase,
    run.sourceChangedSinceRun === true,
    parameterPhase
  );
  const templatesPanel = buildTemplatePanelState(templatesPhase, templatesError);
  const parameterPanel = buildParameterPanelState(parameterPhase, parameterRows.length, source.trim().length > 0);
  const tradeLedger = useMemo(
    () => buildQuantTradeLedgerState(runTrades, selectedTradeRowId),
    [runTrades, selectedTradeRowId]
  );

  return {
    source,
    setSource: updateSource,
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
    tradeLedger,
    toolbarItems: buildToolbarItems(source, templates.length, parameterRows.length, run, parameterPhase),
    runCommand,
    runStatusAnnouncement: buildRunStatusAnnouncement(run),
    runScript: runScriptCommand,
    loadTemplate,
    updateParameter,
    resetParameter,
    selectTradeRow: setSelectedTradeRowId
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

export function reconcileQuantParameterValues(
  existing: Record<string, string>,
  params: QuantParameter[]
): Record<string, string> {
  const parametersByName = new Map(
    params.map((parameter) => [parameter.name.toLowerCase(), parameter] as const)
  );
  const retained: Record<string, string> = {};
  for (const [name, value] of Object.entries(existing)) {
    const parameter = parametersByName.get(name.toLowerCase());
    if (parameter) {
      retained[parameter.name] = value;
    }
  }
  return initializeNewParameterValues(retained, params);
}

export function buildQuantParameters(
  params: QuantParameter[],
  values: Record<string, string>
): Record<string, string | number | boolean | null> {
  const result: Record<string, string | number | boolean | null> = {};
  for (const parameter of params) {
    const raw = values[parameter.name];
    if (raw === undefined || (raw === "" && parameter.typeName !== "string")) {
      continue;
    }
    switch (parameter.typeName) {
      case "bool":
        if (raw !== "true" && raw !== "false") {
          throw new Error(`${parameter.label} must be true or false.`);
        }
        result[parameter.name] = raw === "true";
        break;
      case "int": {
        if (!/^[+-]?\d+$/.test(raw)) {
          throw new Error(`${parameter.label} must be a whole number.`);
        }
        const numericValue = Number(raw);
        if (!Number.isInteger(numericValue) || numericValue < -2147483648 || numericValue > 2147483647) {
          throw new Error(`${parameter.label} is outside the Int32 range.`);
        }
        assertQuantParameterBounds(parameter, numericValue);
        result[parameter.name] = numericValue;
        break;
      }
      case "long": {
        const canonical = canonicalInt64(raw, parameter.label);
        assertQuantParameterBounds(parameter, Number(canonical));
        result[parameter.name] = canonical;
        break;
      }
      case "double":
      case "float": {
        const numericValue = Number(raw);
        if (!Number.isFinite(numericValue) || (parameter.typeName === "float" && Math.abs(numericValue) > 3.4028234663852886e38)) {
          throw new Error(`${parameter.label} must be a finite ${parameter.typeName} value.`);
        }
        assertQuantParameterBounds(parameter, numericValue);
        result[parameter.name] = numericValue;
        break;
      }
      case "decimal": {
        const canonical = canonicalDecimal(raw, parameter.label);
        assertQuantParameterBounds(parameter, Number(canonical));
        result[parameter.name] = canonical;
        break;
      }
      default:
        result[parameter.name] = raw;
    }
  }
  return result;
}

const INT64_MIN = -9223372036854775808n;
const INT64_MAX = 9223372036854775807n;
const DECIMAL_MAX_COEFFICIENT = 79228162514264337593543950335n;

function canonicalInt64(raw: string, label: string): string {
  const trimmed = raw.trim();
  if (!/^[+-]?\d+$/.test(trimmed)) {
    throw new Error(`${label} must be a whole number.`);
  }
  const value = BigInt(trimmed);
  if (value < INT64_MIN || value > INT64_MAX) {
    throw new Error(`${label} is outside the Int64 range.`);
  }
  return value.toString();
}

function canonicalDecimal(raw: string, label: string): string {
  const trimmed = raw.trim();
  const match = /^([+-]?)(\d+)(?:\.(\d+))?$/.exec(trimmed);
  if (!match) {
    throw new Error(`${label} must be a decimal number without exponent notation.`);
  }

  const negative = match[1] === "-";
  const integer = (match[2] ?? "0").replace(/^0+(?=\d)/, "");
  const fraction = (match[3] ?? "").replace(/0+$/, "");
  if (fraction.length > 28) {
    throw new Error(`${label} has more than 28 decimal places.`);
  }

  const coefficientText = `${integer}${fraction}`.replace(/^0+/, "") || "0";
  if (BigInt(coefficientText) > DECIMAL_MAX_COEFFICIENT) {
    throw new Error(`${label} is outside the Decimal range.`);
  }

  const magnitude = fraction.length > 0 ? `${integer}.${fraction}` : integer;
  return negative && coefficientText !== "0" ? `-${magnitude}` : magnitude;
}

function assertQuantParameterBounds(parameter: QuantParameter, value: number): void {
  if (parameter.min !== null && value < parameter.min) {
    throw new Error(`${parameter.label} must be at least ${parameter.min.toString()}.`);
  }
  if (parameter.max !== null && value > parameter.max) {
    throw new Error(`${parameter.label} must be at most ${parameter.max.toString()}.`);
  }
}

export function validateQuantSource(source: string): string | null {
  return source.trim() ? null : "Enter some script source first.";
}

export function buildRunCommandState(
  source: string,
  phase: QuantRunState["phase"],
  sourceChangedSinceRun = false,
  parameterPhase: QuantParameterPhase = "idle"
): QuantCommandState {
  const sourceError = validateQuantSource(source);
  const running = phase === "running";
  const extractingParameters = parameterPhase === "extracting";
  const disabledReason = sourceError ?? (
    extractingParameters ? "Wait for runtime parameter detection to finish." : null
  );
  return {
    label: running ? "Running..." : sourceChangedSinceRun && sourceError === null ? "Run current source" : "Run",
    ariaLabel: running
      ? "Quant script is running"
      : sourceChangedSinceRun && sourceError === null
        ? "Run current edited script source"
        : "Run script",
    disabled: running || disabledReason !== null,
    disabledReason,
    busy: running
  };
}

export function markQuantRunSourceDrift(run: QuantRunState, currentSource: string): QuantRunState {
  if (!run.submittedSource || run.phase === "idle") {
    return run;
  }

  const sourceChangedSinceRun = currentSource !== run.submittedSource;
  if (run.sourceChangedSinceRun === sourceChangedSinceRun) {
    return run;
  }

  return {
    ...run,
    sourceChangedSinceRun
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

export function buildQuantTradeRows(trades: readonly QuantTrade[]): QuantTradeRowViewModel[] {
  return trades.map((trade, index) => {
    const id = buildQuantTradeRowId(trade, index);
    const quantity = formatQuantQuantity(trade.quantity);
    const price = formatQuantMoney(trade.price);
    const commission = formatQuantMoney(trade.commission);
    const notional = formatQuantMoney(Math.abs(trade.quantity * trade.price));
    const side = formatQuantTradeSide(trade.side);
    const timestamp = formatQuantTradeTimestamp(trade.timestamp);

    return {
      id,
      timestamp,
      symbol: trade.symbol,
      side,
      quantity,
      price,
      notional,
      commission,
      ariaLabel: `Select ${trade.symbol} ${side.toLowerCase()} trade at ${timestamp}, ${quantity} shares at ${price}`
    };
  });
}

export function buildQuantTradeLedgerState(
  trades: readonly QuantTrade[],
  selectedRowId: string | null
): QuantTradeLedgerState {
  const rows = buildQuantTradeRows(trades);
  const selectedRow = rows.find((row) => row.id === selectedRowId) ?? rows[0] ?? null;
  const selectedTrade = selectedRow ? trades[rows.indexOf(selectedRow)] ?? null : null;

  return {
    title: rows.length > 0 ? `Trade ledger - ${rows.length}` : "Trade ledger",
    description: rows.length > 0
      ? "Executed trades emitted by the script run. Select a row to inspect notional and commission evidence."
      : "Executed trades emitted by the script run appear here when the strategy records fills.",
    tableLabel: "Quant Lab trade ledger",
    tableCaption: "Select a Quant Lab trade to inspect timestamp, side, notional, and commission evidence.",
    emptyText: "No trades returned by this run.",
    detailPanelId: QUANT_TRADE_DETAIL_PANEL_ID,
    detailEmptyTitle: "No trade selected",
    detailEmptyText: rows.length > 0
      ? "Select a trade row to inspect execution evidence."
      : "This run did not return trades.",
    rows,
    selectedRowId: selectedRow?.id ?? null,
    selectedDetail: selectedTrade && selectedRow ? buildQuantTradeDetail(selectedTrade, selectedRow) : null,
    hasTrades: rows.length > 0
  };
}

export function buildQuantTradeRowId(trade: QuantTrade, index: number): string {
  if (trade.fillId.trim()) {
    return `quant-trade-${trade.fillId.trim().toLowerCase()}`;
  }
  const raw = `${trade.timestamp}-${trade.symbol}-${trade.side}-${index}`;
  const stable = raw.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
  return `quant-trade-${stable || index.toString()}`;
}

function buildQuantTradeDetail(
  trade: QuantTrade,
  row: QuantTradeRowViewModel
): QuantTradeDetailViewModel {
  const signedQuantity = trade.side.toLowerCase().includes("sell") ? -Math.abs(trade.quantity) : Math.abs(trade.quantity);
  const grossNotional = Math.abs(trade.quantity * trade.price);
  const netCashImpact = trade.side.toLowerCase().includes("sell")
    ? grossNotional - trade.commission
    : -(grossNotional + trade.commission);

  return {
    ariaLabel: `${trade.symbol} ${row.side.toLowerCase()} trade detail`,
    eyebrow: "Selected trade",
    title: `${trade.symbol} ${row.side}`,
    subtitle: `${row.timestamp} - ${row.quantity} @ ${row.price}`,
    description: `Net cash impact ${formatSignedQuantMoney(netCashImpact)} after ${row.commission} commission.`,
    statusLabel: row.side.toLowerCase().includes("buy") ? "BUY" : row.side.toLowerCase().includes("sell") ? "SELL" : "TRADE",
    statusTone: row.side.toLowerCase().includes("buy") ? "success" : row.side.toLowerCase().includes("sell") ? "warning" : "default",
    fields: [
      { label: "Fill ID", value: trade.fillId },
      { label: "Order ID", value: trade.orderId },
      { label: "Backtest run", value: (trade.backtestRunIndex + 1).toString() },
      { label: "Symbol", value: trade.symbol },
      { label: "Side", value: row.side },
      { label: "Timestamp", value: row.timestamp },
      { label: "Quantity", value: formatSignedQuantQuantity(signedQuantity) },
      { label: "Price", value: row.price },
      { label: "Gross notional", value: row.notional },
      { label: "Commission", value: row.commission },
      { label: "Net cash", value: formatSignedQuantMoney(netCashImpact) }
    ]
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
  if (run.phase === "running") {
    return run.sourceChangedSinceRun
      ? "Quant script is compiling and running. The editor has changed since this run started."
      : "Quant script is compiling and running.";
  }
  if (run.phase === "error") {
    const message = run.error ? `Quant script error: ${run.error}` : "Quant script error";
    return run.sourceChangedSinceRun
      ? `${message} The editor has changed since this run started.`
      : message.endsWith(".")
        ? message
        : `${message}.`;
  }
  if (run.result?.success) {
    return run.sourceChangedSinceRun
      ? "Quant script completed successfully for a previous source."
      : "Quant script completed successfully.";
  }
  if (run.result && !run.result.success) {
    return run.sourceChangedSinceRun
      ? "Quant script completed with errors for a previous source."
      : "Quant script completed with errors.";
  }
  return "Quant Lab is ready.";
}

function buildQuantRunSourceDriftState(run: QuantRunState): Pick<
  QuantRunResultPanelState,
  "sourceDrifted" | "sourceDriftTitle" | "sourceDriftDetail"
> {
  if (run.sourceChangedSinceRun !== true) {
    return {
      sourceDrifted: false,
      sourceDriftTitle: null,
      sourceDriftDetail: null
    };
  }

  if (run.phase === "running") {
    return {
      sourceDrifted: true,
      sourceDriftTitle: "Editor changed during this run",
      sourceDriftDetail: "The run is still using the source that was submitted. Run current source after it finishes to refresh the evidence."
    };
  }

  return {
    sourceDrifted: true,
    sourceDriftTitle: "Evidence is for previous source",
    sourceDriftDetail: "The editor changed after this run started. Run current source before accepting metrics, console output, diagnostics, or plots."
  };
}

export function buildRunResultPanelState(run: QuantRunState): QuantRunResultPanelState {
  const tone = buildSummaryTone(run);
  const result = run.result;
  const sourceDriftState = buildQuantRunSourceDriftState(run);

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
      metricsTableLabel: "Quant Lab metrics",
      metricsTableCaption: "Metrics emitted by the Quant Lab script run.",
      metricsEmptyText: "No metrics returned by this run.",
      metricRows: [],
      consoleLabel: "Console",
      plotsLabel: "Plots",
      plotsDescription: "No plots returned yet.",
      ...sourceDriftState,
      hasResult: false,
      hasMetrics: false,
      hasConsoleOutput: false,
      hasPlots: false,
      hasTrades: false,
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
      metricsTableLabel: "Quant Lab metrics",
      metricsTableCaption: "Metrics emitted by the Quant Lab script run.",
      metricsEmptyText: "Metrics will appear after the run completes.",
      metricRows: [],
      consoleLabel: "Console",
      plotsLabel: "Plots",
      plotsDescription: "Plots will render after the run completes.",
      ...sourceDriftState,
      hasResult: false,
      hasMetrics: false,
      hasConsoleOutput: false,
      hasPlots: false,
      hasTrades: false,
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
      metricsTableLabel: "Quant Lab metrics",
      metricsTableCaption: "Metrics emitted by the Quant Lab script run.",
      metricsEmptyText: "No metrics returned because the run failed.",
      metricRows: [],
      consoleLabel: "Console",
      plotsLabel: "Plots",
      plotsDescription: "No plots returned because the run failed.",
      ...sourceDriftState,
      hasResult: false,
      hasMetrics: false,
      hasConsoleOutput: false,
      hasPlots: false,
      hasTrades: false,
      hasEvidence: false,
      evidenceEmptyTitle: "No runtime evidence returned",
      evidenceEmptyDetail: "The run failed before Meridian received metrics, console output, diagnostics, or plots.",
      evidenceEmptyRole: "alert",
      evidenceEmptyTone: "danger",
      diagnosticSections: []
    };
  }

  const hasMetrics = result.metrics.length > 0;
  const metricRows = buildQuantMetricRows(result.metrics);
  const consoleLines = result.consoleOutput.split("\n");
  const hasConsoleOutput = consoleLines.some((line) => line.length > 0);
  const plotCount = result.plots.length;
  const tradeCount = result.trades.length;
  const diagnosticSections = [
    { id: "compilation", label: "Compilation errors", entries: result.compilationErrors, tone: "danger" as const },
    { id: "compilation-warnings", label: "Compilation warnings", entries: result.compilationWarnings, tone: "warning" as const },
    { id: "runtime", label: "Runtime diagnostics", entries: result.runtimeDiagnostics, tone: "warning" as const }
  ].filter((section) => section.entries.length > 0);
  const hasEvidence = hasMetrics || hasConsoleOutput || plotCount > 0 || tradeCount > 0 || diagnosticSections.length > 0;

  return {
    phase: run.phase,
    tone,
    role: result.success ? "region" : "alert",
    ariaLive: result.success ? "polite" : "assertive",
    title: sourceDriftState.sourceDrifted
      ? result.success
        ? "Run succeeded for previous source"
        : "Run finished with errors for previous source"
      : result.success ? "Run succeeded" : "Run finished with errors",
    description: result.runtimeError ?? (
      sourceDriftState.sourceDrifted
        ? "Runtime evidence returned by the previously submitted source."
        : "Runtime evidence returned by this run."
    ),
    statusLabel: result.success ? "Completed successfully" : "Completed with errors",
    statusBadgeLabel: result.success ? "OK" : "ERR",
    runtimeSummary: `Compiled in ${formatWholeNumber(result.compileTimeMs)} ms · executed in ${formatWholeNumber(result.elapsedMs)} ms · peak ${formatWholeNumber(result.peakMemoryBytes / 1024)} KB`,
    metricsLabel: hasMetrics ? `Metrics - ${metricRows.length}` : "Metrics",
    metricsTableLabel: "Quant Lab metrics",
    metricsTableCaption: "Metrics emitted by the Quant Lab script run. Values are script-defined evidence and may include ratios, counters, or formatted text.",
    metricsEmptyText: result.success ? "No metrics returned by this run." : "No metrics returned because the run finished with errors.",
    metricRows,
    consoleLabel: hasConsoleOutput ? "Console output" : "Console",
    plotsLabel: "Plots",
    plotsDescription: `${plotCount} chart${plotCount === 1 ? "" : "s"} returned by this run.`,
    ...sourceDriftState,
    hasResult: true,
    hasMetrics,
    hasConsoleOutput,
    hasPlots: plotCount > 0,
    hasTrades: tradeCount > 0,
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

export function buildQuantMetricRows(
  metrics: readonly QuantRunResponse["metrics"][number][]
): QuantMetricRowViewModel[] {
  return metrics.map((metric, index) => {
    const label = metric.label.trim() || `Metric ${index + 1}`;
    const value = metric.value.trim() || "Not reported";
    return {
      id: buildQuantMetricRowId(label, index),
      label,
      value,
      ariaLabel: `${label}: ${value}`
    };
  });
}

export function buildQuantMetricRowId(label: string, index: number): string {
  const stable = label.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
  return `quant-metric-${stable || index.toString()}`;
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

function formatQuantTradeTimestamp(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value || "Unknown time";
  }

  return date.toISOString().replace(".000Z", "Z");
}

function formatQuantTradeSide(value: string): string {
  const normalized = value.trim();
  if (!normalized) return "Trade";
  return normalized.slice(0, 1).toUpperCase() + normalized.slice(1).toLowerCase();
}

function formatQuantMoney(value: number): string {
  return formatCurrencyAmount(value, { minimumFractionDigits: 2, fallback: "$0.00" });
}

function formatSignedQuantMoney(value: number): string {
  if (!Number.isFinite(value)) return "$0.00";
  const prefix = value > 0 ? "+" : value < 0 ? "-" : "";
  return `${prefix}${formatQuantMoney(Math.abs(value))}`;
}

function formatQuantQuantity(value: number): string {
  return formatNumberAmount(Math.abs(value), { maximumFractionDigits: 4, fallback: "0" });
}

function formatSignedQuantQuantity(value: number): string {
  if (!Number.isFinite(value)) return "0";
  const prefix = value > 0 ? "+" : value < 0 ? "-" : "";
  return `${prefix}${formatQuantQuantity(value)}`;
}
