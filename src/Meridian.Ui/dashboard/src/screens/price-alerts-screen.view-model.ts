import type { PriceAlertCondition, PriceAlertDraft, PriceAlertField } from "@/lib/price-alerts/types";

export interface PriceAlertFormState {
  symbol: string;
  condition: PriceAlertCondition;
  field: PriceAlertField;
  threshold: string;
  note: string;
}

export const DEFAULT_PRICE_ALERT_FORM: PriceAlertFormState = {
  symbol: "",
  condition: "above",
  field: "last",
  threshold: "",
  note: ""
};

export interface PriceAlertFormValidation {
  symbolError: string | null;
  thresholdError: string | null;
  canSubmit: boolean;
}

export function validatePriceAlertForm(form: PriceAlertFormState): PriceAlertFormValidation {
  const symbol = form.symbol.trim().toUpperCase();
  const symbolError = !symbol
    ? "Enter a symbol."
    : !/^[A-Z0-9./:_-]{1,16}$/.test(symbol)
      ? "Use 1-16 letters, digits, or . / : _ -"
      : null;

  const thresholdValue = parseThreshold(form.threshold);
  const thresholdError = thresholdValue === null
    ? "Enter a price threshold greater than 0."
    : thresholdValue <= 0
      ? "Threshold must be greater than 0."
      : null;

  return {
    symbolError,
    thresholdError,
    canSubmit: !symbolError && !thresholdError
  };
}

export function priceAlertDraftFromForm(form: PriceAlertFormState): PriceAlertDraft | null {
  const validation = validatePriceAlertForm(form);
  if (!validation.canSubmit) {
    return null;
  }
  const threshold = parseThreshold(form.threshold);
  if (threshold === null) {
    return null;
  }
  return {
    symbol: form.symbol.trim().toUpperCase(),
    condition: form.condition,
    field: form.field,
    threshold,
    note: form.note.trim() || null
  };
}

export function parseThreshold(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }
  const parsed = Number(trimmed.replace(/,/g, ""));
  return Number.isFinite(parsed) ? parsed : null;
}

export const PRICE_ALERT_CONDITION_OPTIONS: Array<{ value: PriceAlertCondition; label: string; helper: string }> = [
  { value: "above", label: "At or above", helper: "Fires whenever price is at or above the threshold." },
  { value: "below", label: "At or below", helper: "Fires whenever price is at or below the threshold." },
  { value: "crosses-up", label: "Crosses up through", helper: "Fires once when price rises through the threshold." },
  { value: "crosses-down", label: "Crosses down through", helper: "Fires once when price falls through the threshold." }
];

export const PRICE_ALERT_FIELD_OPTIONS: Array<{ value: PriceAlertField; label: string }> = [
  { value: "last", label: "Last trade" },
  { value: "bid", label: "Bid" },
  { value: "ask", label: "Ask" },
  { value: "mid", label: "Mid" }
];

export function formatPriceAlertPrice(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "—";
  }
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 4
  });
}

export function formatPriceAlertTimestamp(value: string | null | undefined): string {
  if (!value) {
    return "—";
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "—";
  }
  return date.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  });
}
