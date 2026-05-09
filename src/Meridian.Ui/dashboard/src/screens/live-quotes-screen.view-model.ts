import { useCallback, useMemo, useState, type FormEvent } from "react";
import type { OrderResult, OrderSubmitRequest } from "@/types";

export type QuickTicketPhase = "idle" | "submitting" | "submitted" | "error";

export interface QuickTicketForm {
  side: "Buy" | "Sell";
  type: "Market" | "Limit";
  quantity: string;
  limitPrice: string;
}

export interface QuickTicketState extends QuickTicketForm {
  phase: QuickTicketPhase;
  message: string | null;
  orderId: string | null;
}

export interface QuickTicketStatusViewModel {
  id: string;
  role: "status" | "alert";
  tone: "default" | "success" | "danger";
  message: string;
  showSuccessIcon: boolean;
  showErrorIcon: boolean;
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

export interface QuickTradeTicketViewModel {
  ticket: QuickTicketState;
  submitting: boolean;
  priceDisabled: boolean;
  quantityInvalid: boolean;
  priceInvalid: boolean;
  sideToneClass: string;
  submitCommand: QuickTicketCommandViewModel;
  status: QuickTicketStatusViewModel;
  seedTicket: (side: "Buy" | "Sell", price: number) => void;
  updateField: <K extends keyof QuickTicketForm>(field: K, value: QuickTicketForm[K]) => void;
  submitTicket: (event: FormEvent) => Promise<void>;
  resetTicket: () => void;
}

export interface QuickTradeTicketApi {
  submitOrder: (request: OrderSubmitRequest) => Promise<OrderResult>;
}

export const initialQuickTicketState: QuickTicketState = {
  side: "Buy",
  type: "Limit",
  quantity: "",
  limitPrice: "",
  phase: "idle",
  message: null,
  orderId: null
};

export function useQuickTradeTicket(
  activeSymbol: string | null,
  api: QuickTradeTicketApi
): QuickTradeTicketViewModel {
  const [ticket, setTicket] = useState<QuickTicketState>(initialQuickTicketState);

  const resetTicket = useCallback(() => {
    setTicket(initialQuickTicketState);
  }, []);

  const seedTicket = useCallback((side: "Buy" | "Sell", price: number) => {
    setTicket((current) => ({
      ...current,
      side,
      type: "Limit",
      limitPrice: formatTicketPrice(price),
      phase: "idle",
      message: null,
      orderId: null
    }));
  }, []);

  const updateField = useCallback(<K extends keyof QuickTicketForm>(field: K, value: QuickTicketForm[K]) => {
    setTicket((current) => ({
      ...current,
      [field]: value,
      phase: current.phase === "submitted" ? "idle" : current.phase,
      message: current.phase === "error" ? null : current.message
    }));
  }, []);

  const submitTicket = useCallback(async (event: FormEvent) => {
    event.preventDefault();
    if (!activeSymbol) {
      return;
    }

    const validation = validateQuickTicket(ticket);
    if (validation) {
      setTicket((current) => ({ ...current, phase: "error", message: validation, orderId: null }));
      return;
    }

    const request = buildOrderRequest(activeSymbol, ticket);

    setTicket((current) => ({ ...current, phase: "submitting", message: null, orderId: null }));
    try {
      const result = await api.submitOrder(request);
      if (result.success) {
        setTicket((current) => ({
          ...current,
          phase: "submitted",
          message: result.orderId ? `Order ${result.orderId} accepted.` : "Order accepted.",
          orderId: result.orderId
        }));
      } else {
        setTicket((current) => ({
          ...current,
          phase: "error",
          message: result.reason ?? "Order rejected.",
          orderId: null
        }));
      }
    } catch (error) {
      setTicket((current) => ({
        ...current,
        phase: "error",
        message: error instanceof Error && error.message ? error.message : "Order submission failed.",
        orderId: null
      }));
    }
  }, [activeSymbol, api, ticket]);

  return useMemo(
    () => buildQuickTradeTicketViewModel({
      activeSymbol,
      ticket,
      seedTicket,
      updateField,
      submitTicket,
      resetTicket
    }),
    [activeSymbol, resetTicket, seedTicket, submitTicket, ticket, updateField]
  );
}

export function buildQuickTradeTicketViewModel({
  activeSymbol,
  ticket,
  seedTicket,
  updateField,
  submitTicket,
  resetTicket
}: {
  activeSymbol: string | null;
  ticket: QuickTicketState;
  seedTicket: QuickTradeTicketViewModel["seedTicket"];
  updateField: QuickTradeTicketViewModel["updateField"];
  submitTicket: QuickTradeTicketViewModel["submitTicket"];
  resetTicket: QuickTradeTicketViewModel["resetTicket"];
}): QuickTradeTicketViewModel {
  const validation = validateQuickTicket(ticket);
  const submitting = ticket.phase === "submitting";
  const symbolLabel = activeSymbol ?? "selected symbol";
  const submitLabel = buildSubmitLabel(ticket, symbolLabel);
  const disabledReason = submitting
    ? "Order submission is already running."
    : activeSymbol === null
      ? "Select a symbol before submitting an order."
      : validation;

  return {
    ticket,
    submitting,
    priceDisabled: ticket.type === "Market",
    quantityInvalid: validation !== null && validation.toLowerCase().includes("quantity"),
    priceInvalid: validation !== null && validation.toLowerCase().includes("limit price"),
    sideToneClass: ticket.side === "Buy"
      ? "bg-positive/10 text-positive border-positive/30"
      : "bg-danger/10 text-danger border-danger/30",
    submitCommand: {
      label: submitLabel,
      ariaLabel: activeSymbol
        ? `Submit ${ticket.side.toLowerCase()} order for ${activeSymbol}`
        : "Submit order",
      disabled: disabledReason !== null,
      disabledReason,
      busy: submitting,
      busyLabel: "Submitting...",
      variant: ticket.side === "Buy" ? "default" : "destructive"
    },
    status: buildQuickTicketStatus(ticket, validation),
    seedTicket,
    updateField,
    submitTicket,
    resetTicket
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
    return "Submitting...";
  }

  return `${ticket.side} ${symbol}${ticket.type === "Limit" && ticket.limitPrice ? ` @ ${ticket.limitPrice}` : ""}`;
}

function buildQuickTicketStatus(ticket: QuickTicketState, validation: string | null): QuickTicketStatusViewModel {
  if (ticket.phase === "submitted" && ticket.message) {
    return {
      id: "quick-ticket-status",
      role: "status",
      tone: "success",
      message: ticket.message,
      showSuccessIcon: true,
      showErrorIcon: false
    };
  }

  if (ticket.phase === "error" && ticket.message) {
    return {
      id: "quick-ticket-status",
      role: "alert",
      tone: "danger",
      message: ticket.message,
      showSuccessIcon: false,
      showErrorIcon: true
    };
  }

  return {
    id: "quick-ticket-status",
    role: validation ? "alert" : "status",
    tone: validation ? "danger" : "default",
    message: validation ?? "Orders route through Meridian's pre-trade risk and execution controls.",
    showSuccessIcon: false,
    showErrorIcon: validation !== null
  };
}
