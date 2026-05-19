import type {
  PortfolioWorkspaceResponse,
  TradingFill,
  TradingOrder,
  TradingPosition,
  TradingWorkspaceResponse
} from "@/types";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export type TodayTone = "default" | "success" | "warning" | "danger";

export interface TodayMetric {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: TodayTone;
  ariaLabel: string;
}

export interface TodayMoverRow {
  key: string;
  symbol: string;
  side: "Long" | "Short";
  quantity: string;
  markPrice: string;
  dayPnl: string;
  dayPnlTone: TodayTone;
  rowClassName: string | undefined;
  ariaLabel: string;
}

export interface TodayOrderRow {
  key: string;
  symbol: string;
  side: TradingOrder["side"];
  quantity: string;
  type: TradingOrder["type"];
  priceLabel: string;
  status: TradingOrder["status"];
  submittedLabel: string;
  ariaLabel: string;
}

export interface TodayFillRow {
  key: string;
  symbol: string;
  side: TradingFill["side"];
  quantity: string;
  price: string;
  venue: string;
  timestampLabel: string;
  ariaLabel: string;
}

export type TodayQuickActionId = "place-order" | "add-symbol" | "live-quote" | "reconcile";

export interface TodayQuickAction {
  id: TodayQuickActionId;
  label: string;
  description: string;
  href: string;
  ariaLabel: string;
}

export interface TodayPanelViewModel {
  hasData: boolean;
  headline: string;
  subheadline: string;
  emptyMessage: string;
  emptyActionHref: string;
  emptyActionLabel: string;
  emptyActionAriaLabel: string;
  metrics: TodayMetric[];
  movers: TodayMoverRow[];
  moversTotal: number;
  moversEmptyMessage: string;
  hasMovers: boolean;
  moversMoreLabel: string | null;
  moversTableLabel: string;
  moversTableCaption: string;
  orders: TodayOrderRow[];
  ordersTotal: number;
  ordersEmptyMessage: string;
  hasOrders: boolean;
  ordersMoreLabel: string | null;
  ordersTableLabel: string;
  ordersTableCaption: string;
  fills: TodayFillRow[];
  fillsTotal: number;
  fillsEmptyMessage: string;
  hasFills: boolean;
  fillsMoreLabel: string | null;
  fillsTableLabel: string;
  fillsTableCaption: string;
  quickActionsLabel: string;
  quickActionsEyebrow: string;
  quickActions: TodayQuickAction[];
  brokerageLabel: string | null;
}

const PREVIEW_LIMIT = 5;
const BROKERAGE_SETUP_HREF = WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup;

const QUICK_ACTIONS: TodayQuickAction[] = [
  {
    id: "place-order",
    label: "Place order",
    description: "Open the trading cockpit to stage a paper or live order.",
    href: WORKSTATION_ROUTE_CATALOG.trading,
    ariaLabel: "Open trading cockpit to place an order"
  },
  {
    id: "add-symbol",
    label: "Add symbol",
    description: "Subscribe a symbol to the live data pipeline.",
    href: WORKSTATION_ROUTE_CATALOG.dataWatchlist,
    ariaLabel: "Open the watchlist to add a symbol"
  },
  {
    id: "live-quote",
    label: "Look up quote",
    description: "Inspect live bid/ask, recent trades, and L2 depth.",
    href: WORKSTATION_ROUTE_CATALOG.dataQuotes,
    ariaLabel: "Open live quotes lookup"
  },
  {
    id: "reconcile",
    label: "Reconcile",
    description: "Review accounting breaks and cash posture.",
    href: WORKSTATION_ROUTE_CATALOG.accounting,
    ariaLabel: "Open accounting reconciliation queue"
  }
];

