import {
  appendLinkedContextSearchValue,
  buildLinkedContextItem,
  findLinkedContextSymbolRow,
  isSameLinkedContextSymbol,
  type AppShellLinkedContextItem
} from "@/app-shell.linked-context";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { pluralizeCount } from "@/lib/format";
import type { TradingWorkspaceResponse } from "@/types";

export function buildTradingLinkedContextItem(
  trading: TradingWorkspaceResponse | null,
  symbol: string
): AppShellLinkedContextItem {
  const route = appendLinkedContextSearchValue(WORKSTATION_ROUTE_CATALOG.trading, "symbol", symbol);
  if (!trading) {
    return buildLinkedContextItem({
      id: "trading-cockpit",
      label: "Trading cockpit",
      detail: `Open paper-session controls with ${symbol} kept as the operating subject.`,
      route,
      workspaceLabel: "Trading",
      statusLabel: "Waiting",
      tone: "pending"
    });
  }

  if (trading.risk?.state === "Constrained") {
    return buildLinkedContextItem({
      id: "trading-cockpit",
      label: "Trading cockpit",
      detail: trading.risk.summary,
      route,
      workspaceLabel: "Trading",
      statusLabel: "Risk constrained",
      tone: "blocked"
    });
  }

  const position = findLinkedContextSymbolRow(trading.positions ?? [], symbol);
  const orderCount = (trading.openOrders ?? []).filter((order) => isSameLinkedContextSymbol(order.symbol, symbol)).length;
  const fillCount = (trading.fills ?? []).filter((fill) => isSameLinkedContextSymbol(fill.symbol, symbol)).length;
  if (orderCount > 0) {
    return buildLinkedContextItem({
      id: "trading-cockpit",
      label: "Trading cockpit",
      detail: `${formatTradingLinkedContextCount(orderCount, "open order")} and ${formatTradingLinkedContextCount(fillCount, "recent fill")} are loaded for ${symbol}.`,
      route,
      workspaceLabel: "Trading",
      statusLabel: "Orders open",
      tone: "review"
    });
  }

  if (position) {
    return buildLinkedContextItem({
      id: "trading-cockpit",
      label: "Trading cockpit",
      detail: `${position.side} ${position.quantity} with ${position.exposure} exposure and ${position.unrealizedPnl} unrealized P&L.`,
      route,
      workspaceLabel: "Trading",
      statusLabel: "Position loaded",
      tone: trading.risk?.state === "Observe" ? "review" : "ready"
    });
  }

  return buildLinkedContextItem({
    id: "trading-cockpit",
    label: "Trading cockpit",
    detail: `No open trading rows are loaded for ${symbol}; review before staging orders.`,
    route,
    workspaceLabel: "Trading",
    statusLabel: "No row",
    tone: "review"
  });
}

function formatTradingLinkedContextCount(count: number, singular: string, plural = `${singular}s`): string {
  return pluralizeCount(count, singular, { plural });
}
