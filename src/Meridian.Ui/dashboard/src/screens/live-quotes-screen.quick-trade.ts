// Quick-trade ticket layer for the live-quotes screen: form state, validation,
// submission lifecycle, and the staged-review view models. Split from
// live-quotes-screen.view-model.ts (ADR-017 no-new-god-file ratchet); that
// module re-exports this one, so existing import paths keep working.
import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useRequestLifecycle, type RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";
import { describeApiError } from "@/lib/api-errors";
import { workflowTargetPath } from "@/lib/workspace";
import type { OrderResult, OrderSubmitRequest } from "@/types";

export type QuickTicketPhase = "idle" | "seeded" | "submitting" | "submitted" | "error";

export interface QuickTicketForm {
  side: "Buy" | "Sell";
  type: "Market" | "Limit";
  quantity: string;
  limitPrice: string;
}

export interface QuickTicketState extends QuickTicketForm {
  phase: QuickTicketPhase;
  message: string | null;
  details: string[];
  orderId: string | null;
  validationVisible?: boolean;
  acknowledged: boolean;
}

export interface QuickTicketStatusViewModel {
  id: string;
  role: "status" | "alert";
  tone: "default" | "success" | "danger";
  message: string;
  details: string[];
  showSuccessIcon: boolean;
  showErrorIcon: boolean;
  actions: QuickTicketStatusActionViewModel[];
}

export interface QuickTicketStatusActionViewModel {
  id: string;
  label: string;
  href: string;
  ariaLabel: string;
}

export interface QuickTicketCommandViewModel {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
  busyLabel: string;
  variant: "default" | "destructive";
}

export interface QuickTicketReviewAcknowledgementViewModel {
  id: string;
  label: string;
  description: string;
  checked: boolean;
  disabled: boolean;
  disabledReason: string | null;
}

export type QuickTicketField = "side" | "type" | "quantity" | "limitPrice";

export interface QuickTicketFieldViewModel {
  field: QuickTicketField;
  id: string;
  label: string;
  ariaLabel: string;
  placeholder: string | null;
  describedBy: string;
  inputMode: "numeric" | "decimal" | null;
  min: number | null;
  step: number | string | null;
  disabled: boolean;
  disabledReason: string | null;
}

export interface QuickTradeTicketViewModel {
  ticket: QuickTicketState;
  formLabel: string;
  fields: Record<QuickTicketField, QuickTicketFieldViewModel>;
  submitting: boolean;
  priceDisabled: boolean;
  quantityInvalid: boolean;
  priceInvalid: boolean;
  sideToneClass: string;
  reviewAcknowledgement: QuickTicketReviewAcknowledgementViewModel;
  submitCommand: QuickTicketCommandViewModel;
  status: QuickTicketStatusViewModel;
  seedTicket: (side: "Buy" | "Sell", price: number) => void;
  updateField: <K extends keyof QuickTicketForm>(field: K, value: QuickTicketForm[K]) => void;
  setReviewAcknowledged: (value: boolean) => void;
  submitTicket: (event: FormEvent) => Promise<void>;
  resetTicket: () => void;
  requestStatus: RequestLifecycleStatus;
}

export interface QuickTradeTicketApi {
  submitOrder: (request: OrderSubmitRequest) => Promise<OrderResult>;
}

const idleOrderSubmissionStatus: RequestLifecycleStatus = {
  operation: "quick trade order submission",
  phase: "idle",
  inFlight: false,
  version: 0,
  message: "Ready to submit order.",
  error: null,
  startedAt: null,
  settledAt: null,
  lastSucceededAt: null,
  staleDiscardCount: 0,
  backoff: { attempt: 0, retryCount: 0, nextRetryDelayMs: null, maxRetries: 0 }
};

export const initialQuickTicketState: QuickTicketState = {
  side: "Buy",
  type: "Limit",
  quantity: "",
  limitPrice: "",
  phase: "idle",
  message: null,
  details: [],
  orderId: null,
  validationVisible: false,
  acknowledged: false
};