export function buildTodayPanelViewModel(
  trading: TradingWorkspaceResponse | null,
  portfolio: PortfolioWorkspaceResponse | null
): TodayPanelViewModel {
  const source = trading ?? portfolio;

  if (!source) {
    return {
      hasData: false,
      headline: "Today",
      subheadline: "Connect a brokerage account or start a paper session to see your day's activity.",
      emptyMessage: "No trading or portfolio data is available yet.",
      emptyActionHref: BROKERAGE_SETUP_HREF,
      emptyActionLabel: "Connect a brokerage",
      emptyActionAriaLabel: "Open Alpaca paper provider setup checklist to connect a brokerage account",
      metrics: emptyMetrics(),
      movers: [],
      moversTotal: 0,
      moversEmptyMessage: "No open positions to track today.",
      hasMovers: false,
      moversMoreLabel: null,
      moversTableLabel: buildTableLabel("top mover", 0),
      moversTableCaption: buildMoversTableCaption(0, 0),
      orders: [],
      ordersTotal: 0,
      ordersEmptyMessage: "No working orders.",
      hasOrders: false,
      ordersMoreLabel: null,
      ordersTableLabel: buildTableLabel("open order", 0),
      ordersTableCaption: buildOrdersTableCaption(0, 0),
      fills: [],
      fillsTotal: 0,
      fillsEmptyMessage: "No fills recorded today.",
      hasFills: false,
      fillsMoreLabel: null,
      fillsTableLabel: buildTableLabel("recent fill", 0),
      fillsTableCaption: buildFillsTableCaption(0, 0),
      quickActionsLabel: "Today quick actions",
      quickActionsEyebrow: "Quick actions",
      quickActions: QUICK_ACTIONS,
      brokerageLabel: null
    };
  }

  const positions: TradingPosition[] = (source.positions ?? []).filter((p) => p.symbol && p.symbol !== "—");
  const openOrders: TradingOrder[] = trading?.openOrders ?? [];
  const fills: TradingFill[] = trading?.fills ?? [];

  const totalDayPnl = positions.reduce((sum, pos) => sum + parseSignedAmount(pos.dayPnl), 0);
  const totalUnrealized = positions.reduce((sum, pos) => sum + parseSignedAmount(pos.unrealizedPnl), 0);

  const movers = buildMovers(positions);
  const orders = buildOrders(openOrders);
  const fillsRows = buildFills(fills);

  const dayPnlTone = toneForAmount(totalDayPnl);

  const portfolioMetrics = portfolio?.metrics ?? [];
  const tradingMetrics = trading?.metrics ?? [];
  const equityMetric = portfolioMetrics.find((m) => m.id === "portfolio-equity");
  const cashMetric = portfolioMetrics.find((m) => m.id === "portfolio-cash");
  const fillsMetric = tradingMetrics.find((m) => m.id === "fills");

  const metrics: TodayMetric[] = [
    {
      id: "today-day-pnl",
      label: "Day P&L",
      value: positions.length > 0 ? formatSignedCurrency(totalDayPnl) : "—",
      detail: positions.length > 0
        ? `Across ${formatCountLabel(positions.length, "open position", "open positions")}`
        : "No open positions",
      tone: positions.length > 0 ? dayPnlTone : "default",
      ariaLabel: positions.length > 0
        ? `Day P&L ${formatSignedCurrency(totalDayPnl)} across ${formatCountLabel(positions.length, "open position", "open positions")}`
        : "Day P&L unavailable, no open positions"
    },
    {
      id: "today-equity",
      label: "Account value",
      value: equityMetric?.value ?? "—",
      detail: equityMetric?.delta ?? "Awaiting brokerage equity feed",
      tone: equityMetric?.tone ?? "default",
      ariaLabel: equityMetric
        ? `Account value ${equityMetric.value}, ${equityMetric.delta}`
        : "Account value unavailable"
    },
    {
      id: "today-cash",
      label: "Cash",
      value: cashMetric?.value ?? "—",
      detail: cashMetric?.delta ?? "Awaiting portfolio cash feed",
      tone: cashMetric?.tone ?? "default",
      ariaLabel: cashMetric
        ? `Cash ${cashMetric.value}, ${cashMetric.delta}`
        : "Cash unavailable"
    },
    {
      id: "today-unrealized",
      label: "Unrealized P&L",
      value: positions.length > 0 ? formatSignedCurrency(totalUnrealized) : "—",
      detail: positions.length > 0
        ? `Mark-to-market on ${formatCountLabel(positions.length, "position", "positions")}`
        : "No open positions",
      tone: positions.length > 0 ? toneForAmount(totalUnrealized) : "default",
      ariaLabel: positions.length > 0
        ? `Unrealized P&L ${formatSignedCurrency(totalUnrealized)} on ${formatCountLabel(positions.length, "position", "positions")}`
        : "Unrealized P&L unavailable, no open positions"
    }
  ];

  const brokerage = source.brokerage;
  const brokerageLabel = brokerage
    ? `${brokerage.provider} · ${brokerage.account} · ${brokerage.environment}`
    : null;

  const subheadline = buildSubheadline({
    positions: positions.length,
    orders: openOrders.length,
    fills: fills.length,
    fillsLabel: fillsMetric?.value ?? null
  });

  return {
    hasData: true,
    headline: "Today",
    subheadline,
    emptyMessage: "",
    emptyActionHref: BROKERAGE_SETUP_HREF,
    emptyActionLabel: "Connect a brokerage",
    emptyActionAriaLabel: "Open Alpaca paper provider setup checklist to connect a brokerage account",
    metrics,
    movers,
    moversTotal: positions.length,
    moversEmptyMessage: "No open positions to track today.",
    hasMovers: movers.length > 0,
    moversMoreLabel: positions.length > movers.length
      ? `+${positions.length - movers.length} more in portfolio`
      : null,
    moversTableLabel: buildTableLabel("top mover", movers.length),
    moversTableCaption: buildMoversTableCaption(movers.length, positions.length),
    orders,
    ordersTotal: openOrders.length,
    ordersEmptyMessage: "No working orders.",
    hasOrders: orders.length > 0,
    ordersMoreLabel: openOrders.length > orders.length
      ? `+${openOrders.length - orders.length} more in trading cockpit`
      : null,
    ordersTableLabel: buildTableLabel("open order", orders.length),
    ordersTableCaption: buildOrdersTableCaption(orders.length, openOrders.length),
    fills: fillsRows,
    fillsTotal: fills.length,
    fillsEmptyMessage: "No fills recorded today.",
    hasFills: fillsRows.length > 0,
    fillsMoreLabel: fills.length > fillsRows.length
      ? `+${fills.length - fillsRows.length} more in trading cockpit`
      : null,
    fillsTableLabel: buildTableLabel("recent fill", fillsRows.length),
    fillsTableCaption: buildFillsTableCaption(fillsRows.length, fills.length),
    quickActionsLabel: "Today quick actions",
    quickActionsEyebrow: "Quick actions",
    quickActions: QUICK_ACTIONS,
    brokerageLabel
  };
}

