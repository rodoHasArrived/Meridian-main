import type { OrderSubmitRequest } from "@/types";

import type {
  OrderTicketAcknowledgementState,
  OrderTicketPhase
} from "./trading-screen.view-model";

function normalizeOrderSymbol(symbol: string): string {
  return symbol.trim().toUpperCase();
}

function orderTypeRequiresPrice(type: OrderSubmitRequest["type"]): boolean {
  return type === "Limit" || type === "Stop";
}

/**
 * Operator-facing text for the order ticket. Kept beside the view model rather than in it
 * so the phase wording — including the parked outcome, which is not a failure — stays in
 * one readable place.
 */
export function buildOrderRequirementText(
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

export function buildOrderTicketAcknowledgementState(
  acknowledged: boolean,
  phase: OrderTicketPhase,
  validationError: string | null
): OrderTicketAcknowledgementState {
  const disabledReason = phase === "submitting"
    ? "Order submission is already running."
    : validationError
      ? "Complete valid order fields before acknowledging the preview."
      : null;

  return {
    id: "order-ticket-review-acknowledgement",
    label: "I reviewed the order preview and risk warnings",
    description: "Submit stays locked until the preview, position impact, and risk warnings have been reviewed.",
    checked: acknowledged,
    disabled: disabledReason !== null,
    disabledReason
  };
}

export function buildOrderTicketSubmitDisabledReason(
  phase: OrderTicketPhase,
  validationError: string | null,
  acknowledgement: OrderTicketAcknowledgementState
): string | null {
  if (phase === "submitting") {
    return "Order submission is already running.";
  }

  if (validationError) {
    return validationError;
  }

  if (!acknowledgement.checked) {
    return "Review the order preview and acknowledge before submitting.";
  }

  return null;
}

export function buildOrderTicketStatusAnnouncement({
  phase,
  errorText,
  orderId,
  escalationId,
  riskWarnings = []
}: {
  phase: OrderTicketPhase;
  errorText: string | null;
  orderId: string | null;
  escalationId?: string | null;
  riskWarnings?: string[];
}): string {
  if (phase === "submitting") {
    return "Submitting order request.";
  }

  if (phase === "parked") {
    return `Order parked for governed risk approval${escalationId ? `, escalation ${escalationId}` : ""}.`;
  }

  if (errorText) {
    return `Order submission failed: ${errorText}`;
  }

  if (phase === "submitted") {
    // The order routed, but a warning the rails raised describes exposure the operator now
    // holds — it belongs in the announcement, not only in the visible banner.
    const warningText = riskWarnings.length > 0
      ? ` ${riskWarnings.length} risk warning${riskWarnings.length === 1 ? "" : "s"}: ${riskWarnings.join(" ")}`
      : "";
    return `Order submitted${orderId ? ` with id ${orderId}` : ""}.${warningText}`;
  }

  return "";
}
