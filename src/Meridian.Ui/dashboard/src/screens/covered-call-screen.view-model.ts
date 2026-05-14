import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as coveredCallApi from "@/lib/api/covered-call";
import type {
  CoveredCallBacktestRequest,
  CoveredCallChainRow,
  CoveredCallChainPreview,
  CoveredCallRunPhase,
  CoveredCallRunResult,
  CoveredCallRunStatus,
  CoveredCallRunSummary,
  CoveredCallScoringMode
} from "@/types/covered-call";

export type CoveredCallStage = "configure" | "run" | "results";

export interface CoveredCallFormState {
  underlyingSymbol: string;
  from: string;
  to: string;
  minStrike: string;
  overwriteRatio: string;
  maxDelta: string;
  minDte: string;
  maxDte: string;
  minIvPercentile: string;
  minOpenInterest: string;
  minVolume: string;
  maxSpreadPct: string;
  takeProfitCapture: string;
  rollDelta: string;
  exDivWindowDays: string;
  scoringMode: CoveredCallScoringMode;
  depthBonusWeight: string;
  riskFreeRate: string;
  initialCash: string;
  initialUnderlyingShares: string;
  label: string;
}

export type CoveredCallFormErrors = Partial<Record<keyof CoveredCallFormState, string>>;

export const COVERED_CALL_CHAIN_DETAIL_PANEL_ID = "covered-call-chain-candidate-detail";

type CoveredCallBadgeVariant = "outline" | "success" | "warning" | "danger" | "paper" | "research" | "default";

export interface CoveredCallChainPreviewState {
  status: "idle" | "loading" | "ready" | "error";
  data: CoveredCallChainPreview | null;
  error: string | null;
  selectedIndex: number;
}

export interface CoveredCallChainPreviewRowViewModel {
  id: string;
  index: number;
  strikeLabel: string;
  expirationLabel: string;
  daysToExpirationLabel: string;
  bidLabel: string;
  deltaLabel: string;
  openInterestLabel: string;
  statusLabel: string;
  statusBadgeVariant: CoveredCallBadgeVariant;
  statusAriaLabel: string;
  rowAriaLabel: string;
  rowSelectAriaLabel: string;
  detailPanelId: string;
  ariaExpanded: boolean;
}

export interface CoveredCallChainPreviewDetailViewModel {
  panelId: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusBadgeVariant: CoveredCallBadgeVariant;
  fields: Array<{ label: string; value: string }>;
  ariaLabel: string;
}

export interface CoveredCallChainPreviewPanelViewModel {
  description: string;
  tableLabel: string;
  tableCaption: string;
  emptyText: string;
  detailPanelId: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  rows: CoveredCallChainPreviewRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: CoveredCallChainPreviewDetailViewModel | null;
}

export interface CoveredCallHistoryRowViewModel {
  id: string;
  runId: string;
  isOpening: boolean;
  startedAtLabel: string;
  rangeLabel: string;
  underlyingLabel: string;
  statusLabel: string;
  statusBadgeVariant: CoveredCallBadgeVariant;
  cagrLabel: string;
  sharpeLabel: string;
  winRateLabel: string;
  labelText: string;
  rowAriaLabel: string;
  rowSelectAriaLabel: string;
}

export interface CoveredCallHistoryPanelViewModel {
  title: string;
  description: string;
  tableLabel: string;
  tableCaption: string;
  emptyText: string;
  isLoading: boolean;
  errorTitle: string | null;
  errorDescription: string | null;
  retryLabel: string;
  retryAriaLabel: string;
  retryDisabled: boolean;
  rows: CoveredCallHistoryRowViewModel[];
  selectedRowId: string | null;
}

export interface CoveredCallRunState {
  runId: string | null;
  status: CoveredCallRunStatus | null;
  result: CoveredCallRunResult | null;
  selectedPositionIndex: number;
}

export interface CoveredCallScreenServices {
  startRun: typeof coveredCallApi.startCoveredCallBacktest;
  getStatus: typeof coveredCallApi.getCoveredCallRunStatus;
  getResult: typeof coveredCallApi.getCoveredCallRunResult;
  cancelRun: typeof coveredCallApi.cancelCoveredCallRun;
  previewChain: typeof coveredCallApi.previewCoveredCallChain;
  listRuns: typeof coveredCallApi.listCoveredCallRuns;
}

