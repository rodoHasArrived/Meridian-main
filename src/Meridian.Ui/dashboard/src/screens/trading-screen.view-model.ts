import { useCallback, useEffect, useMemo, useState } from "react";
import * as workstationApi from "@/lib/api";
import type { ApprovePromotionRequest, RejectPromotionRequest } from "@/lib/api";
import type {
  OperatorWorkItem,
  OrderResult,
  OrderSubmitRequest,
  PromotionDecisionResult,
  PromotionEvaluationResult,
  PromotionRecord,
  TradingAcceptanceGateStatus,
  TradingOperatorReadiness,
  WorkstationBrokerageSyncStatus
} from "@/types";

export type AcceptanceLevel = "ready" | "review" | "atRisk";

export interface TradingReadinessSummaryRow {
  id: string;
  label: string;
  value: string;
  level: AcceptanceLevel;
  ariaLabel: string;
}

export interface TradingReadinessState {
  readiness: TradingOperatorReadiness | null;
  refreshing: boolean;
  errorText: string | null;
  workItems: OperatorWorkItem[];
  warnings: string[];
  summaryRows: TradingReadinessSummaryRow[];
  summaryLabel: string;
  refreshButtonLabel: string;
  refreshAriaLabel: string;
  statusAnnouncement: string;
}

export interface TradingReadinessViewModel extends TradingReadinessState {
  refresh: () => Promise<void>;
}

export interface TradingReadinessServices {
  getTradingReadiness: () => Promise<TradingOperatorReadiness | null>;
}

export interface BuildTradingReadinessStateOptions {
  readiness: TradingOperatorReadiness | null;
  refreshing: boolean;
  errorText: string | null;
}

const defaultTradingReadinessServices: TradingReadinessServices = {
  getTradingReadiness: () => workstationApi.getTradingReadiness()
};

export function useTradingReadinessViewModel({
  initialReadiness,
  services = defaultTradingReadinessServices
}: {
  initialReadiness: TradingOperatorReadiness | null;
  services?: TradingReadinessServices;
}): TradingReadinessViewModel {
  const [readiness, setReadiness] = useState<TradingOperatorReadiness | null>(initialReadiness);
  const [refreshing, setRefreshing] = useState(false);
  const [errorText, setErrorText] = useState<string | null>(null);

  useEffect(() => {
    setReadiness(initialReadiness);
    setErrorText(null);
  }, [initialReadiness]);

  const refresh = useCallback(async () => {
    setRefreshing(true);
    setErrorText(null);

    try {
      setReadiness(await services.getTradingReadiness());
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to refresh trading readiness."));
    } finally {
      setRefreshing(false);
    }
  }, [services]);

  const state = useMemo(
    () => buildTradingReadinessState({ readiness, refreshing, errorText }),
    [errorText, readiness, refreshing]
  );

  return {
    ...state,
    refresh
  };
}

export function buildTradingReadinessState({
  readiness,
  refreshing,
  errorText
}: BuildTradingReadinessStateOptions): TradingReadinessState {
  const summaryRows = readiness ? buildTradingReadinessSummaryRows(readiness) : [];

  return {
    readiness,
    refreshing,
    errorText,
    workItems: readiness?.workItems ?? [],
    warnings: readiness?.warnings ?? [],
    summaryRows,
    summaryLabel: "Trading readiness contract summary",
    refreshButtonLabel: refreshing ? "Refreshing..." : "Refresh readiness",
    refreshAriaLabel: refreshing ? "Refreshing trading readiness" : "Refresh trading readiness",
    statusAnnouncement: buildTradingReadinessAnnouncement({ readiness, refreshing, errorText })
  };
}

export function formatReadinessStatusValue(status: TradingAcceptanceGateStatus | string): string {
  if (status === "ReviewRequired") {
    return "Review required";
  }

  return status;
}

export function mapReadinessStatusLevel(status: TradingAcceptanceGateStatus | string): AcceptanceLevel {
  if (status === "Ready") {
    return "ready";
  }

  if (status === "Blocked") {
    return "atRisk";
  }

  return "review";
}

