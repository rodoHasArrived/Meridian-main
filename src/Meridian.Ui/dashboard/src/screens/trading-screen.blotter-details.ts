// Detail-pane content for the row selected in each Trading blotter table (positions, open orders,
// recent fills): the heading, the summary sentence, the field rows and which of those rows the
// table already shows. Kept beside, not inside, the screen view model.
import { markFreshnessFields } from "@/lib/mark-freshness";
import type {
  TradingBlotterDetail,
  TradingDataTone,
  TradingFillRow,
  TradingOrderRow,
  TradingPositionRow
} from "@/screens/trading-screen.view-model";
import type { TradingRiskState } from "@/types";

export function buildPositionDetail(
  row: TradingPositionRow,
  risk: TradingRiskState | null
): TradingBlotterDetail {
  const guardrailCount = risk?.activeGuardrails.length ?? 0;
  const riskText = risk ? `${risk.state}: ${risk.summary}` : "Risk context unavailable.";

  return {
    id: "selected-position",
    objectType: "Position",
    title: row.symbol,
    subtitle: `${row.side} · ${row.quantity} shares`,
    statusLabel: row.markFreshness.reviewRequired ? "Review required" : risk?.state ?? "Position",
    statusTone: row.markFreshness.reviewRequired ? "warning" : riskStateTone(risk?.state, row.unrealizedPnlTone),
    ariaLabel: `Position detail for ${row.symbol}`,
    detail: `${row.markFreshness.reason} Recorded exposure ${row.exposure}, unrealized P&L ${row.unrealizedPnl}. ${riskText}`,
    fields: [
      { label: "Side", value: row.side, tone: "default" },
      { label: "Quantity", value: row.quantity, tone: "default" },
      { label: "Average price", value: row.averagePrice, tone: "muted" },
      { label: "Recorded mark price", value: row.markPrice, tone: "muted" },
      ...markFreshnessFields(row.markFreshness),
      { label: "Day P&L", value: row.dayPnl, tone: row.dayPnlTone },
      { label: "Unrealized P&L", value: row.unrealizedPnl, tone: row.unrealizedPnlTone },
      { label: "Exposure", value: row.exposure, tone: "default" },
      { label: "Risk", value: risk?.state ?? "Unavailable", tone: riskFieldTone(risk?.state) },
      { label: "Guardrails", value: guardrailCount > 0 ? `${guardrailCount} active` : "None active", tone: guardrailCount > 0 ? "warning" : "success" }
    ],
    // The positions table always shows these; its other columns can be hidden.
    shownInRow: ["Side", "Mark readiness"]
  };
}

export function buildOrderDetail(row: TradingOrderRow, provider: string | null): TradingBlotterDetail {
  return {
    id: "selected-order",
    objectType: "Order",
    title: row.orderId,
    subtitle: `${row.side} ${row.quantity} ${row.symbol}`,
    statusLabel: row.status,
    statusTone: row.statusTone,
    ariaLabel: `Order detail for ${row.orderId}`,
    detail: `${row.type} order submitted ${row.submittedAt}. ${provider ? `${provider} is the active execution adapter.` : "Execution adapter context unavailable."}`,
    fields: [
      { label: "Symbol", value: row.symbol, tone: "default" },
      { label: "Side", value: row.side, tone: "default" },
      { label: "Type", value: row.type, tone: "default" },
      { label: "Quantity", value: row.quantity, tone: "default" },
      { label: "Limit", value: row.limitPrice || "Market", tone: "muted" },
      { label: "Status", value: row.status, tone: row.statusTone },
      { label: "Submitted", value: row.submittedAt, tone: "muted" },
      { label: "Provider", value: provider ?? "Unavailable", tone: "muted" }
    ],
    // Quantity and limit columns can be hidden, so the pane keeps them.
    shownInRow: ["Symbol", "Side", "Type", "Status", "Submitted"]
  };
}

export function buildFillDetail(row: TradingFillRow, provider: string | null): TradingBlotterDetail {
  return {
    id: "selected-fill",
    objectType: "Fill",
    title: row.fillId,
    subtitle: `${row.side} ${row.quantity} ${row.symbol}`,
    statusLabel: "Fill evidence",
    statusTone: "success",
    ariaLabel: `Fill detail for ${row.fillId}`,
    detail: `${row.symbol} fill posted at ${row.price} on ${row.venue}. ${provider ? `${provider} supplied the execution adapter context.` : "Execution adapter context unavailable."}`,
    fields: [
      { label: "Order", value: row.orderId, tone: "muted" },
      { label: "Symbol", value: row.symbol, tone: "default" },
      { label: "Side", value: row.side, tone: row.side === "Buy" ? "success" : "warning" },
      { label: "Quantity", value: row.quantity, tone: "default" },
      { label: "Price", value: row.price, tone: "default" },
      { label: "Venue", value: row.venue, tone: "muted" },
      { label: "Timestamp", value: row.timestamp, tone: "muted" },
      { label: "Provider", value: provider ?? "Unavailable", tone: "muted" }
    ],
    shownInRow: ["Order", "Symbol", "Side", "Quantity", "Price", "Venue", "Timestamp"]
  };
}

function riskFieldTone(state: TradingRiskState["state"] | undefined): TradingDataTone {
  if (state === "Healthy") return "success";
  if (state === "Observe") return "warning";
  if (state === "Constrained") return "danger";
  return "muted";
}

function riskStateTone(
  state: TradingRiskState["state"] | undefined,
  fallback: TradingDataTone
): TradingDataTone {
  if (state === "Constrained") return "danger";
  if (state === "Observe") return "warning";
  if (state === "Healthy") return "success";
  return fallback;
}
