import { screen, within } from "@testing-library/react";
import { OverviewScreen } from "@/screens/overview-screen";
import { renderWithRouter } from "@/test/render";
import type { SystemOverviewResponse } from "@/types";

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

describe("OverviewScreen", () => {
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
});