export const DEFAULT_COVERED_CALL_FORM: CoveredCallFormState = {
  underlyingSymbol: "SPY",
  from: defaultIsoDate(-365),
  to: defaultIsoDate(0),
  minStrike: "",
  overwriteRatio: "0.75",
  maxDelta: "0.35",
  minDte: "7",
  maxDte: "60",
  minIvPercentile: "50",
  minOpenInterest: "1000",
  minVolume: "100",
  maxSpreadPct: "0.05",
  takeProfitCapture: "0.80",
  rollDelta: "0.55",
  exDivWindowDays: "7",
  scoringMode: "Relative",
  depthBonusWeight: "0.05",
  riskFreeRate: "0.04",
  initialCash: "100000",
  initialUnderlyingShares: "100",
  label: ""
};

/** Returns an ISO yyyy-MM-dd date `daysOffset` days from today (UTC). */
export function defaultIsoDate(daysOffset: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysOffset);
  return d.toISOString().slice(0, 10);
}

/** Pure validator — returns a map of field-level error messages. Empty object means valid. */
export function validateForm(form: CoveredCallFormState): CoveredCallFormErrors {
  const errors: CoveredCallFormErrors = {};

  if (!form.underlyingSymbol.trim()) {
    errors.underlyingSymbol = "Underlying symbol is required.";
  }
  if (!form.from) errors.from = "From date is required.";
  if (!form.to) errors.to = "To date is required.";
  if (form.from && form.to && form.from > form.to) {
    errors.to = "'To' must be on or after 'From'.";
  }

  const minStrike = Number(form.minStrike);
  if (!form.minStrike.trim() || Number.isNaN(minStrike) || minStrike <= 0) {
    errors.minStrike = "Minimum strike must be greater than zero.";
  }

  const ratio = Number(form.overwriteRatio);
  if (Number.isNaN(ratio) || ratio <= 0 || ratio > 1) {
    errors.overwriteRatio = "Overwrite ratio must be in (0, 1].";
  }

  const maxDelta = Number(form.maxDelta);
  if (Number.isNaN(maxDelta) || maxDelta < 0 || maxDelta > 1) {
    errors.maxDelta = "Max delta must be in [0, 1].";
  }

  if (Number(form.minDte) < 0 || Number.isNaN(Number(form.minDte))) {
    errors.minDte = "Min DTE must be non-negative.";
  }

  if (form.maxDte.trim() !== "" && (Number(form.maxDte) < 0 || Number.isNaN(Number(form.maxDte)))) {
    errors.maxDte = "Max DTE must be non-negative when supplied.";
  }

  const cash = Number(form.initialCash);
  if (Number.isNaN(cash) || cash <= 0) {
    errors.initialCash = "Initial cash must be greater than zero.";
  }

  const shares = Number(form.initialUnderlyingShares);
  if (Number.isNaN(shares) || shares < 0) {
    errors.initialUnderlyingShares = "Initial underlying shares cannot be negative.";
  }

  return errors;
}

/** Pure: maps form state to the API request shape. Caller must validate first. */
export function formToRequest(form: CoveredCallFormState): CoveredCallBacktestRequest {
  const maybeNum = (v: string): number | null => {
    if (!v.trim()) return null;
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  };

  return {
    underlyingSymbol: form.underlyingSymbol.trim().toUpperCase(),
    from: form.from,
    to: form.to,
    minStrike: Number(form.minStrike),
    overwriteRatio: Number(form.overwriteRatio),
    maxDelta: Number(form.maxDelta),
    minDte: Number(form.minDte),
    maxDte: maybeNum(form.maxDte),
    minIvPercentile: Number(form.minIvPercentile),
    minOpenInterest: Number(form.minOpenInterest),
    minVolume: Number(form.minVolume),
    maxSpreadPct: Number(form.maxSpreadPct),
    takeProfitCapture: Number(form.takeProfitCapture),
    rollDelta: Number(form.rollDelta),
    exDivWindowDays: Number(form.exDivWindowDays),
    scoringMode: form.scoringMode,
    depthBonusWeight: Number(form.depthBonusWeight),
    riskFreeRate: Number(form.riskFreeRate),
    initialCash: Number(form.initialCash),
    initialUnderlyingShares: Number(form.initialUnderlyingShares),
    label: form.label.trim() ? form.label.trim() : null
  };
}