export function mapBrokerageSyncLevel(status: WorkstationBrokerageSyncStatus): AcceptanceLevel {
  if (status.health === "Healthy" && !status.isStale) {
    return "ready";
  }

  if (status.health === "Failed" || status.health === "Degraded") {
    return "atRisk";
  }

  return "review";
}

function buildTradingReadinessSummaryRows(readiness: TradingOperatorReadiness): TradingReadinessSummaryRow[] {
  const overallValue = formatReadinessStatusValue(readiness.overallStatus);
  const paperValue = readiness.readyForPaperOperation ? "Ready for paper" : "Not paper ready";
  const brokerageValue = readiness.brokerageSync
    ? formatBrokerageSyncValue(readiness.brokerageSync)
    : "No account sync";

  return [
    {
      id: "overall",
      label: "Overall",
      value: overallValue,
      level: mapReadinessStatusLevel(readiness.overallStatus),
      ariaLabel: `Overall readiness: ${overallValue}`
    },
    {
      id: "paper",
      label: "Paper",
      value: paperValue,
      level: readiness.readyForPaperOperation ? "ready" : "review",
      ariaLabel: `Paper operation readiness: ${paperValue}`
    },
    {
      id: "brokerage",
      label: "Brokerage",
      value: brokerageValue,
      level: readiness.brokerageSync ? mapBrokerageSyncLevel(readiness.brokerageSync) : "review",
      ariaLabel: `Brokerage sync: ${brokerageValue}`
    },
    {
      id: "as-of",
      label: "As of",
      value: readiness.asOf,
      level: "review",
      ariaLabel: `Readiness snapshot timestamp: ${readiness.asOf}`
    }
  ];
}

function formatBrokerageSyncValue(status: WorkstationBrokerageSyncStatus): string {
  const staleSuffix = status.isStale && status.health !== "Stale" ? " stale" : "";
  return `${status.health}${staleSuffix}`;
}

function buildTradingReadinessAnnouncement({
  readiness,
  refreshing,
  errorText
}: BuildTradingReadinessStateOptions): string {
  if (refreshing) {
    return "Refreshing trading readiness.";
  }

  if (errorText) {
    return `Trading readiness refresh failed: ${errorText}`;
  }

  if (readiness) {
    return `Trading readiness ${formatReadinessStatusValue(readiness.overallStatus).toLowerCase()} as of ${readiness.asOf}.`;
  }

  return "";
}

export type OrderTicketField = "symbol" | "side" | "type" | "quantity" | "limitPrice";
export type OrderTicketPhase = "idle" | "submitting" | "submitted" | "error";

export interface OrderTicketServices {
  submitOrder: (request: OrderSubmitRequest) => Promise<OrderResult>;
}

export interface OrderTicketState {
  form: OrderSubmitRequest;
  open: boolean;
  phase: OrderTicketPhase;
  orderId: string | null;
  errorText: string | null;
  validationError: string | null;
  invalidField: OrderTicketField | null;
  canSubmit: boolean;
  canClose: boolean;
  requiresLimitPrice: boolean;
  priceLabel: string;
  openButtonLabel: string;
  submitButtonLabel: string;
  submitAriaLabel: string;
  requirementText: string;
  successText: string | null;
  statusAnnouncement: string;
}

export interface BuildOrderTicketStateOptions {
  form: OrderSubmitRequest;
  open: boolean;
  phase: OrderTicketPhase;
  orderId: string | null;
  errorText: string | null;
}

export const emptyOrderTicketForm: OrderSubmitRequest = {
  symbol: "",
  side: "Buy",
  type: "Market",
  quantity: 0,
  limitPrice: null
};

const defaultOrderTicketServices: OrderTicketServices = {
  submitOrder: (request) => workstationApi.submitOrder(request)
};

