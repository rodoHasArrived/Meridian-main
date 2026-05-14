import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { SettingsScreen } from "@/screens/settings-screen";
import { renderWithRouter } from "@/test/render";
import type { BrokerageConnectionStatus, PortfolioWorkspaceResponse, SessionInfo, SystemOverviewResponse } from "@/types";

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

const portfolio: PortfolioWorkspaceResponse = {
  metrics: [],
  positions: [],
  risk: {
    state: "Healthy",
    summary: "No portfolio risk flags.",
    netExposure: "$0",
    grossExposure: "$0",
    var95: "$0",
    maxDrawdown: "0%",
    activeGuardrails: [],
    buyingPowerUsed: "0%"
  },
  brokerage: {
    provider: "Alpaca",
    account: "PA-DEMO",
    environment: "paper",
    connection: "Connected",
    orderIngress: "healthy",
    fillFeed: "healthy",
    lastHeartbeat: "2026-05-07T12:00:00Z",
    notes: "Paper brokerage fixture is healthy."
  },
  runs: [],
  cashFlow: null
};

describe("SettingsScreen", () => {
  it("renders recent events as accessible status evidence rows", () => {
    renderWithRouter(<SettingsScreen session={session} overview={overview} />);

    expect(screen.getByRole("region", { name: "Settings workbench context" })).toHaveTextContent(
      "Operator control posture"
    );
    const eventTable = screen.getByRole("table", { name: "1 recent system event" });
    const eventRow = within(eventTable).getByRole("row", {
      name: /Select event evt-1\. OBS event from Provider health at May 1, 00:00 UTC\. Brokerage sync delayed\./i
    });
    const eventDetail = screen.getByRole("complementary", { name: "Selected recent event detail" });

    expect(eventRow).toHaveAttribute("aria-selected", "true");
    expect(eventRow).toHaveAttribute("aria-controls", "settings-recent-event-detail");
    expect(eventRow).toHaveAttribute("aria-expanded", "true");
    expect(within(eventRow).getByText("OBS")).toBeInTheDocument();
    expect(within(eventRow).getByText("Brokerage sync delayed.")).toBeInTheDocument();
    expect(within(eventRow).getByText("Provider health")).toBeInTheDocument();
    expect(within(eventDetail).getByText("Brokerage sync delayed.")).toBeInTheDocument();
    expect(within(eventDetail).getByText("Provider health / evt-1")).toBeInTheDocument();
  });

  it("updates recent-event detail with keyboard row selection", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={{
          ...overview,
          recentEvents: [
            ...overview.recentEvents,
            {
              id: "evt-2",
              type: "error",
              message: "Storage heartbeat missed.",
              source: "Storage",
              timestamp: "2026-05-01T00:04:00Z"
            }
          ]
        }}
      />
    );

    const storageRow = screen.getByRole("row", {
      name: /Select event evt-2\. CRIT event from Storage at May 1, 00:04 UTC\. Storage heartbeat missed\./i
    });

    storageRow.focus();
    await user.keyboard("{Enter}");

    const eventDetail = screen.getByRole("complementary", { name: "Selected recent event detail" });
    expect(storageRow).toHaveAttribute("aria-selected", "true");
    expect(within(eventDetail).getByRole("region", { name: "CRIT event detail for evt-2" })).toHaveTextContent(
      "Storage heartbeat missed."
    );
    expect(within(eventDetail).getByText("Critical")).toBeInTheDocument();
  });

  it("keeps the recent-events panel visible when there are no events", () => {
    renderWithRouter(<SettingsScreen session={session} overview={{ ...overview, recentEvents: [] }} />);

    expect(screen.getAllByText("No recent events")).toHaveLength(2);
    expect(screen.getByText("No system events reported for the active session. Diagnostic endpoints remain available below.")).toBeInTheDocument();
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
        portfolio={portfolio}
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

    expect(screen.getByText("Alpaca paper API keys").closest("#alpaca-provider-setup")).toBeInTheDocument();
    expect(screen.getByRole("radiogroup", { name: "Alpaca trading environment" })).toBeInTheDocument();
    const paperEndpoint = screen.getByRole("radio", { name: "Use Alpaca paper endpoint for workstation validation" });
    const liveEndpoint = screen.getByRole("radio", { name: "Use Alpaca live endpoint for production brokerage verification" });
    expect(paperEndpoint).toBeChecked();
    expect(liveEndpoint).not.toBeChecked();
    expect(paperEndpoint).toHaveAccessibleDescription(/Paper endpoint for workstation validation.*Paper endpoint selected/s);
    expect(liveEndpoint).toHaveAccessibleDescription(/Live endpoint for production brokerage verification.*Paper endpoint selected/s);
    expect(screen.getByText("Enter Alpaca credentials")).toBeInTheDocument();
    expect(screen.getAllByText("Key ID").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Needed").length).toBeGreaterThan(0);
    expect(screen.getByText("********1234")).toBeInTheDocument();
    expect(screen.getByText("PA123")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Alpaca provider setup checklist" })).toBeInTheDocument();
    expect(screen.getByText("Move from demo data to a verified paper connection before relying on readiness evidence.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Trading readiness after Alpaca account verification" })).toHaveAttribute(
      "href",
      "/trading/readiness"
    );
    expect(screen.getByRole("button", { name: /connect and test/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /clear/i })).toBeEnabled();
    expect(screen.getByLabelText(/Key ID/)).toHaveAccessibleDescription(/Stored values remain masked after refresh\..*Enter Alpaca credentials/s);
    expect(screen.getByLabelText(/Secret key/)).toHaveAccessibleDescription(/Secret key is never displayed after submit\..*Enter Alpaca credentials/s);
    expect(screen.getByRole("button", { name: /connect and test/i })).toHaveAttribute(
      "title",
      "Enter an Alpaca key ID before testing the connection."
    );
  });

  it("blocks live Alpaca credential testing until the live endpoint is acknowledged", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    await user.click(screen.getByRole("radio", { name: "Use Alpaca live endpoint for production brokerage verification" }));
    await user.type(screen.getByPlaceholderText("ALPACA_KEY_ID"), "AK-LIVE");
    await user.type(screen.getByPlaceholderText("ALPACA_SECRET_KEY"), "secret");

    const submit = screen.getByRole("button", { name: /connect and test/i });
    expect(screen.getByRole("checkbox", { name: "Acknowledge live Alpaca endpoint before testing credentials" })).not.toBeChecked();
    expect(screen.getByText("Live endpoint review required")).toBeInTheDocument();
    expect(submit).toBeDisabled();
    expect(submit).toHaveAttribute(
      "title",
      "Acknowledge the live Alpaca endpoint before testing live credentials."
    );

    await user.click(screen.getByRole("checkbox", { name: "Acknowledge live Alpaca endpoint before testing credentials" }));

    expect(submit).toBeEnabled();
    expect(screen.getByText("Credentials ready for test")).toBeInTheDocument();
  });

  it("renders backend capability groups with mapped API links", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        research={{ metrics: [], runs: [] }}
        trading={{} as never}
        portfolio={portfolio}
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
    expect(screen.queryByRole("link", { name: "POST /api/workstation/reconciliation/runs for Accounting Run reconciliation" })).toBeNull();
    expect(screen.getByRole("group", {
      name: "Reference-only POST /api/workstation/reconciliation/runs for Accounting Run reconciliation"
    })).toHaveTextContent(
      "Reference"
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
