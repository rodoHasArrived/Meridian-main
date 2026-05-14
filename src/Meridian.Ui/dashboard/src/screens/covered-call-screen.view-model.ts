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

export interface CoveredCallRunState {
  runId: string | null;
  status: CoveredCallRunStatus | null;
  result: CoveredCallRunResult | null;
  selectedPositionIndex: number;
  isStarting: boolean;
  isCancelling: boolean;
}

export interface CoveredCallActionCommandState {
  label: string;
  ariaLabel: string;
  feedbackId: string;
  feedbackText: string | null;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
  busyLabel: string;
}

export interface CoveredCallRunProgressPanelViewModel {
  title: string;
  description: string;
  percentComplete: number;
  ariaValueText: string;
  ariaBusy: true | undefined;
}

export interface CoveredCallStageNavigationItemState {
  disabled: boolean;
  disabledReason: string | null;
}

export type CoveredCallStageNavigationState = Record<CoveredCallStage, CoveredCallStageNavigationItemState> & {
  feedbackId: string;
  feedbackText: string | null;
};

export interface CoveredCallResultsActionViewModel {
  id: string;
  label: string;
  description: string;
  href: string;
  ariaLabel: string;
}

export interface CoveredCallResultsActionPanelViewModel {
  title: string;
  description: string;
  actions: CoveredCallResultsActionViewModel[];
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

const FORM_ERROR_PRIORITY: Array<keyof CoveredCallFormState> = [
  "underlyingSymbol",
  "from",
  "to",
  "minStrike",
  "overwriteRatio",
  "maxDelta",
  "minDte",
  "maxDte",
  "initialCash",
  "initialUnderlyingShares"
];

function firstFormError(errors: CoveredCallFormErrors): string | null {
  for (const field of FORM_ERROR_PRIORITY) {
    if (errors[field]) return errors[field] ?? null;
  }
  return Object.values(errors)[0] ?? null;
}

export function buildCoveredCallRunCommandState(
  form: CoveredCallFormState,
  isStarting: boolean
): CoveredCallActionCommandState {
  const feedbackId = "covered-call-run-command-feedback";
  if (isStarting) {
    return {
      label: "Submitting...",
      ariaLabel: "Submitting covered-call backtest",
      feedbackId,
      feedbackText: "Submitting covered-call run request.",
      disabled: false,
      disabledReason: null,
      busy: true,
      busyLabel: "Submitting..."
    };
  }

  const disabledReason = firstFormError(validateForm(form));
  return {
    label: "Run backtest",
    ariaLabel: "Run covered-call backtest",
    feedbackId,
    feedbackText: disabledReason ? `Cannot run yet: ${disabledReason}` : null,
    disabled: Boolean(disabledReason),
    disabledReason,
    busy: false,
    busyLabel: "Submitting..."
  };
}

export function buildCoveredCallCancelCommandState(run: CoveredCallRunState): CoveredCallActionCommandState {
  const feedbackId = "covered-call-cancel-command-feedback";
  if (run.isCancelling) {
    return {
      label: "Cancelling run",
      ariaLabel: "Cancelling covered-call backtest run",
      feedbackId,
      feedbackText: "Cancelling covered-call backtest run.",
      disabled: false,
      disabledReason: null,
      busy: true,
      busyLabel: "Cancelling..."
    };
  }

  if (!run.runId) {
    return {
      label: "Cancel run",
      ariaLabel: "Cancel covered-call backtest run",
      feedbackId,
      feedbackText: "Run ID is not available until the engine accepts the request.",
      disabled: true,
      disabledReason: "Run ID is not available until the engine accepts the request.",
      busy: false,
      busyLabel: "Cancelling..."
    };
  }

  if (run.status && isTerminalPhase(run.status.phase)) {
    const disabledReason = `Run is already ${run.status.phase.toLowerCase()}.`;
    return {
      label: "Cancel run",
      ariaLabel: "Cancel covered-call backtest run",
      feedbackId,
      feedbackText: disabledReason,
      disabled: true,
      disabledReason,
      busy: false,
      busyLabel: "Cancelling..."
    };
  }

  return {
    label: "Cancel run",
    ariaLabel: "Cancel covered-call backtest run",
    feedbackId,
    feedbackText: null,
    disabled: false,
    disabledReason: null,
    busy: false,
    busyLabel: "Cancelling..."
  };
}

export function buildCoveredCallRunProgressPanel(run: CoveredCallRunState): CoveredCallRunProgressPanelViewModel {
  if (run.isStarting) {
    return {
      title: "Submitting backtest",
      description: "Submitting covered-call run request to the strategy engine.",
      percentComplete: 0,
      ariaValueText: "Submitting covered-call run request.",
      ariaBusy: true
    };
  }

  const status = run.status;
  if (!status) {
    return {
      title: "Running backtest",
      description: "Queued - waiting for the engine.",
      percentComplete: 0,
      ariaValueText: "Queued and waiting for the engine.",
      ariaBusy: true
    };
  }

  const percentComplete = Math.round(status.percentComplete * 100);
  const currentDate = status.currentBacktestDate ? ` - ${status.currentBacktestDate}` : "";
  const terminal = isTerminalPhase(status.phase);

  return {
    title: terminal ? "Backtest run finished" : "Running backtest",
    description: `Phase: ${status.phase}${currentDate}`,
    percentComplete,
    ariaValueText: `${status.phase} ${percentComplete}% complete.`,
    ariaBusy: terminal ? undefined : true
  };
}

export function buildCoveredCallStageNavigationState(run: CoveredCallRunState): CoveredCallStageNavigationState {
  const disabledReason = run.isStarting
    ? "Wait until the strategy engine accepts the backtest request before leaving run progress."
    : run.isCancelling
      ? "Wait until cancellation completes before leaving run progress."
      : null;

  return {
    feedbackId: "covered-call-stage-navigation-feedback",
    feedbackText: disabledReason,
    configure: {
      disabled: disabledReason !== null,
      disabledReason
    },
    run: {
      disabled: false,
      disabledReason: null
    },
    results: {
      disabled: disabledReason !== null,
      disabledReason
    }
  };
}

export function buildCoveredCallResultsActionPanel(
  result: CoveredCallRunResult | null
): CoveredCallResultsActionPanelViewModel {
  const symbol = result?.underlyingSymbol?.trim().toUpperCase() || "the underlying";
  const quoteSymbol = encodeURIComponent(symbol);

  return {
    title: "Next workflow",
    description: result
      ? `Use the ${symbol} backtest evidence while the context is fresh.`
      : "Complete or load a covered-call run before moving evidence into the next workflow.",
    actions: result
      ? [
          {
            id: "live-quote",
            label: "Validate live quote",
            description: `Open ${symbol} quote, order book, trades, and chart evidence.`,
            href: `/data/quotes?symbol=${quoteSymbol}`,
            ariaLabel: `Validate live quote evidence for ${symbol}`
          },
          {
            id: "strategy-designer",
            label: "Refine payoff",
            description: "Compare the covered-call shape against editable option-leg structures.",
            href: "/strategy/designer",
            ariaLabel: "Open Strategy Designer to refine covered-call payoff"
          },
          {
            id: "report-pack",
            label: "Package evidence",
            description: "Move selected run evidence toward report-pack preview or export review.",
            href: "/reporting/report-packs",
            ariaLabel: "Open report packs to package covered-call run evidence"
          }
        ]
      : []
  };
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

function formatCount(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return Math.trunc(value).toLocaleString("en-US");
}

export interface CoveredCallScreenState {
  stage: CoveredCallStage;
  form: CoveredCallFormState;
  formErrors: CoveredCallFormErrors;
  chainPreview: CoveredCallChainPreviewState;
  chainPreviewPanel: CoveredCallChainPreviewPanelViewModel;
  run: CoveredCallRunState;
  runCommand: CoveredCallActionCommandState;
  cancelRunCommand: CoveredCallActionCommandState;
  runProgressPanel: CoveredCallRunProgressPanelViewModel;
  stageNavigation: CoveredCallStageNavigationState;
  resultsActionPanel: CoveredCallResultsActionPanelViewModel;
  history: CoveredCallRunSummary[];
  historyError: string | null;
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
    selectedPositionIndex: 0,
    isStarting: false,
    isCancelling: false
  });
  const [history, setHistory] = useState<CoveredCallRunSummary[]>([]);
  const [historyError, setHistoryError] = useState<string | null>(null);
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
  const goToStage = useCallback((next: CoveredCallStage) => {
    setStage((current) => {
      const navigation = buildCoveredCallStageNavigationState(run);
      if (navigation[next].disabled) return current;
      return next;
    });
  }, [run]);

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
      setRun((prev) => ({ ...prev, status, isStarting: false }));

