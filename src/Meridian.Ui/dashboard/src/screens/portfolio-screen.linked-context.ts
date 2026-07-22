import {
  appendLinkedContextSearchValue,
  buildLinkedContextItem,
  findLinkedContextSymbolRow,
  type AppShellLinkedContextItem
} from "@/app-shell.linked-context";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { PortfolioWorkspaceResponse } from "@/types";

export function buildPortfolioLinkedContextItem(
  portfolio: PortfolioWorkspaceResponse | null,
  symbol: string
): AppShellLinkedContextItem {
  const route = appendLinkedContextSearchValue(WORKSTATION_ROUTE_CATALOG.portfolio, "symbol", symbol);
  if (!portfolio) {
    return buildLinkedContextItem({
      id: "portfolio-exposure",
      label: "Portfolio exposure",
      detail: `Open positions, cash, account sync, and risk posture with ${symbol} retained.`,
      route,
      workspaceLabel: "Portfolio",
      statusLabel: "Waiting",
      tone: "pending"
    });
  }

  if (portfolio.risk.state === "Constrained") {
    return buildLinkedContextItem({
      id: "portfolio-exposure",
      label: "Portfolio exposure",
      detail: portfolio.risk.summary,
      route,
      workspaceLabel: "Portfolio",
      statusLabel: "Risk constrained",
      tone: "blocked"
    });
  }

  const position = findLinkedContextSymbolRow(portfolio.positions ?? [], symbol);
  if (position) {
    return buildLinkedContextItem({
      id: "portfolio-exposure",
      label: "Portfolio exposure",
      detail: `${position.side} ${position.quantity} with ${position.exposure} exposure and ${position.unrealizedPnl} unrealized P&L.`,
      route,
      workspaceLabel: "Portfolio",
      statusLabel: portfolio.risk.state === "Observe" ? "Observe" : "Holding loaded",
      tone: portfolio.risk.state === "Observe" ? "review" : "ready"
    });
  }

  return buildLinkedContextItem({
    id: "portfolio-exposure",
    label: "Portfolio exposure",
    detail: `No portfolio position is loaded for ${symbol}; confirm sizing and account sync before trading.`,
    route,
    workspaceLabel: "Portfolio",
    statusLabel: "No holding",
    tone: "review"
  });
}
