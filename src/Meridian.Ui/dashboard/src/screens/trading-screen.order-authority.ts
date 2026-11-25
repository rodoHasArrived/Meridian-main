/**
 * Order-authority presentation for the trading screen (governed order command boundary).
 *
 * The OMS decides authority; these helpers only turn its decisions into ticket and blotter text.
 */

import type { AuthorityDecisionSummary, OrderCommandOrigin, TradingManualOrderEntry } from "@/types";

const originLabels: Record<OrderCommandOrigin, string> = {
  Unattributed: "Untyped",
  OperatorTicket: "Ticket",
  PositionAction: "Position",
  EscalationRelease: "Release",
  StrategyRun: "Strategy",
  KillSwitch: "Kill switch"
};

/** Blotter label for an order's origin. Orders created outside order submission have none. */
export function orderOriginLabel(origin: OrderCommandOrigin | null | undefined): string {
  return origin ? originLabels[origin] ?? origin : "—";
}

/**
 * Ticket text for an order-authority refusal, or null when the refusal came from another gate
 * (the caller then shows the server's plain error message).
 */
export function formatAuthorityRefusal(decision: AuthorityDecisionSummary | null | undefined): string | null {
  if (!decision || decision.isAuthorized) return null;
  const where = decision.checkpoint === "dispatch" ? " just before dispatch" : "";
  const code = decision.reasonCode ? ` (${decision.reasonCode})` : "";
  const reason = decision.reason?.trim() || "The order's authority did not permit routing.";
  return `Order authority refused this order${where}${code}: ${reason}`;
}

/**
 * Why the ticket cannot submit manual orders on the active routing path, or null when it can (or
 * the server did not say, in which case the ticket keeps its other checks).
 */
export function manualOrderEntryDisabledReason(entry: TradingManualOrderEntry | null | undefined): string | null {
  if (!entry || entry.available) return null;
  return entry.reason?.trim() || "Manual orders are not available on the active routing path.";
}
