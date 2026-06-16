import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as coveredCallApi from "@/lib/api/covered-call.api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import type {
  CoveredCallBacktestRequest,
  CoveredCallChainPreview,
  CoveredCallChainRow,
  CoveredCallOpenPosition,
  CoveredCallRunPhase,
  CoveredCallRunResult,
  CoveredCallRunStatus,
  CoveredCallRunSummary,
  CoveredCallScoringMode,
  CoveredCallTrade
} from "@/types/covered-call.types";
import { buildShortCallPayoffCurve, shortCallBreakEven } from "@/lib/covered-call/payoff";

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
export const COVERED_CALL_TRADE_DETAIL_PANEL_ID = "covered-call-trade-detail";

type CoveredCallBadgeVariant = "outline" | "success" | "warning" | "danger" | "paper" | "research" | "default";

export interface CoveredCallFormFieldOptionViewModel {
  value: string;
  label: string;
  description: string;
}

export interface CoveredCallFormFieldViewModel {
  key: keyof CoveredCallFormState;
  id: string;
  label: string;
  type: "text" | "number" | "date" | "select";
  step?: string;
  required: boolean;
  helperText: string;
  errorId: string;
  describedBy: string;
  error: string | null;
  invalid: boolean;
  options: CoveredCallFormFieldOptionViewModel[];
}

export type CoveredCallFormFieldMap = Record<keyof CoveredCallFormState, CoveredCallFormFieldViewModel>;

export interface CoveredCallFormFieldGroupViewModel {
  id: string;
  columns: 1 | 2;
  fields: CoveredCallFormFieldViewModel[];
}

export interface CoveredCallChainPreviewState {
  status: "idle" | "loading" | "ready" | "error";
  data: CoveredCallChainPreview | null;
  error: ApiErrorDisplay | null;
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
  errorDetails: string[];
  detailPanelId: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  rows: CoveredCallChainPreviewRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: CoveredCallChainPreviewDetailViewModel | null;
}

export interface CoveredCallHistoryRowViewModel {
  runId: string;
  startedAtLabel: string;
  underlyingSymbol: string;
  rangeLabel: string;
  statusLabel: string;
  statusBadgeVariant: CoveredCallBadgeVariant;
  cagrLabel: string;
  sharpeRatioLabel: string;
  labelText: string;
  rowAriaLabel: string;
  rowSelectAriaLabel: string;
}