export function useQuickTradeTicket(
  activeSymbol: string | null,
  api: QuickTradeTicketApi
): QuickTradeTicketViewModel {
  const [ticket, setTicket] = useState<QuickTicketState>(initialQuickTicketState);
  const activeSymbolRef = useRef(activeSymbol);
  const submitLifecycle = useRequestLifecycle({
    operation: "quick trade order submission",
    runningMessage: "Submitting quick trade order.",
    successMessage: "Quick trade order submitted.",
    failureMessage: "Quick trade order submission failed.",
    staleMessage: "Older quick trade submission response discarded.",
    maxRetries: 1
  });

  activeSymbolRef.current = activeSymbol;

  const resetTicket = useCallback(() => {
    submitLifecycle.invalidate();
    setTicket(initialQuickTicketState);
  }, [submitLifecycle.invalidate]);

  useEffect(() => {
    submitLifecycle.invalidate();
    setTicket(initialQuickTicketState);
  }, [activeSymbol, submitLifecycle.invalidate]);

  const seedTicket = useCallback((side: "Buy" | "Sell", price: number) => {
    const priceLabel = formatTicketPrice(price);
    const symbolLabel = activeSymbolRef.current ?? "selected symbol";
    setTicket((current) => ({
      ...current,
      side,
      type: "Limit",
      limitPrice: priceLabel,
      phase: "seeded",
      message: buildQuickTicketSeededMessage(symbolLabel, side, priceLabel),
      details: [],
      orderId: null,
      validationVisible: false,
      acknowledged: false
    }));
  }, []);

  const updateField = useCallback(<K extends keyof QuickTicketForm>(field: K, value: QuickTicketForm[K]) => {
    setTicket((current) => ({
      ...current,
      [field]: value,
      phase: resetQuickTicketFeedbackPhase(current.phase),
      message: shouldClearQuickTicketFeedbackMessage(current.phase) ? null : current.message,
      details: shouldClearQuickTicketFeedbackMessage(current.phase) ? [] : current.details,
      validationVisible: true,
      acknowledged: false
    }));
  }, []);

  const setReviewAcknowledged = useCallback((value: boolean) => {
    setTicket((current) => ({
      ...current,
      acknowledged: value,
      phase: value ? "idle" : resetQuickTicketFeedbackPhase(current.phase),
      message: value || shouldClearQuickTicketFeedbackMessage(current.phase) ? null : current.message,
      details: value || shouldClearQuickTicketFeedbackMessage(current.phase) ? [] : current.details
    }));
  }, []);

  const submitTicket = useCallback(async (event: FormEvent) => {
    event.preventDefault();
    const submitSymbol = activeSymbol;
    if (!submitSymbol) {
      return;
    }

    const validation = validateQuickTicket(ticket);
    if (validation) {
      setTicket((current) => ({
        ...current,
        phase: "error" as const,
        message: validation,
        details: [],
        orderId: null,
        validationVisible: true
      }));
      return;
    }

    if (!ticket.acknowledged) {
      setTicket((current) => ({
        ...current,
        phase: "error" as const,
        message: "Review and acknowledge the ticket before submitting.",
        details: [],
        orderId: null,
        validationVisible: false
      }));
      return;
    }

    const request = buildOrderRequest(submitSymbol, ticket);
    const token = submitLifecycle.start();
    if (!token) {
      return;
    }
    const applyCurrentSubmission = (update: (current: QuickTicketState) => QuickTicketState) => {
      if (token.isCurrent() && activeSymbolRef.current === submitSymbol) {
        token.safeSetState(setTicket, update);
      }
    };

    token.safeSetState(setTicket, (current) => ({ ...current, phase: "submitting" as const, message: null, details: [], orderId: null }));
    try {
      const result = await api.submitOrder(request);
      if (result.success) {
        applyCurrentSubmission((current) => ({
          ...current,
          phase: "submitted" as const,
          message: result.orderId ? `Order ${result.orderId} accepted.` : "Order accepted.",
          details: [],
          orderId: result.orderId,
          validationVisible: false,
          acknowledged: false
        }));
        submitLifecycle.succeed(token);
      } else {
        applyCurrentSubmission((current) => ({
          ...current,
          phase: "error" as const,
          message: result.reason ?? "Order rejected.",
          details: [],
          orderId: null,
          validationVisible: false,
          acknowledged: false
        }));
        submitLifecycle.fail(token, result.reason ?? "Order rejected.", { fallback: "Order rejected." });
      }
    } catch (error) {
      const display = describeApiError(error, "Order submission failed.");
      applyCurrentSubmission((current) => ({
        ...current,
        phase: "error" as const,
        message: display.summary,
        details: display.details,
        orderId: null,
        validationVisible: false,
        acknowledged: false
      }));
      submitLifecycle.fail(token, error, { fallback: "Order submission failed." });
    } finally {
      submitLifecycle.finish(token);
    }
  }, [activeSymbol, api, submitLifecycle.fail, submitLifecycle.finish, submitLifecycle.start, submitLifecycle.succeed, ticket]);

  return useMemo(
    () => buildQuickTradeTicketViewModel({
      activeSymbol,
      ticket,
      seedTicket,
      updateField,
      setReviewAcknowledged,
      submitTicket,
      resetTicket,
      requestStatus: submitLifecycle.status
    }),
    [activeSymbol, resetTicket, seedTicket, setReviewAcknowledged, submitLifecycle.status, submitTicket, ticket, updateField]
  );
}

