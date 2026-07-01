import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OverviewScreen } from "@/screens/overview-screen";
import { renderWithRouter } from "@/test/render";
import type { SessionInfo, SystemOverviewResponse, TradingWorkspaceResponse } from "@/types";

const overview: SystemOverviewResponse = {
  systemStatus: "Degraded",
  providersOnline: 2,
  providersTotal: 4,
  activeRuns: 3,
  openPositions: 5,
  activeBackfills: 1,
  symbolsMonitored: 42,
  storageHealth: "Warning",
  lastHeartbeatUtc: "2026-04-28T18:15:00Z",
  metrics: [],
  recentEvents: [
    {
      id: "evt-1",
      type: "warning",
      message: "Brokerage sync delayed.",
      source: "Provider health",
      timestamp: "2026-04-28T18:15:00Z"
    }
  ]
};

const session: SessionInfo = {
  displayName: "Meridian Ops",
  role: "Operator",
  environment: "paper",
  activeWorkspace: "trading",
  commandCount: 9
};

const tradingWorkspace: TradingWorkspaceResponse = {
  metrics: [],
  positions: [],
  openOrders: [],
  fills: [],
  risk: {
    state: "Healthy",
    summary: "Paper account is inside guardrails.",
    netExposure: "$0",
    grossExposure: "$0",
    var95: "$0",
    maxDrawdown: "0%",
    buyingPowerUsed: "0%",
    activeGuardrails: []
  },
  brokerage: {
    provider: "Alpaca",
    account: "Paper",
    environment: "paper",
    connection: "Connected",
    lastHeartbeat: "2026-04-28T18:15:00Z",
    orderIngress: "Ready",
    fillFeed: "Ready",
    notes: "Ready for paper orders."
  }
};

