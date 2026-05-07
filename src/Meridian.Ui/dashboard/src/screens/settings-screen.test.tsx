import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { SettingsScreen } from "@/screens/settings-screen";
import { renderWithRouter } from "@/test/render";
import type { BrokerageConnectionStatus, SessionInfo, SystemOverviewResponse } from "@/types";

const session: SessionInfo = {
  displayName: "Andrew Rowden",
  role: "Fund Manager",
  environment: "paper",
  activeWorkspace: "settings",
  commandCount: 42
};

const overview: SystemOverviewResponse = {
  systemStatus: "Degraded",
  providersOnline: 2,
  providersTotal: 3,
  activeRuns: 1,
  openPositions: 5,
  activeBackfills: 0,
  symbolsMonitored: 120,
  storageHealth: "Warning",
  lastHeartbeatUtc: "2026-05-01T00:00:00Z",
  metrics: [],
  recentEvents: [
    {
      id: "evt-1",
      type: "warning",
      message: "Brokerage sync delayed.",
      source: "Provider health",
      timestamp: "2026-05-01T00:00:00Z"
    }
  ]
};

const alpacaConnection: BrokerageConnectionStatus = {
  providerId: "alpaca",
  displayName: "Alpaca paper",
  state: "Connected",
  isConfigured: true,
  isConnected: true,
  authorizationUrl: null,
  connectedAt: "2026-05-07T11:50:00Z",
  expiresAt: null,
  lastError: null,
  warnings: [],
  scopes: ["trading:account", "brokerage-sync:read"],
  environment: "paper",
  externalAccountId: "PA123",
  verifiedAt: "2026-05-07T11:50:00Z",
  maskedKeyId: "********1234"
};

describe("SettingsScreen", () => {
  it("renders recent events as accessible status evidence rows", () => {
    renderWithRouter(<SettingsScreen session={session} overview={overview} />);

    expect(screen.getByRole("region", { name: "Settings workbench context" })).toHaveTextContent(
      "Operator control posture"
    );
    const eventList = screen.getByRole("list", { name: "1 recent system event" });
    const eventRow = within(eventList).getByRole("group", {
      name: /OBS event from Provider health at 2026-05-01T00:00:00Z\. Brokerage sync delayed\./i
    });

    expect(within(eventRow).getByText("OBS")).toBeInTheDocument();
    expect(within(eventRow).getByText("Brokerage sync delayed.")).toBeInTheDocument();
    expect(within(eventRow).getByText("Provider health · evt-1")).toBeInTheDocument();
  });

  it("keeps the recent-events panel visible when there are no events", () => {
    renderWithRouter(<SettingsScreen session={session} overview={{ ...overview, recentEvents: [] }} />);

    expect(screen.getAllByText("No recent events")).toHaveLength(2);
    expect(screen.getByRole("status")).toHaveTextContent("No system events reported");
  });

  it("renders an alert state when overview data is unavailable", () => {
    renderWithRouter(<SettingsScreen session={session} overview={null} />);

    expect(screen.getAllByText("Event stream unavailable")).toHaveLength(2);
    expect(screen.getByRole("alert")).toHaveTextContent("Reconnect to the Meridian API");
  });

  it("labels diagnostic endpoint links", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        research={{ metrics: [], runs: [] }}
        trading={{} as never}
        dataOperations={{ metrics: [], providers: [], backfills: [], exports: [] }}
        governance={{} as never}
        reporting={{} as never}
      />
    );

    expect(screen.getByRole("link", { name: "Open System overview diagnostic endpoint" })).toHaveAttribute(
      "href",
      "/api/status"
    );
    expect(screen.getByRole("link", { name: "Open Data workspace diagnostic endpoint" })).toHaveAttribute(
      "href",
      "/api/workstation/data"
    );
    expect(screen.getByRole("link", { name: "Open Strategy workspace diagnostic endpoint" })).toHaveAttribute(
      "href",
      "/api/workstation/strategy"
    );
    expect(screen.getByRole("list", { name: "Diagnostic endpoint availability" })).toBeInTheDocument();
    expect(screen.getAllByText("All reachable").length).toBeGreaterThan(0);
  });

  it("renders the Alpaca paper connection panel", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    expect(screen.getByText("Alpaca paper API keys")).toBeInTheDocument();
    expect(screen.getByLabelText("Alpaca trading environment")).toHaveValue("paper");
    expect(screen.getByText("********1234")).toBeInTheDocument();
    expect(screen.getByText("PA123")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /connect \/ test/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /clear/i })).toBeEnabled();
    expect(screen.getByLabelText(/Key ID/)).toHaveAccessibleDescription("Stored values remain masked after refresh.");
    expect(screen.getByLabelText(/Secret key/)).toHaveAccessibleDescription("Secret key is never displayed after submit.");
    expect(screen.getByRole("button", { name: /connect \/ test/i })).toHaveAttribute(
      "title",
      "Enter an Alpaca key ID before testing the connection."
    );
  });

  it("renders backend capability groups with mapped API links", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        research={{ metrics: [], runs: [] }}
        trading={{} as never}
        dataOperations={{ metrics: [], providers: [], backfills: [], exports: [] }}
        governance={{} as never}
        reporting={{} as never}
      />
    );

    expect(screen.getByRole("list", { name: "Backend capability coverage by workstation route" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "GET /api/workstation/workflows for Settings Workflow library" })).toHaveAttribute(
      "href",
      "/api/workstation/workflows"
    );
    expect(screen.getByRole("link", { name: "GET /api/workstation/runs/history for Strategy Run history" })).toHaveAttribute(
      "href",
      "/api/workstation/runs/history"
    );
    expect(screen.getByRole("link", { name: "POST /api/workstation/reconciliation/runs for Accounting Run reconciliation" })).toHaveAttribute(
      "href",
      "/api/workstation/reconciliation/runs"
    );
  });

  it("renders diagnostic endpoint failures as accessible endpoint cards", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        research={null}
        trading={null}
        dataOperations={null}
        governance={null}
        error="Workstation request failed."
        workspaceErrors={{ trading: "Trading API returned 503." }}
      />
    );

    const tradingLink = screen.getByRole("link", { name: "Open Trading workspace diagnostic endpoint" });

    expect(tradingLink).toHaveAttribute("href", "/api/workstation/trading");
    expect(within(tradingLink).getByText("Failed")).toBeInTheDocument();
    expect(within(tradingLink).getByText("Trading API returned 503.")).toBeInTheDocument();
  });
});