export function buildQuickTradeTicketViewModel({
  activeSymbol,
  ticket,
  seedTicket,
  updateField,
  setReviewAcknowledged,
  submitTicket,
  resetTicket,
  requestStatus
}: {
  activeSymbol: string | null;
  ticket: QuickTicketState;
  seedTicket: QuickTradeTicketViewModel["seedTicket"];
  updateField: QuickTradeTicketViewModel["updateField"];
  setReviewAcknowledged: QuickTradeTicketViewModel["setReviewAcknowledged"];
  submitTicket: QuickTradeTicketViewModel["submitTicket"];
  resetTicket: QuickTradeTicketViewModel["resetTicket"];
  requestStatus?: RequestLifecycleStatus;
}): QuickTradeTicketViewModel {
  const validation = validateQuickTicket(ticket);
  const submitting = ticket.phase === "submitting";
  const surfaceValidation = shouldSurfaceQuickTicketValidation(ticket, validation);
  const symbolLabel = activeSymbol ?? "selected symbol";
  const submitLabel = buildSubmitLabel(ticket, symbolLabel);
  const statusId = "quick-ticket-status";
  const disabledReason = submitting
    ? "Order submission is already running."
    : activeSymbol === null
      ? "Select a symbol before submitting an order."
      : validation ?? (ticket.acknowledged ? null : "Review and acknowledge the ticket before submitting.");

  return {
    ticket,
    formLabel: `Quick trade ticket for ${symbolLabel}`,
    fields: buildQuickTicketFields(ticket, statusId, submitting),
    submitting,
    priceDisabled: submitting || ticket.type === "Market",
    quantityInvalid: surfaceValidation && validation !== null && validation.toLowerCase().includes("quantity"),
    priceInvalid: surfaceValidation && validation !== null && validation.toLowerCase().includes("limit price"),
    sideToneClass: ticket.side === "Buy"
      ? "bg-positive/10 text-positive border-positive/30"
      : "bg-danger/10 text-danger border-danger/30",
    reviewAcknowledgement: buildQuickTicketReviewAcknowledgement({
      activeSymbol,
      ticket,
      validation,
      submitting
    }),
    submitCommand: {
      label: submitLabel,
      ariaLabel: activeSymbol
        ? `Submit ${ticket.side.toLowerCase()} order for ${activeSymbol}`
        : "Submit order",
      disabled: disabledReason !== null,
      disabledReason,
      busy: submitting,
      busyLabel: "Submitting…",
      variant: ticket.side === "Buy" ? "default" : "destructive"
    },
    status: buildQuickTicketStatus(ticket, validation, surfaceValidation, activeSymbol),
    seedTicket,
    updateField,
    setReviewAcknowledged,
    submitTicket,
    resetTicket,
    requestStatus: requestStatus ?? idleOrderSubmissionStatus
  };
}