export function useOrderTicketViewModel({
  services = defaultOrderTicketServices,
  onOrderAccepted
}: {
  services?: OrderTicketServices;
  onOrderAccepted?: () => Promise<void> | void;
} = {}) {
  const [form, setForm] = useState<OrderSubmitRequest>(emptyOrderTicketForm);
  const [open, setOpen] = useState(false);
  const [phase, setPhase] = useState<OrderTicketPhase>("idle");
  const [orderId, setOrderId] = useState<string | null>(null);
  const [errorText, setErrorText] = useState<string | null>(null);

  const state = useMemo(
    () => buildOrderTicketState({ form, open, phase, orderId, errorText }),
    [errorText, form, open, orderId, phase]
  );

  const openTicket = useCallback(() => {
    setOpen(true);
    setErrorText(null);
    if (phase !== "submitted") {
      return;
    }

    setPhase("idle");
    setOrderId(null);
  }, [phase]);

  const closeTicket = useCallback(() => {
    if (phase === "submitting") {
      return;
    }

    setOpen(false);
    setPhase("idle");
    setOrderId(null);
    setErrorText(null);
  }, [phase]);

  const toggleTicket = useCallback(() => {
    if (open) {
      closeTicket();
      return;
    }

    openTicket();
  }, [closeTicket, open, openTicket]);

  const updateField = useCallback((field: OrderTicketField, value: string) => {
    setForm((current) => updateOrderTicketForm(current, field, value));
    setPhase((current) => current === "submitted" ? "idle" : current);
    setOrderId(null);
    setErrorText(null);
  }, []);

  const normalizeSymbol = useCallback(() => {
    setForm((current) => ({
      ...current,
      symbol: normalizeOrderSymbol(current.symbol)
    }));
  }, []);

  const submitOrderTicket = useCallback(async () => {
    const validationError = validateOrderTicketForm(form);
    if (validationError) {
      setPhase("error");
      setOrderId(null);
      setErrorText(validationError);
      return;
    }

    setPhase("submitting");
    setOrderId(null);
    setErrorText(null);

    try {
      const result = await services.submitOrder(buildOrderSubmitRequest(form));
      if (result.success) {
        setPhase("submitted");
        setOrderId(result.orderId);
        setErrorText(null);
        setOpen(false);
        setForm(emptyOrderTicketForm);
        await onOrderAccepted?.();
        return;
      }

      setPhase("error");
      setErrorText(result.reason ?? "Order failed.");
    } catch (err) {
      setPhase("error");
      setErrorText(toErrorMessage(err, "Order submission failed."));
    }
  }, [form, onOrderAccepted, services]);

  return {
    ...state,
    openTicket,
    closeTicket,
    toggleTicket,
    updateField,
    normalizeSymbol,
    submitOrder: submitOrderTicket
  };
}

export function buildOrderTicketState({
  form,
  open,
  phase,
  orderId,
  errorText
}: BuildOrderTicketStateOptions): OrderTicketState {
  const validationError = validateOrderTicketForm(form);
  const requiresLimitPrice = orderTypeRequiresPrice(form.type);
  const successText = phase === "submitted"
    ? `Order submitted${orderId ? ` - ${orderId}` : ""}.`
    : null;

  return {
    form,
    open,
    phase,
    orderId,
    errorText,
    validationError,
    invalidField: getOrderTicketInvalidField(form),
    canSubmit: phase !== "submitting" && validationError === null,
    canClose: phase !== "submitting",
    requiresLimitPrice,
    priceLabel: `${form.type} price`,
    openButtonLabel: open ? "Close order ticket" : "New order",
    submitButtonLabel: phase === "submitting" ? "Submitting..." : "Submit order",
    submitAriaLabel: phase === "submitting" ? "Submitting order request" : "Submit order request",
    requirementText: buildOrderRequirementText(form, phase, validationError),
    successText,
    statusAnnouncement: buildOrderTicketStatusAnnouncement({ phase, errorText, orderId })
  };
}

