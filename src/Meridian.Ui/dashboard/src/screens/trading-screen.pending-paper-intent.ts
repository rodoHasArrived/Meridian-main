import type { OrderSubmitRequest } from "@/types";

export const unresolvedPaperIntentMessage =
  "The submission response was unavailable. Check this intent before replacing the order.";

export function readPendingPaperIntent(key: string | null, fundAccountId?: string | null): OrderSubmitRequest | null {
  if (!key || !fundAccountId) return null;

  try {
    const request = JSON.parse(window.sessionStorage.getItem(key) ?? "null") as OrderSubmitRequest | null;
    if (request?.fundAccountId === fundAccountId && request.type === "Market" &&
        (request.side === "Buy" || request.side === "Sell") && Number.isSafeInteger(request.quantity) &&
        request.quantity > 0 && typeof request.symbol === "string" && request.symbol.length > 0 &&
        typeof request.intentId === "string" && /^[0-9a-f-]{36}$/i.test(request.intentId)) {
      return request;
    }
  } catch {
    // A fresh intent is saved successfully before it can be submitted.
  }
  return null;
}

export function clearPendingPaperIntent(key: string | null) {
  if (!key) return;
  try {
    window.sessionStorage.removeItem(key);
  } catch {
    // A retained receipt still makes a repeated check safe.
  }
}