export function buildQuickTicketFields(
  ticket: QuickTicketForm,
  statusId = "quick-ticket-status",
  submitting = false
): Record<QuickTicketField, QuickTicketFieldViewModel> {
  const submittingReason = submitting
    ? "Order submission is in progress; wait before editing the ticket."
    : null;

  return {
    side: {
      field: "side",
      id: "quick-ticket-side",
      label: "Side",
      ariaLabel: "Order side",
      placeholder: null,
      describedBy: statusId,
      inputMode: null,
      min: null,
      step: null,
      disabled: submitting,
      disabledReason: submittingReason
    },
    type: {
      field: "type",
      id: "quick-ticket-type",
      label: "Type",
      ariaLabel: "Order type",
      placeholder: null,
      describedBy: statusId,
      inputMode: null,
      min: null,
      step: null,
      disabled: submitting,
      disabledReason: submittingReason
    },
    quantity: {
      field: "quantity",
      id: "quick-ticket-quantity",
      label: "Quantity",
      ariaLabel: "Order quantity in shares",
      placeholder: "100",
      describedBy: statusId,
      inputMode: "numeric",
      min: 1,
      step: 1,
      disabled: submitting,
      disabledReason: submittingReason
    },
    limitPrice: {
      field: "limitPrice",
      id: "quick-ticket-price",
      label: ticket.type === "Market" ? "Price (market)" : "Limit price",
      ariaLabel: ticket.type === "Market" ? "Market order price" : "Limit price",
      placeholder: ticket.type === "Market" ? "Best available" : "0.00",
      describedBy: statusId,
      inputMode: "decimal",
      min: 0,
      step: "0.01",
      disabled: submitting || ticket.type === "Market",
      disabledReason: submittingReason ?? (ticket.type === "Market"
        ? "Market orders route at the best available price."
        : null)
    }
  };
}

export function validateQuickTicket(state: QuickTicketForm): string | null {
  const qty = Number(state.quantity);
  if (!state.quantity || !Number.isFinite(qty) || qty <= 0) {
    return "Enter a quantity greater than zero.";
  }
  if (!Number.isInteger(qty)) {
    return "Quantity must be a whole number of shares.";
  }
  if (state.type === "Limit") {
    const price = Number(state.limitPrice);
    if (!state.limitPrice || !Number.isFinite(price) || price <= 0) {
      return "Enter a limit price greater than zero.";
    }
  }
  return null;
}

export function buildOrderRequest(symbol: string, ticket: QuickTicketForm): OrderSubmitRequest {
  return {
    symbol,
    side: ticket.side,
    type: ticket.type,
    quantity: Number(ticket.quantity),
    limitPrice: ticket.type === "Market" ? null : Number(ticket.limitPrice)
  };
}

function buildQuickTicketReviewAcknowledgement({
  activeSymbol,
  ticket,
  validation,
  submitting
}: {
  activeSymbol: string | null;
  ticket: QuickTicketState;
  validation: string | null;
  submitting: boolean;
}): QuickTicketReviewAcknowledgementViewModel {
  const disabledReason = submitting
    ? "Order submission is in progress."
    : activeSymbol === null
      ? "Select a symbol before acknowledging the ticket."
      : validation;
  const symbolLabel = activeSymbol ?? "selected symbol";
  const orderDescription = validation
    ? "Complete the required ticket fields before acknowledging the order."
    : `${ticket.side} ${ticket.quantity} ${symbolLabel} as a ${ticket.type.toLowerCase()} order${ticket.type === "Limit" ? ` at ${ticket.limitPrice}` : " at market"}.`;

  return {
    id: "quick-ticket-review-acknowledgement",
    label: "I reviewed this order ticket",
    description: orderDescription,
    checked: ticket.acknowledged,
    disabled: disabledReason !== null,
    disabledReason
  };
}

export function formatTicketPrice(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return "";
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 4,
    useGrouping: false
  });
}

function buildSubmitLabel(ticket: QuickTicketState, symbol: string): string {
  if (ticket.phase === "submitting") {
    return "Submitting…";
  }

  return `${ticket.side} ${symbol}${ticket.type === "Limit" && ticket.limitPrice ? ` @ ${ticket.limitPrice}` : ""}`;
}