export function updateOrderTicketForm(
  current: OrderSubmitRequest,
  field: OrderTicketField,
  value: string
): OrderSubmitRequest {
  if (field === "symbol") {
    return { ...current, symbol: value };
  }

  if (field === "side") {
    return { ...current, side: value === "Sell" ? "Sell" : "Buy" };
  }

  if (field === "type") {
    const type = value === "Limit" || value === "Stop" ? value : "Market";
    return {
      ...current,
      type,
      limitPrice: orderTypeRequiresPrice(type) ? current.limitPrice : null
    };
  }

  if (field === "quantity") {
    return { ...current, quantity: parsePositiveNumber(value) ?? 0 };
  }

  return { ...current, limitPrice: parsePositiveNumber(value) };
}

export function buildOrderSubmitRequest(form: OrderSubmitRequest): OrderSubmitRequest {
  const request: OrderSubmitRequest = {
    symbol: normalizeOrderSymbol(form.symbol),
    side: form.side,
    type: form.type,
    quantity: form.quantity
  };

  if (orderTypeRequiresPrice(form.type)) {
    request.limitPrice = form.limitPrice;
  }

  return request;
}

export function validateOrderTicketForm(form: OrderSubmitRequest): string | null {
  if (!normalizeOrderSymbol(form.symbol)) {
    return "Enter a symbol before submitting an order.";
  }

  if (!Number.isFinite(form.quantity) || form.quantity <= 0) {
    return "Enter an order quantity greater than zero.";
  }

  if (orderTypeRequiresPrice(form.type) && (!Number.isFinite(form.limitPrice) || (form.limitPrice ?? 0) <= 0)) {
    return `Enter a ${form.type.toLowerCase()} price greater than zero.`;
  }

  return null;
}

function getOrderTicketInvalidField(form: OrderSubmitRequest): OrderTicketField | null {
  if (!normalizeOrderSymbol(form.symbol)) {
    return "symbol";
  }

  if (!Number.isFinite(form.quantity) || form.quantity <= 0) {
    return "quantity";
  }

  if (orderTypeRequiresPrice(form.type) && (!Number.isFinite(form.limitPrice) || (form.limitPrice ?? 0) <= 0)) {
    return "limitPrice";
  }

  return null;
}

function buildOrderRequirementText(
  form: OrderSubmitRequest,
  phase: OrderTicketPhase,
  validationError: string | null
): string {
  if (phase === "submitting") {
    return "Submitting order request to the execution layer.";
  }

  if (validationError) {
    return validationError;
  }

  const symbol = normalizeOrderSymbol(form.symbol);
  const priceText = orderTypeRequiresPrice(form.type) && form.limitPrice
    ? ` at ${form.limitPrice}`
    : "";
  return `${form.side} ${form.quantity} ${symbol} ${form.type.toLowerCase()}${priceText}.`;
}

function buildOrderTicketStatusAnnouncement({
  phase,
  errorText,
  orderId
}: {
  phase: OrderTicketPhase;
  errorText: string | null;
  orderId: string | null;
}): string {
  if (phase === "submitting") {
    return "Submitting order request.";
  }

  if (errorText) {
    return `Order submission failed: ${errorText}`;
  }

  if (phase === "submitted") {
    return `Order submitted${orderId ? ` with id ${orderId}` : ""}.`;
  }

  return "";
}

function normalizeOrderSymbol(symbol: string): string {
  return symbol.trim().toUpperCase();
}

function orderTypeRequiresPrice(type: OrderSubmitRequest["type"]): boolean {
  return type === "Limit" || type === "Stop";
}