export function isTerminalPhase(phase: CoveredCallRunPhase): boolean {
  return phase === "Completed" || phase === "Failed" || phase === "Cancelled";
}

export function buildChainPreviewPanelViewModel(
  chainPreview: CoveredCallChainPreviewState
): CoveredCallChainPreviewPanelViewModel {
  const base = {
    tableLabel: "Covered-call chain preview candidates",
    tableCaption: "Covered-call option-chain candidates with filter status.",
    detailPanelId: COVERED_CALL_CHAIN_DETAIL_PANEL_ID
  };

  if (chainPreview.status === "loading") {
    return {
      ...base,
      description: "Loading chain preview...",
      emptyText: "Loading chain preview...",
      detailEmptyTitle: "Chain preview loading",
      detailEmptyText: "Candidate detail will appear after the option-chain preview finishes.",
      detailEmptyAriaLabel: "Covered-call candidate detail loading",
      rows: [],
      selectedRowId: null,
      selectedDetail: null
    };
  }

  if (chainPreview.status === "error") {
    const errorText = chainPreview.error ?? "Unknown error";
    return {
      ...base,
      description: `Error: ${errorText}`,
      emptyText: `Chain preview failed: ${errorText}`,
      detailEmptyTitle: "Chain preview failed",
      detailEmptyText: errorText,
      detailEmptyAriaLabel: "Covered-call candidate detail unavailable",
      rows: [],
      selectedRowId: null,
      selectedDetail: null
    };
  }

  const data = chainPreview.data;
  if (!data || data.candidates.length === 0) {
    const readyEmpty = chainPreview.status === "ready";
    return {
      ...base,
      description: readyEmpty
        ? "No option candidates matched the current filters."
        : "Set an underlying and a positive min strike to preview the chain.",
      emptyText: readyEmpty ? "No candidates match the current filters." : "No candidates yet.",
      detailEmptyTitle: readyEmpty ? "No candidate selected" : "Candidate detail",
      detailEmptyText: readyEmpty
        ? "Adjust strike, delta, DTE, liquidity, or spread filters to find covered-call candidates."
        : "Set an underlying and a positive minimum strike to inspect candidate detail.",
      detailEmptyAriaLabel: "Covered-call candidate detail empty",
      rows: [],
      selectedRowId: null,
      selectedDetail: null
    };
  }

  const selectedIndex = clampIndex(chainPreview.selectedIndex, data.candidates.length);
  const rows = data.candidates.map((row, index) => buildChainPreviewRow(data, row, index, selectedIndex));
  const selectedRow = data.candidates[selectedIndex];

  return {
    ...base,
    description: `${formatCount(data.filtersPassed)} of ${formatCount(data.totalContractsScanned)} candidates pass filters.`,
    emptyText: "No candidates match the current filters.",
    detailEmptyTitle: "No candidate selected",
    detailEmptyText: "Select a candidate row to inspect strike, liquidity, and filter evidence.",
    detailEmptyAriaLabel: "Covered-call candidate detail empty",
    rows,
    selectedRowId: rows[selectedIndex]?.id ?? null,
    selectedDetail: selectedRow ? buildChainPreviewDetail(data, selectedRow) : null
  };
}