export interface CoveredCallRunState {
  runId: string | null;
  status: CoveredCallRunStatus | null;
  result: CoveredCallRunResult | null;
  selectedPositionIndex: number;
  selectedTradeIndex: number;
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

export interface CoveredCallStageNavigationStepViewModel extends CoveredCallStageNavigationItemState {
  stage: CoveredCallStage;
  label: string;
  sequenceLabel: string;
  buttonLabel: string;
  ariaLabel: string;
  ariaCurrent: "step" | undefined;
  ariaDescribedBy: string | undefined;
  isCurrent: boolean;
}

export type CoveredCallStageNavigationState = Record<CoveredCallStage, CoveredCallStageNavigationItemState> & {
  feedbackId: string;
  feedbackText: string | null;
  steps: CoveredCallStageNavigationStepViewModel[];
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

export interface CoveredCallTradeTimelineRowViewModel {
  id: string;
  index: number;
  entryDateLabel: string;
  exitDateLabel: string;
  strikeLabel: string;
  pnlLabel: string;
  pnlClassName: "text-success" | "text-danger" | "text-muted-foreground";
  exitReasonLabel: string;
  statusLabel: string;
  statusBadgeVariant: CoveredCallBadgeVariant;
  rowAriaLabel: string;
  rowSelectAriaLabel: string;
  detailPanelId: string;
  ariaExpanded: boolean;
}

export interface CoveredCallTradeTimelineDetailViewModel {
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

export interface CoveredCallTradeTimelinePanelViewModel {
  title: string;
  tableLabel: string;
  tableCaption: string;
  emptyText: string;
  detailPanelId: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  rows: CoveredCallTradeTimelineRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: CoveredCallTradeTimelineDetailViewModel | null;
}

export interface CoveredCallPayoffPositionOptionViewModel {
  id: string;
  index: number;
  label: string;
  description: string;
  selected: boolean;
  buttonVariant: "secondary" | "outline";
  ariaLabel: string;
}

export interface CoveredCallPayoffChartLineViewModel {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

export interface CoveredCallPayoffChartViewModel {
  viewBox: string;
  ariaLabel: string;
  zeroLine: CoveredCallPayoffChartLineViewModel;
  strikeLine: CoveredCallPayoffChartLineViewModel;
  path: string;
}

export interface CoveredCallPayoffPanelViewModel {
  title: string;
  description: string;
  emptyText: string | null;
  selectorAriaLabel: string;
  positionOptions: CoveredCallPayoffPositionOptionViewModel[];
  chart: CoveredCallPayoffChartViewModel | null;
  note: string;
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

type CoveredCallFormFieldDefinition = Omit<CoveredCallFormFieldViewModel, "error" | "invalid" | "describedBy">;

const COVERED_CALL_FORM_FIELD_DEFINITIONS: CoveredCallFormFieldDefinition[] = [
  {
    key: "underlyingSymbol",
    id: "cc-underlyingSymbol",
    label: "Underlying",
    type: "text",
    required: true,
    helperText: "Ticker symbol used for historical bars, chain preview, and result handoffs.",
    errorId: "cc-underlyingSymbol-error",
    options: []
  },
  {
    key: "from",
    id: "cc-from",
    label: "From",
    type: "date",
    required: true,
    helperText: "First historical session included in the backtest window.",
    errorId: "cc-from-error",
    options: []
  },
  {
    key: "to",
    id: "cc-to",
    label: "To",
    type: "date",
    required: true,
    helperText: "Last historical session included in the backtest window.",
    errorId: "cc-to-error",
    options: []
  },
  {
    key: "minStrike",
    id: "cc-minStrike",
    label: "Min strike",
    type: "number",
    step: "0.01",
    required: true,
    helperText: "Lowest call strike the strategy may sell.",
    errorId: "cc-minStrike-error",
    options: []
  },
  {
    key: "overwriteRatio",
    id: "cc-overwriteRatio",
    label: "Overwrite ratio",
    type: "number",
    step: "0.01",
    required: false,
    helperText: "Target fraction of long shares covered by short calls; use 0.75 for 75%.",
    errorId: "cc-overwriteRatio-error",
    options: []
  },
  {
    key: "maxDelta",
    id: "cc-maxDelta",
    label: "Max delta",
    type: "number",
    step: "0.01",
    required: false,
    helperText: "Highest option delta allowed for selected calls.",
    errorId: "cc-maxDelta-error",
    options: []
  },
  {
    key: "minDte",
    id: "cc-minDte",
    label: "Min DTE",
    type: "number",
    required: false,
    helperText: "Minimum calendar days to expiration.",
    errorId: "cc-minDte-error",
    options: []
  },
  {
    key: "maxDte",
    id: "cc-maxDte",
    label: "Max DTE",
    type: "number",
    required: false,
    helperText: "Maximum calendar days to expiration; leave blank for no cap.",
    errorId: "cc-maxDte-error",
    options: []
  },
  {
    key: "minIvPercentile",
    id: "cc-minIvPercentile",
    label: "Min IV percentile",
    type: "number",
    required: false,
    helperText: "Minimum implied-volatility percentile required for candidate calls.",
    errorId: "cc-minIvPercentile-error",
    options: []
  },
  {
    key: "maxSpreadPct",
    id: "cc-maxSpreadPct",
    label: "Max spread %",
    type: "number",
    step: "0.01",
    required: false,
    helperText: "Maximum bid/ask spread fraction; use 0.05 for 5%.",
    errorId: "cc-maxSpreadPct-error",
    options: []
  },
  {
    key: "minOpenInterest",
    id: "cc-minOpenInterest",
    label: "Min open interest",
    type: "number",
    required: false,
    helperText: "Minimum open interest required before a contract can pass filters.",
    errorId: "cc-minOpenInterest-error",
    options: []
  },
  {
    key: "minVolume",
    id: "cc-minVolume",
    label: "Min volume",
    type: "number",
    required: false,
    helperText: "Minimum current chain volume required before a contract can pass filters.",
    errorId: "cc-minVolume-error",
    options: []
  },
  {
    key: "scoringMode",
    id: "cc-scoringMode",
    label: "Scoring mode",
    type: "select",
    required: false,
    helperText: "Relative ranks candidates by liquidity, depth, and premium quality; Basic keeps the plain filter score.",
    errorId: "cc-scoringMode-error",
    options: [
      { value: "Relative", label: "Relative", description: "Rank by relative candidate quality." },
      { value: "Basic", label: "Basic", description: "Use the baseline filter score." }
    ]
  },
  {
    key: "depthBonusWeight",
    id: "cc-depthBonusWeight",
    label: "Depth bonus weight",
    type: "number",
    step: "0.01",
    required: false,
    helperText: "Extra score weight for deeper option-chain liquidity when Relative scoring is selected.",
    errorId: "cc-depthBonusWeight-error",
    options: []
  },
  {
    key: "takeProfitCapture",
    id: "cc-takeProfitCapture",
    label: "Take-profit capture",
    type: "number",
    step: "0.01",
    required: false,
    helperText: "Premium capture fraction that triggers a take-profit close; use 0.80 for 80%.",
    errorId: "cc-takeProfitCapture-error",
    options: []
  },
  {
    key: "rollDelta",
    id: "cc-rollDelta",
    label: "Roll delta",
    type: "number",
    step: "0.01",
    required: false,
    helperText: "Delta threshold that can trigger a roll review.",
    errorId: "cc-rollDelta-error",
    options: []
  },
  {
    key: "exDivWindowDays",
    id: "cc-exDivWindowDays",
    label: "Ex-div window (days)",
    type: "number",
    required: false,
    helperText: "Days around ex-dividend dates where assignment risk receives extra attention.",
    errorId: "cc-exDivWindowDays-error",
    options: []
  },
  {
    key: "riskFreeRate",
    id: "cc-riskFreeRate",
    label: "Risk-free rate",
    type: "number",
    step: "0.001",
    required: false,
    helperText: "Annual risk-free rate used by option calculations; use 0.04 for 4%.",
    errorId: "cc-riskFreeRate-error",
    options: []
  },
  {
    key: "initialCash",
    id: "cc-initialCash",
    label: "Initial cash",
    type: "number",
    required: false,
    helperText: "Starting cash for the backtest account.",
    errorId: "cc-initialCash-error",
    options: []
  },
  {
    key: "initialUnderlyingShares",
    id: "cc-initialUnderlyingShares",
    label: "Underlying shares",
    type: "number",
    required: false,
    helperText: "Starting long-share inventory available to overwrite.",
    errorId: "cc-initialUnderlyingShares-error",
    options: []
  },
  {
    key: "label",
    id: "cc-label",
    label: "Run label (optional)",
    type: "text",
    required: false,
    helperText: "Optional operator label shown in previous-run history.",
    errorId: "cc-label-error",
    options: []
  }
];

const COVERED_CALL_FORM_FIELD_GROUPS: Array<{ id: string; columns: 1 | 2; fields: Array<keyof CoveredCallFormState> }> = [
  { id: "symbol", columns: 1, fields: ["underlyingSymbol"] },
  { id: "window", columns: 2, fields: ["from", "to"] },
  { id: "strike", columns: 1, fields: ["minStrike"] },
  { id: "overwrite-delta", columns: 2, fields: ["overwriteRatio", "maxDelta"] },
  { id: "dte", columns: 2, fields: ["minDte", "maxDte"] },
  { id: "vol-spread", columns: 2, fields: ["minIvPercentile", "maxSpreadPct"] },
  { id: "liquidity", columns: 2, fields: ["minOpenInterest", "minVolume"] },
  { id: "scoring", columns: 2, fields: ["scoringMode", "depthBonusWeight"] },
  { id: "exit-roll", columns: 2, fields: ["takeProfitCapture", "rollDelta"] },
  { id: "dividend-rate", columns: 2, fields: ["exDivWindowDays", "riskFreeRate"] },
  { id: "account", columns: 2, fields: ["initialCash", "initialUnderlyingShares"] },
  { id: "label", columns: 1, fields: ["label"] }
];

export function buildCoveredCallFormFields(errors: CoveredCallFormErrors): CoveredCallFormFieldMap {
  return COVERED_CALL_FORM_FIELD_DEFINITIONS.reduce((fields, definition) => {
    const error = errors[definition.key] ?? null;
    fields[definition.key] = {
      ...definition,
      error,
      invalid: Boolean(error),
      describedBy: error ? `${definition.id}-help ${definition.errorId}` : `${definition.id}-help`
    };
    return fields;
  }, {} as CoveredCallFormFieldMap);
}

export function buildCoveredCallFormFieldGroups(fields: CoveredCallFormFieldMap): CoveredCallFormFieldGroupViewModel[] {
  return COVERED_CALL_FORM_FIELD_GROUPS.map((group) => ({
    id: group.id,
    columns: group.columns,
    fields: group.fields.map((field) => fields[field])
  }));
}

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

export function buildCoveredCallCancelCommandState(
  run: CoveredCallRunState,
  cancelConfirmationPending = false
): CoveredCallActionCommandState {
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

  if (cancelConfirmationPending) {
    return {
      label: "Confirm cancel",
      ariaLabel: `Confirm cancel covered-call backtest run ${run.runId}. This stops the active backtest request.`,
      feedbackId,
      feedbackText: "Cancel confirmation pending. Confirm cancel stops this covered-call backtest run.",
      disabled: false,
      disabledReason: null,
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

const COVERED_CALL_STAGE_ORDER: CoveredCallStage[] = ["configure", "run", "results"];

const COVERED_CALL_STAGE_LABEL: Record<CoveredCallStage, string> = {
  configure: "Configure",
  run: "Run",
  results: "Results"
};

export function buildCoveredCallStageNavigationState(
  run: CoveredCallRunState,
  currentStage: CoveredCallStage = "configure"
): CoveredCallStageNavigationState {
  const disabledReason = run.isStarting
    ? "Wait until the strategy engine accepts the backtest request before leaving run progress."
    : run.isCancelling
      ? "Wait until cancellation completes before leaving run progress."
      : null;
  const feedbackId = "covered-call-stage-navigation-feedback";
  const configure = {
    disabled: disabledReason !== null,
    disabledReason
  };
  const runStep = {
    disabled: false,
    disabledReason: null
  };
  const results = {
    disabled: disabledReason !== null,
    disabledReason
  };
  const byStage: Record<CoveredCallStage, CoveredCallStageNavigationItemState> = {
    configure,
    run: runStep,
    results
  };

  return {
    feedbackId,
    feedbackText: disabledReason,
    configure,
    run: runStep,
    results,
    steps: COVERED_CALL_STAGE_ORDER.map((stage, index) => {
      const item = byStage[stage];
      const label = COVERED_CALL_STAGE_LABEL[stage];
      const sequenceLabel = `${index + 1}`;
      const isCurrent = stage === currentStage;
      return {
        stage,
        label,
        sequenceLabel,
        buttonLabel: `${sequenceLabel}. ${label}`,
        ariaLabel: `${sequenceLabel}. ${label}`,
        ariaCurrent: isCurrent ? "step" : undefined,
        ariaDescribedBy: item.disabled && disabledReason ? feedbackId : undefined,
        isCurrent,
        disabled: item.disabled,
        disabledReason: item.disabledReason
      };
    })
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
            href: workstationRouteWithQuery("dataQuotes", { symbol: quoteSymbol }),
            ariaLabel: `Validate live quote evidence for ${symbol}`
          },
          {
            id: "strategy-designer",
            label: "Refine payoff",
            description: "Compare the covered-call shape against editable option-leg structures.",
            href: WORKSTATION_ROUTE_CATALOG.strategyDesigner,
            ariaLabel: "Open Strategy Designer to refine covered-call payoff"
          },
          {
            id: "report-pack",
            label: "Package evidence",
            description: "Move selected run evidence toward report-pack preview or export review.",
            href: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
            ariaLabel: "Open report packs to package covered-call run evidence"
          }
        ]
      : []
  };
}

export function buildCoveredCallTradeTimelinePanel(
  result: CoveredCallRunResult | null,
  selectedIndex: number
): CoveredCallTradeTimelinePanelViewModel {
  const base = {
    title: result ? `Trades (${result.trades.length})` : "Trades",
    tableLabel: result ? `${result.underlyingSymbol} covered-call trade timeline` : "Covered-call trade timeline",
    tableCaption: result
      ? `Select a ${result.underlyingSymbol} covered-call trade to inspect premium, holding-period, and assignment evidence.`
      : "Covered-call trade rows appear after a completed run is loaded.",
    emptyText: "No trades recorded.",
    detailPanelId: COVERED_CALL_TRADE_DETAIL_PANEL_ID
  };

  if (!result || result.trades.length === 0) {
    return {
      ...base,
      detailEmptyTitle: "No trade selected",
      detailEmptyText: result
        ? "This completed run did not record covered-call trade fills."
        : "Complete or load a covered-call run to inspect trade evidence.",
      detailEmptyAriaLabel: "Covered-call trade detail empty",
      rows: [],
      selectedRowId: null,
      selectedDetail: null
    };
  }

  const selectedTradeIndex = clampIndex(selectedIndex, result.trades.length);
  const rows = result.trades.map((trade, index) =>
    buildCoveredCallTradeTimelineRow(result.underlyingSymbol, trade, index, selectedTradeIndex)
  );
  const selectedTrade = result.trades[selectedTradeIndex] ?? null;

  return {
    ...base,
    detailEmptyTitle: "No trade selected",
    detailEmptyText: "Select a trade row to inspect premium, holding-period, and assignment evidence.",
    detailEmptyAriaLabel: "Covered-call trade detail empty",
    rows,
    selectedRowId: rows[selectedTradeIndex]?.id ?? null,
    selectedDetail: selectedTrade
      ? buildCoveredCallTradeTimelineDetail(result.underlyingSymbol, selectedTrade, selectedTradeIndex)
      : null
  };
}

export function buildCoveredCallPayoffPanel(
  result: CoveredCallRunResult | null,
  selectedIndex: number
): CoveredCallPayoffPanelViewModel {
  const base = {
    title: "Payoff diagram",
    selectorAriaLabel: "Covered-call open positions",
    note: "Covered-call net curve requires the underlying cost basis which is not yet threaded through the API. The chart shows the short-call leg only."
  };

  if (!result || result.openPositionsAtEnd.length === 0) {
    return {
      ...base,
      description: "Short-call payoff diagram for any open position at end of run.",
      emptyText: result ? "No open positions at end of run." : "Complete or load a run to inspect payoff evidence.",
      positionOptions: [],
      chart: null
    };
  }

  const selectedPositionIndex = clampIndex(selectedIndex, result.openPositionsAtEnd.length);
  const selectedPosition = result.openPositionsAtEnd[selectedPositionIndex];
  const breakEven = shortCallBreakEven({
    strike: selectedPosition.strike,
    entryCredit: selectedPosition.entryCredit,
    contracts: selectedPosition.contracts,
    multiplier: selectedPosition.multiplier
  });

  return {
    ...base,
    title: "Payoff diagram (short call leg)",
    description: `${selectedPosition.contracts} x ${formatPrice(selectedPosition.strike)} call expiring ${selectedPosition.expiration} - short-call break-even about $${formatPrice(breakEven)}`,
    emptyText: null,
    positionOptions: result.openPositionsAtEnd.map((position, index) =>
      buildCoveredCallPayoffPositionOption(result.underlyingSymbol, position, index, selectedPositionIndex)
    ),
    chart: buildCoveredCallPayoffChart(selectedPosition, result.underlyingSymbol)
  };
}

function buildCoveredCallPayoffPositionOption(
  underlyingSymbol: string,
  position: CoveredCallOpenPosition,
  index: number,
  selectedIndex: number
): CoveredCallPayoffPositionOptionViewModel {
  const strikeLabel = formatPrice(position.strike);
  const selected = index === selectedIndex;

  return {
    id: position.positionId || `position-${index}`,
    index,
    label: `${strikeLabel} call`,
    description: `${position.expiration} - ${position.contracts} ${position.contracts === 1 ? "contract" : "contracts"}`,
    selected,
    buttonVariant: selected ? "secondary" : "outline",
    ariaLabel: `${selected ? "Selected" : "Select"} ${underlyingSymbol} ${strikeLabel} call expiring ${position.expiration} payoff diagram`
  };
}

function buildCoveredCallPayoffChart(
  position: CoveredCallOpenPosition,
  underlyingSymbol: string
): CoveredCallPayoffChartViewModel {
  const width = 320;
  const height = 180;
  const spotMin = position.strike * 0.75;
  const spotMax = position.strike * 1.25;
  const samples = buildShortCallPayoffCurve({
    strike: position.strike,
    entryCredit: position.entryCredit,
    contracts: position.contracts,
    multiplier: position.multiplier
  }, spotMin, spotMax, 80);
  const allPayoffs = samples.map((sample) => sample.payoff);
  const yMin = Math.min(...allPayoffs);
  const yMax = Math.max(...allPayoffs);
  const xScale = (spot: number) => ((spot - spotMin) / Math.max(spotMax - spotMin, 1e-6)) * width;
  const yScale = (value: number) => height - ((value - yMin) / Math.max(yMax - yMin, 1e-6)) * height;
  if (samples.length === 0 || !Number.isFinite(yMin) || !Number.isFinite(yMax)) {
    return {
      viewBox: `0 0 ${width} ${height}`,
      ariaLabel: `${underlyingSymbol} ${formatPrice(position.strike)} short-call payoff diagram unavailable`,
      zeroLine: { x1: 0, y1: height / 2, x2: width, y2: height / 2 },
      strikeLine: { x1: width / 2, y1: 0, x2: width / 2, y2: height },
      path: `M0,${height / 2} L${width},${height / 2}`
    };
  }
  const path = samples
    .map((sample, index) => `${index === 0 ? "M" : "L"}${xScale(sample.spot).toFixed(1)},${yScale(sample.payoff).toFixed(1)}`)
    .join(" ");

  return {
    viewBox: `0 0 ${width} ${height}`,
    ariaLabel: `${underlyingSymbol} ${formatPrice(position.strike)} short-call payoff diagram`,
    zeroLine: { x1: 0, y1: yScale(0), x2: width, y2: yScale(0) },
    strikeLine: { x1: xScale(position.strike), y1: 0, x2: xScale(position.strike), y2: height },
    path
  };
}

function buildCoveredCallTradeTimelineRow(
  underlyingSymbol: string,
  trade: CoveredCallTrade,
  index: number,
  selectedIndex: number
): CoveredCallTradeTimelineRowViewModel {
  const strikeLabel = formatPrice(trade.strike);
  const pnlLabel = formatSignedMoney(trade.totalNetPnl);
  const statusLabel = trade.wasAssigned ? "Assigned" : trade.isWin ? "Closed gain" : "Closed loss";
  const exitReasonLabel = formatExitReason(trade.exitReason);
  const statusBadgeVariant: CoveredCallBadgeVariant = trade.wasAssigned
    ? "warning"
    : trade.isWin
      ? "success"
      : "danger";
  const id = coveredCallTradeRowId(trade, index);
  const summary = `${underlyingSymbol} trade ${index + 1}, entry ${trade.entryDate}, exit ${trade.exitDate}, strike ${strikeLabel}, PnL ${pnlLabel}, status ${statusLabel}.`;

  return {
    id,
    index,
    entryDateLabel: trade.entryDate,
    exitDateLabel: trade.exitDate,
    strikeLabel,
    pnlLabel,
    pnlClassName: trade.totalNetPnl > 0
      ? "text-success"
      : trade.totalNetPnl < 0
        ? "text-danger"
        : "text-muted-foreground",
    exitReasonLabel,
    statusLabel,
    statusBadgeVariant,
    rowAriaLabel: summary,
    rowSelectAriaLabel: `Inspect ${summary}`,
    detailPanelId: COVERED_CALL_TRADE_DETAIL_PANEL_ID,
    ariaExpanded: index === selectedIndex
  };
}

function buildCoveredCallTradeTimelineDetail(
  underlyingSymbol: string,
  trade: CoveredCallTrade,
  index: number
): CoveredCallTradeTimelineDetailViewModel {
  const strikeLabel = formatPrice(trade.strike);
  const statusLabel = trade.wasAssigned ? "Assigned" : trade.isWin ? "Closed gain" : "Closed loss";
  const exitReasonLabel = formatExitReason(trade.exitReason);
  const statusBadgeVariant: CoveredCallBadgeVariant = trade.wasAssigned
    ? "warning"
    : trade.isWin
      ? "success"
      : "danger";
  const totalPnl = formatSignedMoney(trade.totalNetPnl);

  return {
    panelId: COVERED_CALL_TRADE_DETAIL_PANEL_ID,
    eyebrow: "Selected trade",
    title: `${underlyingSymbol} ${strikeLabel} call`,
    subtitle: `${trade.entryDate} to ${trade.exitDate} · ${trade.contracts} contract${trade.contracts === 1 ? "" : "s"}`,
    description: `${statusLabel}; exit reason ${exitReasonLabel}; ${totalPnl} total net PnL.`,
    statusLabel,
    statusBadgeVariant,
    fields: [
      { label: "Exit reason", value: exitReasonLabel },
      { label: "Entry credit", value: formatSignedMoney(trade.entryCredit) },
      { label: "Exit debit", value: formatSignedMoney(trade.exitDebit) },
      { label: "Net per contract", value: formatSignedMoney(trade.netPnlPerContract) },
      { label: "Total net PnL", value: totalPnl },
      { label: "Holding days", value: formatCount(trade.holdingDays) },
      { label: "Multiplier", value: formatCount(trade.multiplier) },
      { label: "Entry IV", value: trade.entryImpliedVolatility === null ? "—" : formatPercent(trade.entryImpliedVolatility) },
      { label: "Assignment", value: trade.wasAssigned ? "Assigned" : "Not assigned" }
    ],
    ariaLabel: `Selected covered-call trade ${index + 1}: ${underlyingSymbol} ${strikeLabel} call`
  };
}

function coveredCallTradeRowId(trade: CoveredCallTrade, index: number): string {
  const raw = `${index}-${trade.entryDate}-${trade.exitDate}-${trade.expiration}-${trade.strike}`;
  return `covered-call-trade-${raw.replace(/[^a-zA-Z0-9_-]+/g, "-")}`;
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
      errorDetails: [],
      detailEmptyTitle: "Chain preview loading",
      detailEmptyText: "Candidate detail will appear after the option-chain preview finishes.",
      detailEmptyAriaLabel: "Covered-call candidate detail loading",
      rows: [],
      selectedRowId: null,
      selectedDetail: null
    };
  }

  if (chainPreview.status === "error") {
    const errorText = chainPreview.error?.summary ?? "Unknown error";
    return {
      ...base,
      description: `Error: ${errorText}`,
      emptyText: `Chain preview failed: ${errorText}`,
      errorDetails: chainPreview.error?.details ?? [],
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
      errorDetails: [],
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
    errorDetails: [],
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

function formatSignedMoney(value: number): string {
  if (!Number.isFinite(value)) return "—";
  const sign = value < 0 ? "-$" : "$";
  return `${sign}${Math.abs(value).toLocaleString("en-US", { maximumFractionDigits: 2 })}`;
}

function formatExitReason(value: string): string {
  const normalized = value.trim();
  if (!normalized) return "Closed";
  return normalized
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/\s+/g, " ")
    .trim()
    .toLowerCase()
    .replace(/^\w/, (letter) => letter.toUpperCase());
}

export function buildCoveredCallHistoryRows(history: CoveredCallRunSummary[]): CoveredCallHistoryRowViewModel[] {
  return history.map((row) => {
    const startedAtLabel = formatUtcDateTime(row.startedAt);
    const rangeLabel = `${row.from} to ${row.to}`;
    const statusBadgeVariant = statusBadgeVariantForHistory(row.status);
    const cagrLabel = row.cagr === null ? "—" : formatPercent(row.cagr);
    const sharpeRatioLabel = row.sharpeRatio === null ? "—" : formatDecimal(row.sharpeRatio, 2);
    const labelText = row.label?.trim() || "Unlabeled";

    return {
      runId: row.runId,
      startedAtLabel,
      underlyingSymbol: row.underlyingSymbol,
      rangeLabel,
      statusLabel: row.status,
      statusBadgeVariant,
      cagrLabel,
      sharpeRatioLabel,
      labelText,
      rowAriaLabel: [
        `Covered-call run ${row.runId}.`,
        `${row.underlyingSymbol} from ${rangeLabel}.`,
        `Started ${startedAtLabel}.`,
        `Status ${row.status}.`,
        `CAGR ${cagrLabel}.`,
        `Sharpe ${sharpeRatioLabel}.`
      ].join(" "),
      rowSelectAriaLabel: `Reload covered-call run ${row.runId} for ${row.underlyingSymbol}`
    };
  });
}

function statusBadgeVariantForHistory(status: string): CoveredCallBadgeVariant {
  const normalizedStatus = status.toLowerCase();
  if (normalizedStatus === "completed" || normalizedStatus === "succeeded" || normalizedStatus === "success") {
    return "success";
  }

  if (normalizedStatus === "failed" || normalizedStatus === "cancelled" || normalizedStatus === "canceled") {
    return "danger";
  }

  if (normalizedStatus === "running" || normalizedStatus === "queued") {
    return "warning";
  }

  return "outline";
}

function formatUtcDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "Unavailable";
  }

  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

function padUtc(value: number): string {
  return String(value).padStart(2, "0");
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

export interface CoveredCallScreenState {
  stage: CoveredCallStage;
  form: CoveredCallFormState;
  formErrors: CoveredCallFormErrors;
  formFields: CoveredCallFormFieldMap;
  formFieldGroups: CoveredCallFormFieldGroupViewModel[];
  chainPreview: CoveredCallChainPreviewState;
  chainPreviewPanel: CoveredCallChainPreviewPanelViewModel;
  run: CoveredCallRunState;
  runCommand: CoveredCallActionCommandState;
  cancelRunCommand: CoveredCallActionCommandState;
  runProgressPanel: CoveredCallRunProgressPanelViewModel;
  stageNavigation: CoveredCallStageNavigationState;
  resultsActionPanel: CoveredCallResultsActionPanelViewModel;
  tradeTimelinePanel: CoveredCallTradeTimelinePanelViewModel;
  payoffPanel: CoveredCallPayoffPanelViewModel;
  history: CoveredCallRunSummary[];
  historyRows: CoveredCallHistoryRowViewModel[];
  historyLoading: boolean;
  historyLoaded: boolean;
  historyError: ApiErrorDisplay | null;
  historyTableLabel: string;
  historyCaption: string;
  historyEmptyText: string;
  historyStatusText: string;
  errorBanner: ApiErrorDisplay | null;
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
  selectTradeRow: (index: number) => void;
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
    selectedTradeIndex: 0,
    isStarting: false,
    isCancelling: false
  });
  const [history, setHistory] = useState<CoveredCallRunSummary[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyLoaded, setHistoryLoaded] = useState(false);
  const [historyError, setHistoryError] = useState<ApiErrorDisplay | null>(null);
  const [errorBanner, setErrorBanner] = useState<ApiErrorDisplay | null>(null);
  const [pendingCancelRunId, setPendingCancelRunId] = useState<string | null>(null);

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
    setPendingCancelRunId(null);
  }, []);

  const dismissError = useCallback(() => setErrorBanner(null), []);
  const goToStage = useCallback((next: CoveredCallStage) => {
    setStage((current) => {
      const navigation = buildCoveredCallStageNavigationState(run);
      if (navigation[next].disabled) return current;
      setPendingCancelRunId(null);
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
        error: describeApiError(error, "Covered-call chain preview failed."),
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
    setRun((prev) => ({ ...prev, selectedPositionIndex: clampIndex(index, prev.result?.openPositionsAtEnd.length ?? 0) }));
  }, []);

  const selectTradeRow = useCallback((index: number) => {
    setRun((prev) => ({ ...prev, selectedTradeIndex: index }));
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
            setRun((prev) => ({ ...prev, result, selectedPositionIndex: 0, selectedTradeIndex: 0, isStarting: false, isCancelling: false }));
            setStage("results");
          } catch (resultErr) {
            setErrorBanner(describeApiError(resultErr, "Covered-call result failed to load."));
          }
        } else if (status.phase === "Failed" && status.failureMessage) {
          setErrorBanner({ summary: status.failureMessage, details: [] });
        }
      } else {
        pollTimerRef.current = window.setTimeout(() => {
          void pollOnce(runId);
        }, pollIntervalMs);
      }
    } catch (error) {
      if ((error as Error).name === "AbortError") return;
      setErrorBanner(describeApiError(error, "Covered-call status polling failed."));
      stopPolling();
    }
  }, [pollIntervalMs, services, stopPolling]);

