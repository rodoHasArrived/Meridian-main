import type { OrderSubmitRequest } from "@/types";
import { normalizeFundAccountGuid } from "@/lib/fund-account-scope";
import { parsePositiveDecimal } from "@/lib/positive-decimal";
import {
  buildOrderRequirementText,
  buildOrderTicketAcknowledgementState,
  buildOrderTicketStatusAnnouncement,
  buildOrderTicketSubmitDisabledReason
} from "./trading-screen.order-ticket-text";

export type OrderTicketField = "symbol" | "side" | "type" | "quantity" | "limitPrice";
export type OrderTicketPhase = "idle" | "submitting" | "submitted" | "parked" | "error";

export interface OrderTicketFieldControl {
  field: OrderTicketField;
  id: string;
  label: string;
  value: string;
  describedBy: string;
  ariaLabel: string;
  invalid: boolean;
  required: boolean;
}

export interface OrderTicketSelectControl extends OrderTicketFieldControl {
  options: Array<{ value: string; label: string }>;
}

export interface OrderTicketControls {
  symbol: OrderTicketFieldControl;
  side: OrderTicketSelectControl;
  type: OrderTicketSelectControl;
  quantity: OrderTicketFieldControl;
  limitPrice: OrderTicketFieldControl | null;
}

export interface OrderTicketAcknowledgementState {
  id: string;
  label: string;
  description: string;
  checked: boolean;
  disabled: boolean;
  disabledReason: string | null;
}

export interface OrderTicketState {
  form: OrderSubmitRequest;
  open: boolean;
  phase: OrderTicketPhase;
  orderId: string | null;
  /** Governed-approval queue entry holding a parked order, when phase is "parked". */
  escalationId: string | null;
  /** Non-blocking risk warnings the rails raised for the last submitted order. */
  riskWarnings: string[];
  /** Operator-facing text for a parked order; null in every other phase. */
  parkedText: string | null;
  errorText: string | null;
  validationError: string | null;
  invalidField: OrderTicketField | null;
  canSubmit: boolean;
  canClose: boolean;
  requiresLimitPrice: boolean;
  priceLabel: string;
  formId: string;
  requirementId: string;
  controls: OrderTicketControls;
  openButtonLabel: string;
  submitButtonLabel: string;
  submitAriaLabel: string;
  submitDisabledReason: string | null;
  submitBusy: boolean;
  submitBusyLabel: string | null;
  closeButtonLabel: string;
  closeAriaLabel: string;
  closeDisabledReason: string | null;
  acknowledgement: OrderTicketAcknowledgementState;
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
  escalationId?: string | null;
  riskWarnings?: string[];
  acknowledged?: boolean;
}

export const emptyOrderTicketForm: OrderSubmitRequest = {
  symbol: "",
  side: "Buy",
  type: "Market",
  quantity: 0,
  limitPrice: null
};

export function buildOrderTicketState({
  form,
  open,
  phase,
  orderId,
  errorText,
  escalationId = null,
  riskWarnings = [],
  acknowledged = false
}: BuildOrderTicketStateOptions): OrderTicketState {
  const validationError = validateOrderTicketForm(form);
  const requiresLimitPrice = orderTypeRequiresPrice(form.type);
  const successText = phase === "submitted"
    ? `Order submitted${orderId ? ` - ${orderId}` : ""}.`
    : null;
  const parkedText = phase === "parked"
    ? `Order parked for governed risk approval${escalationId ? ` - escalation ${escalationId}` : ""}. `
      + "It routes only once the risk desk approves it."
    : null;
  const invalidField = getOrderTicketInvalidField(form);
  const requirementId = "order-ticket-requirements";
  const acknowledgement = buildOrderTicketAcknowledgementState(acknowledged, phase, validationError);
  const submitDisabledReason = buildOrderTicketSubmitDisabledReason(phase, validationError, acknowledgement);
  const closeDisabledReason = phase === "submitting" ? "Order submission is in progress." : null;

  return {
    form,
    open,
    phase,
    orderId,
    escalationId,
    riskWarnings,
    parkedText,
    errorText,
    validationError,
    invalidField,
    canSubmit: submitDisabledReason === null,
    canClose: phase !== "submitting",
    requiresLimitPrice,
    priceLabel: `${form.type} price`,
    formId: "trading-order-ticket",
    requirementId,
    controls: buildOrderTicketControls(form, invalidField, requirementId),
    openButtonLabel: open ? "Close order ticket" : "New order",
    submitButtonLabel: phase === "submitting" ? "Submitting…" : "Submit order",
    submitAriaLabel: phase === "submitting" ? "Submitting order request" : "Submit order request",
    submitDisabledReason,
    submitBusy: phase === "submitting",
    submitBusyLabel: phase === "submitting" ? "Submitting…" : null,
    closeButtonLabel: "Cancel",
    closeAriaLabel: closeDisabledReason ?? "Close order ticket without submitting",
    closeDisabledReason,
    acknowledgement,
    requirementText: buildOrderRequirementText(form, phase, validationError),
    successText,
    statusAnnouncement: buildOrderTicketStatusAnnouncement({ phase, errorText, orderId, escalationId, riskWarnings })
  };
}