function parsePositiveNumber(value: string): number | null {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

export type PromotionGateField =
  | "runId"
  | "approvedBy"
  | "approvalReason"
  | "rejectionReason"
  | "reviewNotes"
  | "manualOverrideId";

export interface PromotionGateForm {
  runId: string;
  approvedBy: string;
  approvalReason: string;
  rejectionReason: string;
  reviewNotes: string;
  manualOverrideId: string;
}

export type PromotionGatePhase = "idle" | "evaluating" | "approving" | "rejecting";
export type PromotionOutcomeLevel = "success" | "warning" | "error";

export interface PromotionOutcome {
  level: PromotionOutcomeLevel;
  message: string;
}

export interface PromotionGateServices {
  evaluatePromotion: (runId: string) => Promise<PromotionEvaluationResult>;
  approvePromotion: (request: ApprovePromotionRequest) => Promise<PromotionDecisionResult>;
  rejectPromotion: (request: RejectPromotionRequest) => Promise<PromotionDecisionResult>;
  getPromotionHistory: () => Promise<PromotionRecord[]>;
}

export interface PromotionGateState {
  form: PromotionGateForm;
  trimmedForm: PromotionGateForm;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
  busy: boolean;
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  canEvaluate: boolean;
  canPromote: boolean;
  canReject: boolean;
  evaluateButtonLabel: string;
  promoteButtonLabel: string;
  rejectButtonLabel: string;
  nextActionText: string;
  approvalRequirementText: string;
  rejectionRequirementText: string;
  historyEmptyText: string;
  statusAnnouncement: string;
}

export interface BuildPromotionGateStateOptions {
  form: PromotionGateForm;
  busy: boolean;
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
}

export const emptyPromotionGateForm: PromotionGateForm = {
  runId: "",
  approvedBy: "",
  approvalReason: "",
  rejectionReason: "",
  reviewNotes: "",
  manualOverrideId: ""
};

const defaultPromotionServices: PromotionGateServices = {
  evaluatePromotion: (runId) => workstationApi.evaluatePromotion(runId),
  approvePromotion: (request) => workstationApi.approvePromotion(request),
  rejectPromotion: (request) => workstationApi.rejectPromotion(request),
  getPromotionHistory: () => workstationApi.getPromotionHistory()
};

export function usePromotionGateViewModel(
  services: PromotionGateServices = defaultPromotionServices
) {
  const [form, setForm] = useState<PromotionGateForm>(emptyPromotionGateForm);
  const [evaluation, setEvaluation] = useState<PromotionEvaluationResult | null>(null);
  const [history, setHistory] = useState<PromotionRecord[]>([]);
  const [errorText, setErrorText] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [phase, setPhase] = useState<PromotionGatePhase>("idle");
  const [outcome, setOutcome] = useState<PromotionOutcome | null>(null);

  useEffect(() => {
    let cancelled = false;

    services.getPromotionHistory()
      .then((rows) => {
        if (!cancelled) {
          setHistory(rows);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setHistory([]);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services]);

  const state = useMemo(
    () => buildPromotionGateState({ form, busy, phase, errorText, outcome, evaluation, history }),
    [busy, errorText, evaluation, form, history, outcome, phase]
  );

  const refreshHistory = useCallback(async () => {
    const rows = await services.getPromotionHistory();
    setHistory(rows);
  }, [services]);

  const updateField = useCallback((field: PromotionGateField, value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    setErrorText(null);
    setOutcome(null);

    if (field === "runId") {
      setEvaluation(null);
    }
  }, []);

  const evaluateGateChecks = useCallback(async () => {
    const runId = form.runId.trim();
    if (!runId) {
      setErrorText("Run id is required before evaluating gate checks.");
      return;
    }

    setBusy(true);
    setPhase("evaluating");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.evaluatePromotion(runId);
      setEvaluation(result);
    } catch (err) {
      setErrorText(toErrorMessage(err, "Evaluation failed."));
    } finally {
      setBusy(false);
      setPhase("idle");
    }
  }, [form.runId, services]);

  const promoteToPaper = useCallback(async () => {
    const validationError = validatePromotionApproval(form, evaluation);
    if (validationError) {
      setErrorText(validationError);
      setOutcome(null);
      return;
    }

    setBusy(true);
    setPhase("approving");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.approvePromotion(buildPromotionApprovalRequest(form));
      setOutcome(buildApprovalOutcome(result));
      await refreshHistory();
    } catch (err) {
      setErrorText(toErrorMessage(err, "Promotion approval failed."));
    } finally {
      setBusy(false);
      setPhase("idle");
    }
  }, [evaluation, form, refreshHistory, services]);

  const rejectPromotionDecision = useCallback(async () => {
    const validationError = validatePromotionRejection(form);
    if (validationError) {
      setErrorText(validationError);
      setOutcome(null);
      return;
    }

    setBusy(true);
    setPhase("rejecting");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.rejectPromotion(buildPromotionRejectionRequest(form));
      setOutcome(buildRejectionOutcome(result));
      await refreshHistory();

      if (result.success) {
        setForm((current) => ({ ...current, rejectionReason: "" }));
      }
    } catch (err) {
      setErrorText(toErrorMessage(err, "Promotion rejection failed."));
    } finally {
      setBusy(false);
      setPhase("idle");
    }
  }, [form, refreshHistory, services]);

  return {
    ...state,
    updateField,
    evaluateGateChecks,
    promoteToPaper,
    rejectPromotion: rejectPromotionDecision
  };
}

export function buildPromotionGateState({
  form,
  busy,
  phase,
  errorText,
  outcome,
  evaluation,
  history
}: BuildPromotionGateStateOptions): PromotionGateState {
  const trimmedForm = trimPromotionGateForm(form);
  const canEvaluate = !busy && Boolean(trimmedForm.runId);
  const canPromote = !busy && validatePromotionApproval(trimmedForm, evaluation) === null;
  const canReject = !busy && validatePromotionRejection(trimmedForm) === null;

  return {
    form,
    trimmedForm,
    evaluation,
    history,
    busy,
    phase,
    errorText,
    outcome,
    canEvaluate,
    canPromote,
    canReject,
    evaluateButtonLabel: phase === "evaluating" ? "Evaluating..." : "Evaluate gate checks",
    promoteButtonLabel: phase === "approving" ? "Promoting..." : "Confirm promote",
    rejectButtonLabel: phase === "rejecting" ? "Rejecting..." : "Reject promotion",
    nextActionText: buildNextActionText({ trimmedForm, evaluation, busy, phase }),
    approvalRequirementText: buildApprovalRequirementText(trimmedForm, evaluation),
    rejectionRequirementText: buildRejectionRequirementText(trimmedForm),
    historyEmptyText: "No promotion decisions recorded.",
    statusAnnouncement: buildPromotionStatusAnnouncement({ phase, errorText, outcome, evaluation, history })
  };
}

export function validatePromotionApproval(
  form: PromotionGateForm,
  evaluation: PromotionEvaluationResult | null
): string | null {
  const trimmedForm = trimPromotionGateForm(form);

  if (!evaluation) {
    return "Evaluate gate checks before confirming promotion.";
  }

  if (!evaluation.isEligible) {
    return evaluation.blockingReasons?.[0] ?? evaluation.reason ?? "Promotion gate is blocked.";
  }

  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Run id, operator, and approval reason are required.";
  }

  return null;
}