export function buildHistoryPanelViewModel({
  history,
  historyError,
  historyLoading,
  selectedRunId,
  openingRunId
}: {
  history: CoveredCallRunSummary[];
  historyError: string | null;
  historyLoading?: boolean;
  selectedRunId: string | null;
  openingRunId?: string | null;
}): CoveredCallHistoryPanelViewModel {
  const rows = history.map((run) => buildHistoryRow(run, openingRunId === run.runId));
  return {
    title: "Previous runs",
    description: historyLoading
      ? "Loading saved covered-call evidence..."
      : historyError
      ? "Run history is unavailable. Retry to reload saved covered-call evidence."
      : openingRunId
        ? "Opening saved covered-call evidence. Late results from earlier selections are ignored."
      : "Most recent first. Select a row to reload cached results and payoff evidence.",
    tableLabel: "Previous covered-call backtest runs",
    tableCaption: "Covered-call backtest run history with UTC start time, symbol, status, and outcome metrics.",
    emptyText: historyLoading
      ? "Loading run history..."
      : historyError
        ? "Run history failed to load."
        : "No previous covered-call runs are available.",
    isLoading: Boolean(historyLoading),
    errorTitle: historyError && !historyLoading ? "Run history failed to load" : null,
    errorDescription: historyLoading ? null : historyError,
    retryLabel: historyLoading ? "Loading history..." : "Retry history",
    retryAriaLabel: historyLoading ? "Loading covered-call run history" : "Retry covered-call run history",
    retryDisabled: Boolean(historyLoading),
    rows,
    selectedRowId: (openingRunId ?? selectedRunId)
      ? rows.find((row) => row.runId === (openingRunId ?? selectedRunId))?.id ?? null
      : null
  };
}

function buildHistoryRow(run: CoveredCallRunSummary, isOpening: boolean): CoveredCallHistoryRowViewModel {
  const startedAtLabel = formatUtcMinute(run.startedAt);
  const statusLabel = isOpening ? "Opening..." : run.status || "Unknown";
  const labelText = run.label?.trim() || "Unlabeled run";
  const rangeLabel = `${run.from} to ${run.to}`;
  const cagrLabel = run.cagr !== null ? formatPct(run.cagr) : "—";
  const sharpeLabel = run.sharpeRatio !== null && Number.isFinite(run.sharpeRatio) ? run.sharpeRatio.toFixed(2) : "—";
  const winRateLabel = run.winRate !== null ? formatPct(run.winRate) : "—";
  return {
    id: `covered-call-history-${sanitizeDomId(run.runId)}`,
    runId: run.runId,
    isOpening,
    startedAtLabel,
    rangeLabel,
    underlyingLabel: run.underlyingSymbol,
    statusLabel,
    statusBadgeVariant: isOpening ? "warning" : historyStatusBadgeVariant(statusLabel),
    cagrLabel,
    sharpeLabel,
    winRateLabel,
    labelText,
    rowAriaLabel: isOpening
      ? `Opening covered-call run ${run.runId}. ${run.underlyingSymbol}. Started ${startedAtLabel}.`
      : `Covered-call run ${run.runId}. ${run.underlyingSymbol}. ${statusLabel}. Started ${startedAtLabel}.`,
    rowSelectAriaLabel: isOpening
      ? `Opening covered-call run ${run.runId}. ${run.underlyingSymbol}. Started ${startedAtLabel}.`
      : `Open covered-call run ${run.runId}. ${run.underlyingSymbol}. ${statusLabel}. Started ${startedAtLabel}.`
  };
}

function historyStatusBadgeVariant(status: string): CoveredCallBadgeVariant {
  switch (status.trim().toLowerCase()) {
    case "completed":
    case "complete":
    case "succeeded":
    case "success":
      return "success";
    case "failed":
    case "error":
      return "danger";
    case "cancelled":
    case "canceled":
      return "outline";
    case "queued":
    case "running":
    case "warmingup":
    case "warming up":
      return "warning";
    default:
      return "default";
  }
}

function buildChainPreviewRow(
  preview: CoveredCallChainPreview,
  row: CoveredCallChainRow,
  index: number,
  selectedIndex: number
): CoveredCallChainPreviewRowViewModel {
  const strikeLabel = formatPrice(row.strike);
  const bidLabel = formatPrice(row.bid);
  const deltaLabel = formatDecimal(row.delta, 2);
  const statusLabel = row.meetsAllFilters ? "Pass" : row.rejectReason ?? "Reject";
  const statusBadgeVariant: CoveredCallBadgeVariant = row.meetsAllFilters ? "success" : "outline";
  const rowBase = `${preview.underlyingSymbol} ${strikeLabel} call expiring ${row.expiration}`;

  return {
    id: `covered-call-chain-row-${index}-${row.expiration}-${strikeLabel.replace(".", "-")}`,
    index,
    strikeLabel,
    expirationLabel: row.expiration,
    daysToExpirationLabel: formatCount(row.daysToExpiration),
    bidLabel,
    deltaLabel,
    openInterestLabel: formatCount(row.openInterest),
    statusLabel,
    statusBadgeVariant,
    statusAriaLabel: row.meetsAllFilters
      ? "Candidate passes all configured filters"
      : `Candidate rejected: ${statusLabel}`,
    rowAriaLabel: `${rowBase}. Bid ${bidLabel}. Delta ${deltaLabel}. Status ${statusLabel}.`,
    rowSelectAriaLabel: `Inspect ${rowBase}. Status ${statusLabel}.`,
    detailPanelId: COVERED_CALL_CHAIN_DETAIL_PANEL_ID,
    ariaExpanded: index === selectedIndex
  };
}

