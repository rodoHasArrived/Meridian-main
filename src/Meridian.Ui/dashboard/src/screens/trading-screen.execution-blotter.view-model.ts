/**
 * Presentation logic for the broker-side execution blotter.
 *
 * The blotter's whole value is provenance: the same shape carries a live broker
 * book and a paper simulation, so every derived label below leads with which one
 * the operator is reading rather than presenting the rows unqualified.
 */

import type {
  ExecutionAccountSnapshot,
  ExecutionBlotterPosition,
  ExecutionBlotterSnapshot,
  ExecutionGatewayHealth
} from "@/types/execution-blotter.types";

export type ExecutionBlotterTone = "default" | "success" | "warning" | "danger";

/** Why a read is unavailable, kept apart from "the book is empty". */
export type ExecutionReadState = "loading" | "ready" | "inactive" | "error";

export interface ExecutionBlotterMetric {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: ExecutionBlotterTone;
}

export interface ExecutionBlotterRow {
  positionKey: string;
  symbol: string;
  product: string;
  side: string;
  quantity: string;
  averageCostBasis: string;
  marketPrice: string;
  marketValue: string;
  unrealisedPnl: string;
  unrealisedTone: ExecutionBlotterTone;
  assetClass: string;
  contractDetail: string | null;
  canUpsize: boolean;
  ariaLabel: string;
}

export interface ExecutionProvenance {
  label: string;
  detail: string;
  tone: ExecutionBlotterTone;
}

/**
 * Provenance of the rows on screen. A paper book presented without that word is
 * the one failure this panel must never have, so an unloaded snapshot says so
 * rather than defaulting to either side.
 */
export function buildExecutionProvenance(snapshot: ExecutionBlotterSnapshot | null): ExecutionProvenance {
  if (!snapshot) {
    return {
      label: "Provenance unknown",
      detail: "The blotter has not loaded, so these rows cannot be attributed to a broker or a simulation.",
      tone: "warning"
    };
  }

  const origin = snapshot.isBrokerBacked ? "Broker book" : "Simulated book";
  const mode = snapshot.isLive ? "live" : "not live";
  return {
    label: `${origin} · ${mode}`,
    detail: `${snapshot.source}. ${snapshot.statusMessage}`,
    tone: snapshot.isBrokerBacked && snapshot.isLive ? "success" : "warning"
  };
}

export function buildExecutionBlotterMetrics(
  health: ExecutionGatewayHealth | null,
  account: ExecutionAccountSnapshot | null
): ExecutionBlotterMetric[] {
  return [
    {
      id: "gateway",
      label: "Execution gateway",
      value: health === null ? "—" : health.brokerName,
      detail: health === null
        ? "Gateway health has not loaded."
        : `${health.mode}${health.selectedGatewayId ? ` · ${health.selectedGatewayId}` : ""} · ${health.isAvailable ? "available" : "unavailable"}`,
      tone: health === null ? "default" : health.isAvailable ? "success" : "danger"
    },
    {
      id: "cash",
      label: "Cash",
      value: account === null ? "—" : formatCurrency(account.cash),
      detail: account === null ? "Account snapshot has not loaded." : `Portfolio value ${formatCurrency(account.portfolioValue)}`,
      tone: "default"
    },
    {
      id: "unrealised",
      label: "Unrealised P&L",
      value: account === null ? "—" : formatSignedCurrency(account.unrealisedPnl),
      detail: account === null ? "Account snapshot has not loaded." : `Realised ${formatSignedCurrency(account.realisedPnl)}`,
      tone: account === null ? "default" : pnlTone(account.unrealisedPnl)
    },
    {
      id: "positions",
      label: "Open positions",
      value: account === null ? "—" : String(account.positionCount),
      detail: account === null ? "Account snapshot has not loaded." : `As of ${account.asOf}`,
      tone: "default"
    }
  ];
}

export function buildExecutionBlotterRow(position: ExecutionBlotterPosition): ExecutionBlotterRow {
  const contractDetail = [
    position.expiration ? `exp ${position.expiration}` : null,
    position.strike === null || position.strike === undefined ? null : `strike ${formatNumber(position.strike)}`,
    position.right ?? null
  ].filter((part): part is string => Boolean(part)).join(" · ") || null;

  return {
    positionKey: position.positionKey,
    symbol: position.symbol,
    product: position.productDescription,
    side: position.side,
    quantity: formatNumber(position.quantity),
    averageCostBasis: formatCurrency(position.averageCostBasis),
    marketPrice: formatCurrency(position.marketPrice),
    marketValue: formatCurrency(position.marketValue),
    unrealisedPnl: formatSignedCurrency(position.unrealisedPnl),
    unrealisedTone: pnlTone(position.unrealisedPnl),
    assetClass: position.assetClass,
    contractDetail,
    // The server decides which positions an upsize applies to; absence of the
    // flag is treated as "not offered" rather than assumed to be allowed.
    canUpsize: position.supportsUpsize === true,
    ariaLabel: `${position.side} ${formatNumber(position.quantity)} ${position.symbol}, market value ${formatCurrency(position.marketValue)}, unrealised ${formatSignedCurrency(position.unrealisedPnl)}`
  };
}

/** Copy for the empty body, which must not read as an empty book when a read failed. */
export function executionBlotterEmptyMessage(state: ExecutionReadState): string {
  switch (state) {
    case "loading":
      return "Loading the execution blotter…";
    case "inactive":
      return "Execution services are not active on this host, so there is no broker book to show.";
    case "error":
      return "The execution blotter could not be read. Retry, or confirm the gateway is reachable.";
    default:
      return "No open positions in the execution book.";
  }
}

function pnlTone(value: number): ExecutionBlotterTone {
  if (value > 0) {
    return "success";
  }

  return value < 0 ? "danger" : "default";
}

function formatCurrency(value: number): string {
  return value.toLocaleString(undefined, { style: "currency", currency: "USD", maximumFractionDigits: 2 });
}

function formatSignedCurrency(value: number): string {
  return `${value > 0 ? "+" : ""}${formatCurrency(value)}`;
}

function formatNumber(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: 4 });
}