function emptyMetrics(): TodayMetric[] {
  return [
    {
      id: "today-day-pnl",
      label: "Day P&L",
      value: "—",
      detail: "Awaiting trading session",
      tone: "default",
      ariaLabel: "Day P&L unavailable"
    },
    {
      id: "today-equity",
      label: "Account value",
      value: "—",
      detail: "Awaiting brokerage equity feed",
      tone: "default",
      ariaLabel: "Account value unavailable"
    },
    {
      id: "today-cash",
      label: "Cash",
      value: "—",
      detail: "Awaiting portfolio cash feed",
      tone: "default",
      ariaLabel: "Cash unavailable"
    },
    {
      id: "today-unrealized",
      label: "Unrealized P&L",
      value: "—",
      detail: "No open positions",
      tone: "default",
      ariaLabel: "Unrealized P&L unavailable"
    }
  ];
}

function buildMovers(positions: TradingPosition[]): TodayMoverRow[] {
  return positions
    .map((pos) => ({ pos, magnitude: Math.abs(parseSignedAmount(pos.dayPnl)) }))
    .filter((entry) => entry.magnitude > 0)
    .sort((a, b) => b.magnitude - a.magnitude)
    .slice(0, PREVIEW_LIMIT)
    .map(({ pos }) => {
      const dayPnlAmount = parseSignedAmount(pos.dayPnl);
      const dayPnlTone = toneForAmount(dayPnlAmount);
      return {
        key: pos.positionKey ?? pos.symbol,
        symbol: pos.symbol,
        side: pos.side,
        quantity: pos.quantity,
        markPrice: pos.markPrice,
        dayPnl: pos.dayPnl,
        dayPnlTone,
        rowClassName: todayRowClassName(dayPnlTone),
        ariaLabel: `${pos.symbol} ${pos.side} ${pos.quantity} shares, mark ${pos.markPrice}, day P&L ${pos.dayPnl}`
      };
    });
}