function buildChainPreviewDetail(
  preview: CoveredCallChainPreview,
  row: CoveredCallChainRow
): CoveredCallChainPreviewDetailViewModel {
  const strikeLabel = formatPrice(row.strike);
  const statusLabel = row.meetsAllFilters ? "Pass" : row.rejectReason ?? "Reject";
  const statusBadgeVariant: CoveredCallBadgeVariant = row.meetsAllFilters ? "success" : "outline";

  return {
    panelId: COVERED_CALL_CHAIN_DETAIL_PANEL_ID,
    eyebrow: "Selected candidate",
    title: `${preview.underlyingSymbol} ${strikeLabel} call`,
    subtitle: `${row.expiration} · ${formatCount(row.daysToExpiration)} DTE · bid ${formatPrice(row.bid)} / ask ${formatPrice(row.ask)}`,
    description: row.meetsAllFilters
      ? "This contract currently passes the configured strike, delta, DTE, liquidity, and spread filters."
      : `This contract is excluded by the current filter set: ${statusLabel}.`,
    statusLabel,
    statusBadgeVariant,
    fields: [
      { label: "Underlying", value: `${preview.underlyingSymbol} @ ${formatPrice(preview.underlyingPrice)}` },
      { label: "Strike", value: strikeLabel },
      { label: "Expiration", value: row.expiration },
      { label: "DTE", value: formatCount(row.daysToExpiration) },
      { label: "Bid / Ask", value: `${formatPrice(row.bid)} / ${formatPrice(row.ask)}` },
      { label: "Delta", value: formatDecimal(row.delta, 2) },
      { label: "Implied volatility", value: row.impliedVolatility === null ? "—" : formatPercent(row.impliedVolatility) },
      { label: "Open interest", value: formatCount(row.openInterest) },
      { label: "Volume", value: formatCount(row.volume) }
    ],
    ariaLabel: `Selected covered-call candidate: ${preview.underlyingSymbol} ${strikeLabel} call expiring ${row.expiration}`
  };
}

function clampIndex(index: number, length: number): number {
  if (length <= 0) return -1;
  if (!Number.isFinite(index)) return 0;
  return Math.min(Math.max(Math.trunc(index), 0), length - 1);
}

function formatPrice(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return value.toFixed(2);
}

function formatDecimal(value: number, digits: number): string {
  if (!Number.isFinite(value)) return "—";
  return value.toFixed(digits);
}

function formatPercent(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return `${(value * 100).toFixed(1)}%`;
}

function formatPct(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return "—";
  return `${(value * 100).toFixed(2)}%`;
}

function formatCount(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return Math.trunc(value).toLocaleString("en-US");
}

export function formatUtcMinute(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "Invalid timestamp";
  }

  const month = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"][
    date.getUTCMonth()
  ];
  const day = String(date.getUTCDate()).padStart(2, "0");
  const year = date.getUTCFullYear();
  const hour = String(date.getUTCHours()).padStart(2, "0");
  const minute = String(date.getUTCMinutes()).padStart(2, "0");
  return `${month} ${day}, ${year} ${hour}:${minute} UTC`;
}

function sanitizeDomId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "run";
}

export interface CoveredCallScreenState {
  stage: CoveredCallStage;
  form: CoveredCallFormState;
  formErrors: CoveredCallFormErrors;
  chainPreview: CoveredCallChainPreviewState;
  chainPreviewPanel: CoveredCallChainPreviewPanelViewModel;
  run: CoveredCallRunState;
  history: CoveredCallRunSummary[];
  historyError: string | null;
  historyLoading: boolean;
  historyOpeningRunId: string | null;
  historyPanel: CoveredCallHistoryPanelViewModel;
  errorBanner: string | null;
}