describe("OverviewScreen", () => {
  it("renders system health as a named live status banner", () => {
    renderWithRouter(<OverviewScreen data={overview} session={null} />);

    const banner = screen.getByRole("alert", {
      name: "System Degraded"
    });

    expect(banner).toHaveAttribute("aria-live", "assertive");
    expect(banner).toHaveAttribute("aria-labelledby", "overview-status-title");
    expect(banner).toHaveAttribute("aria-describedby", "overview-status-detail");
    expect(within(banner).getByText("System Degraded")).toBeInTheDocument();
    expect(within(banner).getByText(/2 of 4 providers online/)).toBeInTheDocument();
    expect(within(banner).getByText(/Storage:/)).toBeInTheDocument();
  });

  it("renders loading system health as a polite status banner", () => {
    renderWithRouter(<OverviewScreen data={null} session={null} />);

    const banner = screen.getByRole("status", {
      name: "Connecting to system..."
    });

    expect(banner).toHaveAttribute("aria-live", "polite");
    expect(within(banner).getByText("Waiting for the workstation status payload.")).toBeInTheDocument();
  });

  it("renders recent activity as selectable dense-table evidence with detail", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <OverviewScreen
        data={{
          ...overview,
          recentEvents: [
            ...overview.recentEvents,
            {
              id: "evt-2",
              type: "error",
              message: "Storage verification failed.",
              source: "Storage",
              timestamp: "2026-04-28T18:20:00Z"
            }
          ]
        }}
        session={null}
      />
    );

    expect(screen.getByText("Recent activity")).toBeInTheDocument();

    const activityTable = screen.getByRole("treegrid", { name: "2 recent system events" });
    const warningRow = within(activityTable).getByRole("row", {
      name: /Inspect Warning event from Provider health at .*Brokerage sync delayed\./i
    });
    const errorRow = within(activityTable).getByRole("row", {
      name: /Inspect Error event from Storage at .*Storage verification failed\./i
    });

    expect(warningRow).toHaveAttribute("aria-controls", "overview-activity-selected-detail");
    expect(warningRow).toHaveAttribute("aria-expanded", "true");
    expect(warningRow).toHaveAttribute("aria-selected", "true");
    expect(within(warningRow).getByText("OBS")).toBeInTheDocument();
    expect(within(warningRow).getByText("Provider health")).toBeInTheDocument();
    expect(within(warningRow).getByText("Brokerage sync delayed.")).toBeInTheDocument();

    const detail = screen.getByRole("complementary", {
      name: "Selected recent activity detail for Warning event from Provider health"
    });

    expect(within(detail).getByText("Warning event")).toBeInTheDocument();
    expect(within(detail).getByText("Brokerage sync delayed.")).toBeInTheDocument();
    expect(within(detail).getByText("evt-1")).toBeInTheDocument();

    await user.click(errorRow);

    expect(errorRow).toHaveAttribute("aria-expanded", "true");
    expect(errorRow).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("complementary", {
      name: "Selected recent activity detail for Error event from Storage"
    })).toHaveTextContent("Storage verification failed.");
  });

  it("renders an operator briefing with priority workspace calls to action", () => {
    renderWithRouter(<OverviewScreen data={overview} session={session} />);

    expect(screen.getByText("Meridian workstation control tower")).toBeInTheDocument();
    expect(screen.getByText("Meridian Ops")).toBeInTheDocument();
    expect(screen.getByText(/Operator - 9 commands ready/i)).toBeInTheDocument();
    expect(screen.getAllByText(/^paper$/i).length).toBeGreaterThan(0);
    expect(screen.getByText("Readiness blockers")).toBeInTheDocument();
    expect(screen.getByText("3 blockers need attention before a confident operator handoff.")).toBeInTheDocument();

    const priorityCard = screen.getByText("Move from posture to action").closest("div");
    const providerLink = screen.getByRole("link", {
      name: /Provider baseline is degraded.*Review providers/i
    });
    const storageLink = screen.getByRole("link", {
      name: /Storage evidence needs review.*Review storage/i
    });
    const backfillLink = screen.getByRole("link", {
      name: /Backfill work is still running.*Review backfills/i
    });

    expect(providerLink).toHaveAttribute("href", "/data/providers");
    expect(storageLink).toHaveAttribute("href", "/settings#backend-capability-coverage");
    expect(backfillLink).toHaveAttribute("href", "/data/backfills");
    expect(screen.getByText("Open trading cockpit")).toBeInTheDocument();
    expect(screen.getByText("Open accounting lane")).toBeInTheDocument();
    expect(screen.getByText("Open reporting lane")).toBeInTheDocument();
    expect(priorityCard).not.toBeNull();
  });

  it("routes an empty symbol universe to the watchlist starter packs", () => {
    renderWithRouter(<OverviewScreen data={{ ...overview, symbolsMonitored: 0 }} session={session} />);

    const watchlistLink = screen.getByRole("link", { name: "Open Data watchlist starter packs" });
    expect(watchlistLink).toHaveAttribute("href", "/data/watchlist");
    expect(screen.getByText("Seed a working watchlist")).toBeInTheDocument();
    expect(screen.getByText(/No monitored symbols are loaded yet/i)).toBeInTheDocument();
  });

  it("routes provider-baseline gaps directly to the Alpaca setup checklist", () => {
    renderWithRouter(
      <OverviewScreen
        data={{ ...overview, providersOnline: 0, providersTotal: 0, symbolsMonitored: 0 }}
        session={session}
      />
    );

    const setupLink = screen.getByRole("link", { name: "Open Alpaca paper provider setup checklist" });
    expect(setupLink).toHaveAttribute("href", "/settings#alpaca-provider-setup");
    expect(screen.getByText("Connect provider baseline")).toBeInTheDocument();
    expect(screen.getByText(/No providers are configured yet/i)).toBeInTheDocument();
  });

  it("routes an unhydrated portfolio cockpit to provider setup", () => {
    renderWithRouter(<OverviewScreen data={overview} session={session} />);

    const setupLink = screen.getByRole("link", {
      name: "Open Alpaca paper provider setup checklist from the empty portfolio panel"
    });
    expect(setupLink).toHaveAttribute("href", "/settings#alpaca-provider-setup");
  });

  it("routes an empty connected portfolio cockpit to the trading path", () => {
    renderWithRouter(<OverviewScreen data={overview} session={session} trading={tradingWorkspace} />);

    const tradingLink = screen.getByRole("link", {
      name: "Open Trading cockpit from the empty portfolio positions panel"
    });
    expect(tradingLink).toHaveAttribute("href", "/trading");
  });

  it("renders Today movers, orders, and fills as dense evidence tables", () => {
    renderWithRouter(
      <OverviewScreen
        data={overview}
        session={session}
        trading={{
          ...tradingWorkspace,
          positions: [
            { symbol: "AAPL", side: "Long", quantity: "100", averagePrice: "188", markPrice: "190", dayPnl: "+$200", unrealizedPnl: "+$250", exposure: "$19,000" },
            { symbol: "MSFT", side: "Short", quantity: "25", averagePrice: "410", markPrice: "405", dayPnl: "-$125", unrealizedPnl: "+$100", exposure: "-$10,125" }
          ],
          openOrders: [
            { orderId: "ord-1", symbol: "AAPL", side: "Buy", type: "Limit", quantity: "10", limitPrice: "190.50", status: "Working", submittedAt: "2026-04-28T18:10:00Z" }
          ],
          fills: [
            { fillId: "fill-1", orderId: "ord-0", symbol: "MSFT", side: "Sell", quantity: "5", price: "405.25", venue: "NASDAQ", timestamp: "2026-04-28T18:12:00Z" }
          ]
        }}
      />
    );

    const moversTable = screen.getByRole("table", { name: "2 top movers" });
    expect(within(moversTable).getByRole("row", {
      name: "AAPL Long 100 shares, mark 190, day P&L +$200"
    })).toBeInTheDocument();
    expect(within(moversTable).getByText("Day P&L")).toBeInTheDocument();

    const ordersTable = screen.getByRole("table", { name: "1 open order" });
    const orderRow = within(ordersTable).getByRole("row", {
      name: "Buy 10 AAPL limit at 190.50, Working, submitted 2026-04-28T18:10:00Z"
    });
    expect(orderRow).toBeInTheDocument();
    expect(orderRow).toHaveClass("bg-success/5");
    expect(within(orderRow).getByLabelText("Order status Working")).toHaveTextContent("Working");
    expect(within(ordersTable).getByText("Submitted")).toBeInTheDocument();

    const fillsTable = screen.getByRole("table", { name: "1 recent fill" });
    const fillRow = within(fillsTable).getByRole("row", {
      name: "Sell 5 MSFT at 405.25 on NASDAQ, 2026-04-28T18:12:00Z"
    });
    expect(fillRow).toBeInTheDocument();
    expect(fillRow).toHaveClass("bg-success/5");
    expect(within(fillRow).getByLabelText("Fill fill-1 completed")).toHaveTextContent("Filled");
    expect(within(fillsTable).getByText("Venue")).toBeInTheDocument();
    expect(within(fillsTable).getByText("Status")).toBeInTheDocument();
  });
});