export function validatePromotionRejection(form: PromotionGateForm): string | null {
  const trimmedForm = trimPromotionGateForm(form);

  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.rejectionReason) {
    return "Run id, operator, and rejection reason are required.";
  }

  return null;
}

export function buildPromotionApprovalRequest(form: PromotionGateForm): ApprovePromotionRequest {
  const trimmedForm = trimPromotionGateForm(form);
  return {
    runId: trimmedForm.runId,
    approvedBy: trimmedForm.approvedBy,
    approvalReason: trimmedForm.approvalReason,
    reviewNotes: trimmedForm.reviewNotes || undefined,
    manualOverrideId: trimmedForm.manualOverrideId || undefined
  };
}

export function buildPromotionRejectionRequest(form: PromotionGateForm): RejectPromotionRequest {
  const trimmedForm = trimPromotionGateForm(form);
  return {
    runId: trimmedForm.runId,
    reason: trimmedForm.rejectionReason,
    rejectedBy: trimmedForm.approvedBy,
    reviewNotes: trimmedForm.reviewNotes || undefined,
    manualOverrideId: trimmedForm.manualOverrideId || undefined
  };
}

function trimPromotionGateForm(form: PromotionGateForm): PromotionGateForm {
  return {
    runId: form.runId.trim(),
    approvedBy: form.approvedBy.trim(),
    approvalReason: form.approvalReason.trim(),
    rejectionReason: form.rejectionReason.trim(),
    reviewNotes: form.reviewNotes.trim(),
    manualOverrideId: form.manualOverrideId.trim()
  };
}