export interface CoveredCallScreenViewModel extends CoveredCallScreenState {
  setField: <K extends keyof CoveredCallFormState>(key: K, value: CoveredCallFormState[K]) => void;
  resetForm: () => void;
  refreshChainPreview: () => Promise<void>;
  selectChainRow: (index: number) => void;
  startRun: () => Promise<void>;
  cancelRun: () => Promise<void>;
  loadHistory: () => Promise<void>;
  openRun: (runId: string) => Promise<void>;
  selectOpenPosition: (index: number) => void;
  goToStage: (stage: CoveredCallStage) => void;
  dismissError: () => void;
}

export interface UseCoveredCallScreenOptions {
  services?: Partial<CoveredCallScreenServices>;
  pollIntervalMs?: number;
  chainPreviewDebounceMs?: number;
}

const DEFAULT_SERVICES: CoveredCallScreenServices = {
  startRun: coveredCallApi.startCoveredCallBacktest,
  getStatus: coveredCallApi.getCoveredCallRunStatus,
  getResult: coveredCallApi.getCoveredCallRunResult,
  cancelRun: coveredCallApi.cancelCoveredCallRun,
  previewChain: coveredCallApi.previewCoveredCallChain,
  listRuns: coveredCallApi.listCoveredCallRuns
};

export function useCoveredCallScreenViewModel(
  options: UseCoveredCallScreenOptions = {}
): CoveredCallScreenViewModel {
  const optionsServices = options.services;
  // Stabilise the services ref across renders so dependent callbacks aren't invalidated and
  // the chain-preview debounce isn't reset on every parent render.
  const services: CoveredCallScreenServices = useMemo(
    () => ({ ...DEFAULT_SERVICES, ...optionsServices }),
    // We intentionally take optionsServices as-is; consumers passing a fresh literal each render
    // accept that they bust the memo on purpose.
    [optionsServices]
  );
  const pollIntervalMs = options.pollIntervalMs ?? 1500;
  const chainDebounceMs = options.chainPreviewDebounceMs ?? 300;

  const [stage, setStage] = useState<CoveredCallStage>("configure");
  const [form, setForm] = useState<CoveredCallFormState>(DEFAULT_COVERED_CALL_FORM);
  const [formErrors, setFormErrors] = useState<CoveredCallFormErrors>({});
  const [chainPreview, setChainPreview] = useState<CoveredCallChainPreviewState>({
    status: "idle",
    data: null,
    error: null,
    selectedIndex: 0
  });
  const [run, setRun] = useState<CoveredCallRunState>({
    runId: null,
    status: null,
    result: null,
    selectedPositionIndex: 0
  });
  const [history, setHistory] = useState<CoveredCallRunSummary[]>([]);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyOpeningRunId, setHistoryOpeningRunId] = useState<string | null>(null);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);

  const setField = useCallback(<K extends keyof CoveredCallFormState>(key: K, value: CoveredCallFormState[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setFormErrors((prev) => {
      if (!(key in prev)) return prev;
      const next = { ...prev };
      delete next[key];
      return next;
    });
  }, []);

  const resetForm = useCallback(() => {
    setForm(DEFAULT_COVERED_CALL_FORM);
    setFormErrors({});
  }, []);

  const dismissError = useCallback(() => setErrorBanner(null), []);
  const goToStage = useCallback((next: CoveredCallStage) => setStage(next), []);

  // ---- Chain preview (debounced) -----------------------------------------
  const chainAbortRef = useRef<AbortController | null>(null);
  const chainDebounceRef = useRef<number | null>(null);

  const refreshChainPreview = useCallback(async () => {
    chainAbortRef.current?.abort();
    const controller = new AbortController();
    chainAbortRef.current = controller;
    setChainPreview((prev) => ({ ...prev, status: "loading", error: null }));

    try {
      const minStrike = Number(form.minStrike);
      if (!form.underlyingSymbol.trim() || !(minStrike > 0)) {
        setChainPreview({
          status: "idle",
          data: null,
          error: null,
          selectedIndex: 0
        });
        return;
      }
      const data = await services.previewChain({
        underlyingSymbol: form.underlyingSymbol.trim().toUpperCase(),
        asOf: form.from || defaultIsoDate(0),
        minStrike,
        maxDelta: Number(form.maxDelta),
        minDte: Number(form.minDte),
        maxDte: form.maxDte.trim() ? Number(form.maxDte) : null,
        minOpenInterest: Number(form.minOpenInterest),
        minVolume: Number(form.minVolume),
        maxSpreadPct: Number(form.maxSpreadPct)
      }, controller.signal);

      setChainPreview({ status: "ready", data, error: null, selectedIndex: 0 });
    } catch (error) {
      if ((error as Error).name === "AbortError") return;
      setChainPreview({
        status: "error",
        data: null,
        error: (error as Error).message,
        selectedIndex: 0
      });
    }
  }, [
    form.underlyingSymbol,
    form.from,
    form.minStrike,
    form.maxDelta,
    form.minDte,
    form.maxDte,
    form.minOpenInterest,
    form.minVolume,
    form.maxSpreadPct,
    services
  ]);

  // Debounce auto-refresh when form fields that affect the preview change.
  useEffect(() => {
    if (stage !== "configure") return;
    if (chainDebounceRef.current !== null) {
      clearTimeout(chainDebounceRef.current);
    }
    chainDebounceRef.current = window.setTimeout(() => {
      void refreshChainPreview();
    }, chainDebounceMs);
    return () => {
      if (chainDebounceRef.current !== null) {
        clearTimeout(chainDebounceRef.current);
        chainDebounceRef.current = null;
      }
    };
  }, [stage, chainDebounceMs, refreshChainPreview]);

  const selectChainRow = useCallback((index: number) => {
    setChainPreview((prev) => ({ ...prev, selectedIndex: index }));
  }, []);

  const selectOpenPosition = useCallback((index: number) => {
    setRun((prev) => ({ ...prev, selectedPositionIndex: index }));
  }, []);

  // ---- Run lifecycle -----------------------------------------------------
  const pollAbortRef = useRef<AbortController | null>(null);
  const pollTimerRef = useRef<number | null>(null);
  const activeRunIdRef = useRef<string | null>(null);
  const historyLoadRevisionRef = useRef(0);
  const historyOpenRevisionRef = useRef(0);

  const stopPolling = useCallback(() => {
    if (pollTimerRef.current !== null) {
      clearTimeout(pollTimerRef.current);
      pollTimerRef.current = null;
    }
    pollAbortRef.current?.abort();
    pollAbortRef.current = null;
  }, []);

  const pollOnce = useCallback(async (runId: string) => {
    if (activeRunIdRef.current !== runId) return;
    const controller = new AbortController();
    pollAbortRef.current = controller;
    try {
      const status = await services.getStatus(runId, controller.signal);
      if (activeRunIdRef.current !== runId) return;
      setRun((prev) => ({ ...prev, status }));

      if (isTerminalPhase(status.phase)) {
        stopPolling();
        if (status.phase === "Completed") {
          try {
            const result = await services.getResult(runId);
            if (activeRunIdRef.current !== runId) return;
            setRun((prev) => ({ ...prev, result, selectedPositionIndex: 0 }));
            setStage("results");
          } catch (resultErr) {
            setErrorBanner(`Result fetch failed: ${(resultErr as Error).message}`);
          }
        } else if (status.phase === "Failed" && status.failureMessage) {
          setErrorBanner(status.failureMessage);
        }
      } else {
        pollTimerRef.current = window.setTimeout(() => {
          void pollOnce(runId);
        }, pollIntervalMs);
      }
    } catch (error) {
      if ((error as Error).name === "AbortError") return;
      setErrorBanner(`Status poll failed: ${(error as Error).message}`);
      stopPolling();
    }
  }, [pollIntervalMs, services, stopPolling]);

  const startRun = useCallback(async () => {
    const errors = validateForm(form);
    setFormErrors(errors);
    if (Object.keys(errors).length > 0) {
      setErrorBanner("Fix the highlighted form fields before running.");
      return;
    }

    // Stop any in-flight polling and invalidate the previous active run id so a stale poll that
    // resolves during the startRun await can't push status/result into the new run's state.
    stopPolling();
    activeRunIdRef.current = null;
    historyOpenRevisionRef.current += 1;
    setHistoryOpeningRunId(null);

    setErrorBanner(null);
    setRun({ runId: null, status: null, result: null, selectedPositionIndex: 0 });
    setStage("run");

    try {
      const handle = await services.startRun(formToRequest(form));
      activeRunIdRef.current = handle.runId;
      setRun({
        runId: handle.runId,
        status: { runId: handle.runId, phase: "Queued", percentComplete: 0, currentBacktestDate: null, failureMessage: null },
        result: null,
        selectedPositionIndex: 0
      });
      pollTimerRef.current = window.setTimeout(() => {
        void pollOnce(handle.runId);
      }, pollIntervalMs);
    } catch (error) {
      setErrorBanner((error as Error).message);
      setStage("configure");
    }
  }, [form, pollIntervalMs, pollOnce, services, stopPolling]);

  const cancelRun = useCallback(async () => {
    const runId = run.runId;
    if (!runId) return;
    try {
      const status = await services.cancelRun(runId);
      setRun((prev) => ({ ...prev, status }));
    } catch (error) {
      setErrorBanner(`Cancel failed: ${(error as Error).message}`);
    }
  }, [run.runId, services]);

  const loadHistory = useCallback(async () => {
    const revision = historyLoadRevisionRef.current + 1;
    historyLoadRevisionRef.current = revision;
    setHistoryLoading(true);
    setHistoryError(null);
    try {
      const items = await services.listRuns(50);
      if (historyLoadRevisionRef.current !== revision) {
        return;
      }
      setHistory(items);
    } catch (error) {
      if (historyLoadRevisionRef.current !== revision) {
        return;
      }
      setHistoryError((error as Error).message);
    } finally {
      if (historyLoadRevisionRef.current === revision) {
        setHistoryLoading(false);
      }
    }
  }, [services]);

  const openRun = useCallback(async (runId: string) => {
    const revision = historyOpenRevisionRef.current + 1;
    historyOpenRevisionRef.current = revision;
    setHistoryOpeningRunId(runId);
    setErrorBanner(null);
    try {
      const result = await services.getResult(runId);
      if (historyOpenRevisionRef.current !== revision) {
        return;
      }
      activeRunIdRef.current = runId;
      stopPolling();
      setRun({
        runId,
        status: {
          runId,
          phase: "Completed",
          percentComplete: 1,
          currentBacktestDate: null,
          failureMessage: null
        },
        result,
        selectedPositionIndex: 0
      });
      setStage("results");
    } catch (error) {
      if (historyOpenRevisionRef.current !== revision) {
        return;
      }
      setErrorBanner(`Could not load run ${runId}: ${(error as Error).message}`);
    } finally {
      if (historyOpenRevisionRef.current === revision) {
        setHistoryOpeningRunId(null);
      }
    }
  }, [services, stopPolling]);

  // Stop polling and abort in-flight requests on unmount.
  useEffect(() => () => {
    stopPolling();
    chainAbortRef.current?.abort();
    if (chainDebounceRef.current !== null) clearTimeout(chainDebounceRef.current);
  }, [stopPolling]);

  return useMemo(() => ({
    stage,
    form,
    formErrors,
    chainPreview,
    chainPreviewPanel: buildChainPreviewPanelViewModel(chainPreview),
    run,
    history,
    historyError,
    historyLoading,
    historyOpeningRunId,
    historyPanel: buildHistoryPanelViewModel({
      history,
      historyError,
      historyLoading,
      selectedRunId: run.runId,
      openingRunId: historyOpeningRunId
    }),
    errorBanner,
    setField,
    resetForm,
    refreshChainPreview,
    selectChainRow,
    startRun,
    cancelRun,
    loadHistory,
    openRun,
    selectOpenPosition,
    goToStage,
    dismissError
  }), [
    stage,
    form,
    formErrors,
    chainPreview,
    run,
    history,
    historyError,
    historyLoading,
    historyOpeningRunId,
    errorBanner,
    setField,
    resetForm,
    refreshChainPreview,
    selectChainRow,
    startRun,
    cancelRun,
    loadHistory,
    openRun,
    selectOpenPosition,
    goToStage,
    dismissError
  ]);
}