function buildOrderTicketControls(
  form: OrderSubmitRequest,
  invalidField: OrderTicketField | null,
  requirementId: string
): OrderTicketControls {
  return {
    symbol: {
      field: "symbol",
      id: "order-ticket-symbol",
      label: "Symbol",
      value: form.symbol,
      describedBy: requirementId,
      ariaLabel: "Order symbol",
      invalid: invalidField === "symbol",
      required: true
    },
    side: {
      field: "side",
      id: "order-ticket-side",
      label: "Side",
      value: form.side,
      describedBy: requirementId,
      ariaLabel: "Order side",
      invalid: false,
      required: true,
      options: [
        { value: "Buy", label: "Buy" },
        { value: "Sell", label: "Sell" }
      ]
    },
    type: {
      field: "type",
      id: "order-ticket-type",
      label: "Type",
      value: form.type,
      describedBy: requirementId,
      ariaLabel: "Order type",
      invalid: false,
      required: true,
      options: [
        { value: "Market", label: "Market" },
        { value: "Limit", label: "Limit" },
        { value: "Stop", label: "Stop" }
      ]
    },
    quantity: {
      field: "quantity",
      id: "order-ticket-quantity",
      label: "Quantity",
      value: form.quantity ? String(form.quantity) : "",
      describedBy: requirementId,
      ariaLabel: "Order quantity",
      invalid: invalidField === "quantity",
      required: true
    },
    limitPrice: orderTypeRequiresPrice(form.type)
      ? {
          field: "limitPrice",
          id: "order-ticket-limit-price",
          label: `${form.type} price`,
          value: form.limitPrice ? String(form.limitPrice) : "",
          describedBy: requirementId,
          ariaLabel: `${form.type} order price`,
          invalid: invalidField === "limitPrice",
          required: true
        }
      : null
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
    return { ...current, quantity: parsePositiveDecimal(value) ?? 0 };
  }

  return { ...current, limitPrice: parsePositiveDecimal(value) };
}

export function buildOrderSubmitRequest(form: OrderSubmitRequest, fundAccountId?: string | null): OrderSubmitRequest {
  const request: OrderSubmitRequest = {
    symbol: normalizeOrderSymbol(form.symbol),
    side: form.side,
    type: form.type,
    quantity: form.quantity
  };
  const normalizedFundAccountId = normalizeFundAccountGuid(fundAccountId ?? form.fundAccountId);

  if (orderTypeRequiresPrice(form.type)) {
    request.limitPrice = form.limitPrice;
  }

  if (normalizedFundAccountId) {
    request.fundAccountId = normalizedFundAccountId;
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

export function normalizeOrderSymbol(symbol: string): string {
  return symbol.trim().toUpperCase();
}

function orderTypeRequiresPrice(type: OrderSubmitRequest["type"]): boolean {
  return type === "Limit" || type === "Stop";
}