  const startRun = useCallback(async () => {
    if (run.isStarting) return;

    const errors = validateForm(form);
    setFormErrors(errors);
    if (Object.keys(errors).length > 0) {
      setErrorBanner({ summary: "Fix the highlighted form fields before running.", details: [] });
      return;
    }

    // Stop any in-flight polling and invalidate the previous active run id so a stale poll that
    // resolves during the startRun await can't push status/result into the new run's state.
    stopPolling();
    activeRunIdRef.current = null;
    setPendingCancelRunId(null);

    setErrorBanner(null);
    setRun({ runId: null, status: null, result: null, selectedPositionIndex: 0, selectedTradeIndex: 0, isStarting: true, isCancelling: false });
    setStage("run");

    try {
      const handle = await services.startRun(formToRequest(form));
      activeRunIdRef.current = handle.runId;
      setRun({
        runId: handle.runId,
        status: { runId: handle.runId, phase: "Queued", percentComplete: 0, currentBacktestDate: null, failureMessage: null },
        result: null,
        selectedPositionIndex: 0,
        selectedTradeIndex: 0,
        isStarting: false,
        isCancelling: false
      });
      pollTimerRef.current = window.setTimeout(() => {
        void pollOnce(handle.runId);
      }, pollIntervalMs);
    } catch (error) {
      setErrorBanner(describeApiError(error, "Covered-call backtest request failed."));
      setRun({ runId: null, status: null, result: null, selectedPositionIndex: 0, selectedTradeIndex: 0, isStarting: false, isCancelling: false });
      setStage("configure");
    }
  }, [form, pollIntervalMs, pollOnce, run.isStarting, services, stopPolling]);

