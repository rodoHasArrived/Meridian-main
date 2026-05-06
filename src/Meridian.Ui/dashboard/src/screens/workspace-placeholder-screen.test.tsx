import { screen, within } from "@testing-library/react";
import { workspaceForKey } from "@/lib/workspace";
import { WorkspacePlaceholderScreen } from "@/screens/workspace-placeholder-screen";
import { renderWithRouter } from "@/test/render";
import type { SessionInfo, SystemOverviewResponse } from "@/types";

const session: SessionInfo = {
  displayName: "Ops Desk",
  role: "Operator",
  environment: "paper",
  activeWorkspace: "trading",
  commandCount: 7
};

const overview: SystemOverviewResponse = {
  systemStatus: "Healthy",
  providersOnline: 3,
  providersTotal: 4,
  activeRuns: 2,
  openPositions: 5,
  activeBackfills: 1,
  symbolsMonitored: 128,
  storageHealth: "Healthy",
  lastHeartbeatUtc: "2026-01-02T03:04:05Z",
  metrics: [],
  recentEvents: []
};

describe("WorkspacePlaceholderScreen", () => {
  it("renders a staged-route briefing with a primary next step and telemetry", () => {
    renderWithRouter(
      <WorkspacePlaceholderScreen
        workspace={workspaceForKey("portfolio")}
        session={session}
        overview={overview}
      />
    );

    expect(screen.getByText("Portfolio workspace route is staged")).toBeInTheDocument();
    expect(screen.getByText("Recommended next stop")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Review trading readiness\./i })).toBeInTheDocument();

    const coverageRegion = screen.getByRole("region", { name: "Portfolio pending workspace guidance" });
    expect(within(coverageRegion).getByText("Current portfolio review path")).toBeInTheDocument();
    expect(within(coverageRegion).getByText("Exposure posture stays in Trading")).toBeInTheDocument();

    const telemetryRegion = screen.getByRole("region", { name: "Portfolio route telemetry" });
    expect(within(telemetryRegion).getByText("3 of 4 online")).toBeInTheDocument();
    expect(within(telemetryRegion).getByText("Jan 02, 2026 03:04 UTC")).toBeInTheDocument();
  });
});