function buildApprovalOutcome(result: PromotionDecisionResult): PromotionOutcome {
  return {
    level: result.success ? "success" : "error",
    message: result.success
      ? `Promoted. Promotion ID: ${result.promotionId ?? "n/a"}${result.auditReference ? ` · Audit reference ${result.auditReference}` : ""}`
      : result.reason
  };
}

function buildRejectionOutcome(result: PromotionDecisionResult): PromotionOutcome {
  return {
    level: result.success ? "warning" : "error",
    message: `${result.reason}${result.auditReference ? ` · Audit reference ${result.auditReference}` : ""}`
  };
}

function buildNextActionText({
  trimmedForm,
  evaluation,
  busy,
  phase
}: {
  trimmedForm: PromotionGateForm;
  evaluation: PromotionEvaluationResult | null;
  busy: boolean;
  phase: PromotionGatePhase;
}): string {
  if (busy) {
    if (phase === "evaluating") {
      return "Evaluating promotion gate checks.";
    }

    if (phase === "approving") {
      return "Writing promotion decision and refreshing audit trail.";
    }

    if (phase === "rejecting") {
      return "Writing rejection decision and refreshing audit trail.";
    }
  }

  if (!trimmedForm.runId) {
    return "Enter a backtest run id to evaluate promotion readiness.";
  }

  if (!evaluation) {
    return "Evaluate gate checks before approving or rejecting this run.";
  }

  if (!evaluation.isEligible) {
    return "Gate is blocked. Reject with rationale or review the blocking reason.";
  }

  if (!trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Add operator and approval reason before confirming promotion.";
  }

  return "Promotion trace is ready for confirmation.";
}

function buildApprovalRequirementText(
  trimmedForm: PromotionGateForm,
  evaluation: PromotionEvaluationResult | null
): string {
  if (!evaluation) {
    return "Approval remains disabled until gate checks return an eligible result.";
  }

  if (!evaluation.isEligible) {
    return evaluation.blockingReasons?.[0] ?? evaluation.reason ?? "Gate checks did not return eligible.";
  }

  if (!trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Approval requires an operator id and approval reason.";
  }

  return "Approval request includes run id, operator, rationale, and optional audit notes.";
}

function buildRejectionRequirementText(trimmedForm: PromotionGateForm): string {
  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.rejectionReason) {
    return "Rejection requires run id, operator id, and rejection reason.";
  }

  return "Rejection request is ready to write an audit-linked decision.";
}

function buildPromotionStatusAnnouncement({
  phase,
  errorText,
  outcome,
  evaluation,
  history
}: {
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
}): string {
  if (phase === "evaluating") {
    return "Evaluating promotion gate checks.";
  }

  if (phase === "approving") {
    return "Writing promotion approval.";
  }

  if (phase === "rejecting") {
    return "Writing promotion rejection.";
  }

  if (errorText) {
    return `Promotion gate failed: ${errorText}`;
  }

  if (outcome) {
    return "Promotion gate command completed.";
  }

  if (evaluation) {
    return evaluation.isEligible
      ? "Promotion gate checks returned eligible."
      : "Promotion gate checks returned blocked.";
  }

  if (history.length > 0) {
    return `${history.length} promotion decision ${history.length === 1 ? "record" : "records"} loaded.`;
  }

  return "";
}

function toErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message.trim()) {
    return err.message;
  }

  return fallback;
}