      if (isTerminalPhase(status.phase)) {
        stopPolling();
        if (status.phase === "Completed") {
          try {
            const result = await services.getResult(runId);
            if (activeRunIdRef.current !== runId) return;
            setRun((prev) => ({ ...prev, result, selectedPositionIndex: 0, isStarting: false, isCancelling: false }));
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
    if (run.isStarting) return;

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

    setErrorBanner(null);
    setRun({ runId: null, status: null, result: null, selectedPositionIndex: 0, isStarting: true, isCancelling: false });
    setStage("run");

    try {
      const handle = await services.startRun(formToRequest(form));
      activeRunIdRef.current = handle.runId;
      setRun({
        runId: handle.runId,
        status: { runId: handle.runId, phase: "Queued", percentComplete: 0, currentBacktestDate: null, failureMessage: null },
        result: null,
        selectedPositionIndex: 0,
        isStarting: false,
        isCancelling: false
      });
      pollTimerRef.current = window.setTimeout(() => {
        void pollOnce(handle.runId);
      }, pollIntervalMs);
    } catch (error) {
      setErrorBanner((error as Error).message);
      setRun({ runId: null, status: null, result: null, selectedPositionIndex: 0, isStarting: false, isCancelling: false });
      setStage("configure");
    }
  }, [form, pollIntervalMs, pollOnce, run.isStarting, services, stopPolling]);

  const cancelRun = useCallback(async () => {
    const runId = run.runId;
    if (!runId || run.isCancelling) return;
    setRun((prev) => ({ ...prev, isCancelling: true }));
    try {
      const status = await services.cancelRun(runId);
      setRun((prev) => ({ ...prev, status, isCancelling: false }));
    } catch (error) {
      setErrorBanner(`Cancel failed: ${(error as Error).message}`);
      setRun((prev) => ({ ...prev, isCancelling: false }));
    }
  }, [run.isCancelling, run.runId, services]);

  const loadHistory = useCallback(async () => {
    setHistoryError(null);
    try {
      const items = await services.listRuns(50);
      setHistory(items);
    } catch (error) {
      setHistoryError((error as Error).message);
    }
  }, [services]);

  const openRun = useCallback(async (runId: string) => {
    setErrorBanner(null);
    try {
      const result = await services.getResult(runId);
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
        selectedPositionIndex: 0,
        isStarting: false,
        isCancelling: false
      });
      setStage("results");
    } catch (error) {
      setErrorBanner(`Could not load run ${runId}: ${(error as Error).message}`);
    }
  }, [services, stopPolling]);

  const runCommand = useMemo(
    () => buildCoveredCallRunCommandState(form, run.isStarting),
    [form, run.isStarting]
  );
  const cancelRunCommand = useMemo(
    () => buildCoveredCallCancelCommandState(run),
    [run]
  );
  const runProgressPanel = useMemo(
    () => buildCoveredCallRunProgressPanel(run),
    [run]
  );
  const stageNavigation = useMemo(
    () => buildCoveredCallStageNavigationState(run),
    [run]
  );
  const resultsActionPanel = useMemo(
    () => buildCoveredCallResultsActionPanel(run.result),
    [run.result]
  );

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
    runCommand,
    cancelRunCommand,
    runProgressPanel,
    stageNavigation,
    resultsActionPanel,
    history,
    historyError,
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
    runCommand,
    cancelRunCommand,
    runProgressPanel,
    stageNavigation,
    resultsActionPanel,
    history,
    historyError,
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
