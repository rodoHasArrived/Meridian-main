import { screen, within } from "@testing-library/react";
import { OverviewScreen } from "@/screens/overview-screen";
import { renderWithRouter } from "@/test/render";
import type { SessionInfo, SystemOverviewResponse } from "@/types";

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

  it("renders recent activity as accessible status evidence rows", () => {
    renderWithRouter(<OverviewScreen data={overview} session={null} />);

    expect(screen.getByText("Recent activity")).toBeInTheDocument();

    const activityList = screen.getByRole("list", { name: "1 recent system event" });
    const activityRow = within(activityList).getByRole("group", {
      name: /Warning event from Provider health at .*Brokerage sync delayed\./i
    });

    expect(within(activityRow).getByText("OBS")).toBeInTheDocument();
    expect(within(activityRow).getByText("Provider health")).toBeInTheDocument();
    expect(within(activityRow).getByText("Brokerage sync delayed.")).toBeInTheDocument();
  });

  it("renders an operator briefing with priority workspace calls to action", () => {
    renderWithRouter(<OverviewScreen data={overview} session={session} />);

    expect(screen.getByText("Meridian workstation control tower")).toBeInTheDocument();
    expect(screen.getByText("Meridian Ops")).toBeInTheDocument();
    expect(screen.getByText(/Operator - 9 commands ready/i)).toBeInTheDocument();
    expect(screen.getAllByText(/^paper$/i).length).toBeGreaterThan(0);

    const priorityCard = screen.getByText("Move from posture to action").closest("div");
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
});