function buildQuickTicketStatus(
  ticket: QuickTicketState,
  validation: string | null,
  surfaceValidation: boolean,
  activeSymbol: string | null
): QuickTicketStatusViewModel {
  if (ticket.phase === "submitted" && ticket.message) {
    return {
      id: "quick-ticket-status",
      role: "status",
      tone: "success",
      message: ticket.message,
      details: [],
      showSuccessIcon: true,
      showErrorIcon: false,
      actions: [buildQuickTicketReadinessAction("accepted", activeSymbol, ticket.orderId)]
    };
  }

  if (ticket.phase === "error" && ticket.message) {
    return {
      id: "quick-ticket-status",
      role: "alert",
      tone: "danger",
      message: ticket.message,
      details: ticket.details,
      showSuccessIcon: false,
      showErrorIcon: true,
      actions: isQuickTicketSubmissionFailure(ticket, surfaceValidation)
        ? [buildQuickTicketReadinessAction("rejected", activeSymbol, ticket.orderId)]
        : []
    };
  }

  if (surfaceValidation && validation) {
    return {
      id: "quick-ticket-status",
      role: "alert",
      tone: "danger",
      message: validation,
      details: [],
      showSuccessIcon: false,
      showErrorIcon: true,
      actions: []
    };
  }

  if (ticket.phase === "seeded" && ticket.message) {
    return {
      id: "quick-ticket-status",
      role: "status",
      tone: "success",
      message: ticket.message,
      details: [],
      showSuccessIcon: true,
      showErrorIcon: false,
      actions: []
    };
  }

  return {
    id: "quick-ticket-status",
    role: "status",
    tone: "default",
    message: buildQuickTicketGuidance(ticket, validation),
    details: [],
    showSuccessIcon: false,
    showErrorIcon: false,
    actions: []
  };
}

function buildQuickTicketReadinessAction(
  outcome: "accepted" | "rejected",
  activeSymbol: string | null,
  orderId: string | null
): QuickTicketStatusActionViewModel {
  const symbolLabel = activeSymbol ?? "selected symbol";
  const route = workflowTargetPath("TradingReadiness", "trading");
  const orderLabel = orderId ? `order ${orderId}` : `${symbolLabel} order`;

  return {
    id: "trading-readiness",
    label: "Review readiness",
    href: route,
    ariaLabel: outcome === "accepted"
      ? `Open Trading readiness after ${orderLabel} was accepted`
      : `Open Trading readiness after ${symbolLabel} order submission failed`
  };
}

function isQuickTicketSubmissionFailure(
  ticket: QuickTicketState,
  surfaceValidation: boolean
): boolean {
  return ticket.phase === "error"
    && ticket.message !== null
    && !surfaceValidation
    && ticket.message !== "Review and acknowledge the ticket before submitting.";
}

function shouldSurfaceQuickTicketValidation(ticket: QuickTicketState, validation: string | null): boolean {
  if (!validation) {
    return false;
  }

  return ticket.validationVisible === true || (ticket.phase === "error" && ticket.message === validation);
}

function resetQuickTicketFeedbackPhase(phase: QuickTicketPhase): QuickTicketPhase {
  return phase === "seeded" || phase === "submitted" || phase === "error" ? "idle" : phase;
}

function shouldClearQuickTicketFeedbackMessage(phase: QuickTicketPhase): boolean {
  return phase === "seeded" || phase === "submitted" || phase === "error";
}

function buildQuickTicketSeededMessage(symbol: string, side: "Buy" | "Sell", priceLabel: string): string {
  const action = side.toLowerCase();
  const renderedPrice = priceLabel || "the selected price";
  return `Seeded ${action} ${symbol} limit ticket at ${renderedPrice}. Enter quantity, then acknowledge before submitting.`;
}

function buildQuickTicketGuidance(ticket: QuickTicketState, validation: string | null): string {
  if (ticket.phase === "submitting") {
    return "Submitting order to Meridian execution controls.";
  }

  if (!validation) {
    return ticket.acknowledged
      ? "Orders route through Meridian's pre-trade risk and execution controls."
      : "Review side, quantity, and price, then acknowledge before submitting.";
  }

  const lower = validation.toLowerCase();
  if (lower.includes("quantity")) {
    return "Enter a quantity to enable order submission.";
  }

  if (lower.includes("limit price")) {
    return "Enter a limit price to enable order submission.";
  }

  return "Complete the required ticket fields to enable order submission.";
}
