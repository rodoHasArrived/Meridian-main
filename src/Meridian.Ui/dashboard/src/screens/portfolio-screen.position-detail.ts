import { humanizeIdentifiersInText } from "@/lib/display-labels";
import { markFreshnessFields, markPriceField } from "@/lib/mark-freshness";
import type { RecordIdentity } from "@/lib/record-workspace-context";
import type { TradingWorkspaceResponse } from "@/types";
import { pnlFieldTone, riskFieldTone, riskTone } from "./portfolio-screen.presentation";
import type { PortfolioPositionDetail, PortfolioPositionRow } from "./portfolio-screen.view-model";

export function buildSelectedPositionDetail(
  position: PortfolioPositionRow,
  risk: TradingWorkspaceResponse["risk"] | null,
  brokerage: TradingWorkspaceResponse["brokerage"] | null,
  record: RecordIdentity,
  linkedRef: string
): PortfolioPositionDetail {
  const statusTone = position.markFreshness.reviewRequired ? "warning" : riskTone(risk?.state, position.pnlTone);
  const guardrailSummary = risk?.activeGuardrails.length
    ? risk.activeGuardrails.map((guardrail) => humanizeIdentifiersInText(guardrail)).join(" · ")
    : "No active guardrails";

  return {
    id: position.id,
    record,
    linkedRef,
    title: position.symbol,
    subtitle: `${position.side} · ${position.quantity} shares`,
    ariaLabel: `${position.symbol} holding detail`,
    statusLabel: position.markFreshness.reviewRequired ? "Review required" : risk?.state ?? null,
    statusTitle: position.markFreshness.reviewRequired ? `${position.symbol} · Review required` : `${position.symbol} selected`,
    statusDetail: `${position.markFreshness.reason} Recorded: ${position.exposure} exposure with ${position.unrealizedPnl} unrealized P&L. ${risk?.summary ?? "Risk context unavailable."}`,
    statusTone,
    fields: [
      { label: "Side", value: position.side, tone: "default" },
      { label: "Quantity", value: position.quantity, tone: "default" },
      { label: "Currency", value: position.currencyFigure.display, tone: "muted" },
      { label: "Average price", value: position.avgPrice, tone: "muted" },
      markPriceField(position.markPrice, position.markFreshness),
      ...markFreshnessFields(position.markFreshness),
      {
        label: "Market value",
        value: position.marketValueFigure.staleLabel ? `${position.marketValue} (${position.marketValueFigure.staleLabel})` : position.marketValue,
        tone: position.marketValueFigure.stale ? "warning" : "default"
      },
      { label: "Weight", value: position.weight, tone: "muted" },
      { label: "Exposure", value: position.exposure, tone: "default" },
      { label: "Day P&L", value: position.dayPnl, tone: pnlFieldTone(position.dayPnl) },
      { label: "Unrealized P&L", value: position.unrealizedPnl, tone: pnlFieldTone(position.unrealizedPnl) },
      { label: "Risk state", value: risk?.state ?? "Unavailable", tone: riskFieldTone(risk?.state) },
      { label: "Buying power", value: risk?.buyingPowerUsed ?? "—", tone: "muted" },
      { label: "Guardrails", value: guardrailSummary, tone: risk?.activeGuardrails.length ? "warning" : "success" },
      { label: "Brokerage", value: brokerage ? `${brokerage.provider} · ${brokerage.environment}` : "—", tone: "muted" },
      { label: "Last heartbeat", value: brokerage?.lastHeartbeat ?? "—", tone: "muted" }
    ]
  };
}