function buildOrders(orders: TradingOrder[]): TodayOrderRow[] {
  return orders.slice(0, PREVIEW_LIMIT).map((order) => {
    const priceLabel = order.type === "Market" ? "Market" : order.limitPrice;
    return {
      key: order.orderId,
      symbol: order.symbol,
      side: order.side,
      quantity: order.quantity,
      type: order.type,
      priceLabel,
      status: order.status,
      submittedLabel: order.submittedAt,
      ariaLabel: `${order.side} ${order.quantity} ${order.symbol} ${order.type.toLowerCase()} at ${priceLabel}, ${order.status}, submitted ${order.submittedAt}`
    };
  });
}

function buildFills(fills: TradingFill[]): TodayFillRow[] {
  return fills.slice(0, PREVIEW_LIMIT).map((fill) => ({
    key: fill.fillId,
    symbol: fill.symbol,
    side: fill.side,
    quantity: fill.quantity,
    price: fill.price,
    venue: fill.venue,
    timestampLabel: fill.timestamp,
    ariaLabel: `${fill.side} ${fill.quantity} ${fill.symbol} at ${fill.price} on ${fill.venue}, ${fill.timestamp}`
  }));
}

function buildSubheadline({
  positions,
  orders,
  fills,
  fillsLabel
}: {
  positions: number;
  orders: number;
  fills: number;
  fillsLabel: string | null;
}): string {
  if (positions === 0 && orders === 0 && fills === 0) {
    return "No portfolio activity yet. Add a symbol or place a paper order to get started.";
  }
  const parts: string[] = [];
  parts.push(formatCountLabel(positions, "open position", "open positions"));
  parts.push(formatCountLabel(orders, "working order", "working orders"));
  parts.push(`${fillsLabel ?? String(fills)} fills today`);
  return parts.join(" · ");
}

export function parseSignedAmount(value: string | null | undefined): number {
  if (!value) {
    return 0;
  }
  const trimmed = value.trim();
  if (trimmed === "" || trimmed === "—" || trimmed === "-") {
    return 0;
  }
  const cleaned = trimmed.replace(/[^0-9.\-+]/g, "");
  if (cleaned === "" || cleaned === "+" || cleaned === "-") {
    return 0;
  }
  const parsed = Number.parseFloat(cleaned);
  return Number.isFinite(parsed) ? parsed : 0;
}

function toneForAmount(amount: number): TodayTone {
  if (amount > 0) return "success";
  if (amount < 0) return "danger";
  return "default";
}

function formatSignedCurrency(value: number): string {
  const sign = value >= 0 ? "+" : "-";
  const absLabel = Math.abs(value).toLocaleString(undefined, { maximumFractionDigits: 0 });
  return `${sign}$${absLabel}`;
}

function formatCountLabel(count: number, singular: string, plural: string): string {
  return `${count} ${count === 1 ? singular : plural}`;
}

function buildTableLabel(singular: string, visibleCount: number): string {
  return formatCountLabel(visibleCount, singular, `${singular}s`);
}

function buildMoversTableCaption(visibleCount: number, totalCount: number): string {
  return `${formatCountLabel(visibleCount, "top mover", "top movers")} shown from ${formatCountLabel(totalCount, "open position", "open positions")}. Columns show symbol, side, quantity, mark price, and day P&L sorted by absolute day P&L magnitude.`;
}

function buildOrdersTableCaption(visibleCount: number, totalCount: number): string {
  return `${formatCountLabel(visibleCount, "open order", "open orders")} shown from ${formatCountLabel(totalCount, "working order", "working orders")}. Columns show symbol, side, quantity, order price, status, and submitted time.`;
}

function buildFillsTableCaption(visibleCount: number, totalCount: number): string {
  return `${formatCountLabel(visibleCount, "recent fill", "recent fills")} shown from ${formatCountLabel(totalCount, "fill", "fills")} today. Columns show symbol, side, quantity, price, venue, and fill time.`;
}

function todayRowClassName(tone: TodayTone): string | undefined {
  if (tone === "success") {
    return "bg-success/5";
  }

  if (tone === "danger") {
    return "bg-danger/5";
  }

  return undefined;
}