  const cancelRun = useCallback(async () => {
    const runId = run.runId;
    if (!runId || run.isCancelling) return;
    if (pendingCancelRunId !== runId) {
      setPendingCancelRunId(runId);
      return;
    }
    setPendingCancelRunId(null);
    setRun((prev) => ({ ...prev, isCancelling: true }));
    try {
      const status = await services.cancelRun(runId);
      setRun((prev) => ({ ...prev, status, isCancelling: false }));
    } catch (error) {
      setErrorBanner(describeApiError(error, "Covered-call cancel request failed."));
      setRun((prev) => ({ ...prev, isCancelling: false }));
    }
  }, [pendingCancelRunId, run.isCancelling, run.runId, services]);

  const loadHistory = useCallback(async () => {
    setHistoryLoading(true);
    setHistoryError(null);
    try {
      const items = await services.listRuns(50);
      setHistory(items);
    } catch (error) {
      setHistoryError(describeApiError(error, "Previous covered-call runs failed to load."));
    } finally {
      setHistoryLoaded(true);
      setHistoryLoading(false);
    }
  }, [services]);

  const openRun = useCallback(async (runId: string) => {
    setErrorBanner(null);
    try {
      const result = await services.getResult(runId);
      activeRunIdRef.current = runId;
      stopPolling();
      setPendingCancelRunId(null);
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
        selectedTradeIndex: 0,
        isStarting: false,
        isCancelling: false
      });
      setStage("results");
    } catch (error) {
      setErrorBanner(describeApiError(error, `Could not load covered-call run ${runId}.`));
    }
  }, [services, stopPolling]);

  const runCommand = useMemo(
    () => buildCoveredCallRunCommandState(form, run.isStarting),
    [form, run.isStarting]
  );
  const cancelRunCommand = useMemo(
    () => buildCoveredCallCancelCommandState(run, Boolean(run.runId && pendingCancelRunId === run.runId)),
    [pendingCancelRunId, run]
  );
  const runProgressPanel = useMemo(
    () => buildCoveredCallRunProgressPanel(run),
    [run]
  );
  const stageNavigation = useMemo(
    () => buildCoveredCallStageNavigationState(run, stage),
    [run, stage]
  );
  const resultsActionPanel = useMemo(
    () => buildCoveredCallResultsActionPanel(run.result),
    [run.result]
  );
  const tradeTimelinePanel = useMemo(
    () => buildCoveredCallTradeTimelinePanel(run.result, run.selectedTradeIndex),
    [run.result, run.selectedTradeIndex]
  );
  const payoffPanel = useMemo(
    () => buildCoveredCallPayoffPanel(run.result, run.selectedPositionIndex),
    [run.result, run.selectedPositionIndex]
  );
  const formFields = useMemo(
    () => buildCoveredCallFormFields(formErrors),
    [formErrors]
  );
  const formFieldGroups = useMemo(
    () => buildCoveredCallFormFieldGroups(formFields),
    [formFields]
  );
  const historyRows = useMemo(
    () => buildCoveredCallHistoryRows(history),
    [history]
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
    formFields,
    formFieldGroups,
    chainPreview,
    chainPreviewPanel: buildChainPreviewPanelViewModel(chainPreview),
    run,
    runCommand,
    cancelRunCommand,
    runProgressPanel,
    stageNavigation,
    resultsActionPanel,
    tradeTimelinePanel,
    payoffPanel,
    history,
    historyRows,
    historyLoading,
    historyLoaded,
    historyError,
    historyTableLabel: "Previous covered-call runs",
    historyCaption: "Reload a previous covered-call run from the cached run history.",
    historyEmptyText: historyLoaded ? "No previous covered-call runs are available." : "Previous covered-call runs have not loaded yet.",
    historyStatusText: historyLoading
      ? "Loading previous covered-call runs."
      : historyError
        ? `Previous covered-call runs failed to load: ${historyError.summary}`
        : historyRows.length === 0
          ? "No previous covered-call runs are available."
          : `${historyRows.length} previous covered-call runs loaded.`,
    errorBanner,
    setField,
    resetForm,
    refreshChainPreview,
    selectChainRow,
    startRun,
    cancelRun,
    loadHistory,
    openRun,
    selectTradeRow,
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
    formFields,
    formFieldGroups,
    resultsActionPanel,
    tradeTimelinePanel,
    payoffPanel,
    history,
    historyRows,
    historyLoading,
    historyLoaded,
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
    selectTradeRow,
    selectOpenPosition,
    goToStage,
    dismissError
  ]);
}
